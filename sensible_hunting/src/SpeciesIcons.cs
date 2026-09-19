using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using UnityEngine;

namespace SensibleHunting
{
    /// "Which animal is that" at stage 4. Prefers the mod's own head silhouette shipped next to the DLL
    /// (white art, tinted gold on screen), and falls back to the creature's own trophy - or anything else
    /// it drops - so a species with no art of its own still gets a tell.
    internal static class SpeciesIcons
    {
        private static readonly Dictionary<string, Sprite> Art = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Sprite> Ours = new HashSet<Sprite>();
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        // ImageConversionModule targets netstandard 2.1, which a net48 build cannot reference; bind at runtime.
        private static readonly Func<Texture2D, byte[], bool> LoadImage = (Func<Texture2D, byte[], bool>)Delegate.CreateDelegate(
            typeof(Func<Texture2D, byte[], bool>),
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true)
                .GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }));

        /// Young animals wear the adult's head: Boar_piggy -> Boar, Wolf_cub -> Wolf. Chicken -> Hen is
        /// the one pair that does not share a prefix.
        internal static string ArtName(string prefab)
        {
            if (string.Equals(prefab, "Chicken", StringComparison.OrdinalIgnoreCase)) return "Hen";
            var cut = prefab.IndexOf('_');
            return cut > 0 ? prefab.Substring(0, cut) : prefab;
        }

        /// Every PNG sitting next to the DLL, keyed by file name. Mipmapped: a 256 px head drawn at 30 px
        /// aliases badly without them.
        internal static void Load(string dir, ManualLogSource log)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var path in Directory.GetFiles(dir, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                    { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                try
                {
                    if (!LoadImage(tex, File.ReadAllBytes(path))) throw new InvalidDataException("not a PNG");
                }
                catch (Exception e)
                {
                    log.LogWarning($"Could not load locator art {name}: {e.Message}");
                    UnityEngine.Object.Destroy(tex);
                    continue;
                }
                var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                Art[name] = sprite;
                Ours.Add(sprite);
            }
            if (Art.Count > 0) log.LogInfo($"Loaded {Art.Count} locator icons");
        }

        /// Icon for a species. `c` is null for game that is not a Character (seagulls), which then has no
        /// drop table to fall back on.
        internal static Sprite For(GameObject go, Character c)
        {
            var prefab = Utils.GetPrefabName(go);
            if (Cache.TryGetValue(prefab, out var cached)) return cached;
            if (!Art.TryGetValue(ArtName(prefab), out var result) && c != null) result = FromDrops(c);
            Cache[prefab] = result;
            return result;
        }

        /// Our own art is white, so it takes the gold tint; a trophy icon is finished art and must not.
        internal static bool IsOurs(Sprite s) => s != null && Ours.Contains(s);

        /// Prefer the trophy (a head - reads as a species at a glance), else whatever else it drops.
        private static Sprite FromDrops(Character c)
        {
            Sprite trophy = null, any = null;
            var drops = c.GetComponent<CharacterDrop>();
            if (drops == null) return null;
            foreach (var drop in drops.m_drops)
            {
                var item = drop.m_prefab != null ? drop.m_prefab.GetComponent<ItemDrop>() : null;
                var shared = item != null ? item.m_itemData.m_shared : null;
                if (shared == null || shared.m_icons == null || shared.m_icons.Length == 0) continue;
                var sprite = shared.m_icons[0];
                if (sprite == null) continue;
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) return sprite;
                if (any == null) any = sprite;
            }
            return trophy != null ? trophy : any;
        }
    }
}
