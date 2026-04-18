using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using UnityEngine;
using XRL.Wish;

// =============================================================================
//  DromaDiag — standalone Caves of Qud modding diagnostic tool
//
//  Wish commands:
//
//    dromadiag:type <TypeName>
//      Dumps every method and field on the named type and its full base-class
//      hierarchy. Partial name match supported.
//
//    dromadiag:find <MethodOrFieldName>
//      Finds every type in Assembly-CSharp that DECLARES a method or field
//      with exactly this name.
//
//    dromadiag:live <TypeName>
//      Finds live MonoBehaviour instances and dumps all field values.
//      Open the relevant screen before running.
//
//    dromadiag:rect <TypeName>
//      Dumps RectTransform layout data for a live UI component.
//      Open the relevant screen before running.
//
//    dromadiag:search <substring>
//      Searches all type/method/field names for a case-insensitive substring.
//
//    dromadiag:harmony
//      Lists every active Harmony patch grouped by patched method.
//
//    dromadiag:callers <TypeName> <MethodName>
//      Scans IL of every method in Assembly-CSharp and finds all methods that
//      call the named method. Shows full caller signature and the call site.
//      Example: dromadiag:callers AutoAct FindAutoexploreStep
//
//    dromadiag:stack <TypeName> <MethodName>
//      Installs a temporary Harmony postfix on the named method that captures
//      and writes a full stack trace to DromaDiag.txt the next time it is called.
//      The patch removes itself after the first capture.
//      Example: dromadiag:stack AutoAct FindAutoexploreStep
//
//    dromadiag:body <TypeName> <MethodName>
//      Disassembles the IL of the named method and writes a human-readable
//      opcode listing. Useful for understanding control flow without dnSpy.
//      Example: dromadiag:body AutoAct FindAutoexploreStep
//
//    dromadiag:decompile <TypeName> <MethodName>
//      Pipes Assembly-CSharp.dll through DromadSpy.exe (ICSharpCode.Decompiler)
//      and writes fully decompiled C# source for the named method.
//      Requires DromadSpy.exe — see DROMADIAG_DROMADSPY_PATH or default location.
//      Example: dromadiag:decompile AutoAct FindAutoexploreStep
//
//    dromadiag:refs <TypeName> <MethodName>
//      Like dromadiag:body but uses DromadSpy.exe to produce a fully resolved
//      reference listing: every field-read, field-write, method-call, and
//      type-reference in the method body, with real names not raw tokens.
//      Example: dromadiag:refs AutoAct FindAutoexploreStep
//
//  DromadSpy.exe — the out-of-process decompiler companion:
//    Place DromadSpy.exe (and its deps) in one of these locations (checked in order):
//      1. Path set by env var  DROMADIAG_DROMADSPY_PATH
//      2. <game>\Modding\DromadSpy\DromadSpy.exe
//      3. Same folder as Assembly-CSharp.dll
//    Build it from DromadSpy\ with: dotnet publish -c Release
//
//  All output is written to:
//    C:\Users\<you>\AppData\LocalLow\Freehold Games\CavesOfQud\DromaDiag.txt
//  A green confirmation message also appears in-game.
// =============================================================================

namespace DromaDiag
{
    [HasWishCommand]
    public class DromaDiag
    {
        private static readonly string OutputPath =
            Path.Combine(Application.persistentDataPath, "DromaDiag.txt");

        // ── Entry point ───────────────────────────────────────────────────────

        [WishCommand(Command = "dromadiag")]
        public static bool Handle(string rest)
        {
            rest = (rest ?? "").Trim();

            string[] tokens = rest.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length <= 1)
                return DispatchOne(rest, null, null);

            var shared = new StringBuilder();
            shared.AppendLine("=== DromaDiag batch: " + tokens.Length + " command(s) ===");
            shared.AppendLine();

            var msgs = new List<string>();
            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (token.StartsWith("dromadiag:", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("dromadiag:".Length).Trim();
                DispatchOne(token, shared, msgs);
            }

            Write(shared.ToString());
            Msg("DromaDiag: batch done (" + tokens.Length + " cmd(s)) — " +
                string.Join(" | ", msgs.ToArray()));
            return true;
        }

