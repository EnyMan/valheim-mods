using HarmonyLib;
using UnityEngine;

namespace SensibleHunting
{
    /// Three XP sources: stalking (piggybacks on vanilla's sneak tick), hitting, killing.
    [HarmonyPatch]
    internal static class HuntingXp
    {
        // m_lastHit is protected; it holds the blow that killed the character by the time OnDeath runs.
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit =
            AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        private static ZDOID _lastStalked = ZDOID.None;
        private static Vector3 _lastStalkPos = Vector3.zero;

        /// Raise via Skills directly: going through Player.RaiseSkill would re-enter the Sneak postfix below.
        private static void Award(Player player, float factor)
        {
            if (factor > 0f) player.GetSkills().RaiseSkill(SensibleHuntingPlugin.Hunting, factor);
        }

        /// Vanilla raises Sneak once a second while crouched — 1.0 near a target, 0.1 otherwise.
        /// Mirroring that tick gives the stalking XP its cadence and its "sneaking at something" bonus for free.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
        private static void OnSneakTick(Player __instance, Skills.SkillType skill, float value)
        {
            if (skill != Skills.SkillType.Sneak) return;
            var plugin = SensibleHuntingPlugin.Instance;
            if (plugin == null || __instance != Player.m_localPlayer) return;

            var target = plugin.NearestGame(__instance, plugin.CurrentRange(__instance));
            if (target == null) return;

            // Anti-AFK: crouching next to a penned boar shouldn't farm the skill forever.
            var id = target.GetZDOID();
            var pos = __instance.transform.position;
            var moved = Vector3.Distance(pos, _lastStalkPos) >= plugin.StalkMoveDistance.Value;
            if (id == _lastStalked && !moved) return;
            _lastStalked = id;
            _lastStalkPos = pos;

            Award(__instance, value * plugin.StalkFactor.Value);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static void OnDamage(Character __instance, HitData hit)
        {
            var plugin = SensibleHuntingPlugin.Instance;
            if (plugin == null || hit == null) return;
            if (hit.GetAttacker() != Player.m_localPlayer || Player.m_localPlayer == null) return;
            if (!SensibleHuntingPlugin.IsWildGame(__instance)) return;

            Award(Player.m_localPlayer,
                HuntMath.HitXp(hit.GetTotalDamage(), __instance.GetMaxHealth(), plugin.HitFactor.Value));
        }

        /// Humanoid does not override OnDeath, so patching the base method catches animals.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static void OnDeath(Character __instance)
        {
            var plugin = SensibleHuntingPlugin.Instance;
            if (plugin == null || Player.m_localPlayer == null) return;
            // Not IsWildGame: that excludes the dead, and by now this one is.
            if (!SensibleHuntingPlugin.IsGame(__instance)) return;
            if (LastHit(__instance)?.GetAttacker() != Player.m_localPlayer) return;

            Award(Player.m_localPlayer, HuntMath.KillXp(__instance.GetLevel(), plugin.KillFactor.Value));
        }
    }
}
