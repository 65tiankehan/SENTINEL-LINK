using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace FikaProbe;

/// <summary>
/// Harmony 补丁目标扫描器（纯静态，不把目标程序集加载进运行时）。
///
/// 三条收集路径，缺一条就会漏：
///   A. 特性：类级 [HarmonyPatch(typeof(T))] + 方法级 [HarmonyPatch("M")]，或一体化 [HarmonyPatch(typeof(T), "M")]。
///   B. 命名约定：类上有 [HarmonyPatch(...)] 时，方法叫 Prefix / Postfix / Transpiler / Finalizer（含 Prefix1 这类重载）就算补丁方法，**不需要**特性。
///   C. 手写打补丁：`harmony.Patch(AccessTools.Method(typeof(T), "M"), ...)` 这种全在 IL 里，
///      特性一个都没有 —— 靠扫 IL 里 Harmony::Patch 调用点前的 ldtoken / ldstr 反推目标。
///
/// 输出：每个补丁的 (程序集, 目标类型, 目标方法, 补丁类型)；两份清单的交集；只在一侧出现的目标；CSV。
/// </summary>
internal static class Program
{
    private sealed record Patch(string Assembly, string TargetType, string TargetMethod, string Kind, string DeclaringType = "")
    {
        public string Key => TargetType + "::" + TargetMethod;
    }

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--diag")
        {
            foreach (string p in args.Skip(1)) Diagnose(p);
            return 0;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("用法: FikaProbe <我方dll...> -- <对方dll...>");
            Console.WriteLine("      FikaProbe --diag <dll...>     # 看这个程序集里 Harmony 痕迹到底长什么样");
            return 2;
        }

        int split = Array.IndexOf(args, "--");
        if (split < 1) { Console.WriteLine("缺少 -- 分隔符"); return 2; }

        var ours = Scan(args.Take(split).ToArray());
        var theirs = Scan(args.Skip(split + 1).ToArray());

        Console.WriteLine($"我方补丁 {ours.Count} 条（{Describe(ours)}）");
        Console.WriteLine($"对方补丁 {theirs.Count} 条（{Describe(theirs)}）");

