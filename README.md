

DromaDiag.cs
cs

I have a tool called dromadiag which uses reflection, and reflection can only see structure (method names, signatures, field names, types). IL method bodies aren't exposed through reflection at all. You'd need a decompiler that reads the raw IL from the dll.

I am wondering if there is a way we can make a new tool  that use the wish system which pipes the dll though a DLL tool like ILspy to get these complied information. So we can grab reflection and complied information.

Apr 15

Viewed 2 files
Viewed 2 files
Now I have a thorough understanding of DromaDiag. Let me think through the architecture for a new dromadiag:ilspy command before writing the code.

Here's the situation and the plan:

What DromaDiag already does well (via reflection + raw IL bytes):

body — manual byte-by-byte IL disassembly (limited opcode coverage)
callers — scans raw IL for call tokens (fragile, no symbolic resolution)
What ILSpy/ICSharpCode.Decompiler adds:

Full C# decompilation of method bodies
Proper token resolution (field refs, method refs, type refs → names)
Complete opcode coverage
Decompiled source that's readable, not raw hex
The cleanest approach for a Caves of Qud mod is to ship ICSharpCode.Decompiler as a bundled DLL and call it from new wish commands at runtime. ILSpy itself is a WPF app, but its decompiler engine (ICSharpCode.Decompiler) is a standalone NuGet package that works fine in Unity/Mono.

Here's the new file with two new commands added — dromadiag:decompile and dromadiag:refs — plus an upgraded body that falls back to the old raw scanner if the decompiler isn't present:


Read the rest of the callers/stack/body commands
Read the rest of the callers/stack/body commands
Good, I've now read the full file. Here's the plan laid out clearly before writing code:

Architecture Plan
The core problem: ICSharpCode.Decompiler (ILSpy's engine) is a .NET Standard library. You can't call it from inside the running game — it needs to run out-of-process against the DLL file on disk.

The approach — a two-part system:

DromaILSpy.exe — a tiny standalone .NET console tool you drop next to your mod. It takes Assembly-CSharp.dll + a command as arguments, runs ICSharpCode.Decompiler, and writes results to stdout.
New wish commands in DromaDiag.cs — dromadiag:decompile and dromadiag:refs — that locate the DLL on disk, shell out to DromaILSpy.exe via System.Diagnostics.Process, capture stdout, and write it to DromaDiag.txt just like the other commands.
This is exactly analogous to how dnSpy/ILSpy themselves work — a separate process reads the DLL cold, resolves tokens from PE metadata, and produces human-readable output. It avoids any Unity/Mono compatibility issues with the decompiler engine itself.

New commands:

dromadiag:decompile <TypeName> <MethodName> — full C# decompilation of a method body
dromadiag:refs <TypeName> <MethodName> — all field refs, method calls, and type refs in a method (more structured than body, fully resolved)

Created 2 files
Created 2 files
Now let's write the updated DromaDiag.cs with the two new commands added cleanly:


Edited 5 files, viewed 2 files, ran a command
Edited 5 files, viewed 2 files, ran a command
Now insert the RunILSpyCmd method and FindILSpyExe helper just before the // ── Helpers ─── section:


Edited a file, ran a command, read a file
Edited a file, ran a command, read a file
Here's what was built and why it works this way:

What was delivered
DromaDiag.cs — updated with two new wish commands and using System.Diagnostics:

dromadiag:decompile AutoAct FindAutoexploreStep — full C# decompilation of the method body
dromadiag:refs AutoAct FindAutoexploreStep — every field-read, field-write, method-call, and type-ref in the body, all fully resolved to names (not raw tokens)
DromaILSpy.cs + DromaILSpy.csproj — the new out-of-process companion tool.

Why out-of-process is the right design
You correctly identified the wall: Unity/Mono's runtime only exposes MethodBody.GetILAsByteArray() — raw bytes, no decompiler. ICSharpCode.Decompiler (ILSpy's engine) reads the DLL cold as a PE file from disk, which means it cannot run inside the Unity process without pulling in a full .NET Standard + Roslyn dependency chain that would conflict with Mono.

The solution is to shell out: DromaDiag finds Assembly-CSharp.dll via gameAsm.Location, then launches DromaILSpy.exe as a child process, captures stdout, and writes it to DromaDiag.txt exactly like every other command.

