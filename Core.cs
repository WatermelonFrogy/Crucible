using Alta;
using Alta.Blacksmithing;
using Alta.Inventory;
using Alta.Networking;
using MelonLoader;
using MelonLoader.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Directory = System.IO.Directory;
using File = System.IO.File;
using LibMaterial = MateriaLib.LibMaterial;
using MaterialConfig = MateriaLib.MaterialConfig;
using Path = System.IO.Path;
using SearchOption = System.IO.SearchOption;

[assembly: MelonInfo(typeof(Crucible.CrucibleMod), "Crucible", "0.1", "WatermelonFrogy")]
[assembly: MelonGame("Alta", "A Township Tale")]
[assembly: MelonAdditionalDependencies("MateriaLib", "CustomRecipesAPI")]

namespace Crucible
{
    public class CrucibleMod : MelonMod
    {
        internal const string ModVersion = "0.1";
        internal static readonly List<PackInfo> Packs = new List<PackInfo>();
        internal static readonly Dictionary<string, MaterialDef> MaterialsById = new Dictionary<string, MaterialDef>(StringComparer.Ordinal);
        internal static readonly Dictionary<int, string> UsedWideHashes = new Dictionary<int, string>();
        internal static readonly Dictionary<int, string> UsedNetworkHashes = new Dictionary<int, string>();
        internal static readonly Dictionary<int, string> UsedMaterialHashes = new Dictionary<int, string>();
        internal static readonly Dictionary<int, string> UsedItemHashes = new Dictionary<int, string>();

        private static bool _materialsReady;
        private static bool _recipesEventFired;
        private static bool _recipesBuilt;

        public override void OnInitializeMelon()
        {
            CLog.Info("Crucible " + ModVersion + " starting.");
            try
            {
                Packs.AddRange(PackLoader.LoadAll());
                foreach (PackInfo p in Packs)
                    foreach (MaterialDef m in p.Materials)
                        MaterialsById[m.Id] = m;
            }
            catch (Exception e)
            {
                CLog.Error("Pack loading failed: " + e);
            }

            MateriaLib.Main.SetupMaterial += RegisterMaterials;
            MateriaLib.Main.PostSetupIngots += OnMateriaIngotsReady;
            CustomRecipesAPI.Core.SetUpRecipes += OnRecipesEvent;

            CLog.Info("Discovered " + Packs.Count + " pack(s), " + Packs.Sum(p => p.Materials.Count) + " material(s), " + Packs.Sum(p => p.Recipes.Count) + " recipe(s).");
            CLog.Flush();
        }

        private static void RegisterMaterials()
        {
            int ok = 0, failed = 0;
            CLog.Info("Registering Crucible materials...");
            foreach (PackInfo pack in Packs)
            {
                foreach (MaterialDef def in pack.Materials)
                {
                    try
                    {
                        Hashing.AssignMaterialHashes(def);
                        MaterialBuilder.Build(def);
                        ok++;
                        CLog.Info("Registered material " + def.Id + " [material=" + def.Hash + (def.IngotEnabled ? ", item=" + def.IngotItemHash + ", prefab=" + def.IngotPrefabHash : "") + "]");
                    }
                    catch (Exception e)
                    {
                        failed++;
                        def.Failed = true;
                        CLog.Error(def.Where + ": registration failed: " + e);
                    }
                }
            }
            PackLoader.WriteLocks(Packs);
            CLog.Info("Material phase complete: " + ok + " ok, " + failed + " failed.");
            CLog.Flush();
        }

        private static void OnMateriaIngotsReady()
        {
            _materialsReady = true;
            CLog.Info("MateriaLib PostSetupIngots fired.");
            TryBuildRecipes();
        }

        private static void OnRecipesEvent()
        {
            _recipesEventFired = true;
            CLog.Info("CustomRecipesAPI SetUpRecipes fired.");
            TryBuildRecipes();
        }

        private static void TryBuildRecipes()
        {
            if (_recipesBuilt || !_materialsReady || !_recipesEventFired) return;
            _recipesBuilt = true;

            int ok = 0, failed = 0;
            CLog.Info("Registering Crucible smelting recipes...");
            foreach (PackInfo pack in Packs)
            {
                foreach (SmeltingRecipeDef def in pack.Recipes)
                {
                    try
                    {
                        Hashing.AssignRecipeHash(def);
                        RecipeBuilder.Build(def);
                        ok++;
                        CLog.Info("Registered recipe " + def.Id + " [hash=" + def.Hash + "]");
                    }
                    catch (Exception e)
                    {
                        failed++;
                        CLog.Error(def.Where + ": registration failed: " + e);
                    }
                }
            }
            PackLoader.WriteLocks(Packs);
            CLog.Info("Recipe phase complete: " + ok + " ok, " + failed + " failed.");
            CLog.Flush();
        }
    }

    internal sealed class PackInfo
    {
        public string Id, Name, Version, Author, Dir, RawHash, LockPath;
        public List<MaterialDef> Materials = new List<MaterialDef>();
        public List<SmeltingRecipeDef> Recipes = new List<SmeltingRecipeDef>();
        public Dictionary<string, int> Lock = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool LockDirty;
    }

    internal sealed class ShaderOverride
    {
        public string Name;
        public Vector4? Vec;
        public float? Num;
    }

    internal sealed class MaterialDef
    {
        public PackInfo Pack;
        public string File, Id, DisplayName;
        public LibMaterial.MaterialType Kind;
        public int? HashOverride, IngotItemHashOverride, IngotPrefabHashOverride;
        public int Hash, IngotItemHash, IngotPrefabHash;
        public int IngotTemplateItemHash = 7204;
        public Dictionary<PropertyInfo, double> Props = new Dictionary<PropertyInfo, double>();
        public Dictionary<string, double> PropsByName = new Dictionary<string, double>();
        public List<ShaderOverride> Shader = new List<ShaderOverride>();
        public bool IngotEnabled, IngotSpawnable, Listed = true, Failed;
        public int UnlockAt;
        public LibMaterial Built;
        public Item IngotItem;
        public string Where { get { return Pack.Id + "/" + File; } }
    }

    internal sealed class ItemRefDef
    {
        public string Text;
        public int? Hash;
        public int Count;
    }

    internal sealed class SmeltingRecipeDef
    {
        public PackInfo Pack;
        public string File, Id;
        public int? HashOverride, TemplateHash;
        public int Hash;
        public List<ItemRefDef> Inputs = new List<ItemRefDef>();
        public List<ItemRefDef> Outputs = new List<ItemRefDef>();
        public List<int> SmelterUpgrades = new List<int>();
        public bool AddToDefaultUpgrades = true;
        public float? Duration;
        public string Where { get { return Pack.Id + "/" + File; } }
    }