        private static bool DispatchOne(string token, StringBuilder shared, List<string> msgs)
        {
            if (token.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
                return RunTypeCmd(token.Substring(5).Trim(), shared, msgs);
            if (token.StartsWith("find ", StringComparison.OrdinalIgnoreCase))
                return RunFindCmd(token.Substring(5).Trim(), shared, msgs);
            if (token.StartsWith("live ", StringComparison.OrdinalIgnoreCase))
                return RunLiveCmd(token.Substring(5).Trim(), shared, msgs);
            if (token.StartsWith("rect ", StringComparison.OrdinalIgnoreCase))
                return RunRectCmd(token.Substring(5).Trim(), shared, msgs);
            if (token.StartsWith("search ", StringComparison.OrdinalIgnoreCase))
                return RunSearchCmd(token.Substring(7).Trim(), shared, msgs);
            if (token.Equals("harmony", StringComparison.OrdinalIgnoreCase))
                return RunHarmonyCmd(shared, msgs);
            if (token.StartsWith("callers ", StringComparison.OrdinalIgnoreCase))
                return RunCallersCmd(token.Substring(8).Trim(), shared, msgs);
            if (token.StartsWith("stack ", StringComparison.OrdinalIgnoreCase))
                return RunStackCmd(token.Substring(6).Trim(), shared, msgs);
            if (token.StartsWith("body ", StringComparison.OrdinalIgnoreCase))
                return RunBodyCmd(token.Substring(5).Trim(), shared, msgs);
            if (token.StartsWith("decompile ", StringComparison.OrdinalIgnoreCase))
                return RunILSpyCmd("decompile", token.Substring(10).Trim(), shared, msgs);
            if (token.StartsWith("refs ", StringComparison.OrdinalIgnoreCase))
                return RunILSpyCmd("refs", token.Substring(5).Trim(), shared, msgs);

            string help =
                "DromaDiag commands:\n" +
                "  dromadiag:type <TypeName>              — hierarchy, methods, fields\n" +
                "  dromadiag:find <Name>                  — find declaring type for method/field\n" +
                "  dromadiag:live <TypeName>              — dump live field values (open screen first)\n" +
                "  dromadiag:rect <TypeName>              — dump RectTransform layout (open screen first)\n" +
                "  dromadiag:search <text>                — search all names for substring\n" +
                "  dromadiag:harmony                      — list all active Harmony patches\n" +
                "  dromadiag:callers <TypeName> <Method>  — find all IL callers of a method\n" +
                "  dromadiag:stack <TypeName> <Method>    — capture stack trace on next call\n" +
                "  dromadiag:body <TypeName> <Method>     — disassemble IL body (built-in)\n" +
                "  dromadiag:decompile <TypeName> <Method>— decompile to C# via DromadSpy.exe\n" +
                "  dromadiag:refs <TypeName> <Method>     — fully resolved ref listing via DromadSpy.exe\n" +
                "  Tip: chain with commas.\n";

            if (shared != null) shared.AppendLine(help);
            else { Write(help); Msg("DromaDiag: see DromaDiag.txt for command list."); }
            return true;
        }

        // ── dromadiag:type ────────────────────────────────────────────────────

        private static bool RunTypeCmd(string typeName, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:type " + typeName + " ===");
            sb.AppendLine();

            var matches = FindTypes(typeName);
            if (matches.Length == 0)
            {
                sb.AppendLine("No type found matching: " + typeName);
                WriteOrAppend(sb, shared, msgs, "type '" + typeName + "': no match");
                return true;
            }

            foreach (var t in matches)
            {
                sb.AppendLine("TYPE: " + t.FullName);
                if (t.BaseType != null) sb.AppendLine("BASE: " + t.BaseType.FullName);
                sb.AppendLine();

                var cur = t;
                while (cur != null && cur != typeof(object))
                {
                    sb.AppendLine("────── " + cur.FullName + " ──────");
                    sb.AppendLine("  [methods]");
                    foreach (var m in cur.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        var parms = m.GetParameters();
                        var pstr = parms.Length == 0 ? "()" :
                            "(" + string.Join(", ", Array.ConvertAll(parms,
                                p => p.ParameterType.Name + " " + p.Name)) + ")";
                        sb.AppendLine("    " + m.ReturnType.Name + " " + m.Name + pstr);
                    }
                    sb.AppendLine("  [fields]");
                    foreach (var f in cur.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        string pfx = f.IsStatic ? "static " : "";
                        sb.AppendLine("    " + pfx + f.FieldType.Name + " " + f.Name);
                    }
                    sb.AppendLine("  [properties]");
                    foreach (var p in cur.GetProperties(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        sb.AppendLine("    " + p.PropertyType.Name + " " + p.Name);
                    sb.AppendLine();
                    cur = cur.BaseType;
                }
                sb.AppendLine();
            }

            WriteOrAppend(sb, shared, msgs, "type '" + typeName + "': " + matches.Length + " match(es)");
            return true;
        }

        // ── dromadiag:find ────────────────────────────────────────────────────

        private static bool RunFindCmd(string name, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:find " + name + " ===");
            sb.AppendLine();

            var asm = GetGameAssembly();
            if (asm == null) { sb.AppendLine("Assembly-CSharp not found."); WriteOrAppend(sb, shared, msgs, "find: asm not found"); return true; }

            int hits = 0;
            foreach (var t in asm.GetTypes())
            {
                try
                {
                    bool found = false;
                    var inner = new StringBuilder();
                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (m.Name == name)
                        {
                            var parms = m.GetParameters();
                            var pstr = parms.Length == 0 ? "()" :
                                "(" + string.Join(", ", Array.ConvertAll(parms,
                                    p => p.ParameterType.Name + " " + p.Name)) + ")";
                            inner.AppendLine("  method: " + m.ReturnType.Name + " " + m.Name + pstr);
                            found = true;
                        }
                    }
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        if (f.Name == name)
                        {
                            inner.AppendLine("  field:  " + f.FieldType.Name + " " + f.Name);
                            found = true;
                        }
                    }
                    if (found) { sb.AppendLine(t.FullName); sb.Append(inner); sb.AppendLine(); hits++; }
                }
                catch { }
            }

            if (hits == 0) sb.AppendLine("No type declares '" + name + "'.");
            WriteOrAppend(sb, shared, msgs, "find '" + name + "': " + hits + " result(s)");
            return true;
        }

        // ── dromadiag:live ────────────────────────────────────────────────────

        private static bool RunLiveCmd(string typeName, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:live " + typeName + " ===");
            sb.AppendLine();

            var matches = FindTypes(typeName);
            if (matches.Length == 0) { sb.AppendLine("No type found matching: " + typeName); WriteOrAppend(sb, shared, msgs, "live: no match"); return true; }

            foreach (var t in matches)
            {
                var instances = GameObject.FindObjectsByType(t, FindObjectsSortMode.None);
                sb.AppendLine("TYPE: " + t.FullName);
                sb.AppendLine("Live instances: " + instances.Length);
                sb.AppendLine();
                if (instances.Length == 0) continue;

                for (int i = 0; i < instances.Length; i++)
                {
                    var inst = instances[i];
                    sb.AppendLine("--- Instance " + i + " ---");
                    var cur = inst.GetType();
                    while (cur != null && cur != typeof(object))
                    {
                        foreach (var f in cur.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            try { sb.AppendLine("  " + f.FieldType.Name + " " + f.Name + " = " + FormatValue(f.GetValue(inst))); }
                            catch { sb.AppendLine("  " + f.Name + " = <error reading value>"); }
                        }
                        cur = cur.BaseType;
                    }
                    sb.AppendLine();
                }
            }

            WriteOrAppend(sb, shared, msgs, "live '" + typeName + "': done");
            return true;
        }

        // ── dromadiag:rect ────────────────────────────────────────────────────

        private static bool RunRectCmd(string typeName, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:rect " + typeName + " ===");
            sb.AppendLine();

            var matches = FindTypes(typeName);
            if (matches.Length == 0) { sb.AppendLine("No type found matching: " + typeName); WriteOrAppend(sb, shared, msgs, "rect: no match"); return true; }

            foreach (var t in matches)
            {
                var instances = GameObject.FindObjectsByType(t, FindObjectsSortMode.None);
                sb.AppendLine("TYPE: " + t.FullName);
                sb.AppendLine("Live instances: " + instances.Length);
                sb.AppendLine();
                if (instances.Length == 0) continue;

                var inst = instances[0];
                sb.AppendLine("--- Instance 0 (first only) ---");
                sb.AppendLine();

                var selfComp = inst as Component;
                if (selfComp != null)
                {
                    var selfRt = selfComp.GetComponent<RectTransform>();
                    if (selfRt != null) { sb.AppendLine("[self]"); AppendRect(sb, selfRt); sb.AppendLine(); }
                }

                var cur = inst.GetType();
                while (cur != null && cur != typeof(object))
                {
                    foreach (var f in cur.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            object val = f.GetValue(inst);
                            if (val == null) continue;
                            if (val is Component comp)
                            {
                                var rt = comp.GetComponent<RectTransform>();
                                if (rt != null) { sb.AppendLine("[" + f.Name + "  (" + f.FieldType.Name + ")]"); AppendRect(sb, rt); sb.AppendLine(); }

                                // Recurse into UITextSkin
                                var skinType = val.GetType();
                                if (skinType.Name.Contains("UITextSkin"))
                                {
                                    var goField = skinType.GetField("gameObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (goField != null)
                                    {
                                        var go = goField.GetValue(val) as GameObject;
                                        if (go != null)
                                        {
                                            var innerRt = go.GetComponent<RectTransform>();
                                            if (innerRt != null) { sb.AppendLine("[" + f.Name + ".gameObject (UITextSkin inner)]"); AppendRect(sb, innerRt); sb.AppendLine(); }
                                        }
                                    }
                                }
                            }
                            else if (val is Component[] arr)
                            {
                                for (int i = 0; i < arr.Length; i++)
                                {
                                    if (arr[i] == null) continue;
                                    var rt = arr[i].GetComponent<RectTransform>();
                                    if (rt != null) { sb.AppendLine("[" + f.Name + "[" + i + "]  (" + f.FieldType.Name + ")]"); AppendRect(sb, rt); sb.AppendLine(); }
                                }
                            }
                        }
                        catch { }
                    }
                    cur = cur.BaseType;
                }
            }

            WriteOrAppend(sb, shared, msgs, "rect '" + typeName + "': done");
            return true;
        }

        private static void AppendRect(StringBuilder sb, RectTransform rt)
        {
            sb.AppendLine("  anchoredPosition : " + rt.anchoredPosition);
            sb.AppendLine("  sizeDelta        : " + rt.sizeDelta);
            sb.AppendLine("  rect (w/h)       : " + rt.rect.width + " x " + rt.rect.height);
            sb.AppendLine("  anchorMin        : " + rt.anchorMin);
            sb.AppendLine("  anchorMax        : " + rt.anchorMax);
            sb.AppendLine("  offsetMin        : " + rt.offsetMin);
            sb.AppendLine("  offsetMax        : " + rt.offsetMax);
            sb.AppendLine("  pivot            : " + rt.pivot);
        }

        // ── dromadiag:search ──────────────────────────────────────────────────

        private static bool RunSearchCmd(string query, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:search " + query + " ===");
            sb.AppendLine();

            var asm = GetGameAssembly();
            if (asm == null) { sb.AppendLine("Assembly-CSharp not found."); WriteOrAppend(sb, shared, msgs, "search: asm not found"); return true; }

            string lower = query.ToLowerInvariant();
            int hits = 0;

            foreach (var t in asm.GetTypes())
            {
                try
                {
                    bool typeMatch = (t.FullName ?? "").ToLowerInvariant().Contains(lower);
                    var inner = new StringBuilder();

                    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (m.Name.ToLowerInvariant().Contains(lower))
                            inner.AppendLine("  method: " + m.Name);

                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (f.Name.ToLowerInvariant().Contains(lower))
                            inner.AppendLine("  field:  " + f.FieldType.Name + " " + f.Name);

                    if (typeMatch || inner.Length > 0)
                    {
                        sb.AppendLine(t.FullName);
                        if (typeMatch) sb.AppendLine("  [type match]");
                        sb.Append(inner);
                        sb.AppendLine();
                        hits++;
                    }
                }
                catch { }
            }

            if (hits == 0) sb.AppendLine("No matches for '" + query + "'.");
            WriteOrAppend(sb, shared, msgs, "search '" + query + "': " + hits + " type(s)");
            return true;
        }

        // ── dromadiag:harmony ─────────────────────────────────────────────────

        private static bool RunHarmonyCmd(StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:harmony ===");
            sb.AppendLine();

            try
            {
                var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony") ??
                                  Type.GetType("HarmonyLib.Harmony");
                if (harmonyType == null) { sb.AppendLine("HarmonyLib.Harmony type not found."); WriteOrAppend(sb, shared, msgs, "harmony: lib not found"); return true; }

                var getAllPatched = harmonyType.GetMethod("GetAllPatchedMethods", BindingFlags.Public | BindingFlags.Static);
                var getPatchInfo  = harmonyType.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static);
                if (getAllPatched == null) { sb.AppendLine("GetAllPatchedMethods not found."); WriteOrAppend(sb, shared, msgs, "harmony: method not found"); return true; }

                var patchedMethods = getAllPatched.Invoke(null, null) as IEnumerable;
                if (patchedMethods == null) { sb.AppendLine("No patched methods found."); WriteOrAppend(sb, shared, msgs, "harmony: none"); return true; }

                int count = 0;
                foreach (MethodBase method in patchedMethods)
                {
                    sb.AppendLine((method.DeclaringType?.FullName ?? "?") + "." + method.Name);
                    if (getPatchInfo != null)
                    {
                        object patchInfo = getPatchInfo.Invoke(null, new object[] { method });
                        if (patchInfo != null)
                        {
                            DumpPatches(sb, patchInfo, "Prefix",     "prefixes");
                            DumpPatches(sb, patchInfo, "Postfix",    "postfixes");
                            DumpPatches(sb, patchInfo, "Transpiler", "transpilers");
                        }
                    }
                    sb.AppendLine();
                    count++;
                }
                sb.AppendLine("Total patched methods: " + count);
            }
            catch (Exception ex) { sb.AppendLine("Error: " + ex.Message); }

            WriteOrAppend(sb, shared, msgs, "harmony: done");
            return true;
        }