        var ourKeys = ours.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.ToList());
        var theirKeys = theirs.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.ToList());

        var intersect = ourKeys.Keys.Intersect(theirKeys.Keys).OrderBy(k => k).ToList();
        Console.WriteLine();
        Console.WriteLine($"=== 交集 {intersect.Count} 条 ===");
        foreach (string k in intersect)
        {
            Console.WriteLine($"[撞] {k}");
            foreach (var p in ourKeys[k]) Console.WriteLine($"      我方: {p.Assembly} {p.Kind}");
            foreach (var p in theirKeys[k]) Console.WriteLine($"      对方: {p.Assembly} {p.Kind}");
        }

        var onlyTheirs = theirKeys.Keys.Where(k => !ourKeys.ContainsKey(k)).OrderBy(k => k).ToList();
        Console.WriteLine();
        Console.WriteLine($"=== 仅对方命中 {onlyTheirs.Count} 条 ===");
        foreach (string k in onlyTheirs)
            Console.WriteLine($"{k}  [{string.Join("/", theirKeys[k].Select(p => p.Kind).Distinct())}]");

        string csv = Path.Combine(Environment.CurrentDirectory, "fika-patch-conflicts.csv");
        using (var w = new StreamWriter(csv, false, new System.Text.UTF8Encoding(false)))
        {
            w.WriteLine("侧,程序集,目标类型,目标方法,补丁类型,是否交集,声明类型");
            foreach (var p in ours)
                w.WriteLine($"我方,{p.Assembly},{p.TargetType},{p.TargetMethod},{p.Kind},{(theirKeys.ContainsKey(p.Key) ? "是" : "")},{p.DeclaringType}");
            foreach (var p in theirs)
                w.WriteLine($"对方,{p.Assembly},{p.TargetType},{p.TargetMethod},{p.Kind},{(ourKeys.ContainsKey(p.Key) ? "是" : "")},{p.DeclaringType}");
        }
        Console.WriteLine();
        Console.WriteLine("CSV: " + csv);
        return 0;
    }

    private static string Describe(List<Patch> patches)
    {
        if (patches.Count == 0) return "无";
        return string.Join(", ", patches.GroupBy(p => p.Assembly).Select(g => $"{g.Key}={g.Count()}"));
    }

    private static List<Patch> Scan(string[] paths)
    {
        var result = new List<Patch>();
        foreach (string path in paths)
        {
            if (!File.Exists(path)) { Console.Error.WriteLine("找不到: " + path); continue; }
            string label = Path.GetFileNameWithoutExtension(path);
            using var asm = AssemblyDefinition.ReadAssembly(path);

            foreach (var type in AllTypes(asm.MainModule.Types))
            {
                var typeTargets = Collect(type.CustomAttributes);
                string typeLevelType = typeTargets.Type;
                string typeLevelMethod = typeTargets.Method;
                bool typeHasPatchAttribute = typeLevelType != null || typeLevelMethod != null;

                // Fika 风格：类名 == "目标类型_目标方法_Patch"，目标一个字段都没写进特性里，
                // 由它自己的 PatchManager 运行时按类名反查（特性只有空的 PatchPrefix/PatchPostfix/PatchTranspiler）。
                if (!typeHasPatchAttribute && type.Name.EndsWith("_Patch", StringComparison.Ordinal))
                {
                    var (fikaType, fikaMethod) = SplitFikaClassName(type.Name);
                    foreach (var fikaMethodDef in type.Methods)
                    {
                        string fikaKind = FikaPatchKind(fikaMethodDef);
                        if (fikaKind != null)
                            result.Add(new Patch(label, fikaType, fikaMethod, fikaKind, type.FullName ?? type.Name));
                    }
                }

                foreach (var method in type.Methods)
                {
                    string kind = PatchKind(method, typeHasPatchAttribute);
                    if (kind == null) continue;

                    var methodTargets = Collect(method.CustomAttributes);
                    string targetType = methodTargets.Type ?? typeLevelType ?? type.Name;
                    string targetMethod = methodTargets.Method ?? typeLevelMethod ?? method.Name;

                    result.Add(new Patch(label, targetType, targetMethod, kind, type.FullName ?? type.Name));
                }
            }

            // 路径 C：手写 Patch 调用
            foreach (var type in AllTypes(asm.MainModule.Types))
            {
                foreach (var method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    var found = ManualPatches(method);
                    foreach (var t in found)
                        result.Add(new Patch(label, t.Type, t.Method, "Manual", type.FullName ?? type.Name));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 找 `Harmony.Patch(...)` 调用点，往前回溯同一基本块里最近的目标线索。
    /// 线索优先级：ldtoken 方法引用（最准）→ ldtoken 类型 + ldstr 方法名（AccessTools.Method(typeof(T), "M")）。
    /// 这是静态推断：拿不准的就不报，避免制造假冲突。
    /// </summary>
    private static List<(string Type, string Method)> ManualPatches(MethodDefinition method)
    {
        var hits = new List<(string, string)>();
        var instructions = method.Body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var ins = instructions[i];
            if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
            if (ins.Operand is not MethodReference mr) continue;
            if (mr.Name != "Patch" || mr.DeclaringType?.Name != "Harmony") continue;

            string methodRef = null;   // ldtoken 出来的方法
            string typeRef = null;     // ldtoken 出来的类型
            string nameStr = null;     // 紧随其后的字符串

            for (int j = Math.Max(0, i - 16); j < i; j++)
            {
                var prev = instructions[j];
                if (prev.OpCode == OpCodes.Ldtoken)
                {
                    if (prev.Operand is MethodReference tokenMethod) methodRef = Short(tokenMethod.DeclaringType) + "::" + tokenMethod.Name;
                    else if (prev.Operand is TypeReference tokenType) typeRef = Short(tokenType);
                }
                else if (prev.OpCode == OpCodes.Ldstr && prev.Operand is string s)
                {
                    nameStr = s;
                }
            }

            if (methodRef != null)
            {
                var parts = methodRef.Split(new[] { "::" }, StringSplitOptions.None);
                hits.Add((parts[0], parts[1]));
            }
            else if (typeRef != null && nameStr != null)
            {
                hits.Add((typeRef, nameStr));
            }
        }
        return hits;
    }

    private static void Diagnose(string path)
    {
        if (!File.Exists(path)) { Console.WriteLine($"缺失 {path}"); return; }
        using var asm = AssemblyDefinition.ReadAssembly(path);
        int typesWithPatchAttr = 0;
        int methodsWithPatchAttr = 0;
        int conventional = 0;
        int patchCalls = 0;
        foreach (var type in AllTypes(asm.MainModule.Types))
        {
            if (type.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch")) typesWithPatchAttr++;
            foreach (var m in type.Methods)
            {
                if (m.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch")) methodsWithPatchAttr++;
                if (m.Name.StartsWith("Prefix") || m.Name.StartsWith("Postfix") || m.Name.StartsWith("Transpiler")) conventional++;
                if (!m.HasBody) continue;
                patchCalls += m.Body.Instructions.Count(i =>
                    (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                    i.Operand is MethodReference imr && imr.Name == "Patch" && imr.DeclaringType?.Name == "Harmony");
            }
        }
        // 额外：看看补丁是怎么声明的（特性名 / 被调的 Patch 方法）
        var patchAttrs = new Dictionary<string,int>();
        var patchCallsByName = new Dictionary<string,int>();
        foreach (var type in AllTypes(asm.MainModule.Types))
        {
            foreach (var a in type.CustomAttributes)
                if (a.AttributeType.Name.Contains("Patch", StringComparison.OrdinalIgnoreCase))
                    patchAttrs[a.AttributeType.Name] = patchAttrs.GetValueOrDefault(a.AttributeType.Name) + 1;
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if ((ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt) && ins.Operand is MethodReference cmr && cmr.Name.Contains("Patch", StringComparison.OrdinalIgnoreCase))
                    {
                        string k = (cmr.DeclaringType?.Name ?? "?") + "." + cmr.Name;
                        patchCallsByName[k] = patchCallsByName.GetValueOrDefault(k) + 1;
                    }
                }
            }
        }
        // diag3：所有特性名 + 补丁类名样本，用来判断"目标是怎么写出来的"
        var allAttrs = new Dictionary<string,int>();
        var patchClassNames = new List<string>();
        foreach (var t in AllTypes(asm.MainModule.Types))
        {
            foreach (var a in t.CustomAttributes)
                allAttrs[a.AttributeType.Name] = allAttrs.GetValueOrDefault(a.AttributeType.Name) + 1;
            foreach (var m in t.Methods)
            {
                foreach (var a in m.CustomAttributes)
                    allAttrs[a.AttributeType.Name] = allAttrs.GetValueOrDefault(a.AttributeType.Name) + 1;
            }
            if (t.Methods.Any(m => m.Name.StartsWith("Prefix") || m.Name.StartsWith("Postfix")))
                patchClassNames.Add(t.FullName ?? t.Name);
        }
        Console.WriteLine("   全部特性: " + string.Join(", ", allAttrs.OrderByDescending(kv => kv.Value).Take(14).Select(kv => kv.Key + "×" + kv.Value)));
        Console.WriteLine("   补丁类样本(" + patchClassNames.Count + "): " + string.Join(" | ", patchClassNames.Take(8)));
        // diag4：挑 3 个补丁类，看它们的特性参数（目标是不是写在特性里）
        int shown = 0;
        foreach (var t in AllTypes(asm.MainModule.Types))
        {
            if (!t.Methods.Any(m => m.Name.StartsWith("Prefix") || m.Name.StartsWith("Postfix"))) continue;
            if (shown++ >= 3) break;
            Console.WriteLine("   --- " + t.FullName);
            foreach (var a in t.CustomAttributes)
                Console.WriteLine("       [类] " + a.AttributeType.Name + "(" + string.Join(", ", a.ConstructorArguments.Select(x => x.Value)) + ")");
            foreach (var m in t.Methods.Where(m => m.Name.StartsWith("Prefix") || m.Name.StartsWith("Postfix")))
            {
                foreach (var a in m.CustomAttributes)
                    Console.WriteLine("       [方法] " + m.Name + " -> " + a.AttributeType.Name + "(" + string.Join(", ", a.ConstructorArguments.Select(x => x.Value)) + ")");
            }
        }
        if (patchAttrs.Count > 0)
            Console.WriteLine("   特性含 Patch: " + string.Join(", ", patchAttrs.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + "×" + kv.Value)));
        if (patchCallsByName.Count > 0)
            Console.WriteLine("   被调含 Patch: " + string.Join(", ", patchCallsByName.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + "×" + kv.Value)));
        Console.WriteLine($"{Path.GetFileName(path)}: 类级 HarmonyPatch {typesWithPatchAttr} / 方法级 {methodsWithPatchAttr} / 约定名方法 {conventional} / Harmony.Patch 调用点 {patchCalls}");
    }

    /// <summary>
    /// 补丁方法判定。两条路都要走：显式特性（[HarmonyPrefix] 等），
    /// 以及 Harmony 的**命名约定** —— 类上有 [HarmonyPatch(...)] 时，方法只要叫 Prefix / Postfix /
    /// Transpiler / Finalizer（含 Prefix1、Postfix2 这种带序号的重载）就自动算补丁方法。
    /// </summary>
    private static string PatchKind(MethodDefinition method, bool typeHasPatchAttribute)
    {
        foreach (var a in method.CustomAttributes)
        {
            string n = a.AttributeType.Name;
            if (n is "HarmonyPrefix") return "Prefix";
            if (n is "HarmonyPostfix") return "Postfix";
            if (n is "HarmonyTranspiler") return "Transpiler";
            if (n is "HarmonyFinalizer") return "Finalizer";
            if (n is "HarmonyReversePatch") return "ReversePatch";
        }

        bool methodHasPatchAttribute = method.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch");
        if (!typeHasPatchAttribute && !methodHasPatchAttribute) return null;

        string name = method.Name;
        if (name.StartsWith("Prefix", StringComparison.Ordinal)) return "Prefix";
        if (name.StartsWith("Postfix", StringComparison.Ordinal)) return "Postfix";
        if (name.StartsWith("Transpiler", StringComparison.Ordinal)) return "Transpiler";
        if (name.StartsWith("Finalizer", StringComparison.Ordinal)) return "Finalizer";
        if (name.StartsWith("ReversePatch", StringComparison.Ordinal)) return "ReversePatch";
        return null;
    }

    /// <summary>从 HarmonyPatch 特性里抽目标类型名与方法名；MethodType.Constructor 记作 .ctor。</summary>
    private static (string Type, string Method) Collect(IEnumerable<CustomAttribute> attributes)
    {
        string type = null;
        string method = null;
        foreach (var a in attributes)
        {
            if (a.AttributeType.Name != "HarmonyPatch") continue;
            foreach (var arg in a.ConstructorArguments)
            {
                if (arg.Value is TypeReference tr)
                {
                    type ??= Short(tr);
                }
                else if (arg.Value is string s)
                {
                    method ??= s;
                }
                else if (arg.Value is int i)
                {
                    // MethodType 枚举：Constructor=1 / Getter=2 / Setter=3 ...
                    method ??= i switch { 1 => ".ctor", 2 => "get_", 3 => "set_", _ => null };
                }
            }
        }
        return (type, method);
    }

    private static string Short(TypeReference tr)
    {
        // 泛型实例（如 Action<int>）统一去掉参数，只要能对上号
        if (tr is GenericInstanceType git) return git.ElementType.Name;
        return tr.Name;
    }

    /// <summary>把 `AchievementsScreen_Show_Patch` 拆成 ("AchievementsScreen", "Show")；方法名里带下划线时按最后一个下划线切。</summary>
    private static (string Type, string Method) SplitFikaClassName(string className)
    {
        string core = className.EndsWith("_Patch", StringComparison.Ordinal) ? className[..^"_Patch".Length] : className;
        int cut = core.LastIndexOf('_');
        if (cut <= 0) return (core, "?");
        return (core[..cut], core[(cut + 1)..]);
    }

    /// <summary>Fika 的补丁方法：优先看它自己的三个空特性，退回命名约定（PrefixChange 这类也常见）。</summary>
    private static string FikaPatchKind(MethodDefinition method)
    {
        foreach (var a in method.CustomAttributes)
        {
            switch (a.AttributeType.Name)
            {
                case "PatchPrefixAttribute": return "Prefix";
                case "PatchPostfixAttribute": return "Postfix";
                case "PatchTranspilerAttribute": return "Transpiler";
                case "PatchFinalizerAttribute": return "Finalizer";
            }
        }

        string n = method.Name;
        if (n.StartsWith("Prefix", StringComparison.Ordinal)) return "Prefix";
        if (n.StartsWith("Postfix", StringComparison.Ordinal)) return "Postfix";
        if (n.StartsWith("Transpiler", StringComparison.Ordinal)) return "Transpiler";
        if (n.StartsWith("Finalizer", StringComparison.Ordinal)) return "Finalizer";
        return null;
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var t in types)
        {
            yield return t;
            foreach (var n in AllTypes(t.NestedTypes)) yield return n;
        }
    }
}
