# Crucible

Crucible is a data-driven modding framework for **A Township Tale** that allows custom materials, ingots, and smelting recipes to be created through JSON packs.

Crucible is currently in early development.

The long-term goal is to allow a large amount of A Township Tale content to be created through data packs without requiring every creator to write a full standalone mod.

## Requirements

- [TavernLib](https://github.com/ModdingTavern/TavernLib)
- [MateriaLib](https://github.com/Circl-NoE/ATTMateriaLib) (1.3.0 or newer for `durabilityMultiplier`)
- [CustomRecipesAPI](https://github.com/CG-Nik/CustomRecipesAPI)

The server and every player need Crucible and the same packs, including each pack's `crucible.lock.json`.

## Pack Structure

Crucible packs go in:

```text
A Township Tale/
└─ UserData/
   └─ Crucible/
      └─ packs/
         └─ example/
            ├─ manifest.json
            ├─ crucible.lock.json
            ├─ materials/
            │  └─ orangenium.json
            └─ recipes/
               └─ orangenium_smelt.json
```

Each pack has its own folder.

The pack folder should contain a `manifest.json`, plus folders for the content it adds.

## Manifest

Example `manifest.json`:

```json
{
  "id": "example",
  "name": "Example Pack",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "An example Crucible content pack."
}
```

## Visual Shader Profiles

Crucible supports reusable shader/profile JSONs so multiple materials can share a base visual style and still allow per-material overrides.

- Place profiles under `shaders/` in your pack. Each profile is a `shaderProfile` JSON and may include a `kind` (metal/canvas/wood/leather), global properties, and per-slot property maps.
- Materials reference profiles from `visuals.profile` (a namespaced id like `example:metal_basic`).

Example profile reference in a material:

```json
"visuals": {
  "profile": "example:metal_basic",
  "shader": {
    "_ColorA": "#29354D"
  }
}
```

Precedence: profile defaults -> profile per-slot -> material global overrides (`visuals.overrides` / legacy `visuals.shader`) -> material per-slot (`visuals.materials`). If a profile or material sets a shader property that the runtime shader doesn't expose, Crucible logs a warning and continues.

Profiles are intentionally conservative — they configure existing Unity Materials (no custom shader compilation/AssetBundles in this first pass). Crucible attempts to set properties by name on the chosen Unity material; if a property name is not present the system will try a small set of common aliases (for example `_ColorA` will try `_BaseColor` and `_MainColor`) before logging a warning. This helps with packs that target slightly different shader property naming conventions.

KNOWN ISSUE: Emission properties (e.g., _EmissionColor) are currently unreliable and often do not appear on generated materials. I don't yet know why this happens; you will see related warnings in the logs. If you have any insight or a fix, please contribute.

## Material kinds

Crucible supports multiple material kinds: `metal`, `wood`, `leather`, and `canvas` (cloth). The example pack contains samples for each kind in folders:

```
example_pack/materials/wood/
example_pack/materials/leather/
example_pack/materials/cloth/
```

Adapters map kind -> friendly material slots (for example canvas uses `worn` and `cutout`, wood exposes `A`, `B`, `charred`, `burnt`, `ashen`, leather exposes a single slot). Material profiles and per-material `visuals.materials` can target those slot names.

## Reusing existing PhysicalMaterials

You can request Crucible to clone an existing `PhysicalMaterial` (vanilla or provided by other mods) and use it as the visual/source for a new material. This lets Crucible wrap and reuse MateriaLib materials without fully recreating their visuals.

Set `visuals.sourceMaterialHash` to the low-16 hash of the material you want to clone. Crucible will attempt to find and clone it at runtime; if not found it falls back to the normal creation path and logs a warning.

Example:

```json
"visuals": {
  "sourceMaterialHash": 1,
  "profile": "example:metal_sheen",
  "shader": { "_ColorA": "#8C6210" }
}
```

## Example pack notes

The example pack in `example_pack/` has been updated to demonstrate:
- shader profiles under `shaders/` (metal_fire, metal_frost, metal_void, metal_sheen, canvas_worn)
- fantasy names for example materials (to avoid colliding with in-game content)
- multiple material kinds under `materials/wood`, `materials/leather`, and `materials/cloth`

If you ship packs, include the generated `crucible.lock.json` so clients and servers agree on numeric hashes.

Wood materials: custom wood visuals historically failed when the source PhysicalMaterial did not expose the expected channel spots. Crucible now uses safer aliasing for shader properties and the example `example_pack/materials/wood/` files target the adapter slots `A`, `B`, `charred`, `burnt`, and `ashen`. If you still see missing visuals for wood, inspect the source material (use `visuals.sourceMaterialHash`) to ensure it provides the same channel spots; if not, set `visuals.materials` in your material JSON to supply explicit replacements for the desired slots.

The `id` is used as the namespace for everything inside the pack.

For example:

```text
example:orangenium
example:orangenium_smelt
```

## Material Example

```json
{
  "type": "material",
  "id": "example:orangenium",
  "displayName": "Orangenium",
  "kind": "metal",
  "properties": {
    "damageMultiplier": 1.25,
    "durabilityMultiplier": 1.15
  },
  "visuals": {
    "profile": "example:metal_sheen",
    "shader": {
      "_ColorA": "#A1330E",
      "_ColorB": "#FF6300",
      "_Glossiness": 0.6
    },
    "materials": {
      "A": { "_ColorA": "#A1330E" },
      "B": { "_ColorB": "#FF6300" }
    }
  },
  "ingot": {
    "enabled": true,
    "templateItemHash": 7204,
    "spawnableByCommand": true
  },
  "listed": true,
  "unlockAt": 0
}
```
Notes on the example above:
- `visuals.profile` points at a reusable shader profile (see `shaders/`).
- `visuals.shader` contains per-material global overrides; `visuals.materials` targets adapter slots (A/B/charred/etc.).
- If you want to clone an existing in-game `PhysicalMaterial` as the visual source, set `visuals.sourceMaterialHash` to the low-16 material hash and provide per-slot `visuals.materials` to override specific channels when necessary.

Small wood example (shows explicit per-slot materials):

```json
{
  "type": "material",
  "id": "example:verthorn",
  "displayName": "Verthorn",
  "kind": "wood",
  "visuals": {
    "profile": "example:wood_basic",
    "shader": {
      "_ColorA": "#8B5A2B",
      "_ColorB": "#5C3A20"
    },
    "materials": {
      "A": { "_ColorA": "#8B5A2B" },
      "B": { "_ColorB": "#5C3A20" },
      "charred": { "_ColorA": "#2B2B2B" }
    }
  },
  "properties": { "durabilityMultiplier": 1.0 },
  "listed": true
}
```

For available material properties, see MateriaLib's:
[MaterialConfig.cs](https://github.com/Circl-NoE/ATTMateriaLib/blob/Main/MaterialConfig.cs)

Notes:

- `durabilityMultiplier` only works with MateriaLib 1.3.0 or newer. The game itself never reads it.
- `meltingPoint` and `internalThermalConductivity` are never read by the game. The Recycler melts parts at `glowingEnd`.
- `unlockAt` is the smelter tier needed to cast the ingot: 0 is the default smelter, 1 to 3 are smelter gems 1 to 3.

## Smelting Recipe Example

```json
{
  "type": "smeltingRecipe",
  "id": "example:orangenium_smelt",
  "inputs": [
    { "item": "IronIngot", "count": 3 },
    { "item": "CopperOre", "count": 1 }
  ],
  "outputs": [
    { "item": "example:orangenium#ingot", "count": 1 }
  ],
  "duration": 5,
  "smelterUpgrades": [35404],
  "addToDefaultUpgrades": true
}
```

Custom Crucible ingots can also be used as ingredients for other alloys:

```json
{
  "type": "smeltingRecipe",
  "id": "example:embersteel_smelt",
  "inputs": [
    { "item": "example:orangenium#ingot", "count": 1 },
    { "item": "example:ferracite#ingot", "count": 1 }
  ],
  "outputs": [
    { "item": "example:embersteel#ingot", "count": 1 }
  ],
  "smelterUpgrades": [35404],
  "addToDefaultUpgrades": true
}
```

## Smelting Rules

The smelter has two ore docks and checks recipes before moulds. Crucible refuses a recipe that breaks these rules:

- At most 2 input types.
- No recipe whose only input is one ingot type. It would run instead of mould casting, so nobody could cast that ingot any more.
- The set of input types must be unique across all smelting recipes, vanilla ones included. The smelter only ever runs one of two recipes with the same inputs.

`duration` is the time per batch in seconds, and also the fuel the batch needs. Without it the recipe keeps the template recipe's duration.

## Numbers

Material, ingot item and ingot prefab numbers must be 1 to 65535, because the game sends them over the network in 16 bits. Recipe numbers stay below 2^31.

Crucible picks the numbers from the content ids and writes them to `crucible.lock.json` in the pack folder. Ship that file with your pack so servers and clients agree. You can also set them yourself with `hash`, `ingot.itemHash` and `ingot.prefabHash`.

## Building

Put `MateriaLib.dll` and `CustomRecipesAPI.dll` in a `lib` folder next to `Crucible.csproj`, then:

```text
dotnet build -c Release -p:GamePath="C:\path\to\A Township Tale"
```

The build copies `Crucible.dll` into the game's `Mods` folder. Add `-p:CopyToMods=false` to skip that.

## AI Disclosure

AI tools are used in the development of Crucible only for limited, non-programming tasks.

They are primarily used for tedious or repetitive work, including:

- Generating and improving code comments
- Writing and formatting documentation
- Creating example JSON files and content definitions
- Producing repetitive test/example data
- Cleaning up wording and explanatory text

**No AI-generated source code is used in Crucible.**

The implementation itself, including programming, debugging, architecture, integration with A Township Tale, and technical decision-making, is handled manually.

AI may be used to discuss ideas or help explain technical concepts, but generated code is not copied into or used as part of the project.

All AI-generated documentation, comments, and JSON content are reviewed before being included.

In short:

> AI is used for the tedious writing and repetitive work I don't feel like doing manually. It does not write the mod.
