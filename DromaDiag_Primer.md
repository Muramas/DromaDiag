# DromaDiag — AI Primer & Reference Guide

## What Is DromaDiag?

DromaDiag is a runtime code-structure diagnostic tool for Caves of Qud mods. It operates entirely through the game's wish system and inspects the compiled game assembly (Assembly-CSharp) via .NET reflection. It does not modify game state in any way — it is purely a read-only inspection tool.

All output is written to a text file on disk:
```
C:\Users\<user>\AppData\LocalLow\Freehold Games\CavesOfQud\DromaDiag.txt
```
A green confirmation message also appears in the game's message log after each command runs.

DromaDiag is one half of a two-tool diagnostic suite. Its companion tool, DromadState, inspects live game state (runtime object values, faction data, zone contents). DromaDiag inspects code structure — types, methods, fields, and Harmony patches as they exist in the compiled assembly.

---

## How To Invoke Commands

Commands are entered through the Caves of Qud wish interface, opened with the `[` key in-game.

The syntax is always:
```
dromadiag:<command> <argument>
```

Examples:
```
dromadiag:type Brain
dromadiag:find HandleEvent
dromadiag:search faction
dromadiag:harmony
```

### Chaining Multiple Commands

Multiple commands can be chained in a single wish entry using commas with no spaces:
```
dromadiag:type Brain,dromadiag:find GetFeelingTowardsFaction
```

When chaining, all output is collected into a single DromaDiag.txt file in one pass. Each command's output is separated by its own header. The in-game message reports how many commands ran and a brief status for each.

The leading `dromadiag:` prefix on each token after the first is optional when chaining but supported if included:
```
dromadiag:find Foo,dromadiag:type Bar
```
is equivalent to:
```
dromadiag:find Foo,type Bar
```

---

## Command Reference

### `dromadiag:type <TypeName>`

**Purpose:** Dumps the full structure of a type — every method, field, and property declared on it and its entire base class hierarchy.

**Input:** A type name. Supports three matching modes, tried in order:
1. Exact full name match (e.g. `XRL.World.Parts.Brain`)
2. Exact short name match (e.g. `Brain`)
3. Partial name match (e.g. `rain` would match `Brain`)

If multiple types match the input, all of them are dumped.

**Output structure for each matched type:**
```
TYPE: XRL.World.Parts.Brain
BASE: XRL.World.IPart

────── XRL.World.Parts.Brain ──────
  [methods]
    Boolean HandleEvent(BeforeApplyDamageEvent E)
    Void SetHostile(Boolean value)
    ...
  [fields]
    Int32 Flags
    Int32 MaxMissileRange
    ...
  [properties]
    Boolean WantFieldReflection
    ...

────── XRL.World.IPart ──────       ← walks up the hierarchy
  [methods]
    ...
```

Each tier of the hierarchy is shown separately under its own banner, with only the members declared on that tier (`DeclaredOnly` flag). This means you see exactly where each method or field originates, not just what's accessible.

**Method signatures** include return type, method name, and all parameters with their types and names:
```
Boolean HandleEvent(BeforeApplyDamageEvent E)
Int32 GetFeelingTowardsFaction(String Faction, Nullable`1 DefaultBeforeGeneral, Int32 DefaultAfterGeneral)
```

**Field entries** include whether the field is static:
```
static Int32 BRAIN_FLAG_HOSTILE
Int32 Flags
String LastThought
```

**When to use this command:**
- You know a type name and want to see everything on it
- You want to understand the full inheritance chain of a type
- You are about to write a Harmony patch and need the exact method signature
- You want to find what fields a Part stores so you can read or modify them
- You are verifying that a method or field you expect to exist actually does

**Example invocations:**
```
dromadiag:type Brain
dromadiag:type XRL.World.Parts.Brain
dromadiag:type GameObjectFactory
dromadiag:type InventoryAndEquipment
dromadiag:type IPart
```

---

### `dromadiag:find <MethodOrFieldName>`

**Purpose:** Finds every type in Assembly-CSharp that declares a method or field with exactly the given name.

**Input:** An exact name string. Case-sensitive exact match only (no partial matching).

**Output structure:**
```
XRL.World.Parts.Brain
  method: Boolean GetFeelingTowardsFaction(String Faction, ...)
  method: Void SetFeeling(String Faction, Int32 Feeling)

XRL.World.Factions
  method: Int32 GetFeelingFactionToFaction(String Faction1, String Faction2)
```

Each type that declares the name is listed, followed by each matching member (showing full signature for methods, type and name for fields).

**When to use this command:**
- You know a method or field name but don't know which type declares it
- `AccessTools.Method(typeof(SomeType), "MethodName")` is returning null and you need to find the real declaring type
- You want to find all places a particular field name appears across the codebase
- You are tracing where a specific piece of functionality lives

**Important note:** This command uses `DeclaredOnly` — it only finds types that directly declare the member, not types that inherit it. If `Brain` inherits `HandleEvent` from `IPart`, searching for `HandleEvent` will find `IPart` (and every other type that directly overrides it), but not `Brain` unless `Brain` itself declares an override.

**Example invocations:**
```
dromadiag:find HandleEvent
dromadiag:find GetFeelingTowardsFaction
dromadiag:find PartsList
dromadiag:find Blueprint
dromadiag:find CheckInit
```

---

### `dromadiag:live <TypeName>`

**Purpose:** Finds live MonoBehaviour instances of the named type currently in memory (via `FindObjectsByType`) and dumps all field names and their current runtime values.

**Input:** A type name. Uses the same three-tier matching as `dromadiag:type` (exact full, exact short, partial).

**Requirement:** The relevant screen or game state must be active before running this command. For example, if you want to inspect a UI screen's state, that screen must be open. For game object components, the relevant scene must be loaded.

**Output structure:**
```
TYPE: SomeMonoBehaviourType
Live instances: 2