        private static void DumpPatches(StringBuilder sb, object patchInfo, string label, string fieldName)
        {
            try
            {
                var f = patchInfo.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) return;
                var list = f.GetValue(patchInfo) as IEnumerable;
                if (list == null) return;
                foreach (var patch in list)
                {
                    var ownerF  = patch.GetType().GetField("owner",       BindingFlags.Public | BindingFlags.Instance);
                    var methodF = patch.GetType().GetField("PatchMethod",  BindingFlags.Public | BindingFlags.Instance);
                    string owner  = ownerF?.GetValue(patch) as string ?? "?";
                    string method = (methodF?.GetValue(patch) as MethodInfo)?.Name ?? "?";
                    sb.AppendLine("  [" + label + "] " + owner + " -> " + method);
                }
            }
            catch { }
        }

        // ── dromadiag:callers ─────────────────────────────────────────────────
        // Scans the IL of every method in Assembly-CSharp and reports any method
        // that contains a call instruction targeting the specified method.

        private static bool RunCallersCmd(string arg, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:callers " + arg + " ===");
            sb.AppendLine();

            // Parse "TypeName MethodName"
            int space = arg.IndexOf(' ');
            if (space < 0)
            {
                sb.AppendLine("Usage: dromadiag:callers <TypeName> <MethodName>");
                WriteOrAppend(sb, shared, msgs, "callers: bad args"); return true;
            }
            string typeName   = arg.Substring(0, space).Trim();
            string methodName = arg.Substring(space + 1).Trim();

            var asm = GetGameAssembly();
            if (asm == null) { sb.AppendLine("Assembly-CSharp not found."); WriteOrAppend(sb, shared, msgs, "callers: asm not found"); return true; }

            // Find all overloads of the target method.
            var targets = new HashSet<int>(); // metadata tokens
            var targetTypes = FindTypes(typeName);
            if (targetTypes.Length == 0) { sb.AppendLine("No type matching: " + typeName); WriteOrAppend(sb, shared, msgs, "callers: type not found"); return true; }

            foreach (var tt in targetTypes)
                foreach (var m in tt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (m.Name == methodName)
                        targets.Add(m.MetadataToken);

            if (targets.Count == 0) { sb.AppendLine("No methods named '" + methodName + "' on " + typeName); WriteOrAppend(sb, shared, msgs, "callers: method not found"); return true; }

            sb.AppendLine("Searching for callers of " + typeName + "." + methodName + " (" + targets.Count + " overload(s))...");
            sb.AppendLine();

            int callerCount = 0;
            foreach (var t in asm.GetTypes())
            {
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        var body = m.GetMethodBody();
                        if (body == null) continue;
                        byte[] il = body.GetILAsByteArray();
                        if (il == null) continue;

                        var calledTokens = ExtractCallTokens(il);
                        bool calls = false;
                        foreach (int tok in calledTokens)
                        {
                            // Resolve the token to a MethodBase in this module.
                            try
                            {
                                var resolved = t.Module.ResolveMethod(tok, null, null);
                                if (resolved != null && targets.Contains(resolved.MetadataToken))
                                { calls = true; break; }
                            }
                            catch { }
                        }

                        if (calls)
                        {
                            var parms = m.GetParameters();
                            var pstr  = parms.Length == 0 ? "()" :
                                "(" + string.Join(", ", Array.ConvertAll(parms,
                                    p => p.ParameterType.Name + " " + p.Name)) + ")";
                            sb.AppendLine(t.FullName + "." + m.Name + pstr);
                            callerCount++;
                        }
                    }
                    catch { }
                }
            }

