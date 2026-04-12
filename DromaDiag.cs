using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using XRL.Wish;

// =============================================================================
//  DromaDiag — standalone Caves of Qud modding diagnostic tool
//
//  Wish commands (use your wish key in-game):
//
//    dromadiag:type <TypeName>
//      Dumps every method and field on the named type and its full base-class
//      hierarchy. Partial name match supported (e.g. "InventoryAndEquipment").
//
//    dromadiag:find <MethodOrFieldName>
//      Finds every type in Assembly-CSharp that DECLARES a method or field
//      with exactly this name. Useful for locating where a method lives when
//      AccessTools returns null.
//
//    dromadiag:live <TypeName>
//      Finds a live MonoBehaviour instance via FindObjectsOfType, then dumps
//      all field names and their current runtime values. Open the relevant
//      screen before running this command.
//
//    dromadiag:rect <TypeName>
//      Finds a live MonoBehaviour instance and dumps RectTransform layout data
//      for every Component field on the instance: anchoredPosition, sizeDelta,
//      rect (width/height), anchorMin, anchorMax, offsetMin, offsetMax, and
//      pivot. Also recurses into UITextSkin fields to dump their inner
//      GameObject's RectTransform. Open the relevant screen before running.
//      Use this to understand UI layout before making positional/size changes.
//
//    dromadiag:search <substring>
//      Searches all type names, method names, and field names in
//      Assembly-CSharp for a case-insensitive substring match. Good for
//      exploring unknown APIs.
//
//    dromadiag:harmony
//      Lists every Harmony patch currently applied in the game, grouped by
//      patched method. Shows which mod owns each patch.
//
//  All output is written to:
//    C:\Users\<you>\AppData\LocalLow\Freehold Games\CavesOfQud\DromaDiag.txt
//  and a confirmation message appears in-game.
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

            // Split on commas to support chained commands, e.g.:
            //   dromadiag:type AutoAct,dromadiag:find FindAutoexploreStep
            // Each token may optionally include a leading "dromadiag:" prefix.
            string[] tokens = rest.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            // Single-token fast-path — original behaviour, no shared buffer.
            if (tokens.Length <= 1)
                return DispatchOne(rest, null, null);

            // Multi-command path — accumulate all output into one shared buffer.
            var shared = new StringBuilder();
            shared.AppendLine("=== DromaDiag batch: " + tokens.Length + " command(s) ===");
            shared.AppendLine();

            var msgs = new System.Collections.Generic.List<string>();
            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                // Strip leading "dromadiag:" prefix if the user included it per-token.
                if (token.StartsWith("dromadiag:", StringComparison.OrdinalIgnoreCase))
                    token = token.Substring("dromadiag:".Length).Trim();

                DispatchOne(token, shared, msgs);
            }

            // Write the combined buffer once — individual commands never overwrite it.
            Write(shared.ToString());
            Msg("DromaDiag: batch done (" + tokens.Length + " cmd(s)) — " +
                string.Join(" | ", msgs.ToArray()));
            return true;
        }

        /// <summary>
        /// Dispatch a single subcommand token (the part after "dromadiag:").
        /// When <paramref name="shared"/> is null the method writes its own file and
        /// sends its own in-game message (single-command mode). When non-null it
        /// appends to the shared buffer and pushes a short status onto
        /// <paramref name="msgs"/> (batch mode).
        /// </summary>
        private static bool DispatchOne(string token,
                                        StringBuilder shared,
                                        System.Collections.Generic.List<string> msgs)
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

            // No recognised subcommand — print help.
            string help =
                "DromaDiag commands:\n" +
                "  dromadiag:type <TypeName>    — hierarchy, methods, fields\n" +
                "  dromadiag:find <n>           — find declaring type for method/field\n" +
                "  dromadiag:live <TypeName>    — dump live field values (open screen first)\n" +
                "  dromadiag:rect <TypeName>    — dump RectTransform layout (open screen first)\n" +
                "  dromadiag:search <text>      — search all names for substring\n" +
                "  dromadiag:harmony            — list all active Harmony patches\n" +
                "  Tip: chain commands with commas, e.g.:\n" +
                "    dromadiag:find Foo,dromadiag:type Bar\n";

            if (shared != null) shared.AppendLine(help);
            else { Write(help); Msg("DromaDiag: see DromaDiag.txt for command list."); }
            return true;
        }
        // ── dromadiag:type ────────────────────────────────────────────────────

        private static bool RunTypeCmd(string typeName, StringBuilder shared, System.Collections.Generic.List<string> msgs)
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
                if (t.BaseType != null)
                    sb.AppendLine("BASE: " + t.BaseType.FullName);
                sb.AppendLine();

                // Walk the full hierarchy
                var cur = t;
                while (cur != null && cur != typeof(object))
                {
                    sb.AppendLine("────── " + cur.FullName + " ──────");

                    sb.AppendLine("  [methods]");
                    foreach (var m in cur.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        var parms = m.GetParameters();
                        var pstr  = parms.Length == 0 ? "()" :
                            "(" + string.Join(", ", Array.ConvertAll(parms,
                                p => p.ParameterType.Name + " " + p.Name)) + ")";
                        sb.AppendLine("    " + m.ReturnType.Name + " " + m.Name + pstr);
                    }

                    sb.AppendLine("  [fields]");
                    foreach (var f in cur.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        string prefix = f.IsStatic ? "static " : "";
                        sb.AppendLine("    " + prefix + f.FieldType.Name + " " + f.Name);
                    }

                    sb.AppendLine("  [properties]");
                    foreach (var p in cur.GetProperties(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        sb.AppendLine("    " + p.PropertyType.Name + " " + p.Name);
                    }

                    sb.AppendLine();
                    cur = cur.BaseType;
                }

                sb.AppendLine();
            }

            WriteOrAppend(sb, shared, msgs, "type '" + typeName + "': " + matches.Length + " match(es)");
            return true;
        }

        // ── dromadiag:find ────────────────────────────────────────────────────

        private static bool RunFindCmd(string name, StringBuilder shared, System.Collections.Generic.List<string> msgs)
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

                    foreach (var m in t.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        if (m.Name == name)
                        {
                            var parms = m.GetParameters();
                            var pstr  = parms.Length == 0 ? "()" :
                                "(" + string.Join(", ", Array.ConvertAll(parms,
                                    p => p.ParameterType.Name + " " + p.Name)) + ")";
                            inner.AppendLine("  method: " + m.ReturnType.Name + " " + m.Name + pstr);
                            found = true;
                        }
                    }

                    foreach (var f in t.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        if (f.Name == name)
                        {
                            inner.AppendLine("  field:  " + f.FieldType.Name + " " + f.Name);
                            found = true;
                        }
                    }

                    if (found)
                    {
                        sb.AppendLine(t.FullName);
                        sb.Append(inner);
                        sb.AppendLine();
                        hits++;
                    }
                }
                catch { }
            }

            if (hits == 0) sb.AppendLine("No type declares '" + name + "'.");
            WriteOrAppend(sb, shared, msgs, "find '" + name + "': " + hits + " result(s)");
            return true;
        }

        // ── dromadiag:live ────────────────────────────────────────────────────

        private static bool RunLiveCmd(string typeName, StringBuilder shared, System.Collections.Generic.List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:live " + typeName + " ===");
            sb.AppendLine();

            var matches = FindTypes(typeName);
            if (matches.Length == 0)
            {
                sb.AppendLine("No type found matching: " + typeName);
                WriteOrAppend(sb, shared, msgs, "live '" + typeName + "': no type match");
                return true;
            }

            foreach (var t in matches)
            {
                var instances = GameObject.FindObjectsByType(t, UnityEngine.FindObjectsSortMode.None);
                sb.AppendLine("TYPE: " + t.FullName);
                sb.AppendLine("Live instances: " + instances.Length);
                sb.AppendLine();

                if (instances.Length == 0) continue;

                // Dump field values for each instance
                for (int i = 0; i < instances.Length; i++)
                {
                    var inst = instances[i];
                    sb.AppendLine("--- Instance " + i + " ---");

                    var cur = inst.GetType();
                    while (cur != null && cur != typeof(object))
                    {
                        foreach (var f in cur.GetFields(
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            try
                            {
                                object val = f.GetValue(inst);
                                string valStr = FormatValue(val);
                                sb.AppendLine("  " + f.FieldType.Name + " " + f.Name + " = " + valStr);
                            }
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

        private static bool RunRectCmd(string typeName, StringBuilder shared, System.Collections.Generic.List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:rect " + typeName + " ===");
            sb.AppendLine();

            var matches = FindTypes(typeName);
            if (matches.Length == 0)
            {
                sb.AppendLine("No type found matching: " + typeName);
                WriteOrAppend(sb, shared, msgs, "rect '" + typeName + "': no type match");
                return true;
            }

            foreach (var t in matches)
            {
                var instances = GameObject.FindObjectsByType(t, UnityEngine.FindObjectsSortMode.None);
                sb.AppendLine("TYPE: " + t.FullName);
                sb.AppendLine("Live instances: " + instances.Length);
                sb.AppendLine();

                if (instances.Length == 0) continue;

                // Only dump first instance to keep output manageable
                var inst = instances[0];
                sb.AppendLine("--- Instance 0 (first only) ---");
                sb.AppendLine();

                // Dump the instance's own RectTransform first
                var selfComp = inst as Component;
                if (selfComp != null)
                {
                    var selfRt = selfComp.GetComponent<RectTransform>();
                    if (selfRt != null)
                    {
                        sb.AppendLine("[self]");
                        AppendRect(sb, selfRt);
                        sb.AppendLine();
                    }
                }

                // Walk all fields and dump RectTransform for any Component fields
                var cur = inst.GetType();
                while (cur != null && cur != typeof(object))
                {
                    foreach (var f in cur.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            object val = f.GetValue(inst);
                            if (val == null) continue;

                            // Direct Component field
                            if (val is Component comp)
                            {
                                RectTransform rt = comp.GetComponent<RectTransform>();
                                if (rt != null)
                                {
                                    sb.AppendLine("[" + f.Name + "  (" + f.FieldType.Name + ")]");
                                    AppendRect(sb, rt);
                                    sb.AppendLine();
                                }
                            }
                            // Array of Components (e.g. UITextSkin[])
                            else if (val is Array arr)
                            {
                                for (int i = 0; i < arr.Length; i++)
                                {
                                    var elem = arr.GetValue(i);
                                    if (elem is Component elemComp)
                                    {
                                        RectTransform rt = elemComp.GetComponent<RectTransform>();
                                        if (rt != null)
                                        {
                                            sb.AppendLine("[" + f.Name + "[" + i + "]  (" + f.FieldType.Name + ")]");
                                            AppendRect(sb, rt);
                                            sb.AppendLine();
                                        }
                                    }
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
            sb.AppendLine("  rect (w/h)       : " + rt.rect.width.ToString("F1") + " x " + rt.rect.height.ToString("F1"));
            sb.AppendLine("  anchorMin        : " + rt.anchorMin);
            sb.AppendLine("  anchorMax        : " + rt.anchorMax);
            sb.AppendLine("  offsetMin        : " + rt.offsetMin);
            sb.AppendLine("  offsetMax        : " + rt.offsetMax);
            sb.AppendLine("  pivot            : " + rt.pivot);
        }

        // ── dromadiag:search ──────────────────────────────────────────────────

        private static bool RunSearchCmd(string query, StringBuilder shared, System.Collections.Generic.List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:search " + query + " ===");
            sb.AppendLine();

            var asm = GetGameAssembly();
            if (asm == null) { sb.AppendLine("Assembly-CSharp not found."); WriteOrAppend(sb, shared, msgs, "search: asm not found"); return true; }

            string q = query.ToLowerInvariant();
            int hits = 0;

            foreach (var t in asm.GetTypes())
            {
                try
                {
                    bool typeMatch = t.FullName != null &&
                                     t.FullName.ToLowerInvariant().Contains(q);
                    var inner = new StringBuilder();

                    if (typeMatch)
                        inner.AppendLine("  [type match]");

                    foreach (var m in t.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        if (m.Name.ToLowerInvariant().Contains(q))
                            inner.AppendLine("  method: " + m.Name);
                    }

                    foreach (var f in t.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly))
                    {
                        if (f.Name.ToLowerInvariant().Contains(q))
                            inner.AppendLine("  field:  " + f.FieldType.Name + " " + f.Name);
                    }

                    if (inner.Length > 0)
                    {
                        sb.AppendLine(t.FullName);
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

        private static bool RunHarmonyCmd(StringBuilder shared, System.Collections.Generic.List<string> msgs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== dromadiag:harmony ===");
            sb.AppendLine();

            try
            {
                // HarmonyLib.Harmony.GetAllPatchedMethods() returns IEnumerable<MethodBase>
                var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony") ??
                                  Type.GetType("HarmonyLib.Harmony");
                if (harmonyType == null)
                {
                    sb.AppendLine("HarmonyLib.Harmony type not found.");
                    Write(sb.ToString());
                    return true;
                }

                var getAllPatched = harmonyType.GetMethod("GetAllPatchedMethods",
                    BindingFlags.Public | BindingFlags.Static);
                if (getAllPatched == null)
                {
                    sb.AppendLine("GetAllPatchedMethods not found.");
                    Write(sb.ToString());
                    return true;
                }

                var getPatchInfo = harmonyType.GetMethod("GetPatchInfo",
                    BindingFlags.Public | BindingFlags.Static);

                var patchedMethods = getAllPatched.Invoke(null, null) as IEnumerable;
                if (patchedMethods == null)
                {
                    sb.AppendLine("No patched methods found.");
                    Write(sb.ToString());
                    return true;
                }

                int count = 0;
                foreach (MethodBase method in patchedMethods)
                {
                    sb.AppendLine((method.DeclaringType?.FullName ?? "?") + "." + method.Name);

                    if (getPatchInfo != null)
                    {
                        object patchInfo = getPatchInfo.Invoke(null, new object[] { method });
                        if (patchInfo != null)
                        {
                            DumpPatches(sb, patchInfo, "Prefixes",  "prefixes");
                            DumpPatches(sb, patchInfo, "Postfixes", "postfixes");
                            DumpPatches(sb, patchInfo, "Transpilers","transpilers");
                        }
                    }

                    sb.AppendLine();
                    count++;
                }

                sb.AppendLine("Total patched methods: " + count);
            }
            catch (Exception ex)
            {
                sb.AppendLine("Error: " + ex.Message);
            }

            WriteOrAppend(sb, shared, msgs, "harmony: done");
            return true;
        }

        private static void DumpPatches(StringBuilder sb, object patchInfo,
                                        string label, string fieldName)
        {
            try
            {
                var f = patchInfo.GetType().GetField(fieldName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (f == null) return;
                var list = f.GetValue(patchInfo) as IEnumerable;
                if (list == null) return;

                foreach (var patch in list)
                {
                    var ownerF = patch.GetType().GetField("owner",
                        BindingFlags.Public | BindingFlags.Instance);
                    var methodF = patch.GetType().GetField("PatchMethod",
                        BindingFlags.Public | BindingFlags.Instance);
                    string owner  = ownerF?.GetValue(patch) as string ?? "?";
                    string method = (methodF?.GetValue(patch) as MethodInfo)?.Name ?? "?";
                    sb.AppendLine("  [" + label.TrimEnd('s') + "] " + owner + " -> " + method);
                }
            }
            catch { }
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

            var results = new System.Collections.Generic.List<Type>();
            string lower = typeName.ToLowerInvariant();

            // Exact full name first
            foreach (var t in asm.GetTypes())
                if (string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    results.Add(t);

            // Then short name exact match
            if (results.Count == 0)
                foreach (var t in asm.GetTypes())
                    if (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                        results.Add(t);

            // Then partial name match
            if (results.Count == 0)
                foreach (var t in asm.GetTypes())
                    if ((t.FullName ?? "").ToLowerInvariant().Contains(lower))
                        results.Add(t);

            return results.ToArray();
        }

        private static string FormatValue(object val)
        {
            if (val == null) return "null";
            if (val is string s)
            {
                if (s.Length > 120) s = s.Substring(0, 120) + "...";
                return "\"" + s + "\"";
            }
            if (val is IList list)
                return "[" + list.GetType().Name + " Count=" + list.Count + "]";
            if (val is IDictionary dict)
                return "[" + dict.GetType().Name + " Count=" + dict.Count + "]";
            if (val is Array arr)
                return "[" + val.GetType().Name + " Length=" + arr.Length + "]";

            string str = val.ToString();
            if (str.Length > 120) str = str.Substring(0, 120) + "...";
            return str;
        }

        /// <summary>
        /// Batch mode: append <paramref name="sb"/> to <paramref name="shared"/> and
        /// record a status string. Single-command mode (shared == null): write to disk
        /// and send an in-game message.
        /// </summary>
        private static void WriteOrAppend(StringBuilder sb,
                                          StringBuilder shared,
                                          System.Collections.Generic.List<string> msgs,
                                          string status)
        {
            if (shared != null)
            {
                shared.Append(sb);
                shared.AppendLine();
                if (msgs != null) msgs.Add(status);
            }
            else
            {
                Write(sb.ToString());
                Msg("DromaDiag: " + status + ".");
            }
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
