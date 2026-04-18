// DromadSpy.cs — standalone console companion for DromaDiag
//
// Build:
//   dotnet publish -c Release -o "<game>\Modding\DromadSpy\"
//
// Usage (called by DromaDiag automatically):
//   DromadSpy.exe decompile  <Assembly-CSharp.dll> <TypeName> <MethodName>
//   DromadSpy.exe refs       <Assembly-CSharp.dll> <TypeName> <MethodName>
//   DromadSpy.exe typeinfo   <Assembly-CSharp.dll> <TypeName>

using System;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace DromadSpy
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: DromadSpy <command> <assembly.dll> [TypeName] [MethodName]");
                Console.Error.WriteLine("Commands: decompile | refs | typeinfo");
                return 1;
            }

            string command  = args[0].ToLowerInvariant();
            string dllPath  = args[1];
            string typeName = args.Length > 2 ? args[2] : "";
            string methName = args.Length > 3 ? args[3] : "";

            if (!File.Exists(dllPath))
            {
                Console.Error.WriteLine("DLL not found: " + dllPath);
                return 2;
            }

            try
            {
                switch (command)
                {
                    case "decompile": return RunDecompile(dllPath, typeName, methName);
                    case "refs":      return RunRefs(dllPath, typeName, methName);
                    case "typeinfo":  return RunTypeInfo(dllPath, typeName);
                    default:
                        Console.Error.WriteLine("Unknown command: " + command);
                        return 3;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal: " + ex);
                return 99;
            }
        }

        // ── decompile ─────────────────────────────────────────────────────────
        // Decompiles the named method(s) to readable C# via ICSharpCode.Decompiler.

        static int RunDecompile(string dllPath, string typeName, string methName)
        {
            var decompiler = MakeDecompiler(dllPath);
            var types      = FindTypes(decompiler.TypeSystem, typeName);

            if (types.Length == 0)
            {
                Console.WriteLine("No type found matching: " + typeName);
                return 0;
            }

            int dumped = 0;
            foreach (var t in types)
            {
                var methods = t.Methods
                    .Where(m => string.Equals(m.Name, methName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (methods.Length == 0)
                {
                    Console.WriteLine("No method '" + methName + "' on " + t.FullName);
                    continue;
                }

                foreach (var method in methods)
                {
                    Console.WriteLine("=== " + t.FullName + "." + method.Name + BuildSig(method) + " ===");
                    Console.WriteLine();

                    try
                    {
                        // DecompileAsString is the cleanest correct API — returns C# directly.
                        string src = decompiler.DecompileAsString(method.MetadataToken);
                        Console.WriteLine(src);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("  [decompile error: " + ex.Message + "]");
                        Console.WriteLine();
                    }

                    dumped++;
                }
            }

            if (dumped == 0)
                Console.WriteLine("Nothing decompiled for " + typeName + "." + methName);

            return 0;
        }

        // ── refs ──────────────────────────────────────────────────────────────
        // Uses the ICSharpCode IL disassembler to produce a resolved reference
        // listing: every field-read/write, method-call, and type-reference
        // inside the named method, with real symbolic names.

        static int RunRefs(string dllPath, string typeName, string methName)
        {
            var decompiler = MakeDecompiler(dllPath);
            var types      = FindTypes(decompiler.TypeSystem, typeName);

            if (types.Length == 0)
            {
                Console.WriteLine("No type found matching: " + typeName);
                return 0;
            }

            using var peFile = new PEFile(dllPath);

            int found = 0;
            foreach (var t in types)
            {
                var methods = t.Methods
                    .Where(m => string.Equals(m.Name, methName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var method in methods)
                {
                    Console.WriteLine("=== refs in " + t.FullName + "." + method.Name + BuildSig(method) + " ===");
                    Console.WriteLine();

                    try
                    {
                        // Disassemble to IL text — operands are fully resolved to
                        // symbolic names by ICSharpCode's own resolver.
                        var sw     = new StringWriter();
                        var output = new PlainTextOutput(sw);
                        var disasm = new ReflectionDisassembler(output, default);

                        var mdHandle = (System.Reflection.Metadata.MethodDefinitionHandle)method.MetadataToken;
                        disasm.DisassembleMethod(peFile, mdHandle);

                        // Filter to only the cross-reference instructions.
                        string raw = sw.ToString();
                        foreach (string line in raw.Split('\n'))
                        {
                            string trimmed = line.Trim();
                            if (IsRefLine(trimmed))
                                Console.WriteLine("  " + trimmed);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Fallback: emit the full IL disassembly unfiltered.
                        Console.WriteLine("  [ref filter failed (" + ex.Message + "), full IL follows]");
                        Console.WriteLine();
                        try
                        {
                            var sw2     = new StringWriter();
                            var output2 = new PlainTextOutput(sw2);
                            var disasm2 = new ReflectionDisassembler(output2, default);
                            var mdHandle2 = (System.Reflection.Metadata.MethodDefinitionHandle)method.MetadataToken;
                            disasm2.DisassembleMethod(peFile, mdHandle2);
                            Console.WriteLine(sw2.ToString());
                        }
                        catch (Exception ex2)
                        {
                            Console.WriteLine("  [IL disassembly also failed: " + ex2.Message + "]");
                        }
                    }

                    Console.WriteLine();
                    found++;
                }
            }

            if (found == 0)
                Console.WriteLine("Nothing found for " + typeName + "." + methName);

            return 0;
        }

        // Returns true for IL lines that carry a meaningful cross-reference.
        static bool IsRefLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            int colon = line.IndexOf(':');
            string op = colon >= 0 ? line.Substring(colon + 1).TrimStart() : line;

            string[] prefixes = {
                "call ", "callvirt ", "newobj ", "ldfld ", "ldflda ",
                "stfld ", "ldsfld ", "ldsflda ", "stsfld ",
                "castclass ", "isinst ", "box ", "unbox ", "newarr ",
                "initobj ", "constrained. "
            };
            foreach (var p in prefixes)
                if (op.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // ── typeinfo ──────────────────────────────────────────────────────────

        static int RunTypeInfo(string dllPath, string typeName)
        {
            var decompiler = MakeDecompiler(dllPath);
            var types      = FindTypes(decompiler.TypeSystem, typeName);

            if (types.Length == 0)
            {
                Console.WriteLine("No type found matching: " + typeName);
                return 0;
            }

            foreach (var t in types)
            {
                Console.WriteLine("TYPE: " + t.FullName);
                if (t.DirectBaseTypes.Any())
                    Console.WriteLine("BASE: " + string.Join(", ", t.DirectBaseTypes.Select(b => b.FullName)));
                Console.WriteLine();

                ITypeDefinition cur = t;
                while (cur != null)
                {
                    Console.WriteLine("────── " + cur.FullName + " ──────");

                    Console.WriteLine("  [methods]");
                    foreach (var m in cur.Methods.Where(m => m.DeclaringTypeDefinition == cur))
                        Console.WriteLine("    " + m.ReturnType.Name + " " + m.Name + BuildSig(m));

                    Console.WriteLine("  [fields]");
                    foreach (var f in cur.Fields.Where(f => f.DeclaringTypeDefinition == cur))
                        Console.WriteLine("    " + (f.IsStatic ? "static " : "") + f.Type.Name + " " + f.Name);

                    Console.WriteLine("  [properties]");
                    foreach (var p in cur.Properties.Where(p => p.DeclaringTypeDefinition == cur))
                        Console.WriteLine("    " + p.ReturnType.Name + " " + p.Name);

                    Console.WriteLine();

                    var baseType = cur.DirectBaseTypes
                        .Select(b => b.GetDefinition())
                        .FirstOrDefault(b => b != null && b.FullName != "System.Object");
                    cur = baseType;
                }
                Console.WriteLine();
            }

            return 0;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static CSharpDecompiler MakeDecompiler(string dllPath)
        {
            var settings = new DecompilerSettings
            {
                ThrowOnAssemblyResolveErrors = false,
                AnonymousMethods = true,
                ExpressionTrees  = false,
                YieldReturn      = true,
                AsyncAwait       = true,
            };
            return new CSharpDecompiler(dllPath, settings);
        }

        static ITypeDefinition[] FindTypes(ICompilation ts, string name)
        {
            string lower = name.ToLowerInvariant();
            var all = ts.MainModule.TypeDefinitions;

            var exact = all.Where(t => string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (exact.Length > 0) return exact;

            var byShort = all.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (byShort.Length > 0) return byShort;

            return all.Where(t => t.FullName.ToLowerInvariant().Contains(lower)).ToArray();
        }

        static string BuildSig(IMethod m)
        {
            if (m.Parameters.Count == 0) return "()";
            return "(" + string.Join(", ", m.Parameters.Select(p => p.Type.Name + " " + p.Name)) + ")";
        }
    }
}