--- Instance 0 ---
  String someField = "value"
  Int32 aCounter = 42
  Boolean aFlag = True
  List`1 aList = [List`1 Count=5]
  ...

--- Instance 1 ---
  ...
```

**Value formatting:**
- Strings are shown quoted and truncated at 120 characters with `...` if longer
- Lists, dictionaries, and arrays show their type name and count/length
- Other values use `.ToString()`
- Null values show as `null`
- Fields that throw on read show as `<error reading value>`

**When to use this command:**
- Debugging UI components — open the relevant screen first, then run the command
- Inspecting the live state of a MonoBehaviour-based system
- Checking what values a component currently holds at a specific moment in gameplay

**Example invocations:**
```
dromadiag:live InventoryScreen
dromadiag:live CharacterCreationController
dromadiag:live GameManager
```

---

### `dromadiag:rect <TypeName>`

**Purpose:** Finds a live MonoBehaviour instance and dumps RectTransform layout data for the instance itself and every Component field it contains, including array fields of Components.

**Input:** A type name. Same matching rules as other commands. Only the first matched instance is inspected (to keep output manageable).

**Requirement:** The relevant UI screen must be open before running.

**Output per RectTransform found:**
```
[fieldName  (FieldTypeName)]
  anchoredPosition : (x, y)
  sizeDelta        : (x, y)
  rect (w/h)       : 800.0 x 600.0
  anchorMin        : (x, y)
  anchorMax        : (x, y)
  offsetMin        : (x, y)
  offsetMax        : (x, y)
  pivot            : (x, y)
```

The instance's own RectTransform is shown under `[self]` first, then each Component field and array-of-Component field follows. Fields that contain `UITextSkin` components also recurse into their inner GameObject's RectTransform.

**When to use this command:**
- Before making any positional or size changes to UI elements
- Understanding the layout hierarchy of a UI screen
- Figuring out why a UI element is positioned incorrectly
- Getting anchor, pivot, and offset values needed to match or align with existing UI

**Example invocations:**
```
dromadiag:rect InventoryScreen
dromadiag:rect CharacterCreationController
dromadiag:rect AbilityManagerScreen
```

---

### `dromadiag:search <substring>`

**Purpose:** Searches all type names, method names, and field names in Assembly-CSharp for a case-insensitive substring match.

**Input:** Any substring. Case-insensitive.

**Output structure:**
```
XRL.World.GameObjectFactory
  [type match]
  method: GetBlueprint
  field:  Dictionary`2 Blueprints

XRL.World.GameObject
  method: GetBlueprint
  method: SetBlueprint
  field:  String Blueprint
```

Each type that has any match is listed once, with:
- `[type match]` if the type's own name contains the substring
- `method: MethodName` for each matching method
- `field: TypeName FieldName` for each matching field

**When to use this command:**
- Exploring an unknown API — you know a keyword but not where it lives
- Finding all types related to a concept (e.g. `dromadiag:search faction` finds everything faction-related)
- Discovering what methods and fields exist when you don't know the exact names
- As a first step before using `dromadiag:type` to zero in on a specific type
- Finding all usages of a particular naming convention across the codebase

**Example invocations:**
```
dromadiag:search faction
dromadiag:search blueprint
dromadiag:search handleevent
dromadiag:search zonemanager
dromadiag:search reputation
dromadiag:search inventory
dromadiag:search mutation
```

---

### `dromadiag:harmony`

**Purpose:** Lists every Harmony patch currently applied in the running game, grouped by the method being patched. Shows which mod owns each patch and what kind of patch it is (Prefix, Postfix, or Transpiler).

**Input:** None.

**Output structure:**
```
XRL.World.Parts.Brain.Think
  [Prefix] com.mymod.id -> MyPatchClass_ThinkPrefix
  [Postfix] com.othermod.id -> OtherPatch_ThinkPostfix

XRL.World.GameObjectFactory.CreateObject
  [Transpiler] com.mymod.id -> MyTranspiler_CreateObject
```