    internal static class CLog
    {
        private static readonly List<string> Lines = new List<string>();
        public static int Errors, Warnings;
        public static void Info(string m) { MelonLogger.Msg(m); Lines.Add("[INFO]  " + m); }
        public static void Warn(string m) { Warnings++; MelonLogger.Warning(m); Lines.Add("[WARN]  " + m); }
        public static void Error(string m) { Errors++; MelonLogger.Error(m); Lines.Add("[ERROR] " + m); }
        public static void Flush()
        {
            try
            {
                string path = Path.Combine(PackLoader.Root, "logs", "crucible.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, Lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal static class Hashing
    {
        public const int MaxNetwork = 65535;

        private static readonly int[][] Reserved =
        {
            new[] { 30227, 30541 },
            new[] { 35211, 35216 },
            new[] { 37301, 37309 },
            new[] { 49221, 49224 },
            new[] { 61791, 61795 },
            new[] { 62913, 62913 },
            new[] { 64101, 64199 }
        };

        public static int Wide(string key)
        {
            uint h = Fnv("crucible:" + key);
            long range = (long)int.MaxValue - 100000;
            return (int)(100000 + (h % range));
        }

        public static int Network(string key)
        {
            uint h = Fnv("crucible:net:" + key);
            return 10000 + (int)(h % (65535 - 10000));
        }

        private static uint Fnv(string text)
        {
            uint h = 2166136261;
            unchecked
            {
                foreach (byte b in Encoding.UTF8.GetBytes(text)) { h ^= b; h *= 16777619; }
            }
            return h;
        }

        private static int Locked(PackInfo p, string key, Func<string, int> generator, int? explicitValue, int max, Func<int, bool> taken)
        {
            if (explicitValue.HasValue)
            {
                p.Lock[key] = explicitValue.Value;
                p.LockDirty = true;
                return explicitValue.Value;
            }
            int value;
            if (p.Lock.TryGetValue(key, out value))
            {
                if (value <= max && !taken(value)) return value;
                CLog.Warn(p.Id + ": lock entry '" + key + "' = " + value + (value > max ? " is above " + max : " is already taken") + ", assigning a new number");
            }
            value = generator(key);
            for (int salt = 1; salt <= 32 && taken(value); salt++) value = generator(key + "#" + salt);
            if (taken(value)) throw new InvalidOperationException("no free number found for " + key + ", set one in the pack");
            p.Lock[key] = value;
            p.LockDirty = true;
            return value;
        }

        public static void AssignMaterialHashes(MaterialDef d)
        {
            d.Hash = Locked(d.Pack, d.Id, Network, d.HashOverride, MaxNetwork, MaterialTaken);
            ClaimMaterial(d.Hash, d.Id);
            if (!d.IngotEnabled) return;

            d.IngotItemHash = Locked(d.Pack, d.Id + "#ingot:item", Network, d.IngotItemHashOverride, MaxNetwork, ItemTaken);
            d.IngotPrefabHash = Locked(d.Pack, d.Id + "#ingot:prefab", Network, d.IngotPrefabHashOverride, MaxNetwork, PrefabTaken);
            ClaimItem(d.IngotItemHash, d.Id + "#ingot:item");
            ClaimNetwork(d.IngotPrefabHash, d.Id + "#ingot:prefab");
        }

        public static void AssignRecipeHash(SmeltingRecipeDef d)
        {
            d.Hash = Locked(d.Pack, d.Id, Wide, d.HashOverride, int.MaxValue, RecipeTaken);
            Claim(CrucibleMod.UsedWideHashes, d.Hash, d.Id, int.MaxValue);
            if (SmeltingRecipe.All.Any(x => x.Hash == (uint)d.Hash))
                throw new InvalidOperationException("hash " + d.Hash + " already belongs to an existing SmeltingRecipe");
        }

        private static bool InReserved(int h)
        {
            foreach (int[] r in Reserved) if (h >= r[0] && h <= r[1]) return true;
            return false;
        }

        private static bool Low16(uint hash, int h) { return (hash & 0xFFFF) == (uint)h; }

        private static bool MaterialExists(int h)
        {
            return PhysicalMaterial.All.Any(x => Low16(x.Hash, h)) ||
                   LibMaterial.NewMaterials.Any(m => m != null && m.physicalMaterial != null && Low16(m.physicalMaterial.Hash, h));
        }

        private static bool PrefabExists(int h)
        {
            PrefabManager.PrepareSpawnSetups();
            return PrefabManager.Exists((uint)h) || Resources.FindObjectsOfTypeAll<NetworkPrefab>().Any(x => x != null && x.Hash == (uint)h);
        }

        private static bool MaterialTaken(int h) { return InReserved(h) || CrucibleMod.UsedMaterialHashes.ContainsKey(h) || MaterialExists(h); }
        private static bool ItemTaken(int h) { return InReserved(h) || CrucibleMod.UsedItemHashes.ContainsKey(h) || Item.All.Any(x => Low16(x.Hash, h)); }
        private static bool PrefabTaken(int h) { return InReserved(h) || CrucibleMod.UsedNetworkHashes.ContainsKey(h) || PrefabExists(h); }
        private static bool RecipeTaken(int h) { return CrucibleMod.UsedWideHashes.ContainsKey(h) || SmeltingRecipe.All.Any(x => x.Hash == (uint)h); }

        private static void Claim(Dictionary<int, string> used, int hash, string id, int max)
        {
            if (hash < 1 || hash > max) throw new InvalidOperationException("hash " + hash + " must be 1.." + max);
            string other;
            if (used.TryGetValue(hash, out other) && other != id)
                throw new InvalidOperationException("hash " + hash + " collides with " + other);
            used[hash] = id;
            if (max == MaxNetwork && InReserved(hash)) CLog.Warn(id + ": hash " + hash + " is in a range another ATT mod already uses");
        }

        private static void ClaimMaterial(int hash, string id)
        {
            Claim(CrucibleMod.UsedMaterialHashes, hash, id, MaxNetwork);
            if (MaterialExists(hash))
                throw new InvalidOperationException("material hash " + hash + " (or one with the same low 16 bits) already exists");
        }

        private static void ClaimItem(int hash, string id)
        {
            Claim(CrucibleMod.UsedItemHashes, hash, id, MaxNetwork);
            if (Item.All.Any(x => Low16(x.Hash, hash)))
                throw new InvalidOperationException("item hash " + hash + " (or one with the same low 16 bits) already exists");
        }

        private static void ClaimNetwork(int hash, string id)
        {
            Claim(CrucibleMod.UsedNetworkHashes, hash, id, MaxNetwork);
            if (PrefabExists(hash))
                throw new InvalidOperationException("network prefab hash " + hash + " already exists");
        }
    }

    internal static class PackLoader
    {
        public static string Root { get { return Path.Combine(MelonEnvironment.UserDataDirectory, "Crucible"); } }
        public static string PacksDir { get { return Path.Combine(Root, "packs"); } }
        private const long MaxFileBytes = 256 * 1024;
        private const int MaxFilesPerPack = 500;
        private static readonly Regex PackIdRx = new Regex("^[a-z0-9_-]{1,48}$");
        private static readonly Regex ContentIdRx = new Regex("^([a-z0-9_-]{1,48}):([a-z0-9_]{1,64})$");
        private static Dictionary<string, PropertyInfo> _propMap;

        public static List<PackInfo> LoadAll()
        {
            Directory.CreateDirectory(PacksDir);
            var result = new List<PackInfo>();
            var packIds = new HashSet<string>(StringComparer.Ordinal);
            var contentIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (string dir in Directory.GetDirectories(PacksDir).OrderBy(x => x, StringComparer.Ordinal))
            {
                string folder = Path.GetFileName(dir);
                if (folder.StartsWith("_") || folder.StartsWith(".")) continue;
                try
                {
                    PackInfo p = LoadPack(dir, contentIds);
                    if (p == null) continue;
                    if (!packIds.Add(p.Id)) { CLog.Error(folder + ": duplicate pack id '" + p.Id + "'"); continue; }
                    result.Add(p);
                }
                catch (Exception e) { CLog.Error(folder + ": " + e.Message); }
            }
            return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
        }

        private static PackInfo LoadPack(string dir, HashSet<string> contentIds)
        {
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) { CLog.Error(Path.GetFileName(dir) + ": manifest.json missing"); return null; }
            var mo = Obj(MiniJson.Parse(ReadLimited(manifestPath)), "manifest.json");
            string id = Str(mo, "id");
            if (id == null || !PackIdRx.IsMatch(id)) throw new InvalidOperationException("manifest id must match [a-z0-9_-]{1,48}");

            var p = new PackInfo
            {
                Id = id,
                Dir = dir,
                Name = Str(mo, "name") ?? id,
                Version = Str(mo, "version") ?? "0.0.0",
                Author = Str(mo, "author") ?? "unknown",
                LockPath = Path.Combine(dir, "crucible.lock.json")
            };
            LoadLock(p);

            LoadFiles(Path.Combine(dir, "materials"), p, contentIds, ParseMaterial);
            LoadFiles(Path.Combine(dir, "recipes"), p, contentIds, ParseRecipe);
            p.RawHash = RawPackHash(dir);
            CLog.Info("Pack '" + p.Id + "' v" + p.Version + ": " + p.Materials.Count + " material(s), " + p.Recipes.Count + " recipe(s), hash " + p.RawHash.Substring(0, 12));
            return p;
        }

        private delegate void JsonFileParser(PackInfo p, string rel, Dictionary<string, object> o, HashSet<string> ids);
        private static void LoadFiles(string root, PackInfo p, HashSet<string> ids, JsonFileParser parser)
        {
            if (!Directory.Exists(root)) return;
            string[] files = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (files.Length > MaxFilesPerPack) throw new InvalidOperationException(p.Id + ": too many files in " + Path.GetFileName(root));
            foreach (string file in files)
            {
                string rel = file.Substring(p.Dir.Length).TrimStart('\\', '/').Replace('\\', '/');
                try { parser(p, rel, Obj(MiniJson.Parse(ReadLimited(file)), rel), ids); }
                catch (Exception e) { CLog.Error(p.Id + "/" + rel + ": " + e.Message); }
            }
        }

        private static void ParseMaterial(PackInfo p, string rel, Dictionary<string, object> o, HashSet<string> ids)
        {
            if (Str(o, "type") != "material") throw new InvalidOperationException("type must be 'material'");
            string id = RequiredId(p, rel, o, ids);
            var d = new MaterialDef { Pack = p, File = rel, Id = id, DisplayName = Str(o, "displayName") ?? id.Split(':')[1] };
            string kind = Str(o, "kind");
            if (kind == "metal") d.Kind = LibMaterial.MaterialType.metal;
            else if (kind == "wood") d.Kind = LibMaterial.MaterialType.wood;
            else if (kind == "leather") d.Kind = LibMaterial.MaterialType.leather;
            else if (kind == "canvas") d.Kind = LibMaterial.MaterialType.canvas;
            else throw new InvalidOperationException("kind must be metal, wood, leather, or canvas");

            d.HashOverride = OptInt(o, "hash", 1, Hashing.MaxNetwork);
            object props;
            if (o.TryGetValue("properties", out props) && props != null)
            {
                var po = Obj(props, "properties");
                foreach (var kv in po)
                {
                    PropertyInfo pi;
                    if (!PropMap().TryGetValue(kv.Key.ToLowerInvariant(), out pi)) { CLog.Warn(d.Where + ": unknown property '" + kv.Key + "'"); continue; }
                    double n = Num(kv.Value, "properties." + kv.Key);
                    d.Props[pi] = n;
                    d.PropsByName[pi.Name.ToLowerInvariant()] = n;
                }
            }

            object visuals;
            if (o.TryGetValue("visuals", out visuals) && visuals != null)
            {
                var vo = Obj(visuals, "visuals"); object shader;
                if (vo.TryGetValue("shader", out shader) && shader != null)
                    foreach (var kv in Obj(shader, "visuals.shader")) d.Shader.Add(ParseShader(kv.Key, kv.Value));
            }

            object ingot;
            if (o.TryGetValue("ingot", out ingot) && ingot != null)
            {
                var io = Obj(ingot, "ingot");
                d.IngotEnabled = Bool(io, "enabled", true);
                d.IngotTemplateItemHash = OptInt(io, "templateItemHash", 1, int.MaxValue) ?? 7204;
                d.IngotItemHashOverride = OptInt(io, "itemHash", 1, Hashing.MaxNetwork);
                d.IngotPrefabHashOverride = OptInt(io, "prefabHash", 1, 65535);
                d.IngotSpawnable = Bool(io, "spawnableByCommand", true);
            }
            d.Listed = Bool(o, "listed", true);
            d.UnlockAt = OptInt(o, "unlockAt", 0, 3) ?? 0;
            Sanity(d);
            p.Materials.Add(d);
        }

        private static void ParseRecipe(PackInfo p, string rel, Dictionary<string, object> o, HashSet<string> ids)
        {
            if (Str(o, "type") != "smeltingRecipe") throw new InvalidOperationException("type must be 'smeltingRecipe'");
            string id = RequiredId(p, rel, o, ids);
            var d = new SmeltingRecipeDef { Pack = p, File = rel, Id = id };
            d.HashOverride = OptInt(o, "hash", 1, int.MaxValue);
            d.TemplateHash = OptInt(o, "template", 1, int.MaxValue);
            d.AddToDefaultUpgrades = Bool(o, "addToDefaultUpgrades", true);
            object duration;
            if (o.TryGetValue("duration", out duration) && duration != null)
            {
                double sec = Num(duration, "duration");
                if (sec < 0.5 || sec > 3600) throw new InvalidOperationException("duration must be 0.5..3600 seconds");
                d.Duration = (float)sec;
            }
            d.Inputs = ParseItemRefs(o, "inputs");
            d.Outputs = ParseItemRefs(o, "outputs");
            if (d.Inputs.Count == 0 || d.Outputs.Count == 0) throw new InvalidOperationException("inputs and outputs must not be empty");
            if (d.Inputs.Count > 2) throw new InvalidOperationException("a smelter has two ore docks, so a recipe can take at most 2 input types");
            object ups;
            if (o.TryGetValue("smelterUpgrades", out ups) && ups != null)
            {
                var a = Arr(ups, "smelterUpgrades");
                foreach (object x in a) d.SmelterUpgrades.Add(Int(x, "smelterUpgrades[]", 1, int.MaxValue));
            }
            p.Recipes.Add(d);
        }

        private static List<ItemRefDef> ParseItemRefs(Dictionary<string, object> o, string key)
        {
            object raw; if (!o.TryGetValue(key, out raw) || raw == null) throw new InvalidOperationException("missing '" + key + "'");
            var result = new List<ItemRefDef>();
            foreach (object x in Arr(raw, key))
            {
                var io = Obj(x, key + "[]");
                var r = new ItemRefDef { Count = OptInt(io, "count", 1, int.MaxValue) ?? 1 };
                object item;
                if (!io.TryGetValue("item", out item) || item == null) throw new InvalidOperationException(key + "[].item is required");
                if (item is string) r.Text = (string)item;
                else if (item is Dictionary<string, object>) r.Hash = OptInt((Dictionary<string, object>)item, "hash", 1, int.MaxValue);
                else throw new InvalidOperationException(key + "[].item must be a string or {\"hash\":N}");
                if (r.Text == null && !r.Hash.HasValue) throw new InvalidOperationException(key + "[].item hash missing");
                result.Add(r);
            }
            return result;
        }

        private static Dictionary<string, PropertyInfo> PropMap()
        {
            if (_propMap != null) return _propMap;
            _propMap = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            foreach (PropertyInfo pi in typeof(MaterialConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (pi.CanWrite) _propMap[pi.Name.ToLowerInvariant()] = pi;
            PropertyInfo misspelled;
            if (_propMap.TryGetValue("recievethermalconductivity", out misspelled)) _propMap["receivethermalconductivity"] = misspelled;
            return _propMap;
        }

        private static ShaderOverride ParseShader(string name, object value)
        {
            if (!name.StartsWith("_") || name.Length > 64) throw new InvalidOperationException("shader key '" + name + "' is invalid");
            var x = new ShaderOverride { Name = name };
            Vector4 v;
            if (value is string && TryColor((string)value, out v)) x.Vec = v;
            else if (value is double) x.Num = (float)(double)value;
            else if (value is List<object> && TryVec4((List<object>)value, out v)) x.Vec = v;
            else throw new InvalidOperationException("shader value for '" + name + "' must be color, number, or vec4");
            return x;
        }

        private static string RequiredId(PackInfo p, string rel, Dictionary<string, object> o, HashSet<string> ids)
        {
            string id = Str(o, "id"); Match m = id == null ? null : ContentIdRx.Match(id);
            if (m == null || !m.Success) throw new InvalidOperationException("id must look like namespace:name");
            if (m.Groups[1].Value != p.Id) throw new InvalidOperationException("id namespace must equal pack id '" + p.Id + "'");
            if (!ids.Add(id)) throw new InvalidOperationException("duplicate content id '" + id + "'");
            return id;
        }

        private static void Sanity(MaterialDef d)
        {
            double a, b;
            if (d.PropsByName.TryGetValue("glowingstart", out a) && d.PropsByName.TryGetValue("glowingend", out b) && a >= b) CLog.Warn(d.Where + ": glowingStart >= glowingEnd");
            if (d.PropsByName.TryGetValue("density", out a) && a <= 0) CLog.Warn(d.Where + ": density <= 0");
            if (d.PropsByName.ContainsKey("meltingpoint")) CLog.Warn(d.Where + ": meltingPoint is never read by the game (the Recycler melts parts at glowingEnd)");
            if (d.PropsByName.ContainsKey("internalthermalconductivity")) CLog.Warn(d.Where + ": internalThermalConductivity is never read by the game");
        }

        private static void LoadLock(PackInfo p)
        {
            if (!File.Exists(p.LockPath)) return;
            try
            {
                var o = Obj(MiniJson.Parse(ReadLimited(p.LockPath)), "crucible.lock.json"); object h;
                if (!o.TryGetValue("hashes", out h) || h == null) return;
                foreach (var kv in Obj(h, "hashes")) p.Lock[kv.Key] = Int(kv.Value, "hashes." + kv.Key, 1, int.MaxValue);
            }
            catch (Exception e) { CLog.Warn(p.Id + ": ignored bad lock file: " + e.Message); }
        }

        public static void WriteLocks(IEnumerable<PackInfo> packs)
        {
            foreach (PackInfo p in packs)
            {
                if (!p.LockDirty) continue;
                var keys = p.Lock.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                var sb = new StringBuilder(); sb.Append("{\n  \"version\": 2,\n  \"hashes\": {\n");
                for (int i = 0; i < keys.Length; i++) sb.Append("    \"").Append(keys[i]).Append("\": ").Append(p.Lock[keys[i]].ToString(CultureInfo.InvariantCulture)).Append(i + 1 < keys.Length ? ",\n" : "\n");
                sb.Append("  }\n}\n"); File.WriteAllText(p.LockPath, sb.ToString(), new UTF8Encoding(false)); p.LockDirty = false;
            }
        }

        private static string RawPackHash(string dir)
        {
            using (SHA256 sha = SHA256.Create()) using (var ms = new System.IO.MemoryStream())
            {
                foreach (string f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    string rel = f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/'); byte[] n = Encoding.UTF8.GetBytes(rel); byte[] d = File.ReadAllBytes(f);
                    ms.Write(n, 0, n.Length); ms.WriteByte(0); ms.Write(d, 0, d.Length); ms.WriteByte(0);
                }
                return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string ReadLimited(string path) { if (new System.IO.FileInfo(path).Length > MaxFileBytes) throw new InvalidOperationException("file exceeds 256 KB"); return File.ReadAllText(path, Encoding.UTF8); }
        private static Dictionary<string, object> Obj(object x, string where) { var o = x as Dictionary<string, object>; if (o == null) throw new InvalidOperationException(where + " must be an object"); return o; }
        private static List<object> Arr(object x, string where) { var a = x as List<object>; if (a == null) throw new InvalidOperationException(where + " must be an array"); return a; }
        private static string Str(Dictionary<string, object> o, string k) { object x; return o.TryGetValue(k, out x) ? x as string : null; }
        private static bool Bool(Dictionary<string, object> o, string k, bool d) { object x; if (!o.TryGetValue(k, out x) || x == null) return d; if (!(x is bool)) throw new InvalidOperationException(k + " must be boolean"); return (bool)x; }
        private static double Num(object x, string where) { if (!(x is double)) throw new InvalidOperationException(where + " must be a number"); return (double)x; }
        private static int Int(object x, string where, int min, int max) { double n = Num(x, where); if (n != Math.Floor(n) || n < min || n > max) throw new InvalidOperationException(where + " must be integer " + min + ".." + max); return (int)n; }
        private static int? OptInt(Dictionary<string, object> o, string k, int min, int max) { object x; if (!o.TryGetValue(k, out x) || x == null) return null; return Int(x, k, min, max); }
        private static bool TryColor(string s, out Vector4 v) { v = default(Vector4); if (s == null || !s.StartsWith("#")) return false; string h = s.Substring(1); if (h.Length != 6 && h.Length != 8) return false; float[] c = { 0, 0, 0, 1 }; for (int i = 0; i < h.Length / 2; i++) { int n; if (!int.TryParse(h.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return false; c[i] = n / 255f; } v = new Vector4(c[0], c[1], c[2], c[3]); return true; }
        private static bool TryVec4(List<object> a, out Vector4 v) { v = default(Vector4); if (a.Count != 4 || a.Any(x => !(x is double))) return false; v = new Vector4((float)(double)a[0], (float)(double)a[1], (float)(double)a[2], (float)(double)a[3]); return true; }
    }

    internal static class MaterialBuilder
    {
        public static void Build(MaterialDef d)
        {
            var lib = new LibMaterial(d.DisplayName, d.Hash, d.Kind);
            var cfg = new MaterialConfig();
            foreach (var kv in d.Props)
            {
                Type t = Nullable.GetUnderlyingType(kv.Key.PropertyType) ?? kv.Key.PropertyType;
                object value = t == typeof(uint) ? (object)(uint)kv.Value : Convert.ChangeType(kv.Value, t, CultureInfo.InvariantCulture);
                kv.Key.SetValue(cfg, value, null);
            }
            lib.Configure(cfg);
            if (d.PropsByName.ContainsKey("durabilitymultiplier"))
            {
                FieldInfo useDurability = typeof(LibMaterial).GetField("useDurabilityMultiplier");
                if (useDurability != null) useDurability.SetValue(lib, true);
                else CLog.Warn(d.Where + ": durabilityMultiplier needs MateriaLib 1.3.0 or newer, the game itself never reads it");
            }

            if (d.Kind == LibMaterial.MaterialType.metal && d.Shader.Count > 0)
            {
                Material main = UnityEngine.Object.Instantiate(lib.GetMaterialFromChannelSpot(0, 1));
                Material atlas = UnityEngine.Object.Instantiate(lib.GetMaterialFromChannelSpot(0, 2));
                main.name = d.DisplayName; atlas.name = d.DisplayName + " NonForging";
                foreach (ShaderOverride ov in d.Shader) foreach (Material m in new[] { main, atlas })
                {
                    if (!m.HasProperty(ov.Name)) { CLog.Warn(d.Where + ": shader property '" + ov.Name + "' missing on " + m.name); continue; }
                    if (ov.Vec.HasValue) m.SetVector(ov.Name, ov.Vec.Value); else m.SetFloat(ov.Name, ov.Num.Value);
                }
                lib.ReplaceAllMaterials(new[] { main, atlas }, atlas);
            }

            lib.addToList = d.Listed;
            lib.unlockAt = d.UnlockAt;
            RuntimeCompat.TrySetShowingInList(lib.physicalMaterial, d.Listed);

            if (d.IngotEnabled)
            {
                GameObject ingot = IngotBuilder.Build(d, lib);
                lib.ingot = ingot;
                Pickup ingotPickup = ingot.GetComponent<Pickup>();
                d.IngotItem = RuntimeCompat.GetPickupItem(ingotPickup);
                if (d.IngotItem == null) throw new InvalidOperationException("generated ingot Pickup does not expose an Item reference");
                if (!CustomRecipesAPI.Core.itemsToAddToSmelter.Contains(d.IngotItem)) CustomRecipesAPI.Core.itemsToAddToSmelter.Add(d.IngotItem);
                RuntimeCompat.SetSourceMaterialHash(lib.physicalMaterial, d.IngotPrefabHash);
            }

            d.Built = lib;
            LibMaterial.NewMaterials.Add(lib);
        }
    }

    internal static class RuntimePrefabStorage
    {
        private static GameObject _root;

        public static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new GameObject("__Crucible_RuntimePrefabs");
                    UnityEngine.Object.DontDestroyOnLoad(_root);
                    _root.SetActive(false);
                }
                return _root.transform;
            }
        }
    }

    internal static class IngotBuilder
    {
        public static GameObject Build(MaterialDef d, LibMaterial lib)
        {
            Item template = null;

            CustomRecipesAPI.Core.VanillaOreAndIngotItems.TryGetValue(d.IngotTemplateItemHash, out template);
            if (template == null) template = Item.All.FirstOrDefault(x => x.Hash == (uint)d.IngotTemplateItemHash);
            if (template == null) throw new InvalidOperationException("ingot template item " + d.IngotTemplateItemHash + " not found");

            NetworkPrefab templatePrefab = RuntimeCompat.GetItemPrefab(template);
            if (templatePrefab == null) throw new InvalidOperationException("template item has no NetworkPrefab");

            GameObject go = UnityEngine.Object.Instantiate(templatePrefab.gameObject, RuntimePrefabStorage.Root);
            go.name = "Crucible_" + d.Id.Replace(':', '_') + "_Ingot";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            NetworkPrefab np = go.GetComponent<NetworkPrefab>();
            Pickup pickup = go.GetComponent<Pickup>();
            if (np == null || pickup == null) throw new InvalidOperationException("template ingot prefab lacks NetworkPrefab or Pickup");

            Item item = UnityEngine.Object.Instantiate(template);
            item.name = d.DisplayName + " Ingot";

            RuntimeCompat.SetHash(item, d.IngotItemHash);
            RuntimeCompat.SetHash(np, d.IngotPrefabHash);
            RuntimeCompat.ReplacePhysicalMaterialReferences(item, go, RuntimeCompat.TemplateMaterial(template), lib.physicalMaterial);
            RuntimeCompat.SetPickupItem(pickup, item);
            RuntimeCompat.TrySetSpawnableByCommand(np, d.IngotSpawnable);

            RuntimeCompat.RegisterPrefab(np);
            RuntimeCompat.SetItemPrefab(item, np);
            RuntimeCompat.RegisterItem(item);

            Item resolvedItem = Item.All.FirstOrDefault(x => x.Hash == (uint)d.IngotItemHash);
            if (resolvedItem == null)
                throw new InvalidOperationException("generated ingot Item was not accepted by ATT's Item registry");

            NetworkPrefab resolvedPrefab = RuntimeCompat.GetItemPrefab(resolvedItem);
            if (resolvedPrefab == null || resolvedPrefab.Hash != (uint)d.IngotPrefabHash)
                throw new InvalidOperationException(
                    "generated ingot Item resolves to prefab " +
                    (resolvedPrefab == null ? "<null>" : resolvedPrefab.Hash.ToString(CultureInfo.InvariantCulture)) +
                    " instead of " + d.IngotPrefabHash.ToString(CultureInfo.InvariantCulture));

            Pickup resolvedPickup = np.GetComponent<Pickup>();
            Item pickupItem = RuntimeCompat.GetPickupItem(resolvedPickup);
            if (pickupItem == null || pickupItem.Hash != (uint)d.IngotItemHash)
                throw new InvalidOperationException("generated NetworkPrefab Pickup does not resolve to the generated Item");

            if (!go.activeSelf)
                throw new InvalidOperationException("generated runtime prefab unexpectedly has activeSelf=false");
            if (go.activeInHierarchy)
                CLog.Warn("Generated runtime prefab storage is active in hierarchy; source shell may be visible in-world.");

            CLog.Info("Generated ingot " + d.Id +
                      " registered [item=" + d.IngotItemHash +
                      ", prefab=" + d.IngotPrefabHash +
                      ", templateItem=" + d.IngotTemplateItemHash + "]");
            return go;
        }
    }

    internal static class RecipeBuilder
    {
        public static void Build(SmeltingRecipeDef d)
        {
            Item[] inputs = d.Inputs.Select(x => ResolveItem(x, d.Where)).ToArray();
            Item[] outputs = d.Outputs.Select(x => ResolveItem(x, d.Where)).ToArray();
            if (inputs.Any(x => outputs.Contains(x))) CLog.Warn(d.Where + ": an item appears in both inputs and outputs");
            CheckSmelterRules(d, inputs);

            SmeltingRecipe template = d.TemplateHash.HasValue ? SmeltingRecipe.All.FirstOrDefault(x => x.Hash == (uint)d.TemplateHash.Value) : SmeltingRecipe.All.FirstOrDefault();
            if (template == null) throw new InvalidOperationException("no SmeltingRecipe template available");
            SmeltingRecipe recipe = UnityEngine.Object.Instantiate(template);
            recipe.name = "Crucible_" + d.Id.Replace(':', '_');
            RuntimeCompat.SetHash(recipe, d.Hash);
            RuntimeCompat.ReplaceItemCounts(recipe, "input", inputs, d.Inputs.Select(x => x.Count).ToArray());
            RuntimeCompat.ReplaceItemCounts(recipe, "output", outputs, d.Outputs.Select(x => x.Count).ToArray());
            if (d.Duration.HasValue) RuntimeCompat.SetRecipeDuration(recipe, d.Duration.Value);

            SmelterUpgrades first = null;
            if (d.SmelterUpgrades.Count > 0)
            {
                first = SmelterUpgrades.All.FirstOrDefault(x => x.Hash == (uint)d.SmelterUpgrades[0]);
                if (first == null) throw new InvalidOperationException("SmelterUpgrades " + d.SmelterUpgrades[0] + " not found");
            }

            CustomRecipesAPI.Core.SetUpSmeltingRecipe(recipe, inputs, outputs, first, d.AddToDefaultUpgrades);
            for (int i = 1; i < d.SmelterUpgrades.Count; i++)
            {
                SmelterUpgrades up = SmelterUpgrades.All.FirstOrDefault(x => x.Hash == (uint)d.SmelterUpgrades[i]);
                if (up == null) throw new InvalidOperationException("SmelterUpgrades " + d.SmelterUpgrades[i] + " not found");
                CustomRecipesAPI.Core.AddSmeltingRecipeToSmelterUpgrades(recipe, up, false);
            }
        }

        private static void CheckSmelterRules(SmeltingRecipeDef d, Item[] inputs)
        {
            if (inputs.Distinct().Count() != inputs.Length)
                throw new InvalidOperationException(d.Where + ": the same item is listed twice in inputs");
            if (inputs.Length == 1 && inputs[0].GetComponent<Ingot>() != null)
                throw new InvalidOperationException(d.Where + ": a recipe whose only input is one ingot type runs before mould casting, so players could no longer cast " + inputs[0].name + " into moulds; add a second input type");
            var wanted = new HashSet<Item>(inputs);
            foreach (SmeltingRecipe r in SmeltingRecipe.All)
            {
                if (r == null || r.Input == null) continue;
                var have = new HashSet<Item>(r.Input.Where(x => x != null && x.Item != null).Select(x => x.Item));
                if (have.SetEquals(wanted))
                    throw new InvalidOperationException(d.Where + ": recipe '" + r.name + "' already uses exactly these input types; the smelter only ever runs one of them");
            }
        }

        private static Item ResolveItem(ItemRefDef r, string where)
        {
            if (r.Hash.HasValue)
            {
                Item byHash = Item.All.FirstOrDefault(x => x.Hash == (uint)r.Hash.Value);
                if (byHash == null) throw new InvalidOperationException(where + ": item hash " + r.Hash.Value + " not found");
                return byHash;
            }

            string s = r.Text;
            if (s.EndsWith("#ingot", StringComparison.Ordinal))
            {
                string materialId = s.Substring(0, s.Length - 6);
                MaterialDef m;
                if (!CrucibleMod.MaterialsById.TryGetValue(materialId, out m) || m.Failed || m.IngotItem == null)
                    throw new InvalidOperationException(where + ": Crucible ingot '" + s + "' is unresolved");
                return m.IngotItem;
            }

            CustomRecipesAPI.Core.VanillaOreAndIngotIndexers e;
            if (Enum.TryParse(s, false, out e))
            {
                int hash = (int)e;
                Item item;
                if (CustomRecipesAPI.Core.VanillaOreAndIngotItems.TryGetValue(hash, out item)) return item;
            }

            throw new InvalidOperationException(where + ": unknown item reference '" + s + "'");
        }
    }

    internal static class RuntimeCompat
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void SetHash(object obj, int hash)
        {
            Type t = obj.GetType();
            PropertyInfo p = t.GetProperty("Hash", Any);
            if (p != null && p.CanWrite) { p.SetValue(obj, (uint)hash, null); return; }
            FieldInfo f = t.GetField("hash", Any) ?? t.GetField("Hash", Any);
            if (f == null) throw new MissingFieldException(t.FullName, "hash/Hash");
            object value = f.FieldType == typeof(uint) ? (object)(uint)hash : hash;
            f.SetValue(obj, value);
        }

        public static void TrySetShowingInList(PhysicalMaterial pm, bool value)
        {
            try
            {
                FieldInfo f = typeof(PhysicalMaterial).GetField("isShowingInList", Any);
                if (f != null) f.SetValue(pm, value);
                else
                {
                    PropertyInfo p = typeof(PhysicalMaterial).GetProperty("isShowingInList", Any);
                    if (p != null && p.CanWrite) p.SetValue(pm, value, null);
                }
            }
            catch (Exception e) { CLog.Warn("Could not set isShowingInList: " + e.Message); }
        }

        public static Item GetPickupItem(Pickup pickup)
        {
            if (pickup == null) return null;
            Type t = pickup.GetType();

            FieldInfo f = AllFields(t).FirstOrDefault(x =>
                string.Equals(x.Name, "item", StringComparison.OrdinalIgnoreCase) &&
                typeof(Item).IsAssignableFrom(x.FieldType));
            if (f == null) f = AllFields(t).FirstOrDefault(x => typeof(Item).IsAssignableFrom(x.FieldType));
            if (f != null) return f.GetValue(pickup) as Item;

            PropertyInfo p = AllProperties(t).FirstOrDefault(x =>
                x.CanRead && x.GetIndexParameters().Length == 0 &&
                string.Equals(x.Name, "item", StringComparison.OrdinalIgnoreCase) &&
                typeof(Item).IsAssignableFrom(x.PropertyType));
            if (p == null) p = AllProperties(t).FirstOrDefault(x =>
                x.CanRead && x.GetIndexParameters().Length == 0 && typeof(Item).IsAssignableFrom(x.PropertyType));
            return p == null ? null : p.GetValue(pickup, null) as Item;
        }

        public static void SetPickupItem(Pickup pickup, Item item)
        {
            if (pickup == null) throw new ArgumentNullException(nameof(pickup));
            Type t = pickup.GetType();

            FieldInfo f = AllFields(t).FirstOrDefault(x =>
                string.Equals(x.Name, "item", StringComparison.OrdinalIgnoreCase) &&
                typeof(Item).IsAssignableFrom(x.FieldType));
            if (f == null) f = AllFields(t).FirstOrDefault(x => typeof(Item).IsAssignableFrom(x.FieldType));
            if (f != null)
            {
                f.SetValue(pickup, item);
                return;
            }

            PropertyInfo p = AllProperties(t).FirstOrDefault(x =>
                x.CanWrite && x.GetIndexParameters().Length == 0 &&
                string.Equals(x.Name, "item", StringComparison.OrdinalIgnoreCase) &&
                typeof(Item).IsAssignableFrom(x.PropertyType));
            if (p == null) p = AllProperties(t).FirstOrDefault(x =>
                x.CanWrite && x.GetIndexParameters().Length == 0 && typeof(Item).IsAssignableFrom(x.PropertyType));
            if (p != null)
            {
                p.SetValue(pickup, item, null);
                return;
            }

            throw new MissingMemberException(t.FullName, "Item reference");
        }

        public static void TrySetSpawnableByCommand(NetworkPrefab prefab, bool value)
        {
            if (prefab == null) return;
            try
            {
                Type t = prefab.GetType();
                string[] names = { "canBeSpawnedThroughCommand", "CanBeSpawnedThroughCommand", "spawnableByCommand", "SpawnableByCommand" };

                foreach (string name in names)
                {
                    FieldInfo f = t.GetField(name, Any);
                    if (f != null && f.FieldType == typeof(bool))
                    {
                        f.SetValue(prefab, value);
                        return;
                    }

                    PropertyInfo p = t.GetProperty(name, Any);
                    if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                    {
                        p.SetValue(prefab, value, null);
                        return;
                    }
                }

                FieldInfo fallbackField = t.GetFields(Any).FirstOrDefault(f =>
                    f.FieldType == typeof(bool) &&
                    f.Name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    f.Name.IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0);
                if (fallbackField != null)
                {
                    fallbackField.SetValue(prefab, value);
                    return;
                }

                PropertyInfo fallbackProperty = t.GetProperties(Any).FirstOrDefault(p =>
                    p.CanWrite && p.PropertyType == typeof(bool) &&
                    p.Name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    p.Name.IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0);
                if (fallbackProperty != null)
                {
                    fallbackProperty.SetValue(prefab, value, null);
                    return;
                }

                if (value) CLog.Warn("NetworkPrefab has no discoverable command-spawn flag; spawnableByCommand ignored.");
            }
            catch (Exception e)
            {
                CLog.Warn("Could not set command-spawn flag: " + e.Message);
            }
        }

        public static NetworkPrefab GetItemPrefab(Item item)
        {
            if (item == null) return null;

            try
            {
                NetworkPrefab p = item.Prefab;
                if (p != null) return p;
            }
            catch { }

            foreach (PropertyInfo p in AllProperties(item.GetType()))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0 || !typeof(NetworkPrefab).IsAssignableFrom(p.PropertyType)) continue;
                try
                {
                    NetworkPrefab value = p.GetValue(item, null) as NetworkPrefab;
                    if (value != null) return value;
                }
                catch { }
            }
            foreach (FieldInfo f in AllFields(item.GetType()))
            {
                if (!typeof(NetworkPrefab).IsAssignableFrom(f.FieldType)) continue;
                try
                {
                    NetworkPrefab value = f.GetValue(item) as NetworkPrefab;
                    if (value != null) return value;
                }
                catch { }
            }
            return null;
        }

        public static void SetItemPrefab(Item item, NetworkPrefab prefab)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));

            NetworkPrefab oldPrefab = GetItemPrefab(item);
            bool touched = false;

            foreach (FieldInfo f in AllFields(item.GetType()))
            {
                if (!typeof(NetworkPrefab).IsAssignableFrom(f.FieldType)) continue;
                try
                {
                    object current = f.GetValue(item);
                    if (ReferenceEquals(current, oldPrefab) ||
                        f.Name.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        f.SetValue(item, prefab);
                        touched = true;
                    }
                }
                catch { }
            }

            foreach (PropertyInfo p in AllProperties(item.GetType()))
            {
                if (!p.CanWrite || p.GetIndexParameters().Length != 0 ||
                    !typeof(NetworkPrefab).IsAssignableFrom(p.PropertyType)) continue;
                try
                {
                    object current = p.CanRead ? p.GetValue(item, null) : null;
                    if (ReferenceEquals(current, oldPrefab) ||
                        p.Name.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        p.SetValue(item, prefab, null);
                        touched = true;
                    }
                }
                catch { }
            }

            foreach (FieldInfo f in AllFields(item.GetType()))
            {
                if (f.Name.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) < 0 || !IsIntegral(f.FieldType)) continue;
                try
                {
                    f.SetValue(item, ConvertHash(prefab.Hash, f.FieldType));
                    touched = true;
                }
                catch { }
            }
            foreach (PropertyInfo p in AllProperties(item.GetType()))
            {
                if (!p.CanWrite || p.GetIndexParameters().Length != 0 ||
                    p.Name.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) < 0 ||
                    !IsIntegral(p.PropertyType)) continue;
                try
                {
                    p.SetValue(item, ConvertHash(prefab.Hash, p.PropertyType), null);
                    touched = true;
                }
                catch { }
            }

