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
    "shader": {
      "_ColorA": "#A1330E",
      "_ColorB": "#FF6300"
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

Put `MateriaLib.dll` and `CustomRecipesAPI.dll` in a `dependencies` folder next to `Crucible.csproj`, then:

```text
dotnet build -c Release -p:GamePath="C:\path\to\A Township Tale"
```

The build copies `Crucible.dll` into the game's `Mods` folder. Add `-p:CopyToMods=false` to skip that.