Setup
# 1. Build the companion (only needs dotnet SDK, not Unity)
cd DromaILSpy\
dotnet publish -c Release -o "<game>\Modding\DromaILSpy\"

# 2. Drop your updated DromaDiag.cs into your mod and rebuild as usual
DromaDiag searches for DromaILSpy.exe in three places (in order): the DROMADIAG_ILSPY_PATH env var, <game>\Modding\DromaILSpy\, and next to Assembly-CSharp.dll. If it's not found it tells you exactly where it looked.


Dromadiag
CS 

Dromailspy
CS 

Dromailspy
CSPROJ 
Restore complete (8.9s)
  DromaILSpy failed with 9 error(s) (0.7s)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(9,7): error CS0246: The type or namespace name 'HarmonyLib' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(10,7): error CS0246: The type or namespace name 'UnityEngine' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(11,7): error CS0246: The type or namespace name 'XRL' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(82,6): error CS0246: The type or namespace name 'HasWishCommandAttribute' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(82,6): error CS0246: The type or namespace name 'HasWishCommand' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(394,58): error CS0246: The type or namespace name 'RectTransform' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(735,24): error CS0246: The type or namespace name 'Harmony' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(90,10): error CS0246: The type or namespace name 'WishCommandAttribute' could not be found (are you missing a using directive or an assembly reference?)
    C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy\DromaDiag.cs(90,10): error CS0246: The type or namespace name 'WishCommand' could not be found (are you missing a using directive or an assembly reference?)
Build failed with 9 error(s) in 10.1s
PS C:\Users\Mur\AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaILSpy>

Show more
1:01 PM
The Qud mod builder is picking up DromaDiag.cs inside the DromaILSpy folder and trying to compile it as part of that project. The two files need to live in completely separate places — DromaILSpy.cs and .csproj are a standalone console app, not a mod.

The fix is to make sure your folder structure is:

Mods\
  DromaDiag\          ← your actual Qud mod folder
    DromaDiag.cs      ← the updated wish command file

DromaILSpy\           ← NOT inside Mods\ at all
  DromaILSpy.cs
  DromaILSpy.csproj
Put DromaILSpy\ somewhere outside the Mods directory — your Desktop, Documents\, or directly under the game root like CavesOfQud\DromaILSpy\. Then from inside that folder run:

dotnet publish -c Release -o "C:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\Modding\DromaILSpy"
(adjust the game path to match yours)

DromaDiag will find the exe there automatically since it checks <game>\Modding\DromaILSpy\DromaILSpy.exe as its second search location. The DromaDiag.cs file itself only goes in your Mods\DromaDiag\ folder like before — the Qud mod builder compiles that one normally using the game's own assemblies.

PS H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy> dotnet publish -c Release -o "H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\Modding\DromaILSpy"
Restore complete (0.3s)
  DromaILSpy failed with 7 error(s) (0.2s)
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(113,74): error CS1503: Argument 1: cannot convert from 'ICSharpCode.Decompiler.PlainTextOutput' to 'System.IO.TextWriter'
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(364,38): error CS0103: The name 'MetadataTokens' does not exist in the current context
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(373,38): error CS0103: The name 'MetadataTokens' does not exist in the current context
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(381,38): error CS0103: The name 'MetadataTokens' does not exist in the current context
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(390,38): error CS0103: The name 'MetadataTokens' does not exist in the current context
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(396,38): error CS0103: The name 'MetadataTokens' does not exist in the current context
    H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy\DromaILSpy.cs(402,38): error CS0103: The name 'MetadataTokens' does not exist in the current context
Build failed with 7 error(s) in 0.9s
PS H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy>

Show more
1:04 PM
Real API mismatches — let me fix all three issues:

PlainTextOutput takes a TextWriter in its constructor, not the other way around
MetadataTokens lives in System.Reflection.Metadata but needs the right using/assembly
The refs command's low-level token walking is more trouble than it's worth when ICSharpCode.Decompiler can resolve everything for us anyway

Read current DromaILSpy.cs to fix it properly
Read current DromaILSpy.cs to fix it properly
The refs command is doing too much work manually when ICSharpCode.Decompiler can do all of it cleanly. I'll rewrite RunRefs to use the decompiler's own type system for token resolution (no raw MetadataTokens API needed), and fix the PlainTextOutput constructor order.


