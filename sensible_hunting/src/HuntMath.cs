using System;
using System.Collections.Generic;
using UnityEngine;

namespace SensibleHunting
{
    /// Pure logic, no Unity scene access — everything here is reflection-invokable from tools/selfcheck.ps1.
    internal static class HuntMath
    {
        /// Where a blip lands on screen and which way its icon points.
        internal struct Blip
        {
            public float X;         // pixels from the left
            public float Y;         // pixels from the bottom
            public float Angle;     // degrees, 0 = pointing right; only meaningful when OffScreen
            public bool OffScreen;  // true = clamped to the edge, false = drawn over the animal
        }

        /// Projects a camera viewport point onto the screen, clamping off-screen targets
        /// (including ones behind the camera, where vz < 0) to a margin inset from the edge.
        internal static Blip Project(float vx, float vy, float vz, float width, float height, float margin)
        {
            // Direction from screen centre, in pixels.
            var dx = (vx - 0.5f) * width;
            var dy = (vy - 0.5f) * height;
            if (vz < 0f)
            {
                // Behind the camera the projection is mirrored through the centre; undo that,
                // and force it outside the rect so it always resolves to an edge indicator.
                dx = -dx;
                dy = -dy;
                if (dx * dx + dy * dy < 1f) dy = -1f; // dead astern: no direction to preserve, put it below
                var push = width + height;            // guaranteed larger than the rect, clamped below
                var len = Mathf.Sqrt(dx * dx + dy * dy);
                dx = dx / len * push;
                dy = dy / len * push;
            }

            var halfW = Mathf.Max(1f, width * 0.5f - margin);
            var halfH = Mathf.Max(1f, height * 0.5f - margin);
            var over = Mathf.Max(Mathf.Abs(dx) / halfW, Mathf.Abs(dy) / halfH);
            var offScreen = over > 1f;
            if (offScreen)
            {
                dx /= over;
                dy /= over;
            }

            return new Blip
            {
                X = width * 0.5f + dx,
                Y = height * 0.5f + dy,
                Angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg,
                OffScreen = offScreen,
            };
        }

        /// How much daylight there is, 0 at night, 1 at midday, ramping across dawn and dusk. Same
        /// curve EnvMan uses for its own day intensity, so it turns over when the light does.
        internal static float Daylight(float dayFraction) =>
            Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Abs(dayFraction - 0.5f) / 0.25f));

        /// Is this viewport point actually on screen in front of the camera? Only then can the player
        /// be looking at it, whatever a line-of-sight raycast says.
        internal static bool InView(float vx, float vy, float vz) =>
            vz > 0f && vx >= 0f && vx <= 1f && vy >= 0f && vy <= 1f;

        /// Anything the skill scales: detection radius, how long a pulse shows, how long the gap is.
        /// Pass a larger at0 than at100 for the things that shrink as you get better.
        internal static float BySkill(float skillFactor, float at0, float at100) =>
            Mathf.Lerp(at0, at100, Mathf.Clamp01(skillFactor));

        /// Which pulse we are in. Same cycle definition as PulseAlpha, so a new index is exactly a new flash.
        internal static int PulseIndex(float time, float onDuration, float gap) =>
            Mathf.FloorToInt(time / Mathf.Max(0.01f, onDuration + gap));

        /// The instinct comes in pulses, not a constant readout: visible for `onDuration`, gone for
        /// `gap`, with `fade` seconds of ramp at each end. One clock for every blip, so they breathe
        /// together like a single sweep of attention rather than a field of blinking lights.
        internal static float PulseAlpha(float time, float onDuration, float gap, float fade)
        {
            var cycle = Mathf.Max(0.01f, onDuration + gap);
            var t = Mathf.Repeat(time, cycle);
            if (t >= onDuration) return 0f;
            if (fade <= 0f) return 1f;
            var envelope = Mathf.Min(t / fade, (onDuration - t) / fade);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(envelope));
        }

        /// The four stages of the skill:
        /// 0 = rough direction only, 1 = tighter direction + ballpark distance,
        /// 2 = true direction + exact distance, 3 = + which animal it is.
        internal static int DetailTier(float skillFactor) =>
            skillFactor < 0.25f ? 0 : skillFactor < 0.5f ? 1 : skillFactor < 0.75f ? 2 : 3;

        /// How far off the blip may point at each stage. Zero from stage 2 on.
        internal static float ErrorForTier(int tier, float maxDegrees) =>
            tier <= 0 ? maxDegrees : tier == 1 ? maxDegrees * 0.35f : 0f;

        /// A fixed bearing error per animal — derived from its ZDOID so it stays put instead of
        /// jittering every frame, which would give the real position away by averaging.
        internal static float BearingErrorDegrees(int seed, float maxDegrees)
        {
            unchecked
            {
                var h = (uint)seed * 2654435761u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                var unit = h % 20001u / 10000f - 1f; // -1 .. 1
                return unit * maxDegrees;
            }
        }

        /// "about fifteen paces" rather than a rangefinder reading.
        internal static string Ballpark(float distance, float step)
        {
            if (step <= 0f) step = 1f;
            return "~" + Mathf.RoundToInt(distance / step) * (int)step + " m";
        }

        internal static string Exact(float distance) => Mathf.RoundToInt(distance) + " m";

        /// Near game reads bigger, as in the mockup — the blip carries distance before the number does.
        internal static float BlipScale(float distance, float range, float min, float max) =>
            Mathf.Lerp(max, min, range <= 0f ? 1f : Mathf.Clamp01(distance / range));

        /// Blips fade out over the last quarter of the detection range.
        internal static float DistanceFade(float distance, float range) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(range * 0.75f, range, distance));

        /// Damage is worth XP in proportion to the chunk it took out of the animal.
        internal static float HitXp(float damage, float maxHealth, float factor) =>
            maxHealth <= 0f ? 0f : Mathf.Clamp01(damage / maxHealth) * factor;

        /// "Boar, neck ,,Lox" -> {Boar, Neck, Lox}, case-insensitive, blanks dropped.
        internal static HashSet<string> ParseNames(string list)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(list)) return set;
            foreach (var part in list.Split(','))
            {
                var name = part.Trim();
                if (name.Length > 0) set.Add(name);
            }
            return set;
        }

        /// Starred animals are worth more: 1x / 2x / 3x.
        internal static float KillXp(int level, float factor) => factor * Mathf.Max(1, level);
    }
}