Each patched method is listed by its fully qualified name. Under each method, every patch is shown with:
- The patch type (`Prefix`, `Postfix`, or `Transpiler`)
- The Harmony instance owner ID (usually the mod's ID string)
- The name of the patch method

**When to use this command:**
- Diagnosing mod conflicts — seeing which mods are patching the same methods
- Verifying that your own patches are being applied correctly
- Understanding the full patch chain on a method before adding your own patch
- Debugging a situation where a method isn't behaving as expected and you suspect interference from another patch

**Example invocation:**
```
dromadiag:harmony
```

---

### `dromadiag:callers <TypeName> <MethodName>`

**Purpose:** Scans the IL bytecode of every method in Assembly-CSharp and reports every method that contains a `call` or `callvirt` instruction targeting the specified method. This is the equivalent of "Find Usages" in an IDE, performed at runtime without needing a decompiler.

All overloads of the target method are searched simultaneously. Any method in the assembly that calls any overload of the target will appear in results.

**Input:** Two space-separated tokens — a type name (partial matching supported, same rules as `dromadiag:type`) followed by an exact method name.

**Output structure:**
```
=== dromadiag:callers AutoAct FindAutoexploreStep ===

Searching for callers of AutoAct.FindAutoexploreStep (2 overload(s))...

XRL.Core.ActionManager.RunSegment()
XRL.World.Capabilities.AutoAct.FindAutoexploreStep(String& Step, Boolean& Blackout)
XRL.World.Capabilities.AutoAct.FindAutoexploreStep(Boolean Force, String& Step, Boolean& Blackout)

Total callers found: 3
```

Each result shows the full declaring type and method signature of the caller.

**How it works:** The command reads the raw IL byte array of every method body in the assembly and scans for `call` (0x28), `callvirt` (0x6F), and `newobj` (0x73) opcodes. It extracts the metadata token from each call site and resolves it using `Module.ResolveMethod`. If the resolved method's metadata token matches any overload of the target, the scanning method is recorded as a caller.

**Important limitations:**
- Only scans Assembly-CSharp. Callers in Unity engine assemblies, third-party libraries, or other mod assemblies will not appear.
- Cannot detect indirect calls (delegates, reflection, dynamic dispatch through interfaces) — only direct `call`/`callvirt` instructions.
- Methods with no body (abstract, extern, native) are skipped silently.
- The IL scanner uses a simplified opcode size table. In rare cases involving unusual opcodes, the scan may misalign and miss a call site. This is uncommon in typical game code.

**When to use this command:**
- You need to understand what drives a method — i.e. what calls it and when
- You are debugging why a Harmony patch on a method is or isn't firing
- You want to know all the entry points into a system before patching it
- A method behaves unexpectedly and you want to trace backwards to find the trigger
- You cannot use dnSpy or ILSpy and need call-graph information at runtime

**Example invocations:**
```
dromadiag:callers AutoAct FindAutoexploreStep
dromadiag:callers ActionManager RunSegment
dromadiag:callers Zone FloodAutoexplore
dromadiag:callers Brain Think
```

---

### `dromadiag:stack <TypeName> <MethodName>`

**Purpose:** Installs a one-shot Harmony postfix on the named method that captures a full .NET stack trace the next time the method is called, writes it to DromaDiag.txt, and then automatically removes itself. This tells you exactly what called the method at runtime — the full call chain from the top of the stack down.

**Input:** Two space-separated tokens — a type name (partial matching supported) followed by an exact method name. All overloads of the named method are patched simultaneously.

**Requirement:** You must trigger the patched method in-game after running this command. The patch fires on the first call, captures the trace, removes itself, and sends a green confirmation message.

**Output (written to DromaDiag.txt automatically on trigger):**
```
=== dromadiag:stack capture ===

Captured at: 14:52:10.040
  at DromaDiag.DromaDiag._StackTracePostfix () [0x00000] in DromaDiag.cs:718
  at XRL.World.Capabilities.AutoAct.FindAutoexploreStep_Patch2 (...)
  at XRL.Core.ActionManager.RunSegment () [0x00000]
  at XRL.Core.XRLCore.RunGame () [0x00000]
  at XRL.Core.XRLCore._Start () [0x00000]
  ...
```

**Behaviour details:**
- The patch installs on ALL overloads of the named method. The first overload that fires captures the trace.
- After capture, the patch removes itself via `harmony.UnpatchAll`. The method returns to normal.
- If you run the command again on the same method before it fires, it reports "already pending" and does nothing.
- The postfix does not interrupt or alter the method's execution — it only observes.

**When to use this command:**
- You can see a method exists but don't know what calls it and when
- A Harmony patch is registered (confirmed via `dromadiag:harmony`) but you need to know the call context to understand why it isn't behaving as expected
- You want to confirm a code path is being taken — e.g. "does this method actually get called when I do X?"
- You need the runtime call chain rather than the static IL-level callers (use `dromadiag:callers` for static, `dromadiag:stack` for runtime)

**Workflow:**
1. Run `dromadiag:stack TypeName MethodName`
2. Perform the in-game action that should trigger the method
3. A green message confirms capture — open DromaDiag.txt immediately
4. The patch has self-removed; subsequent calls to the method are unaffected

**Example invocations:**
```
dromadiag:stack AutoAct FindAutoexploreStep
dromadiag:stack AutoAct set_Setting
dromadiag:stack ActionManager RunSegment
dromadiag:stack Zone FloodAutoexplore
```

---

### `dromadiag:body <TypeName> <MethodName>`

**Purpose:** Disassembles the IL bytecode of the named method into a human-readable opcode listing. For call and field-access instructions, the target method or field name is resolved and shown inline. This is a lightweight runtime alternative to opening dnSpy or ILSpy — it lets you read method logic directly from within the game.

**Input:** Two space-separated tokens — a type name (partial matching supported) followed by an exact method name. All overloads are disassembled and shown separately.

**Output structure:**
```
── XRL.Core.ActionManager.RunSegment() ──
  MaxStackSize : 21
  LocalVars    : 94
    [0] XRLGame
    [1] GameObject
    ...

  IL_0000: nop
  IL_0001: call The.get_Game()
  IL_0006: stloc.0
  IL_0007: call AutoAct.IsActive(Boolean)
  IL_000C: brfalse IL_0200
  IL_0011: call AutoAct.get_Setting()
  IL_0016: ldfld AutoAct._someField
  IL_001B: callvirt Zone.FindAutoexploreStep(String&, Boolean&)
  ...
```

Each line shows the IL offset, opcode mnemonic, and — for call/callvirt/newobj instructions — the resolved `DeclaringType.MethodName(ParamTypes)`. For ldfld/stfld/ldsfld/stsfld instructions, the resolved `DeclaringType.FieldName` is shown. Branches show their byte offset operand. Local variable types are listed at the top.

**How it works:** The command calls `MethodBody.GetILAsByteArray()` and walks the bytes using a hand-written opcode size table covering all common CIL opcodes. Metadata tokens in call and field instructions are resolved via `Module.ResolveMethod` and `Module.ResolveField`. Two-byte opcodes (prefixed with `0xFE`) are decoded from a separate lookup table.

**Important limitations:**
- The opcode size table covers the vast majority of CIL but is not exhaustive. Unusual opcodes (e.g. `switch`, `calli`, some prefix instructions) may cause the walk to misalign partway through a large method. In practice this is uncommon for typical game code, but very large methods may have garbled output toward the end.
- Abstract and extern methods have no body and will show `(no body — abstract or extern)`.
- The output is raw IL — it shows the compiler's output, not the original C# source. Compiler-generated patterns (closures, async state machines, LINQ) will appear as their compiled form.
- This is most useful for understanding control flow, identifying which methods are called and in what order, and reading conditional branches — not for reconstructing readable C# logic.

**When to use this command:**
- You need to understand the internal logic of a game method without dnSpy
- You want to know exactly what conditions gate a particular call — e.g. "what check runs before `FindAutoexploreStep` is called?"
- You are trying to write a Harmony Transpiler and need to see the exact IL to identify injection points
- `dromadiag:callers` told you what calls a method; now you want to read the calling method's body to understand the context
- You are debugging an interaction between two methods and need to trace the data flow

**Example invocations:**
```
dromadiag:body ActionManager RunSegment
dromadiag:body AutoAct FindAutoexploreStep
dromadiag:body XRLCore PlayerTurn
dromadiag:body Zone SetInfluenceAutoexploreSeeds
```

---

### `dromadiag:decompile <TypeName> <MethodName>` *(requires DromadSpy)*

**Purpose:** Produces full C# source for the named method by piping `Assembly-CSharp.dll` through DromadSpy.exe (powered by ICSharpCode.Decompiler, the engine behind ILSpy and dnSpy). This is the highest-fidelity output available — real variable names, reconstructed control flow, correct generic types, expanded lambdas, async/await reconstruction, and yield return patterns. Where `dromadiag:body` shows you IL, this shows you readable C#.

**Input:** Two space-separated tokens — a type name (partial matching supported) followed by an exact method name. All overloads are decompiled and shown separately.

**Requirement:** DromadSpy.exe must be installed. If it is not found, the command writes a message to DromaDiag.txt listing exactly where it looked and how to install it. All other DromaDiag commands continue to work without DromadSpy.

DromadSpy is searched for in this order:
1. The path in environment variable `DROMADIAG_DROMADSPY_PATH`
2. `<game>\Modding\DromadSpy\DromadSpy.exe`
3. Same folder as `Assembly-CSharp.dll`

**Output structure:**
```
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
    ...
}
```

**How it works:** DromaDiag locates `Assembly-CSharp.dll` on disk via `Assembly.Location`, then launches DromadSpy.exe as a child process with the dll path, type name, and method name as arguments. DromadSpy runs ICSharpCode.Decompiler out-of-process — this sidesteps Unity/Mono runtime constraints that would prevent the decompiler from loading inside the game. Stdout is captured and written to DromaDiag.txt.

**When to use this command:**
- You want to read method logic in C# rather than IL — faster to understand
- `dromadiag:body` output is ambiguous and you want the decompiler to resolve the intent
- You are writing a patch and want to see the full method body including all branches before deciding where to inject
- You are debugging an unexpected interaction and want to read exactly what a method does
- You want to paste decompiled output into an AI conversation to get accurate patch code without guessing

**Example invocations:**
```
dromadiag:decompile AutoAct FindAutoexploreStep
dromadiag:decompile Brain Think
dromadiag:decompile ActionManager RunSegment
dromadiag:decompile Zone FloodAutoexplore
```

---

### `dromadiag:refs <TypeName> <MethodName>` *(requires DromadSpy)*

**Purpose:** Produces a flat, fully resolved reference listing for the named method body. Every field read, field write, method call, and type reference inside the method is extracted and shown with its complete type-qualified name and IL offset. This is the structured cross-reference view of the method — more detailed than the decompiled source for tracking data flow, and more readable than raw IL.

**Input:** Two space-separated tokens — a type name (partial matching supported) followed by an exact method name. All overloads are processed separately.

**Requirement:** DromadSpy.exe must be installed. Same search order and fallback behaviour as `dromadiag:decompile`.

**Output structure:**
```
=== refs in XRL.World.Capabilities.AutoAct.FindAutoexploreStep(Boolean Force, String& Step, Boolean& Blackout) ===

  IL_000a: call        XRL.The::get_PlayerCell()
  IL_0016: ldfld       XRL.World.Cell::ParentZone
  IL_0023: ldsfld      XRL.World.Capabilities.AutoAct::AutoexploreZone
  IL_002b: ldsfld      XRL.World.Capabilities.AutoAct::AutoexploreLastAct
  IL_0030: ldsfld      XRL.World.Capabilities.AutoAct::Count
  IL_0038: stsfld      XRL.World.Capabilities.AutoAct::AutoexploreZone
  IL_0042: stsfld      XRL.World.Capabilities.AutoAct::AutoexploreLastAct
  IL_0047: ldsfld      XRL.World.Capabilities.AutoAct::AutoexploreObjects
  IL_004c: callvirt    XRL.Collections.Rack`1<XRL.World.GameObject>::Clear()
  IL_0076: callvirt    XRL.World.Zone::FloodAutoexplore(Rack<Cell>, Rack<GameObject>)
  IL_00a2: callvirt    XRL.World.Cell::HasObject(Predicate<GameObject>)
  IL_00b3: call        XRL.World.Capabilities.AutoAct::TryGetAutoexploreStepToCell(Cell, String&)
  ...
```

Each line shows:
- The IL offset of the instruction
- The instruction category (`call`, `callvirt`, `ldfld`, `stfld`, `ldsfld`, `stsfld`, `newobj`, `castclass`, `box`, etc.)
- The fully type-qualified target with complete generic parameter types

**How it works:** DromadSpy uses ICSharpCode.Decompiler's `ReflectionDisassembler` to disassemble the method to IL text with all operands symbolically resolved, then filters the output to only the instruction categories that represent meaningful cross-references — stripping branches, arithmetic, stack manipulation, and other non-reference instructions.

**When to use this command:**
- You want a flat inventory of everything a method touches — every field it reads or writes, every method it calls — without reading through the full control flow
- You are checking whether a method accesses a particular field or calls a particular method at all before writing a patch
- You want to understand what data a method depends on (all `ldsfld`/`ldfld` entries) and what it produces (all `stsfld`/`stfld` entries)
- You are tracing data flow between methods and need the complete call surface of each one
- You found a method via `dromadiag:callers` and want to quickly verify it is actually calling the target before reading the full body

**Compared to `dromadiag:body`:** `body` uses a hand-written opcode table and raw reflection — it works without DromadSpy but has coverage gaps and shows all instructions including branches and arithmetic. `refs` uses ICSharpCode.Decompiler's full resolver — it is more accurate, covers all opcodes, and filters to only the cross-reference instructions, making the output much shorter and more focused.

**Example invocations:**
```
dromadiag:refs AutoAct FindAutoexploreStep
dromadiag:refs Brain Think
dromadiag:refs ActionManager RunSegment
dromadiag:refs Zone FloodAutoexplore
```

---

## Worked Examples

### Example 1: Finding Where A Method Lives

You want to patch `GetFeelingTowardsFaction` but don't know which type declares it.

```
dromadiag:find GetFeelingTowardsFaction
```

Output tells you it's declared on `XRL.World.Faction`. You then run:

```
dromadiag:type Faction
```

To get the exact signature:
```
Int32 GetFeelingTowardsFaction(String Faction, Nullable`1 DefaultBeforeGeneral, Int32 DefaultAfterGeneral)
```

You now have everything needed to write your Harmony patch.

---

### Example 2: Verifying XML Mod Structure

You're writing a new Part and need to know what fields `XRL.World.Parts.Corpse` uses so you can write compatible XML parameters.

```
dromadiag:type Corpse
```

Output shows all fields:
```
Int32 CorpseChance
String CorpseBlueprint
String CorpseRequiresBodyPart
Int32 BurntCorpseChance
String BurntCorpseBlueprint
...
```

These field names are exactly what you use as XML attribute names in your blueprint's `<part Name="Corpse" CorpseChance="75" .../>`.

---

### Example 3: Exploring An Unknown System

You're trying to understand how the zone manager works but don't know the API.

```
dromadiag:search zonemanager
```

This finds `XRL.World.ZoneManager`, `XRL.XRLGame` (which has a `ZoneManager` field), and `XRL.The` (which has a `ZoneManager` property). You then run:

```
dromadiag:type ZoneManager
```

To see everything available on it.

---

### Example 4: Checking For Mod Conflicts

Your mod patches `Brain.Think` and something is going wrong. You run:

```
dromadiag:harmony
```

And discover another mod has a Transpiler on the same method. You can now look up that mod and understand the interaction.

---

### Example 5: Chained Research Session

You're building a mod that modifies faction feelings and need to understand the full data model.

```
dromadiag:search factionfeeling,dromadiag:type Faction,dromadiag:type Factions,dromadiag:find GetFeelingTowardsFaction
```

This runs all four commands in one pass and writes a single output file covering the full territory.

---

### Example 6: Tracing What Drives A Method (1.1.0)

Your Harmony patch on `AutoAct.FindAutoexploreStep` is registered (confirmed via `dromadiag:harmony`) but never fires. You need to know what calls it and under what conditions.

**Step 1 — Find all static callers:**
```
dromadiag:callers AutoAct FindAutoexploreStep
```

Output shows `ActionManager.RunSegment` is the only caller. Now you know the entry point.

**Step 2 — Read the calling method's body:**
```
dromadiag:body ActionManager RunSegment
```

The IL output shows that `FindAutoexploreStep` is only reached after a branch that checks `AutoAct.IsActive(Boolean)` and then inspects `AutoAct.Setting[0]`. You can now see exactly what conditions must be true for your patch to fire.

**Step 3 — Confirm the runtime call chain:**
```
dromadiag:stack AutoAct FindAutoexploreStep
```

Trigger the relevant in-game action. The stack trace written to DromaDiag.txt confirms the exact runtime path: `RunSegment` → `FindAutoexploreStep`, called from the game loop thread. You now have both the static structure and the runtime proof.

---

### Example 7: Understanding A Method's Internal Logic (1.1.0)

You want to know what `XRLCore.PlayerTurn` does when the autoexplore key is pressed — specifically, what string it passes to `AutoAct.set_Setting`.

```
dromadiag:body XRLCore PlayerTurn
```

The disassembly shows a giant switch on a string hash, with one branch calling `AutoAct.set_Setting` followed by `ret`. By reading the IL around that branch you can identify the condition and the exact string constant being set. No dnSpy required.

---

### Example 8: Full Method Inspection With DromadSpy (v1.1.0)

You want to understand exactly what `AutoAct.FindAutoexploreStep` does before patching it. You start with the quick structural view:

```
dromadiag:refs AutoAct FindAutoexploreStep
```

The refs output shows at a glance that the method reads `AutoexploreZone`, `AutoexploreLastAct`, and `Count`, calls `FloodAutoexplore`, `IsAutoexploreCell`, `IsAutoexploreObject`, and `TryGetAutoexploreStepToCell`. You can immediately see all the data it touches and all the methods it delegates to, without reading the full body.

You then want to understand the conditional logic — when does it call `FloodAutoexplore` vs skip it?

```
dromadiag:decompile AutoAct FindAutoexploreStep
```

The decompiled C# shows the full if-chain clearly: `FloodAutoexplore` is only called when `Force` is true, or the zone has changed, or the action count has advanced. You now have everything needed to write a precise patch with full confidence in the surrounding logic.

---

### Example 9: Passing Decompiled Output To An AI (v1.1.0)

You want to write a Harmony postfix on `FindAutoexploreStep` that adds a custom autoexplore target. You run:

```
dromadiag:decompile AutoAct FindAutoexploreStep
```

The output is automatically copied to your clipboard. You paste it directly into your AI conversation and ask for a postfix that appends your custom target to the existing logic. The AI can read the exact method body — real variable names, correct types, actual control flow — and write patch code that is accurate to the actual assembly rather than guessed from documentation.

---

## Key Behaviours To Know

**Assembly scope:** DromaDiag only inspects `Assembly-CSharp` — the main game assembly. It does not inspect Unity engine assemblies, third-party libraries, or other mod assemblies. If a type lives in `UnityEngine` or a library DLL, `dromadiag:find`, `dromadiag:search`, `dromadiag:callers`, and `dromadiag:body` will not find it.

**DeclaredOnly:** All member lookups use the `DeclaredOnly` binding flag. This means each type's output only shows members it directly declares, not inherited members (though the hierarchy walker in `dromadiag:type` covers inherited members by walking up the chain manually). When using `dromadiag:find`, remember that a type that only *inherits* a method will not appear in results — only the type that *declares* it will.

**Clipboard copy:** Every command automatically copies its full output to the system clipboard via `GUIUtility.systemCopyBuffer` immediately after writing DromaDiag.txt. The in-game confirmation message notes `(copied to clipboard)`. This means you can run a command and paste the result directly into an editor or AI conversation without opening the file.

**DromadSpy dependency:** The `decompile` and `refs` commands require DromadSpy.exe to be installed separately. If it is not found, these two commands write a helpful message to DromaDiag.txt explaining where it looked and how to install it. All other nine commands have no dependency on DromadSpy and always work.

**Partial matching in type commands:** `dromadiag:type`, `dromadiag:live`, `dromadiag:rect`, `dromadiag:callers`, `dromadiag:stack`, `dromadiag:body`, `dromadiag:decompile`, and `dromadiag:refs` all support partial name matching for the type name argument. `dromadiag:find` does not — it requires an exact name.

**No game state modification:** DromaDiag never modifies any game state. It is safe to run at any point during a session. The `dromadiag:stack` postfix patch is a temporary Harmony patch, but it only reads the call stack and removes itself after one use — it does not alter method behaviour.

**Output is overwritten:** Each single command run overwrites DromaDiag.txt. When chaining commands, the single combined output overwrites the file once. If you need to preserve previous output, copy the file before running a new command.

**Compiler-generated types:** The search and type commands will surface compiler-generated types (closure classes, async state machines, display classes) with names like `<>c__DisplayClass49_0`. These are normal — they represent lambda captures and async internals. They are usually not what you are looking for but can occasionally be informative. The `dromadiag:body` command will also show compiler-generated patterns in IL form.

---

## Common Patterns For AI-Assisted Modding

When working with an AI assistant on a Caves of Qud mod, the recommended workflow is:

**Step 1 — Discover:** Use `dromadiag:search` with relevant keywords to find what types and members exist in the area you're working in.

**Step 2 — Inspect:** Use `dromadiag:type` on the specific types identified in step 1 to get exact method signatures and field names.

**Step 3 — Locate:** Use `dromadiag:find` when you know a name but not its declaring type — especially before writing Harmony patches.

**Step 4 — Verify patches:** After loading your mod, use `dromadiag:harmony` to confirm your patches are applied and check for conflicts.

**Step 5 — Trace call chains:** When a patch is registered but not firing, or a method behaves unexpectedly, use `dromadiag:callers` to find what drives the method, `dromadiag:body` to read the calling context in IL, and `dromadiag:stack` to capture the runtime call chain.

**Step 6 — Read method logic:** When you need to understand what a method actually does before patching it, use `dromadiag:decompile` to get full C# source, or `dromadiag:refs` to get a flat inventory of everything it touches. Both require DromadSpy. Paste the output directly into your AI conversation — the decompiled source gives the AI exact, confirmed method logic to work from rather than guesses from documentation.

**Step 7 — Inspect live state:** Switch to DromadState (the companion tool) to verify that your mod's changes are actually having the intended effect at runtime.

**What to pass to an AI:** Paste the contents of DromaDiag.txt directly into your conversation. The AI can read the type and method information and use it to write correct code without guessing. Always run the relevant `dromadiag:type` commands for any types your mod will interact with before asking the AI to write code — this ensures all method signatures, field names, and type relationships are confirmed from the actual assembly rather than assumed from documentation or memory.

---

## Quick Reference Card

| Command | Input | Finds |
|---|---|---|
| `dromadiag:type` | Type name (partial ok) | All methods, fields, properties on type + full hierarchy |
| `dromadiag:find` | Exact method/field name | Every type that declares that member |
| `dromadiag:live` | Type name (partial ok) | Live MonoBehaviour instances + current field values |
| `dromadiag:rect` | Type name (partial ok) | RectTransform layout data for a live UI component |
| `dromadiag:search` | Any substring (case-insensitive) | All types/methods/fields containing that substring |
| `dromadiag:harmony` | None | All active Harmony patches grouped by patched method |
| `dromadiag:callers` | Type name + method name | Every method in the assembly that calls the target method (IL scan) |
| `dromadiag:stack` | Type name + method name | One-shot runtime stack trace captured on next call |
| `dromadiag:body` | Type name + method name | IL disassembly of method body with resolved call/field names |
| `dromadiag:decompile` | Type name + method name | Full decompiled C# source via DromadSpy *(optional)* |
| `dromadiag:refs` | Type name + method name | Flat resolved reference listing (fields, calls, types) via DromadSpy *(optional)* |

---

## Version History

### 1.1.0 Changes

Three new commands were added to enable runtime call-graph analysis and IL inspection without requiring an external decompiler. These were developed to solve a class of debugging problem that the original six commands could not address: "I know a method exists and my patch is registered, but I can't tell what drives it, when it fires, or what logic surrounds the call site."

---

#### `dromadiag:callers <TypeName> <MethodName>` — Static IL caller scan

This command answers the question: **what calls this method?**

It scans the raw IL bytecode of every method in Assembly-CSharp, looking for `call`, `callvirt`, and `newobj` opcodes whose metadata token resolves to any overload of the target method. All overloads are searched simultaneously. Results list the full declaring type and signature of every method that directly calls the target.

This is the runtime equivalent of "Find Usages" in an IDE. It is a static scan — it reads what the compiler emitted, not what executed at runtime. Use it when you need to understand the complete call graph around a method before patching it, or when a Harmony patch is firing unexpectedly (or not at all) and you need to trace why.

**Key limitation:** Only direct `call`/`callvirt` instructions are detected. Calls through delegates, reflection, or virtual dispatch via interface references are not visible to this scanner.

---

#### `dromadiag:stack <TypeName> <MethodName>` — One-shot runtime stack capture

This command answers the question: **what is the exact runtime call chain when this method fires?**

It installs a temporary Harmony postfix on all overloads of the named method. The next time any overload is called, the postfix captures `new System.Diagnostics.StackTrace(true).ToString()`, writes it to DromaDiag.txt, sends a green in-game confirmation message, and immediately unpatches itself. The method's behaviour is not altered — the postfix only observes.

This complements `dromadiag:callers`: callers gives you the static picture (all possible callers), stack gives you the runtime picture (the specific call chain that actually fired during a given action). Together they let you confirm both that a path exists and that it was taken.

**Key details:**
- The patch is one-shot and self-removing. It fires exactly once.
- If you run the command again on the same method before it fires, it reports "already pending" and skips.
- The DromaDiag.txt file is overwritten when the trace is captured — copy it first if you need to preserve previous output.

---

#### `dromadiag:body <TypeName> <MethodName>` — IL body disassembly

This command answers the question: **what does this method actually do, at the IL level?**

It calls `MethodBody.GetILAsByteArray()` and walks the bytes using a hand-written opcode dispatch table. For each instruction it emits the IL offset and opcode mnemonic. For `call`/`callvirt`/`newobj` instructions it resolves the metadata token and shows the full `DeclaringType.MethodName(ParamTypes)`. For `ldfld`/`stfld`/`ldsfld`/`stsfld` instructions it shows `DeclaringType.FieldName`. Local variable types are listed at the top of each method's section.

All overloads of the named method are disassembled and shown in sequence.

This is most useful when `dromadiag:callers` has told you *which* method contains the call site you care about, and you need to understand the conditional logic surrounding it — e.g. what guards a particular call, what string constant is passed, or what fields are read before the branch. It is also the starting point for writing Harmony Transpilers, since it shows the exact IL sequence you need to match.

**Key limitation:** The opcode size table covers the vast majority of CIL but is not exhaustive. Very large methods with unusual opcodes may have misaligned output partway through. Abstract and extern methods have no body and are reported as such.

---

#### Updated: Common Patterns For AI-Assisted Modding

A new Step 5 was added to the workflow section:

> **Step 5 — Trace call chains:** When a patch is registered but not firing, or a method behaves unexpectedly, use `dromadiag:callers` to find what drives the method, `dromadiag:body` to read the calling context in IL, and `dromadiag:stack` to capture the runtime call chain.

---

#### Updated: Key Behaviours To Know

The "Partial matching" note was updated to include `dromadiag:callers`, `dromadiag:stack`, and `dromadiag:body` in the list of commands that support partial type name matching.

The "No game state modification" note was updated to clarify that `dromadiag:stack`'s temporary Harmony patch does not alter method behaviour — it only observes the call stack and removes itself after one use.

The "Assembly scope" note was updated to include `dromadiag:callers` and `dromadiag:body` in the list of commands limited to Assembly-CSharp.

---

#### Updated: Quick Reference Card

Three rows added:

| Command | Input | Finds |
|---|---|---|
| `dromadiag:callers` | Type name + method name | Every method in the assembly that calls the target method (IL scan) |
| `dromadiag:stack` | Type name + method name | One-shot runtime stack trace captured on next call |
| `dromadiag:body` | Type name + method name | IL disassembly of method body with resolved call/field names |

---

### 1.1.0 Changes

Two new commands added that require the optional DromadSpy companion tool, plus a quality-of-life improvement applied to all commands.

---

#### `dromadiag:decompile <TypeName> <MethodName>` — Full C# decompilation

This command answers the question: **what does this method actually do, in readable C#?**

It runs ICSharpCode.Decompiler (the engine behind ILSpy and dnSpy) out-of-process via DromadSpy.exe, piping `Assembly-CSharp.dll` through it cold and capturing the output. The result is full C# source with real variable names, reconstructed control flow, correct generic types, and compiler-pattern expansion (closures, async, yield return).

This is the highest-fidelity output DromaDiag can produce. Where `dromadiag:body` shows you what the compiler emitted, `dromadiag:decompile` shows you what the programmer wrote — or as close to it as is recoverable. It is the most useful output to paste into an AI conversation, since the AI can read and reason about C# directly.

The reason this requires an out-of-process tool is a fundamental constraint of the Unity/Mono runtime: ICSharpCode.Decompiler reads the DLL as a raw PE file on disk, which cannot be done from inside a running Unity process without pulling in a dependency chain incompatible with Mono. DromadSpy solves this by running as a separate .NET process that reads the file independently.

**Key detail:** DromadSpy is optional. If not installed, the command writes a clear message explaining where it looked and how to install it. All other commands are unaffected.

---

#### `dromadiag:refs <TypeName> <MethodName>` — Resolved reference listing

This command answers the question: **what does this method touch?**

It uses ICSharpCode.Decompiler's `ReflectionDisassembler` (via DromadSpy) to disassemble the method to IL with all operands fully resolved, then filters the output to only the instructions that carry meaningful cross-references: `call`, `callvirt`, `newobj`, `ldfld`, `ldflda`, `stfld`, `ldsfld`, `ldsflda`, `stsfld`, `castclass`, `isinst`, `box`, `unbox`, `newarr`, `initobj`, and `constrained`.

The result is a compact, flat listing of every field the method reads or writes and every method it calls, with full type-qualified names and IL offsets. This is the fastest way to answer "does this method touch X?" without reading the full body.

**Compared to `dromadiag:body`:** `body` works without DromadSpy but uses a hand-written opcode table with coverage gaps and shows all instructions. `refs` uses the full decompiler resolver — complete opcode coverage, fully qualified names, and filtered to only what matters.

---

#### Clipboard copy — all commands

Every command now calls `GUIUtility.systemCopyBuffer` after writing DromaDiag.txt, copying the full output to the system clipboard. The in-game confirmation message includes `(copied to clipboard)`.

This makes the tool significantly faster to use with an AI assistant: run a command, switch to your browser or chat client, paste. No need to navigate to the AppData folder and open a text file.

---

#### Updated: Common Patterns For AI-Assisted Modding

Steps 6 and 7 added to the workflow:

> **Step 6 — Read method logic:** Use `dromadiag:decompile` for full C# source or `dromadiag:refs` for a flat reference inventory. Paste either directly into your AI conversation.

> **Step 7 — Inspect live state:** Switch to DromadState to verify runtime effects.

---

#### Updated: Key Behaviours To Know

Two new notes added: **Clipboard copy** and **DromadSpy dependency**. Partial matching note updated to include `decompile` and `refs`.

---

#### Updated: Quick Reference Card

Two rows added:

| Command | Input | Finds |
|---|---|---|
| `dromadiag:decompile` | Type name + method name | Full decompiled C# source via DromadSpy *(optional)* |
| `dromadiag:refs` | Type name + method name | Flat resolved reference listing (fields, calls, types) via DromadSpy *(optional)* |

---

## Companion Tool: DromadState

DromadState is the sister tool that covers everything DromaDiag does not. Where DromaDiag reads code structure, DromadState reads live game state.

| Need | Use |
|---|---|
| What methods exist on a type? | DromaDiag: `type` |
| What fields does a Part have? | DromaDiag: `type` |
| Where is this method declared? | DromaDiag: `find` |
| What patches are active? | DromaDiag: `harmony` |
| What calls this method? | DromaDiag: `callers` |
| What is the runtime call chain? | DromaDiag: `stack` |
| What does this method's IL look like? | DromaDiag: `body` |
| What does this method do in C#? | DromaDiag: `decompile` *(requires DromadSpy)* |
| What fields and methods does this method touch? | DromaDiag: `refs` *(requires DromadSpy)* |
| What Parts are on this creature right now? | DromadState: `part` |
| Did my XML mod actually apply? | DromadState: `xml` |
| What's in the current zone? | DromadState: `zone` |
| What's the player's current state? | DromadState: `player` |
| What are this faction's feeling values? | DromadState: `faction` |
| What handles this event? | DromadState: `event` |

Both tools write to their own txt files in the same save data folder and support command chaining with commas.

---

*DromaDiag — made by Mur. Caves of Qud modding tool.*
