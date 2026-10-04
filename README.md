# Crucible

Crucible is a data-driven modding framework for **A Township Tale** that allows custom materials, ingots, and smelting recipes to be created through JSON packs.

Crucible is currently in early development.

## Requirements

- [TavernLib](https://github.com/ModdingTavern/TavernLib)
- [MateriaLib](https://github.com/Circl-NoE/ATTMateriaLib)
- [CustomRecipesAPI](https://github.com/CG-Nik/CustomRecipesAPI)

## Pack Structure

Crucible packs go in:

```text
A Township Tale/
└─ UserData/
   └─ Crucible/
      └─ packs/
         └─ example/
            ├─ manifest.json
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
    "durabilityMultiplier": 1.15,
    "meltingPoint": 1800
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

## Smelting Recipe Example

```json
{
  "type": "smeltingRecipe",
  "id": "example:orangenium_smelt",
  "inputs": [
    { "item": "IronIngot", "count": 3 }
  ],
  "outputs": [
    { "item": "example:orangenium#ingot", "count": 1 }
  ],
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
