using System.Collections.Generic;
using UnityEngine;

namespace SensibleHunting
{
    /// "Which animal is that" without drawing any art: every huntable creature already carries its own
    /// icon in its drop table. Prefer the trophy (a head — reads as a species at a glance), fall back to
    /// whatever else it drops, so creatures without a trophy still get something.
    internal static class TrophyIcons
    {
        private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

        internal static Sprite For(Character c)
        {
            // m_name is the localization token, identical for every instance of a species.
            var key = (c.m_name ?? "").GetStableHashCode();
            if (Cache.TryGetValue(key, out var cached)) return cached;

            Sprite trophy = null, any = null;
            var drops = c.GetComponent<CharacterDrop>();
            if (drops != null)
            {
                foreach (var drop in drops.m_drops)
                {
                    var item = drop.m_prefab != null ? drop.m_prefab.GetComponent<ItemDrop>() : null;
                    var shared = item != null ? item.m_itemData.m_shared : null;
                    if (shared == null || shared.m_icons == null || shared.m_icons.Length == 0) continue;
                    var sprite = shared.m_icons[0];
                    if (sprite == null) continue;
                    if (shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy) { trophy = sprite; break; }
                    if (any == null) any = sprite;
                }
            }

            var result = trophy != null ? trophy : any;
            Cache[key] = result;
            return result;
        }
    }
}
