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

## Key Behaviours To Know

**Assembly scope:** DromaDiag only inspects `Assembly-CSharp` — the main game assembly. It does not inspect Unity engine assemblies, third-party libraries, or other mod assemblies. If a type lives in `UnityEngine` or a library DLL, `dromadiag:find` and `dromadiag:search` will not find it.

**DeclaredOnly:** All member lookups use the `DeclaredOnly` binding flag. This means each type's output only shows members it directly declares, not inherited members (though the hierarchy walker in `dromadiag:type` covers inherited members by walking up the chain manually). When using `dromadiag:find`, remember that a type that only *inherits* a method will not appear in results — only the type that *declares* it will.

**Partial matching in type commands:** `dromadiag:type` and `dromadiag:live` and `dromadiag:rect` all support partial name matching as a fallback, and will return multiple results if multiple types match. `dromadiag:find` does not support partial matching — it requires an exact name.

**No game state modification:** DromaDiag never modifies any game state. It is safe to run at any point during a session.

**Output is overwritten:** Each single command run overwrites DromaDiag.txt. When chaining commands, the single combined output overwrites the file once. If you need to preserve previous output, copy the file before running a new command.

**Compiler-generated types:** The search and type commands will surface compiler-generated types (closure classes, async state machines, display classes) with names like `<>c__DisplayClass49_0`. These are normal — they represent lambda captures and async internals. They are usually not what you are looking for but can occasionally be informative.

---

## Common Patterns For AI-Assisted Modding

When working with an AI assistant on a Caves of Qud mod, the recommended workflow is:

**Step 1 — Discover:** Use `dromadiag:search` with relevant keywords to find what types and members exist in the area you're working in.

**Step 2 — Inspect:** Use `dromadiag:type` on the specific types identified in step 1 to get exact method signatures and field names.

**Step 3 — Locate:** Use `dromadiag:find` when you know a name but not its declaring type — especially before writing Harmony patches.

**Step 4 — Verify patches:** After loading your mod, use `dromadiag:harmony` to confirm your patches are applied and check for conflicts.

**Step 5 — Inspect live state:** Switch to DromadState (the companion tool) to verify that your mod's changes are actually having the intended effect at runtime.

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

---

## Companion Tool: DromadState

DromadState is the sister tool that covers everything DromaDiag does not. Where DromaDiag reads code structure, DromadState reads live game state.

| Need | Use |
|---|---|
| What methods exist on a type? | DromaDiag: `type` |
| What fields does a Part have? | DromaDiag: `type` |
| Where is this method declared? | DromaDiag: `find` |
| What patches are active? | DromaDiag: `harmony` |
| What Parts are on this creature right now? | DromadState: `part` |
| Did my XML mod actually apply? | DromadState: `xml` |
| What's in the current zone? | DromadState: `zone` |
| What's the player's current state? | DromadState: `player` |
| What are this faction's feeling values? | DromadState: `faction` |
| What handles this event? | DromadState: `event` |

Both tools write to their own txt files in the same save data folder and support command chaining with commas.

---

*DromaDiag — made by Mur. Caves of Qud modding tool.*