            NetworkPrefab resolved = GetItemPrefab(item);
            if (resolved == null || resolved.Hash != prefab.Hash)
            {
                throw new InvalidOperationException(
                    "could not bind Item '" + item.name + "' to generated NetworkPrefab " +
                    prefab.Hash.ToString(CultureInfo.InvariantCulture) +
                    (touched ? " (candidate fields were written but Item.Prefab still resolves elsewhere)" :
                               " (no writable prefab reference/hash member was found)"));
            }
        }

        public static void RegisterItem(Item item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            TryInvokeStaticNoArg(typeof(Item), "CheckItems");

            IDictionary items = GetHashedRegistry(typeof(Item));
            object key = ConvertHash(item.Hash, DictionaryKeyType(items) ?? typeof(uint));

            if (items.Contains(key))
            {
                object existing = items[key];
                if (!ReferenceEquals(existing, item))
                    throw new InvalidOperationException("Item hash " + item.Hash + " is already registered to another Item");
            }
            else
            {
                items.Add(key, item);
            }

            Item resolved = Item.All.FirstOrDefault(x => x.Hash == item.Hash);
            if (resolved == null)
                throw new InvalidOperationException("Item registry accepted hash " + item.Hash + " but Item.All cannot resolve it");
        }

        public static void RegisterPrefab(NetworkPrefab prefab)
        {
            PrefabManager.PrepareSpawnSetups();
            NetworkPrefab existing = PrefabManager.GetPrefab(prefab.Hash);
            if (existing != null && !ReferenceEquals(existing, prefab))
                throw new InvalidOperationException("network prefab hash " + prefab.Hash + " already belongs to " + existing.name);
            if (existing == null)
            {
                MethodInfo add = typeof(PrefabManager).GetMethod("AddToPrefabMap", AnyStatic);
                if (add == null) throw new MissingMethodException(typeof(PrefabManager).FullName, "AddToPrefabMap");
                add.Invoke(null, new object[] { new[] { prefab } });
            }
            if (!ReferenceEquals(PrefabManager.GetPrefab(prefab.Hash), prefab))
                throw new InvalidOperationException("PrefabManager did not accept network prefab " + prefab.Hash);
            CLog.Info("Registered NetworkPrefab " + prefab.Hash + " via PrefabManager.AddToPrefabMap");
        }

        public static bool ReplaceReference(object obj, object oldValue, object newValue)
        {
            if (obj == null || oldValue == null || newValue == null) return false;
            bool changed = false;
            foreach (FieldInfo f in AllFields(obj.GetType()))
            {
                if (!f.FieldType.IsAssignableFrom(newValue.GetType())) continue;
                try { if (ReferenceEquals(f.GetValue(obj), oldValue)) { f.SetValue(obj, newValue); changed = true; } } catch { }
            }
            foreach (PropertyInfo p in AllProperties(obj.GetType()))
            {
                if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length != 0 ||
                    !p.PropertyType.IsAssignableFrom(newValue.GetType())) continue;
                try { if (ReferenceEquals(p.GetValue(obj, null), oldValue)) { p.SetValue(obj, newValue, null); changed = true; } } catch { }
            }
            return changed;
        }

        private static IEnumerable<FieldInfo> AllFields(Type t)
        {
            for (Type x = t; x != null; x = x.BaseType)
                foreach (FieldInfo f in x.GetFields(Any | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        private static IEnumerable<PropertyInfo> AllProperties(Type t)
        {
            for (Type x = t; x != null; x = x.BaseType)
                foreach (PropertyInfo p in x.GetProperties(Any | BindingFlags.DeclaredOnly))
                    yield return p;
        }

        private static bool IsIntegral(Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t == typeof(byte) || t == typeof(sbyte) ||
                   t == typeof(short) || t == typeof(ushort) ||
                   t == typeof(int) || t == typeof(uint) ||
                   t == typeof(long) || t == typeof(ulong);
        }

        private static object ConvertHash(uint hash, Type t)
        {
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t == typeof(byte)) return checked((byte)hash);
            if (t == typeof(sbyte)) return checked((sbyte)hash);
            if (t == typeof(short)) return checked((short)hash);
            if (t == typeof(ushort)) return checked((ushort)hash);
            if (t == typeof(int)) return checked((int)hash);
            if (t == typeof(uint)) return hash;
            if (t == typeof(long)) return (long)hash;
            if (t == typeof(ulong)) return (ulong)hash;
            return Convert.ChangeType(hash, t, CultureInfo.InvariantCulture);
        }

        private static void TryInvokeStaticNoArg(Type t, string name)
        {
            try
            {
                MethodInfo m = t.GetMethod(name, AnyStatic, null, Type.EmptyTypes, null);
                if (m != null) m.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException ?? e;
            }
        }

        private static IDictionary GetHashedRegistry(Type valueType)
        {
            return GetHashedRegistry(valueType, true);
        }

        private static IDictionary GetHashedRegistry(Type valueType, bool required)
        {
            Type open = typeof(HashedGeneralValue<>);
            Type closed;
            try { closed = open.MakeGenericType(valueType); }
            catch
            {
                if (required) throw;
                return null;
            }

            FieldInfo field = null;
            for (Type x = closed; x != null && field == null; x = x.BaseType)
            {
                field = x.GetField("items", AnyStatic | BindingFlags.DeclaredOnly);
                if (field == null)
                    field = x.GetFields(AnyStatic | BindingFlags.DeclaredOnly)
                        .FirstOrDefault(f => typeof(IDictionary).IsAssignableFrom(f.FieldType));
            }

            if (field == null)
            {
                if (required) throw new MissingFieldException(closed.FullName, "items");
                return null;
            }

            IDictionary dict = field.GetValue(null) as IDictionary;
            if (dict == null && required)
                throw new InvalidOperationException(closed.FullName + ".items is not an IDictionary");
            return dict;
        }

        private static Type DictionaryKeyType(IDictionary dictionary)
        {
            if (dictionary == null) return null;
            Type t = dictionary.GetType();
            if (t.IsGenericType)
            {
                Type[] args = t.GetGenericArguments();
                if (args.Length == 2) return args[0];
            }
            foreach (DictionaryEntry e in dictionary)
                if (e.Key != null) return e.Key.GetType();
            return null;
        }

        public static PhysicalMaterial TemplateMaterial(Item template)
        {
            Ingot ingot = template.GetComponent<Ingot>();
            if (ingot != null && ingot.PhysicalMaterial != null) return ingot.PhysicalMaterial;
            return HashedGeneralValue<PhysicalMaterial>.Get((uint)LibMaterial.MaterialType.metal);
        }

        public static void SetRecipeDuration(SmeltingRecipe recipe, float seconds)
        {
            FieldInfo f = typeof(SmeltingRecipe).GetField("duration", Any);
            if (f == null) throw new MissingFieldException(typeof(SmeltingRecipe).FullName, "duration");
            f.SetValue(recipe, seconds);
        }

        public static void SetSourceMaterialHash(PhysicalMaterial material, int prefabHash)
        {
            FieldInfo f = typeof(PhysicalMaterial).GetField("sourceMaterialHash", Any);
            if (f == null) throw new MissingFieldException(typeof(PhysicalMaterial).FullName, "sourceMaterialHash");
            f.SetValue(material, (uint)prefabHash);
        }

        public static void ReplacePhysicalMaterialReferences(Item item, GameObject newPrefab, PhysicalMaterial oldMaterial, PhysicalMaterial replacement)
        {
            ReplacePhysicalOnObject(item, oldMaterial, replacement);

            // Clone ItemComponents before editing them so vanilla/shared item data is never mutated.
            FieldInfo componentsField = typeof(Item).GetField("components", Any);
            if (componentsField != null)
            {
                IList source = componentsField.GetValue(item) as IList;
                if (source != null)
                {
                    IList cloned = Activator.CreateInstance(source.GetType()) as IList;
                    if (cloned != null)
                    {
                        foreach (object c in source)
                        {
                            object cc = c;
                            UnityEngine.Object uo = c as UnityEngine.Object;
                            if (uo != null) cc = UnityEngine.Object.Instantiate(uo);
                            ReplacePhysicalOnObject(cc, oldMaterial, replacement);
                            cloned.Add(cc);
                        }
                        componentsField.SetValue(item, cloned);
                    }
                }
            }

            foreach (Component c in newPrefab.GetComponentsInChildren<Component>(true)) ReplacePhysicalOnObject(c, oldMaterial, replacement);
        }

        private static void ReplacePhysicalOnObject(object obj, PhysicalMaterial oldMaterial, PhysicalMaterial replacement)
        {
            if (obj == null) return;
            foreach (FieldInfo f in AllFields(obj.GetType()))
            {
                try
                {
                    if (typeof(PhysicalMaterial).IsAssignableFrom(f.FieldType) && ReferenceEquals(f.GetValue(obj), oldMaterial)) f.SetValue(obj, replacement);
                    else if (f.FieldType.IsArray && f.FieldType.GetElementType() == typeof(PhysicalMaterial))
                    {
                        PhysicalMaterial[] a = f.GetValue(obj) as PhysicalMaterial[]; if (a == null) continue;
                        for (int i = 0; i < a.Length; i++) if (ReferenceEquals(a[i], oldMaterial)) a[i] = replacement;
                    }
                }
                catch { }
            }
        }

        public static void ReplaceItemCounts(SmeltingRecipe recipe, string fieldName, Item[] items, int[] counts)
        {
            FieldInfo arrayField = typeof(SmeltingRecipe).GetField(fieldName, Any);
            if (arrayField == null) throw new MissingFieldException(typeof(SmeltingRecipe).FullName, fieldName);
            Type arrayType = arrayField.FieldType;
            Type elem = arrayType.GetElementType();
            if (elem == null) throw new InvalidOperationException("SmeltingRecipe." + fieldName + " is not an array");
            Array a = Array.CreateInstance(elem, items.Length);
            for (int i = 0; i < items.Length; i++) a.SetValue(CreateItemCount(elem, items[i], counts[i]), i);
            arrayField.SetValue(recipe, a);
        }

        private static object CreateItemCount(Type t, Item item, int count)
        {
            object o;
            try { o = Activator.CreateInstance(t, true); }
            catch { o = FormatterServices.GetUninitializedObject(t); }

            FieldInfo itemField = t.GetFields(Any).FirstOrDefault(f => typeof(Item).IsAssignableFrom(f.FieldType));
            if (itemField == null) throw new MissingFieldException(t.FullName, "Item field");
            itemField.SetValue(o, item);

            FieldInfo countField = t.GetFields(Any).FirstOrDefault(f =>
                f != itemField && (f.FieldType == typeof(int) || f.FieldType == typeof(uint) || f.FieldType == typeof(short) || f.FieldType == typeof(ushort) || f.FieldType == typeof(byte)) &&
                (f.Name.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("amount", StringComparison.OrdinalIgnoreCase) >= 0));
            if (countField == null) countField = t.GetFields(Any).FirstOrDefault(f => f != itemField && (f.FieldType == typeof(int) || f.FieldType == typeof(uint)));
            if (countField == null) throw new MissingFieldException(t.FullName, "count/amount field");
            countField.SetValue(o, Convert.ChangeType(count, countField.FieldType, CultureInfo.InvariantCulture));
            return o;
        }
    }

    internal static class MiniJson
    {
        public static object Parse(string text) { var p = new Reader(text); p.Ws(); object v = p.Value(0); p.Ws(); if (!p.End) throw p.Err("Unexpected content after JSON value"); return v; }
        private sealed class Reader
        {
            private readonly string s; private int i;
            public Reader(string text) { s = text ?? ""; }
            public bool End { get { return i >= s.Length; } }
            public Exception Err(string msg) { int line = 1, col = 1; for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; col = 1; } else col++; } return new FormatException(msg + " (line " + line + ", col " + col + ")"); }
            public void Ws() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '\uFEFF')) i++; }
            public object Value(int depth) { if (depth > 32) throw Err("Nesting too deep"); Ws(); if (End) throw Err("Unexpected end"); char c = s[i]; if (c == '{') return Obj(depth); if (c == '[') return Arr(depth); if (c == '\"') return Str(); if (c == 't') { Lit("true"); return true; } if (c == 'f') { Lit("false"); return false; } if (c == 'n') { Lit("null"); return null; } return Num(); }
            private Dictionary<string, object> Obj(int d) { var o = new Dictionary<string, object>(StringComparer.Ordinal); i++; Ws(); if (!End && s[i] == '}') { i++; return o; } while (true) { Ws(); if (End || s[i] != '\"') throw Err("Expected string key"); string k = Str(); Ws(); if (End || s[i] != ':') throw Err("Expected ':'"); i++; object v = Value(d + 1); if (o.ContainsKey(k)) throw Err("Duplicate key '" + k + "'"); o[k] = v; Ws(); if (End) throw Err("Unterminated object"); if (s[i] == ',') { i++; continue; } if (s[i] == '}') { i++; return o; } throw Err("Expected ',' or '}'"); } }
            private List<object> Arr(int d) { var a = new List<object>(); i++; Ws(); if (!End && s[i] == ']') { i++; return a; } while (true) { a.Add(Value(d + 1)); Ws(); if (End) throw Err("Unterminated array"); if (s[i] == ',') { i++; continue; } if (s[i] == ']') { i++; return a; } throw Err("Expected ',' or ']'"); } }
            private string Str() { i++; var b = new StringBuilder(); while (true) { if (End) throw Err("Unterminated string"); char c = s[i++]; if (c == '\"') return b.ToString(); if (c == '\\') { if (End) throw Err("Bad escape"); char e = s[i++]; switch (e) { case '\"': b.Append('\"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break; case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break; case 'u': int cp; if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp)) throw Err("Bad unicode escape"); b.Append((char)cp); i += 4; break; default: throw Err("Bad escape"); } } else if (c < ' ') throw Err("Control character in string"); else b.Append(c); } }
            private object Num() { int st = i; while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++; if (st == i) throw Err("Unexpected character '" + s[i] + "'"); double d; if (!double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw Err("Bad number"); return d; }
            private void Lit(string w) { if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw Err("Unexpected token"); i += w.Length; }
        }
    }
}