            sb.AppendLine();
            sb.AppendLine("Total callers found: " + callerCount);
            WriteOrAppend(sb, shared, msgs, "callers: " + callerCount + " found");
            return true;
        }

        // Walks raw IL bytes and returns all metadata tokens from call/callvirt instructions.
        private static List<int> ExtractCallTokens(byte[] il)
        {
            var tokens = new List<int>();
            int i = 0;
            while (i < il.Length)
            {
                int opSize   = 1;
                int operand  = 0;
                bool isCall  = false;

                byte b = il[i];

                // Two-byte opcodes start with 0xFE
                if (b == 0xFE && i + 1 < il.Length)
                {
                    i += 2; // skip two-byte opcode, no call operand
                    continue;
                }

                switch (b)
                {
                    case 0x28: // call
                    case 0x6F: // callvirt
                    case 0x73: // newobj
                    case 0x70: // cpobj - not a call but safe to skip with 4-byte operand
                        isCall = true;
                        if (i + 4 < il.Length)
                            operand = BitConverter.ToInt32(il, i + 1);
                        opSize = 5;
                        break;
                    // Variable-length instructions — rough skip table
                    case 0x20: case 0x21: case 0x22: case 0x23: opSize = 5; break; // ldc.i4 etc
                    case 0x2A: opSize = 1; break; // ret
                    case 0x2B: case 0x2C: opSize = 2; break; // br.s, brfalse.s etc
                    case 0x38: case 0x39: case 0x3A: case 0x3B:
                    case 0x3C: case 0x3D: case 0x3E: case 0x3F:
                    case 0x40: case 0x41: case 0x42: case 0x43:
                    case 0x44: case 0x45: opSize = 5; break; // br, brtrue, etc
                    case 0x7B: case 0x7C: case 0x7D: case 0x7E:
                    case 0x7F: opSize = 5; break; // ldfld, ldflda etc
                    case 0x80: opSize = 5; break; // stfld
                    case 0x1A: case 0x1B: case 0x1C: case 0x1D:
                    case 0x1E: case 0x1F: opSize = 1; break; // ldc.i4.0..8
                    default:   opSize = 1; break;
                }

                if (isCall && operand != 0)
                    tokens.Add(operand);

                i += opSize;
            }
            return tokens;
        }

        // ── dromadiag:stack ───────────────────────────────────────────────────
        // Installs a one-shot Harmony postfix that captures a stack trace on the
        // next call to the named method, then removes itself.

        // Tracks installed one-shot stack patches so we don't double-install.
        private static readonly HashSet<string> _stackPatchKeys = new HashSet<string>();

        private static bool RunStackCmd(string arg, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:stack " + arg + " ===");
            sb.AppendLine();

            int space = arg.IndexOf(' ');
            if (space < 0)
            {
                sb.AppendLine("Usage: dromadiag:stack <TypeName> <MethodName>");
                WriteOrAppend(sb, shared, msgs, "stack: bad args"); return true;
            }
            string typeName   = arg.Substring(0, space).Trim();
            string methodName = arg.Substring(space + 1).Trim();

            var targetTypes = FindTypes(typeName);
            if (targetTypes.Length == 0) { sb.AppendLine("No type matching: " + typeName); WriteOrAppend(sb, shared, msgs, "stack: type not found"); return true; }

            var harmony   = new Harmony("com.dromadiag.stack." + typeName + "." + methodName);
            string patchKey = typeName + "." + methodName;

            if (_stackPatchKeys.Contains(patchKey))
            {
                sb.AppendLine("Stack patch already pending for " + patchKey + ". Trigger the method to capture it.");
                WriteOrAppend(sb, shared, msgs, "stack: already pending"); return true;
            }

            int patched = 0;
            foreach (var tt in targetTypes)
            {
                foreach (var m in tt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != methodName) continue;
                    try
                    {
                        // We use a closure-based postfix via HarmonyMethod + MethodInfo.
                        // Build a dynamic postfix that captures the harmony instance for self-removal.
                        var postfix = typeof(DromaDiag).GetMethod("_StackTracePostfix",
                            BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(m, postfix: new HarmonyMethod(postfix));
                        patched++;
                    }
                    catch (Exception ex) { sb.AppendLine("  Failed to patch overload: " + ex.Message); }
                }
            }

            if (patched > 0)
            {
                _stackPatchKeys.Add(patchKey);
                // Store context for the postfix to use.
                _pendingStackHarmony   = harmony;
                _pendingStackOutputPath = OutputPath;
                _pendingStackPatchKey  = patchKey;
                sb.AppendLine("Stack trace postfix installed on " + patched + " overload(s) of " + typeName + "." + methodName + ".");
                sb.AppendLine("Trigger the method in-game. Stack trace will be written to DromaDiag.txt automatically.");
            }
            else
            {
                sb.AppendLine("No overloads of '" + methodName + "' found on " + typeName + ".");
            }

            WriteOrAppend(sb, shared, msgs, "stack: " + patched + " overload(s) patched");
            return true;
        }

        // Shared state for the one-shot stack postfix.
        private static Harmony  _pendingStackHarmony;
        private static string   _pendingStackOutputPath;
        private static string   _pendingStackPatchKey;

        // Called by the Harmony postfix — must be static, non-generic, public or accessible.
        private static void _StackTracePostfix()
        {
            try
            {
                string trace = new System.Diagnostics.StackTrace(true).ToString();
                string output =
                    "=== dromadiag:stack capture ===\n\n" +
                    "Captured at: " + DateTime.Now.ToString("HH:mm:ss.fff") + "\n\n" +
                    trace;

                string path = _pendingStackOutputPath ?? OutputPath;
                File.WriteAllText(path, output);
                XRL.Messages.MessageQueue.AddPlayerMessage("&GDromaDiag: stack trace captured — see DromaDiag.txt");
                Debug.Log("[DromaDiag] Stack trace written.");
            }
            catch (Exception ex) { Debug.LogWarning("[DromaDiag] Stack capture failed: " + ex.Message); }
            finally
            {
                // Remove self — one shot.
                try
                {
                    if (_pendingStackHarmony != null)
                    {
                        _pendingStackHarmony.UnpatchAll(_pendingStackHarmony.Id);
                        _pendingStackHarmony = null;
                    }
                    if (_pendingStackPatchKey != null)
                    {
                        _stackPatchKeys.Remove(_pendingStackPatchKey);
                        _pendingStackPatchKey = null;
                    }
                }
                catch { }
            }
        }

        // ── dromadiag:body ────────────────────────────────────────────────────
        // Disassembles the IL body of a method into a human-readable opcode listing.

        private static bool RunBodyCmd(string arg, StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:body " + arg + " ===");
            sb.AppendLine();

            int space = arg.IndexOf(' ');
            if (space < 0)
            {
                sb.AppendLine("Usage: dromadiag:body <TypeName> <MethodName>");
                WriteOrAppend(sb, shared, msgs, "body: bad args"); return true;
            }
            string typeName   = arg.Substring(0, space).Trim();
            string methodName = arg.Substring(space + 1).Trim();

            var targetTypes = FindTypes(typeName);
            if (targetTypes.Length == 0) { sb.AppendLine("No type matching: " + typeName); WriteOrAppend(sb, shared, msgs, "body: type not found"); return true; }

            int dumped = 0;
            foreach (var tt in targetTypes)
            {
                foreach (var m in tt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != methodName) continue;
                    try
                    {
                        var parms = m.GetParameters();
                        var pstr  = parms.Length == 0 ? "()" :
                            "(" + string.Join(", ", Array.ConvertAll(parms,
                                p => p.ParameterType.Name + " " + p.Name)) + ")";
                        sb.AppendLine("── " + tt.FullName + "." + m.Name + pstr + " ──");

                        var body = m.GetMethodBody();
                        if (body == null) { sb.AppendLine("  (no body — abstract or extern)"); sb.AppendLine(); continue; }

                        sb.AppendLine("  MaxStackSize : " + body.MaxStackSize);
                        sb.AppendLine("  LocalVars    : " + body.LocalVariables.Count);
                        foreach (var lv in body.LocalVariables)
                            sb.AppendLine("    [" + lv.LocalIndex + "] " + lv.LocalType.Name + (lv.IsPinned ? " (pinned)" : ""));
                        sb.AppendLine();

                        byte[] il = body.GetILAsByteArray();
                        sb.Append(DisassembleIL(il, m.Module));
                        sb.AppendLine();
                        dumped++;
                    }
                    catch (Exception ex) { sb.AppendLine("  Error disassembling: " + ex.Message); }
                }
            }

            if (dumped == 0) sb.AppendLine("No overloads of '" + methodName + "' found on " + typeName + ".");
            WriteOrAppend(sb, shared, msgs, "body: " + dumped + " overload(s) dumped");
            return true;
        }

        private static string DisassembleIL(byte[] il, Module module)
        {
            // Opcode name table for common opcodes — good enough for control-flow analysis.
            // We focus on call sites, branches, and field accesses which are most useful.
            var sb = new StringBuilder();
            int i = 0;
            while (i < il.Length)
            {
                int offset = i;
                byte b = il[i];

                if (b == 0xFE && i + 1 < il.Length)
                {
                    // Two-byte opcode
                    byte b2 = il[i + 1];
                    string name2 = TwoByteOpName(b2);
                    sb.AppendLine("  IL_" + offset.ToString("X4") + ": " + name2);
                    i += 2;
                    continue;
                }

                string opName = OneByteOpName(b);
                int operandSize = GetOperandSize(b);

                string operandStr = "";
                if (operandSize == 4 && i + 4 < il.Length)
                {
                    int token = BitConverter.ToInt32(il, i + 1);
                    // For call/callvirt/newobj — resolve the method name.
                    if (b == 0x28 || b == 0x6F || b == 0x73)
                    {
                        try
                        {
                            var resolved = module.ResolveMethod(token);
                            if (resolved != null)
                            {
                                var rp = resolved.GetParameters();
                                var rpstr = rp.Length == 0 ? "()" :
                                    "(" + string.Join(", ", Array.ConvertAll(rp,
                                        p => p.ParameterType.Name)) + ")";
                                operandStr = " " + (resolved.DeclaringType?.Name ?? "?") + "." + resolved.Name + rpstr;
                            }
                        }
                        catch
                        {
                            try { operandStr = " token:0x" + token.ToString("X8"); } catch { }
                        }
                    }
                    else if (b == 0x7B || b == 0x7C || b == 0x7D || b == 0x7E || b == 0x7F || b == 0x80)
                    {
                        // Field access — resolve field name.
                        try
                        {
                            var field = module.ResolveField(token);
                            if (field != null)
                                operandStr = " " + (field.DeclaringType?.Name ?? "?") + "." + field.Name;
                        }
                        catch { operandStr = " token:0x" + token.ToString("X8"); }
                    }
                    else
                    {
                        operandStr = " 0x" + token.ToString("X8");
                    }
                }
                else if (operandSize == 1 && i + 1 < il.Length)
                {
                    operandStr = " " + ((sbyte)il[i + 1]).ToString();
                }
                else if (operandSize == 2 && i + 2 < il.Length)
                {
                    operandStr = " " + BitConverter.ToInt16(il, i + 1).ToString();
                }

                sb.AppendLine("  IL_" + offset.ToString("X4") + ": " + opName + operandStr);
                i += 1 + operandSize;
            }
            return sb.ToString();
        }

        private static int GetOperandSize(byte b)
        {
            switch (b)
            {
                case 0x00: return 0; // nop
                case 0x01: return 0; // break
                case 0x02: case 0x03: case 0x04: case 0x05:
                case 0x06: case 0x07: case 0x08: case 0x09:
                case 0x0A: case 0x0B: case 0x0C: case 0x0D:
                case 0x0E: return 1; // ldarg.s, ldloc.s etc
                case 0x10: return 1; // stloc.s
                case 0x11: return 1; // ldloca.s
                case 0x12: return 1; // starg.s (not standard but close)
                case 0x13: return 1; // stloc.s
                case 0x14: return 0; // ldnull
                case 0x15: return 0; // ldc.i4.m1
                case 0x16: case 0x17: case 0x18: case 0x19:
                case 0x1A: case 0x1B: case 0x1C: case 0x1D:
                case 0x1E: return 0; // ldc.i4.0..8
                case 0x1F: return 1; // ldc.i4.s
                case 0x20: return 4; // ldc.i4
                case 0x21: return 8; // ldc.i8
                case 0x22: return 4; // ldc.r4
                case 0x23: return 8; // ldc.r8
                case 0x25: return 0; // dup
                case 0x26: return 0; // pop
                case 0x27: return 4; // jmp
                case 0x28: return 4; // call
                case 0x29: return 4; // calli
                case 0x2A: return 0; // ret
                case 0x2B: return 1; // br.s
                case 0x2C: return 1; // brfalse.s
                case 0x2D: return 1; // brtrue.s
                case 0x2E: case 0x2F: case 0x30: case 0x31:
                case 0x32: case 0x33: case 0x34: case 0x35: return 1; // beq.s..ble.un.s
                case 0x38: return 4; // br
                case 0x39: return 4; // brfalse
                case 0x3A: return 4; // brtrue
                case 0x3B: case 0x3C: case 0x3D: case 0x3E:
                case 0x3F: case 0x40: case 0x41: case 0x42:
                case 0x43: case 0x44: case 0x45: return 4; // beq..ble.un
                case 0x58: case 0x59: case 0x5A: case 0x5B:
                case 0x5C: case 0x5D: case 0x5E: case 0x5F:
                case 0x60: case 0x61: case 0x62: case 0x63:
                case 0x64: case 0x65: return 0; // add..neg
                case 0x6F: return 4; // callvirt
                case 0x73: return 4; // newobj
                case 0x74: return 4; // castclass
                case 0x75: return 4; // isinst
                case 0x79: return 4; // unbox
                case 0x7B: return 4; // ldfld
                case 0x7C: return 4; // ldflda
                case 0x7D: return 4; // stfld
                case 0x7E: return 4; // ldsfld
                case 0x7F: return 4; // ldsflda
                case 0x80: return 4; // stsfld
                case 0x8C: return 4; // box
                case 0x8D: return 4; // newarr
                default:   return 0;
            }
        }

        private static string OneByteOpName(byte b)
        {
            switch (b)
            {
                case 0x00: return "nop";
                case 0x02: return "ldarg.0";   case 0x03: return "ldarg.1";
                case 0x04: return "ldarg.2";   case 0x05: return "ldarg.3";
                case 0x06: return "ldloc.0";   case 0x07: return "ldloc.1";
                case 0x08: return "ldloc.2";   case 0x09: return "ldloc.3";
                case 0x0A: return "stloc.0";   case 0x0B: return "stloc.1";
                case 0x0C: return "stloc.2";   case 0x0D: return "stloc.3";
                case 0x0E: return "ldarg.s";
                case 0x10: return "starg.s";   case 0x11: return "ldloc.s";
                case 0x12: return "ldloca.s";  case 0x13: return "stloc.s";
                case 0x14: return "ldnull";
                case 0x15: return "ldc.i4.m1";
                case 0x16: return "ldc.i4.0";  case 0x17: return "ldc.i4.1";
                case 0x18: return "ldc.i4.2";  case 0x19: return "ldc.i4.3";
                case 0x1A: return "ldc.i4.4";  case 0x1B: return "ldc.i4.5";
                case 0x1C: return "ldc.i4.6";  case 0x1D: return "ldc.i4.7";
                case 0x1E: return "ldc.i4.8";
                case 0x1F: return "ldc.i4.s";  case 0x20: return "ldc.i4";
                case 0x21: return "ldc.i8";    case 0x22: return "ldc.r4";
                case 0x23: return "ldc.r8";
                case 0x25: return "dup";       case 0x26: return "pop";
                case 0x27: return "jmp";
                case 0x28: return "call";      case 0x29: return "calli";
                case 0x2A: return "ret";
                case 0x2B: return "br.s";      case 0x2C: return "brfalse.s";
                case 0x2D: return "brtrue.s";
                case 0x2E: return "beq.s";     case 0x2F: return "bge.s";
                case 0x30: return "bgt.s";     case 0x31: return "ble.s";
                case 0x32: return "blt.s";     case 0x33: return "bne.un.s";
                case 0x34: return "bge.un.s";  case 0x35: return "bgt.un.s";
                case 0x36: return "ble.un.s";  case 0x37: return "blt.un.s";
                case 0x38: return "br";        case 0x39: return "brfalse";
                case 0x3A: return "brtrue";
                case 0x3B: return "beq";       case 0x3C: return "bge";
                case 0x3D: return "bgt";       case 0x3E: return "ble";
                case 0x3F: return "blt";       case 0x40: return "bne.un";
                case 0x58: return "add";       case 0x59: return "sub";
                case 0x5A: return "mul";       case 0x5B: return "div";
                case 0x5F: return "and";       case 0x60: return "or";
                case 0x61: return "xor";       case 0x64: return "neg";
                case 0x65: return "not";
                case 0x6F: return "callvirt";
                case 0x72: return "ldstr";
                case 0x73: return "newobj";
                case 0x74: return "castclass"; case 0x75: return "isinst";
                case 0x79: return "unbox";
                case 0x7B: return "ldfld";     case 0x7C: return "ldflda";
                case 0x7D: return "stfld";     case 0x7E: return "ldsfld";
                case 0x7F: return "ldsflda";   case 0x80: return "stsfld";
                case 0x8C: return "box";       case 0x8D: return "newarr";
                case 0x8E: return "ldlen";
                case 0xA3: return "stelem";
                case 0xA4: return "stelem.ref";
                default:   return "0x" + b.ToString("X2");
            }
        }

        private static string TwoByteOpName(byte b2)
        {
            switch (b2)
            {
                case 0x00: return "arglist";   case 0x01: return "ceq";
                case 0x02: return "cgt";       case 0x03: return "cgt.un";
                case 0x04: return "clt";       case 0x05: return "clt.un";
                case 0x06: return "ldftn";     case 0x07: return "ldvirtftn";
                case 0x09: return "ldarg";     case 0x0A: return "ldarga";
                case 0x0B: return "starg";     case 0x0C: return "ldloc";
                case 0x0D: return "ldloca";    case 0x0E: return "stloc";
                case 0x0F: return "localloc";
                case 0x11: return "endfilter";
                case 0x12: return "unaligned"; case 0x13: return "volatile";
                case 0x14: return "tail";
                case 0x15: return "initobj";   case 0x16: return "constrained";
                case 0x17: return "cpblk";     case 0x18: return "initblk";
                case 0x1A: return "rethrow";
                case 0x1C: return "sizeof";    case 0x1D: return "refanytype";
                case 0x1E: return "readonly";
                default:   return "FE." + b2.ToString("X2");
            }
        }

        // ── dromadiag:decompile / dromadiag:refs ──────────────────────────────
        // Both commands shell out to DromadSpy.exe (the ICSharpCode.Decompiler
        // companion) so the decompiler runs out-of-process against the DLL on
        // disk. This sidesteps Unity/Mono runtime constraints — the companion
        // reads raw PE metadata, not the loaded assembly.
        //
        // DromadSpy.exe is located by checking (in order):
        //   1. Environment variable  DROMADIAG_DROMADSPY_PATH  (full path to exe)
        //   2. <Caves of Qud install>\Modding\DromadSpy\DromadSpy.exe
        //   3. Same directory as Assembly-CSharp.dll

        private static bool RunILSpyCmd(string subCommand, string arg,
            StringBuilder shared, List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:" + subCommand + " " + arg + " ===");
            sb.AppendLine();

            // Parse "TypeName MethodName"
            int space = arg.IndexOf(' ');
            if (space < 0)
            {
                sb.AppendLine("Usage: dromadiag:" + subCommand + " <TypeName> <MethodName>");
                WriteOrAppend(sb, shared, msgs, subCommand + ": bad args"); return true;
            }
            string typeName   = arg.Substring(0, space).Trim();
            string methodName = arg.Substring(space + 1).Trim();

            // Find Assembly-CSharp.dll on disk.
            var gameAsm = GetGameAssembly();
            if (gameAsm == null)
            {
                sb.AppendLine("Assembly-CSharp not loaded — cannot locate DLL on disk.");
                WriteOrAppend(sb, shared, msgs, subCommand + ": asm not found"); return true;
            }

            string dllPath = gameAsm.Location;
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                sb.AppendLine("Assembly-CSharp.Location is empty or missing: '" + dllPath + "'");
                sb.AppendLine("This can happen with dynamic assemblies. Try locating the DLL manually.");
                WriteOrAppend(sb, shared, msgs, subCommand + ": dll not found"); return true;
            }

            // Find DromadSpy.exe.
            string exePath = FindDromadSpyExe(dllPath);
            if (exePath == null)
            {
                sb.AppendLine("DromadSpy.exe not found. Checked:");
                sb.AppendLine("  1. DROMADIAG_DROMADSPY_PATH environment variable");
                sb.AppendLine("  2. <game>\\Modding\\DromadSpy\\DromadSpy.exe");
                sb.AppendLine("  3. " + Path.GetDirectoryName(dllPath) + "\\DromadSpy.exe");
                sb.AppendLine();
                sb.AppendLine("Build DromadSpy.exe from the DromadSpy\\ project:");
                sb.AppendLine("  dotnet publish -c Release -o <one of the paths above>");
                WriteOrAppend(sb, shared, msgs, subCommand + ": DromadSpy.exe not found"); return true;
            }

            // Launch DromadSpy.exe and capture stdout.
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = exePath,
                    Arguments              = QuoteArg(subCommand) + " "
                                           + QuoteArg(dllPath)   + " "
                                           + QuoteArg(typeName)  + " "
                                           + QuoteArg(methodName),
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    sb.Append(stdout);

                    if (!string.IsNullOrEmpty(stderr))
                    {
                        sb.AppendLine();
                        sb.AppendLine("--- DromadSpy stderr ---");
                        sb.AppendLine(stderr);
                    }

                    if (proc.ExitCode != 0)
                        sb.AppendLine("DromadSpy.exe exited with code " + proc.ExitCode);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Failed to launch DromadSpy.exe: " + ex.Message);
            }

            WriteOrAppend(sb, shared, msgs, subCommand + " '" + typeName + "." + methodName + "': done");
            return true;
        }

        private static string FindDromadSpyExe(string dllPath)
        {
            // 1. Explicit env var.
            string env = Environment.GetEnvironmentVariable("DROMADIAG_DROMADSPY_PATH");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;

            // 2. <game>\Modding\DromadSpy\DromadSpy.exe
            //    Application.dataPath points to <game>\<Name>_Data — go up one level.
            string gameRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(gameRoot))
            {
                string candidate = Path.Combine(gameRoot, "Modding", "DromadSpy", "DromadSpy.exe");
                if (File.Exists(candidate)) return candidate;
            }

            // 3. Same folder as the DLL.
            string sibling = Path.Combine(Path.GetDirectoryName(dllPath) ?? "", "DromadSpy.exe");
            if (File.Exists(sibling)) return sibling;

            return null;
        }

        private static string QuoteArg(string s)
        {
            // Simple double-quote wrapping; escape any embedded quotes.
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Assembly GetGameAssembly()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "Assembly-CSharp") return asm;
            return null;
        }

        private static Type[] FindTypes(string typeName)
        {
            var asm = GetGameAssembly();
            if (asm == null) return new Type[0];

            var results = new List<Type>();
            string lower = typeName.ToLowerInvariant();

            foreach (var t in asm.GetTypes())
                if (string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    results.Add(t);

            if (results.Count == 0)
                foreach (var t in asm.GetTypes())
                    if (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        results.Add(t);

            if (results.Count == 0)
                foreach (var t in asm.GetTypes())
                    if ((t.FullName ?? "").ToLowerInvariant().Contains(lower))
                        results.Add(t);

            return results.ToArray();
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is string s) { if (s.Length > 120) s = s.Substring(0, 120) + "..."; return "\"" + s + "\""; }
            if (val is IList list)   return "[" + list.GetType().Name + " Count=" + list.Count + "]";
            if (val is IDictionary dict) return "[" + dict.GetType().Name + " Count=" + dict.Count + "]";
            if (val is Array arr)    return "[" + val.GetType().Name + " Length=" + arr.Length + "]";
            string str = val.ToString();
            if (str.Length > 120) str = str.Substring(0, 120) + "...";
            return str;
        }

        private static void WriteOrAppend(StringBuilder sb, StringBuilder shared, List<string> msgs, string status)
        {
            if (shared != null) { shared.Append(sb); shared.AppendLine(); if (msgs != null) msgs.Add(status); }
            else { Write(sb.ToString()); Msg("DromaDiag: " + status + "."); }
        }

        private static void Write(string content)
        {
            try { File.WriteAllText(OutputPath, content); }
            catch (Exception ex) { Debug.LogWarning("[DromaDiag] Write failed: " + ex.Message); }
        }

        private static void Msg(string text)
        {
            XRL.Messages.MessageQueue.AddPlayerMessage("&G" + text);
            Debug.Log("[DromaDiag] " + text);
        }
    }
}