Ran 2 commands, created a file, read a file
Ran 2 commands, created a file, read a file
Three things changed:

1. PlainTextOutput constructor fixed — it takes a TextWriter, so now it's new PlainTextOutput(new StringWriter(sb)) / new PlainTextOutput(sw). The old code had the arguments backwards.

2. MetadataTokens removed entirely — that static class lives in System.Reflection.Metadata and its handle factory methods don't exist on the version pulled by ICSharpCode.Decompiler's transitive deps in net48. The refs command now uses ReflectionDisassembler instead, which is ICSharpCode's own IL printer — it resolves every operand to symbolic names internally, so we get callvirt AutoAct::FindAutoexploreStep(...) output with zero manual token table work.

3. Removed unused using statements — System.Reflection.Metadata, ICSharpCode.Decompiler.CSharp.OutputVisitor, and ICSharpCode.Decompiler.CSharp.Syntax are all gone since the rewritten code doesn't need them.

The .csproj file is unchanged — same net48 target and ICSharpCode.Decompiler 8.2.0.7535 package reference.


Dromailspy
CS 
PS H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy> dotnet publish -c Release -o "H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\Modding\DromaILSpy"
Restore complete (0.2s)
  DromaILSpy failed with 1 error(s) (0.2s) → bin\Release\DromaILSpy.exe
    C:\Program Files\dotnet\sdk\9.0.313\Sdks\Microsoft.NET.Sdk\targets\Microsoft.NET.Publish.targets(236,5): error MSB3191: Unable to create directory "H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\Modding\DromaILSpy\". The filename, directory name, or volume label syntax is incorrect. : 'H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy:\Program Files (x86)\Steam\steamapps\common\Caves of Qud\Modding\DromaILSpy\'.
Build failed with 1 error(s) in 0.7s
PS H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy>

Just to say, the name should be DromadSpy instead of IL spy

Show more
1:07 PM
The output path got mangled — it literally concatenated your current directory with the example path I gave. Just use a clean path. Also renaming to DromadSpy throughout:

First, rename the project files:


Copy and rename files, update internal references
Copy and rename files, update internal references
Now update DromaDiag.cs to reference DromadSpy instead of DromaILSpy:


