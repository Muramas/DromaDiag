# DromaDiag

> A standalone diagnostic tool for Caves of Qud modders. Inspect types, live objects, Harmony patches, and compiled method bodies — all from inside the game via wish commands.

Output is written to `DromaDiag.txt` in your save folder and **copied to your clipboard** automatically after every command.

---

## Features

| | |
|---|---|
| **Type inspection** | Full method/field/property hierarchy across base classes |
| **Symbol search** | Find any type, method, or field by name or substring |
| **Live instance dump** | Read field values off running MonoBehaviour instances |
| **UI layout** | Dump RectTransform data for any live UI component |
| **Harmony audit** | List every active patch in the session, grouped by method |
| **Caller scan** | Find every method that calls a given method, by scanning IL |
| **Stack capture** | One-shot Harmony postfix that captures a live stack trace |
| **IL disassembly** | Built-in opcode disassembler with resolved method and field names |
| **C# decompilation** | Full decompiled source via DromadSpy *(optional)* |
| **Reference listing** | Fully resolved field/method/type refs with IL offsets via DromadSpy *(optional)* |

---

## Installation

### Download from Steam Workshop

[DromaDiag](https://steamcommunity.com/sharedfiles/filedetails/?id=3692727013)

### Option B — Manual

Copy `DromaDiag.cs` and `ModInfo.json` into your mod folder:

```
%APPDATA%\..\LocalLow\Freehold Games\CavesOfQud\Mods\DromaDiag\
```

---

## DromadSpy (optional)

DromadSpy is a small companion executable that unlocks the `decompile` and `refs` commands by running [ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) against `Assembly-CSharp.dll` out-of-process. All other DromaDiag commands work fine without it.

If DromadSpy is not found, DromaDiag will tell you exactly which paths it checked and how to fix it — nothing breaks.

### Install DromadSpy

**Download** `DromadSpy.zip` from the [Releases](../../releases) page and extract it to:

```
<Caves of Qud install>\Modding\DromadSpy\
```

For a typical Steam install:
```
H:\SteamLibrary\steamapps\common\Caves of Qud\Modding\DromadSpy\
```

**Build from source** (requires the [.NET SDK](https://dotnet.microsoft.com/download)):

```powershell
cd DromadSpy
dotnet publish -c Release -o "<Caves of Qud install>\Modding\DromadSpy"
```

**Custom location** — set an environment variable instead:
```
DROMADIAG_DROMADSPY_PATH=C:\wherever\DromadSpy.exe
```

---

## Commands

Open the wish system with `Ctrl+W` or `Alt+W` and type any command below.

Output goes to:
```
%APPDATA%\..\LocalLow\Freehold Games\CavesOfQud\DromaDiag.txt
```
...and is **copied to your clipboard** automatically.

---

### `dromadiag:type <TypeName>`

Dumps every method, field, and property on the named type and its full base-class hierarchy. Supports exact full name, short name, or partial substring.

```
dromadiag:type AutoAct
dromadiag:type XRL.World.Capabilities.AutoAct
```

<details>
<summary>Example output</summary>

```
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
```
</details>

---

### `dromadiag:find <MethodOrFieldName>`

Finds every type in `Assembly-CSharp` that declares a method or field with exactly this name.

```
dromadiag:find FindAutoexploreStep
dromadiag:find AutoexploreZone
```

---

### `dromadiag:live <TypeName>`

Finds live MonoBehaviour instances of the type and dumps all current field values. Open the relevant screen before running.

```
dromadiag:live TradeScreen
dromadiag:live MainMenuScreen
```

---

### `dromadiag:rect <TypeName>`

Dumps `RectTransform` layout data (anchored position, size delta, rect dimensions, anchors, offsets, pivot) for a live UI component and all `RectTransform`-carrying fields it references. Open the screen first.

```
dromadiag:rect TradeScreen
dromadiag:rect CharacterCreationScreen
```

---

### `dromadiag:search <substring>`

Case-insensitive substring search across all type names, method names, and field names in `Assembly-CSharp`.

```
dromadiag:search autoexplore
dromadiag:search FloodFill
dromadiag:search mutation
```

---

### `dromadiag:harmony`

Lists every active Harmony patch in the current session, grouped by patched method. Shows prefix, postfix, and transpiler owners.

```
dromadiag:harmony
```

---

### `dromadiag:callers <TypeName> <MethodName>`

Scans the IL of every method in `Assembly-CSharp` and reports every method that calls the named method. Handles multiple overloads.

```
dromadiag:callers AutoAct FindAutoexploreStep
dromadiag:callers Zone FloodAutoexplore
```

---

### `dromadiag:stack <TypeName> <MethodName>`

Installs a one-shot Harmony postfix that captures a full stack trace the next time the method is called, writes it to `DromaDiag.txt`, then removes itself. Trigger the method in-game after running the command.

```
dromadiag:stack AutoAct FindAutoexploreStep
```

---

### `dromadiag:body <TypeName> <MethodName>`

Disassembles the raw IL of the named method using a built-in opcode table. Resolves `call`/`callvirt` tokens to method names and `ldfld`/`stfld` tokens to field names. No DromadSpy required.

```
dromadiag:body AutoAct FindAutoexploreStep
```

---

### `dromadiag:decompile <TypeName> <MethodName>` *(requires DromadSpy)*

Full C# decompilation of the method body. Returns readable source with real variable names, reconstructed control flow, correct generic types, and expanded lambdas.

```
dromadiag:decompile AutoAct FindAutoexploreStep
```

<details>
<summary>Example output</summary>

```csharp
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
```
</details>

---

### `dromadiag:refs <TypeName> <MethodName>` *(requires DromadSpy)*

Produces a flat, fully resolved reference listing for the method body. Every field read/write, method call, and type reference is shown with its complete type-qualified name and IL offset.

```
dromadiag:refs AutoAct FindAutoexploreStep
```

<details>
<summary>Example output</summary>

```
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
```
</details>

---

## Batch mode

Chain any commands with commas in a single wish. All results are combined into one `DromaDiag.txt` write.

```
dromadiag:type AutoAct, dromadiag:callers AutoAct FindAutoexploreStep
dromadiag:decompile AutoAct FindAutoexploreStep, dromadiag:refs AutoAct FindAutoexploreStep
```

---

## Repository layout

```
DromaDiag/
  DromaDiag.cs        ← the mod — copy this into your Mods folder
DromadSpy/
  DromadSpy.cs        ← companion decompiler tool source
  DromadSpy.csproj    ← build with: dotnet publish -c Release
CHANGELOG.md
README.md
```

---

## Requirements

- Caves of Qud (any recent version)
- HarmonyLib (bundled with the game)
- .NET 4.8 runtime (included with Windows 10+)
- DromadSpy: .NET SDK 6+ to build, or download the pre-built release

---

## Acknowledgements

Decompilation powered by [ICSharpCode.Decompiler](https://github.com/icsharpcode/ILSpy) — the engine behind ILSpy and dnSpy.