Ran 2 commands
Ran 2 commands
All clean. Now also update the env var name in DromaDiag.cs since DROMADIAG_ILSPY_PATH should stay as-is (it's a user-facing env var name, fine to keep), but let's check the FindILSpyExe method got renamed too:


Ran 2 commands, read a file
Ran 2 commands, read a file
Everything renamed to DromadSpy throughout. For the publish command, the path issue was that you were running it from inside the DromadSpy folder and PowerShell concatenated the paths. Just use a simple absolute path:

powershell
dotnet publish -c Release -o "H:\SteamLibrary\steamapps\common\Caves of Qud\Modding\DromadSpy"
That's it — one clean path, no colons in the middle. DromaDiag will find DromadSpy.exe there automatically since it checks <game>\Modding\DromadSpy\DromadSpy.exe as its second search location.


Dromadspy
CS 

Dromadspy
CSPROJ 

Dromadiag
CS 
okay built. now what?

PS H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy> dotnet publish -c Release -o "H:\SteamLibrary\steamapps\common\Caves of Qud\Modding\DromadSpy"
Restore complete (0.4s)
  DromadSpy succeeded (0.2s) → H:\SteamLibrary\steamapps\common\Caves of Qud\Modding\DromadSpy\
Build succeeded in 0.9s
PS H:\SteamLibrary\steamapps\common\Caves of Qud\DromaILSpy>

1:11 PM
Built and in the right place. Now:

Replace your mod's DromaDiag.cs with the updated one from the outputs above (the one that has dromadiag:decompile and dromadiag:refs in it). Drop it into your mod folder at AppData\LocalLow\Freehold Games\CavesOfQud\Mods\DromaDiag\ replacing the old file.
Launch Caves of Qud and let the mod rebuild.
Test it with a wish command, for example:
   dromadiag:decompile AutoAct FindAutoexploreStep
Then check DromaDiag.txt in your AppData folder for the decompiled C# output.

If it can't find DromadSpy.exe it'll tell you exactly which paths it checked in the output file, so that's your diagnostic if something goes wrong. The most likely hiccup is that gameAsm.Location returns empty in some Unity builds — if that happens, let me know and we can add a fallback that searches for Assembly-CSharp.dll relative to Application.dataPath instead.

=== DromaDiag: Modding Diagnostic Tool Errors ===
<...>/CavesOfQud/Mods/DromaDiag/DromaDiag.cs(753,17): error CS0104: 'Debug' is an ambiguous reference between 'System.Diagnostics.Debug' and 'UnityEngine.Debug'
<...>/CavesOfQud/Mods/DromaDiag/DromaDiag.cs(755,36): error CS0104: 'Debug' is an ambiguous reference between 'System.Diagnostics.Debug' and 'UnityEngine.Debug'
<...>/CavesOfQud/Mods/DromaDiag/DromaDiag.cs(1246,36): error CS0104: 'Debug' is an ambiguous reference between 'System.Diagnostics.Debug' and 'UnityEngine.Debug'
<...>/CavesOfQud/Mods/DromaDiag/DromaDiag.cs(1252,13): error CS0104: 'Debug' is an ambiguous reference between 'System.Diagnostics.Debug' and 'UnityEngine.Debug'
== Warnings ==
None

Show more
1:12 PM
Adding using System.Diagnostics; introduced an ambiguity with UnityEngine.Debug. Easy fix — just remove that using and fully qualify the one Process call instead.


Remove System.Diagnostics using to fix Debug ambiguity
Remove System.Diagnostics using to fix Debug ambiguity
Now fully qualify the two Process usages:


Edited 2 files, ran a command, read a file
Edited 2 files, ran a command, read a file
Removed using System.Diagnostics; and replaced the two usages with System.Diagnostics.ProcessStartInfo and System.Diagnostics.Process.Start(...) inline. UnityEngine.Debug is unambiguous again.


Dromadiag
CS 
=== dromadiag:decompile AutoAct FindAutoexploreStep ===
=== XRL.World.Capabilities.AutoAct.FindAutoexploreStep(String& Step, Boolean& Blackout) ===
public static void FindAutoexploreStep(out string Step, out bool Blackout)
{
    FindAutoexploreStep(Force: false, out Step, out Blackout);
}
=== XRL.World.Capabilities.AutoAct.FindAutoexploreStep(Boolean Force, String& Step, Boolean& Blackout) ===
public static void FindAutoexploreStep(bool Force, out string Step, out bool Blackout)
{
    Blackout = false;
    Step = ".";
    Zone zone = The.PlayerCell?.ParentZone;
    if (zone == null)
    {
        return;
    }
    if (Force || AutoexploreZone != zone || AutoexploreLastAct != Count)
    {
        AutoexploreZone = zone;
        AutoexploreLastAct = Count;
        AutoexploreObjects.Clear();
        AutoexploreCells.Clear();
        AutoexplorePath.Clear();
        AutoexploreLastTarget = null;
        zone.FloodAutoexplore(AutoexploreCells, AutoexploreObjects);
    }
    Cell autoexploreLastTarget = AutoexploreLastTarget;
    if (autoexploreLastTarget != null && autoexploreLastTarget.ParentZone == zone && (IsAutoexploreCell(autoexploreLastTarget) || autoexploreLastTarget.HasObject(IsAutoexploreObject)) && !autoexploreLastTarget.IsBlackedOut() && TryGetAutoexploreStepToCell(autoexploreLastTarget, out Step))
    {
        return;
    }
    AutoexploreLastTarget = null;
    if (!AutoexploreObjects.IsNullOrEmpty())
    {
        GameObject[] array = AutoexploreObjects.GetArray();
        for (int num = AutoexploreObjects.Count - 1; num >= 0; num--)
        {
            GameObject gameObject = array[num];
            if (!IsAutoexploreObject(gameObject))
            {
                AutoexploreObjects.RemoveAt(num);
            }
            else
            {
                Cell currentCell = gameObject.CurrentCell;
                if (currentCell.IsBlackedOut())
                {
                    Blackout = true;
                }
                else if (currentCell.IsReallyExplored() && TryGetAutoexploreStepToCell(currentCell, out Step))
                {
                    return;
                }
            }
        }
    }
    if (!AutoexploreCells.IsNullOrEmpty())
    {
        Cell[] array2 = AutoexploreCells.GetArray();
        for (int num2 = AutoexploreCells.Count - 1; num2 >= 0; num2--)
        {
            Cell cell = array2[num2];
            if (!IsAutoexploreCell(cell))
            {
                AutoexploreCells.RemoveAt(num2);
            }
            else if (cell.IsBlackedOut())
            {
                Blackout = true;
            }
            else if (TryGetAutoexploreStepToCell(cell, out Step))
            {
                return;
            }
        }
    }
    if (!Force)
    {
        FindAutoexploreStep(Force: true, out Step, out Blackout);
    }
}

Show more
1:13 PM
It works! That's the full decompiled C# body — clean, readable, with real variable names and control flow reconstructed. You can now see exactly what FindAutoexploreStep does:

It lazily caches a flood-fill result (AutoexploreCells / AutoexploreObjects) per zone, invalidating when the zone or action count changes
It tries the last known target cell first before re-scanning
It iterates objects first, then unexplored cells, and sets Blackout = true if a candidate is blacked out but otherwise skips it
If nothing is found on the first pass, it recurses once with Force: true to flush the cache and retry
Try dromadiag:refs AutoAct FindAutoexploreStep too — that'll give you a flat list of every method call and field access inside it, which is handy when you want to know all the touch points without reading the full body.


=== dromadiag:refs AutoAct FindAutoexploreStep === === refs in XRL.World.Capabilities.AutoAct.FindAutoexploreStep(String& Step, Boolean& Blackout) === IL_0003: call void XRL.World.Capabilities.AutoAct::FindAutoexploreStep(bool, string&, bool&) === refs in XRL.World.Capabilities.AutoAct.Fi

pasted

1:14 PM
Both commands working perfectly. The refs output is especially useful here — you can see the full type-qualified signatures that the decompiled C# abbreviates. A few things that stand out from the refs that aren't obvious from the C# alone:

Rack<T> is not a standard collection — it's XRL.Collections.Rack<T> which extends XRL.Collections.Container<T>. The refs show get_Count() is inherited from Container<T> while GetArray(), Clear(), RemoveAt() are on Rack<T> itself. Worth knowing if you're patching anything that touches those collections.

FloodAutoexplore signature is explicit — Zone::FloodAutoexplore(Rack<Cell>, Rack<GameObject>) — so if you want to intercept or replace the flood-fill, that's your Harmony target and you know the exact parameter types.

The predicate at IL_009d — new Predicate<GameObject>(IsAutoexploreObject) — shows that HasObject(IsAutoexploreObject) is allocating a delegate on every call through that branch. If you were optimizing hot paths that's a place to look.

IsReallyExplored() vs IsBlackedOut() are called in sequence on the object path but only IsBlackedOut() is called on the cell path — the cell loop skips the explored check entirely and goes straight to TryGetAutoexploreStepToCell. That asymmetry is invisible in the C# unless you look carefully.

DromadSpy is doing its job. What are you investigating?

How can i deploy this using steam workshop? I would need the user to 

Can you upgrade dromadiag so that exports are copied to the clipboard?

Can you put them up on github? https://github.com/Muramas/DromaDiag

Will the base of dromadiag work even if the user didn't have the decompiler setup? Like if the decompiler doesn't work it tells them instructions on how to set it up so the rest of the tool still works?

1:18 PM
Let me take these one at a time:

Steam Workshop — DromaDiag is a C# mod, so it deploys through Caves of Qud's built-in mod loader, not Steam Workshop directly. Qud mods on the Workshop are just zip-packaged mod folders. You'd need to set up a Workshop item via the Qud mod manager or manually through Steamworks. The catch is DromadSpy.exe is a separate compiled binary that can't live inside the mod folder itself — Workshop mods are code-only. You'd need to either ship DromadSpy as a pre-built release on GitHub and have users download it separately, or document the build step. That's actually the natural split: mod on Workshop, DromadSpy binary as a GitHub release.

GitHub — I can't push to your repo directly, but I can give you exactly what to run. Do you want me to also write a README.md that documents all the commands, the DromadSpy setup, and the Workshop install flow?

Base tool without decompiler — yes, already works that way. RunILSpyCmd checks for DromadSpy.exe first and if it's missing writes a friendly message to DromaDiag.txt listing exactly where it looked and how to build it. All the other commands (type, find, live, rect, search, harmony, callers, stack, body) have zero dependency on DromadSpy and will always work.

Clipboard — on it now:


Viewed 2 files, ran 2 commands, edited a file
Viewed 2 files, ran 2 commands, edited a file
Good. Now write the README and prepare the GitHub file structure:


Created a file, ran a command, read a file
Created a file, ran a command, read a file
Here's a summary of everything:

Clipboard — Write() now calls GUIUtility.systemCopyBuffer after every file write. The in-game confirmation message says (copied to clipboard). Batch mode copies the combined output too.

GitHub — push this structure:

DromaDiag/DromaDiag.cs
DromadSpy/DromadSpy.cs
DromadSpy/DromadSpy.csproj
README.md
Then go to the Releases tab and upload a built DromadSpy.zip (the contents of the publish output folder) so users don't need the .NET SDK.

Steam Workshop — Qud has a built-in Workshop uploader in the mod manager (Mods menu in-game → select your mod → Upload). The mod folder itself is self-contained. Just make sure your mod folder has a manifest.json — if you don't have one already, let me know and I'll generate it. DromadSpy stays as a separate GitHub release download since Workshop can't ship arbitrary executables.

Base tool without DromadSpy — confirmed already works. All 9 non-decompiler commands are fully independent. If someone runs decompile or refs without it, they get a clear message in DromaDiag.txt listing exactly where it looked and the exact dotnet publish command to run.


Dromadiag
CS 

Readme
Document · MD 
Do i only need the files in the bin folder or also the obj folder?

1:22 PM
Only the bin folder — specifically the publish output, which in your case is:

H:\SteamLibrary\steamapps\common\Caves of Qud\Modding\DromadSpy\
That's what dotnet publish already put there. The obj folder is just intermediate build artifacts (compiled temps, NuGet restore caches) — it's never needed at runtime and shouldn't be distributed.

So for the GitHub release zip, just zip up the contents of that Modding\DromadSpy\ folder — the .exe, .dll dependencies, and .runtimeconfig.json. That's everything a user needs to drop in place.

Can you write a release page for the dromadspy zip file

1:23 PM

Created a file, ran a command, read a file
Created a file, ran a command, read a file
Paste the contents of that into the GitHub release description box when you create the release. The flow on GitHub is:

Go to your repo → Releases → Draft a new release
Create a tag like dromadspy-v1.0.0
Paste the release notes in the description box
Attach DromadSpy.zip as the release asset
Publish

Release
Document · MD 
Can you make a full changelog of what was done to dromadiag?

1:31 PM

Viewed a file, created a file, ran a command
Viewed a file, created a file, ran a command
Structured as two versions:

v1.0.0 — documents everything that was in the original file you shared: all 9 commands, batch mode, and output behaviour
v1.1.0 — documents everything added in this session: decompile, refs, DromadSpy, clipboard copy, and the Debug ambiguity fix
You can drop CHANGELOG.md into the repo root alongside README.md. If you want to use it as the body of the main GitHub release for the mod itself (separate from the DromadSpy release), it works for that too.


Changelog
Document · MD 
Can you make the github page for dromadiag

2:06 PM
I can't push to GitHub directly, but I can tell you exactly what to set up. Let me prepare all the files you need first though — the repo should have a proper structure with a manifest.json for the Qud mod loader.


Viewed a file, created a file
Viewed a file, created a file
Claude's response was interrupted
Sorry i mean can you write me the readme.md for github page of dromadiag

2:06 PM

Check existing README
Check existing README
Good base to work from — I'll expand it significantly into a proper GitHub landing page with badges, a feature overview, real example output, and better visual structure.


Created a file, ran a command, read a file
Created a file, ran a command, read a file
Drop that README.md in the root of https://github.com/Muramas/DromaDiag and it'll render as the repo front page. A few things to fill in once you're set up:

Replace *(Workshop link coming soon)* with your actual Workshop URL after publishing
The <details> blocks with example output will render as collapsible sections on GitHub, which keeps the page tidy without hiding anything important

Readme
Document · MD 




Claude is AI and can make mistakes. Please double-check responses.
Readme · MD
Copy

DromaDiag
A standalone diagnostic tool for Caves of Qud modders. Inspect types, live objects, Harmony patches, and compiled method bodies — all from inside the game via wish commands.

Output is written to DromaDiag.txt in your save folder and copied to your clipboard automatically after every command.

Features
Type inspection	Full method/field/property hierarchy across base classes
Symbol search	Find any type, method, or field by name or substring
Live instance dump	Read field values off running MonoBehaviour instances
UI layout	Dump RectTransform data for any live UI component
Harmony audit	List every active patch in the session, grouped by method
Caller scan	Find every method that calls a given method, by scanning IL
Stack capture	One-shot Harmony postfix that captures a live stack trace
IL disassembly	Built-in opcode disassembler with resolved method and field names
C# decompilation	Full decompiled source via DromadSpy (optional)
Reference listing	Fully resolved field/method/type refs with IL offsets via DromadSpy (optional)
Installation
Option A — Steam Workshop
(Workshop link coming soon)

Option B — Manual
Copy DromaDiag.cs into your mod folder:

%APPDATA%\..\LocalLow\Freehold Games\CavesOfQud\Mods\DromaDiag\
DromadSpy (optional)
DromadSpy is a small companion executable that unlocks the decompile and refs commands by running ICSharpCode.Decompiler against Assembly-CSharp.dll out-of-process. All other DromaDiag commands work fine without it.

If DromadSpy is not found, DromaDiag will tell you exactly which paths it checked and how to fix it — nothing breaks.

Install DromadSpy
Download DromadSpy.zip from the Releases page and extract it to:

<Caves of Qud install>\Modding\DromadSpy\
For a typical Steam install:

H:\SteamLibrary\steamapps\common\Caves of Qud\Modding\DromadSpy\
Build from source (requires the .NET SDK):

powershell
cd DromadSpy
dotnet publish -c Release -o "<Caves of Qud install>\Modding\DromadSpy"
Custom location — set an environment variable instead:

DROMADIAG_DROMADSPY_PATH=C:\wherever\DromadSpy.exe
Commands
Open the wish system with Ctrl+W or Alt+W and type any command below.

Output goes to:

%APPDATA%\..\LocalLow\Freehold Games\CavesOfQud\DromaDiag.txt
...and is copied to your clipboard automatically.

dromadiag:type <TypeName>
Dumps every method, field, and property on the named type and its full base-class hierarchy. Supports exact full name, short name, or partial substring.

dromadiag:type AutoAct
dromadiag:type XRL.World.Capabilities.AutoAct
<details> <summary>Example output</summary>
=== dromadiag:type AutoAct ===

TYPE: XRL.World.Capabilities.AutoAct
BASE: System.Object

────── XRL.World.Capabilities.AutoAct ──────
  [methods]
    Void FindAutoexploreStep(String& Step, Boolean& Blackout)
    Void FindAutoexploreStep(Boolean Force, String& Step, Boolean& Blackout)
    Boolean IsAutoexploreCell(Cell cell)
    Boolean IsAutoexploreObject(GameObject obj)
    ...
  [fields]
    static Zone AutoexploreZone
    static Int32 AutoexploreLastAct
    static Cell AutoexploreLastTarget
    static Rack`1 AutoexploreObjects
    static Rack`1 AutoexploreCells
    static FindPath AutoexplorePath
    ...
</details>
dromadiag:find <MethodOrFieldName>
Finds every type in Assembly-CSharp that declares a method or field with exactly this name.

dromadiag:find FindAutoexploreStep
dromadiag:find AutoexploreZone
dromadiag:live <TypeName>
Finds live MonoBehaviour instances of the type and dumps all current field values. Open the relevant screen before running.

dromadiag:live TradeScreen
dromadiag:live MainMenuScreen
dromadiag:rect <TypeName>
Dumps RectTransform layout data (anchored position, size delta, rect dimensions, anchors, offsets, pivot) for a live UI component and all RectTransform-carrying fields it references. Open the screen first.

dromadiag:rect TradeScreen
dromadiag:rect CharacterCreationScreen
dromadiag:search <substring>
Case-insensitive substring search across all type names, method names, and field names in Assembly-CSharp.

dromadiag:search autoexplore
dromadiag:search FloodFill
dromadiag:search mutation
dromadiag:harmony
Lists every active Harmony patch in the current session, grouped by patched method. Shows prefix, postfix, and transpiler owners.

dromadiag:harmony
dromadiag:callers <TypeName> <MethodName>
Scans the IL of every method in Assembly-CSharp and reports every method that calls the named method. Handles multiple overloads.

dromadiag:callers AutoAct FindAutoexploreStep
dromadiag:callers Zone FloodAutoexplore
dromadiag:stack <TypeName> <MethodName>
Installs a one-shot Harmony postfix that captures a full stack trace the next time the method is called, writes it to DromaDiag.txt, then removes itself. Trigger the method in-game after running the command.

dromadiag:stack AutoAct FindAutoexploreStep
dromadiag:body <TypeName> <MethodName>
Disassembles the raw IL of the named method using a built-in opcode table. Resolves call/callvirt tokens to method names and ldfld/stfld tokens to field names. No DromadSpy required.

dromadiag:body AutoAct FindAutoexploreStep
dromadiag:decompile <TypeName> <MethodName> (requires DromadSpy)
Full C# decompilation of the method body. Returns readable source with real variable names, reconstructed control flow, correct generic types, and expanded lambdas.

dromadiag:decompile AutoAct FindAutoexploreStep
<details> <summary>Example output</summary>
csharp
public static void FindAutoexploreStep(bool Force, out string Step, out bool Blackout)
{
    Blackout = false;
    Step = ".";
    Zone zone = The.PlayerCell?.ParentZone;
    if (zone == null)
    {
        return;
    }
    if (Force || AutoexploreZone != zone || AutoexploreLastAct != Count)
    {
        AutoexploreZone = zone;
        AutoexploreLastAct = Count;
        AutoexploreObjects.Clear();
        AutoexploreCells.Clear();
        AutoexplorePath.Clear();
        AutoexploreLastTarget = null;
        zone.FloodAutoexplore(AutoexploreCells, AutoexploreObjects);
    }
    ...
}
</details>
dromadiag:refs <TypeName> <MethodName> (requires DromadSpy)
Produces a flat, fully resolved reference listing for the method body. Every field read/write, method call, and type reference is shown with its complete type-qualified name and IL offset.

dromadiag:refs AutoAct FindAutoexploreStep
<details> <summary>Example output</summary>
=== refs in XRL.World.Capabilities.AutoAct.FindAutoexploreStep(Boolean Force, String& Step, Boolean& Blackout) ===

  IL_000a: call        XRL.The::get_PlayerCell()
  IL_0016: ldfld       XRL.World.Cell::ParentZone
  IL_0023: ldsfld      XRL.World.Capabilities.AutoAct::AutoexploreZone
  IL_0038: stsfld      XRL.World.Capabilities.AutoAct::AutoexploreZone
  IL_0047: ldsfld      XRL.World.Capabilities.AutoAct::AutoexploreObjects
  IL_004c: callvirt    XRL.Collections.Rack`1<XRL.World.GameObject>::Clear()
  IL_0076: callvirt    XRL.World.Zone::FloodAutoexplore(Rack<Cell>, Rack<GameObject>)
  IL_00a2: callvirt    XRL.World.Cell::HasObject(Predicate<GameObject>)
  IL_00b3: call        XRL.World.Capabilities.AutoAct::TryGetAutoexploreStepToCell(Cell, String&)
  ...
</details>
Batch mode
Chain any commands with commas in a single wish. All results are combined into one DromaDiag.txt write.

dromadiag:type AutoAct, dromadiag:callers AutoAct FindAutoexploreStep
dromadiag:decompile AutoAct FindAutoexploreStep, dromadiag:refs AutoAct FindAutoexploreStep
Repository layout
DromaDiag/
  DromaDiag.cs        ← the mod — copy this into your Mods folder
DromadSpy/
  DromadSpy.cs        ← companion decompiler tool source
  DromadSpy.csproj    ← build with: dotnet publish -c Release
CHANGELOG.md
README.md
Requirements
Caves of Qud (any recent version)
HarmonyLib (bundled with the game)
.NET 4.8 runtime (included with Windows 10+)
DromadSpy: .NET SDK 6+ to build, or download the pre-built release
Acknowledgements
Decompilation powered by ICSharpCode.Decompiler — the engine behind ILSpy and dnSpy.




