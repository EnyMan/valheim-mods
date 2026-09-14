using System;
using System.Collections.Generic;
using HarmonyLib;
using Jotunn;
using UnityEngine;

namespace WeatherAltar
{
    public enum WeatherKind : byte
    {
        None = 0,
        Clear = 1,
        Rain = 2,
        Storm = 3,
    }

    public static class WeatherKindNames
    {
        public static readonly string[] Names = { "Clear", "Rain", "ThunderStorm" };

        public static bool IsValid(byte value) => value >= (byte)WeatherKind.Clear && value <= (byte)WeatherKind.Storm;

        public static string GetEnvName(WeatherKind kind)
        {
            int index = (int)kind - 1;
            return (uint)index < Names.Length ? Names[index] : null;
        }
    }

    public static class WeatherService
    {
        public static WeatherKind Kind { get; private set; } = WeatherKind.None;
        public static long Revision { get; private set; }
        public static byte SupportedMask { get; set; }

        private static double _deadline;
        private static bool _deadlineActive;
        private static readonly bool[] Supported = new bool[4];

        public static bool HasActive => Kind != WeatherKind.None && _deadlineActive && RemainingSeconds() > 0f;
        public static bool StateKnown { get; private set; }
        public static bool EverReceivedState { get; private set; }


        public static float RemainingSeconds()
        {
            if (!_deadlineActive)
            {
                return 0f;
            }

            double remaining = _deadline - Time.realtimeSinceStartupAsDouble;
            return remaining > 0.0 ? (float)remaining : 0f;
        }

        public static void ResetSession()
        {
            Kind = WeatherKind.None;
            Revision = 0;
            _deadline = 0.0;
            _deadlineActive = false;
            StateKnown = false;
            EverReceivedState = false;
            SupportedMask = 0;
            for (int i = 0; i < Supported.Length; i++)
            {
                Supported[i] = false;
            }
        }

        public static void ResolvePresets()
        {
            SupportedMask = 0;
            for (int i = 0; i < Supported.Length; i++)
            {
                Supported[i] = false;
            }

            EnvMan envMan = EnvMan.instance;
            if (!envMan)
            {
                return;
            }

            for (byte kind = (byte)WeatherKind.Clear; kind <= (byte)WeatherKind.Storm; kind++)
            {
                string envName = WeatherKindNames.GetEnvName((WeatherKind)kind);
                if (envMan.m_environments.Exists(env => env.m_name == envName))
                {
                    Supported[kind] = true;
                    SupportedMask |= (byte)(1 << (kind - 1));
                }
                else
                {
                    Jotunn.Logger.LogWarning($"Weather Altar: environment '{envName}' not found in this world; choice disabled.");
                }
            }

            StateKnown = true;
        }

        public static bool IsSupported(byte kind)
        {
            if (!WeatherKindNames.IsValid(kind))
            {
                return false;
            }

            return Supported[kind];
        }

        public static bool TryGetSupportedKind(byte kind, out WeatherKind resolved)
        {
            resolved = WeatherKind.None;
            if (!WeatherKindNames.IsValid(kind) || !Supported[kind])
            {
                return false;
            }

            resolved = (WeatherKind)kind;
            return true;
        }

        /// <summary>Server-authoritative commit. Applies locally then lets the caller broadcast.</summary>
        public static void Commit(WeatherKind kind, int durationSeconds)
        {
            Kind = kind;
            Revision++;
            if (kind == WeatherKind.None)
            {
                _deadlineActive = false;
                _deadline = 0.0;
            }
            else
            {
                _deadlineActive = true;
                _deadline = Time.realtimeSinceStartupAsDouble + Math.Max(0, durationSeconds);
            }
        }

        /// <summary>Client-side snapshot application.</summary>
        public static void ApplySnapshot(long revision, WeatherKind kind, float remainingSeconds)
        {
            Revision = revision;
            Kind = kind;
            _deadlineActive = kind != WeatherKind.None && remainingSeconds > 0f;
            _deadline = Time.realtimeSinceStartupAsDouble + Math.Max(0f, remainingSeconds);
            StateKnown = true;
            EverReceivedState = true;
        }

        public static void Update()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            if (Kind != WeatherKind.None && _deadlineActive && RemainingSeconds() <= 0f)
            {
                Commit(WeatherKind.None, 0);
                OfferingRpc.BroadcastExpiry();
            }
        }



        public static void Shutdown()
        {
            ResetSession();
        }

        [HarmonyPatch(typeof(EnvMan), "Awake")]
        public static class EnvManAwakePatch
        {
            private static void Postfix()
            {
                ResetSession();
                ResolvePresets();
            }
        }

        [HarmonyPatch(typeof(EnvMan), "GetEnvironmentOverride")]
        public static class EnvOverridePatch
        {
            private static void Postfix(ref string __result)
            {
                if (!string.IsNullOrEmpty(__result))
                {
                    return;
                }

                Player player = Player.m_localPlayer;
                if (player == null || player.InInterior() || !WeatherService.HasActive)
                {
                    return;
                }

                string envName = WeatherKindNames.GetEnvName(Kind);
                if (string.IsNullOrEmpty(envName))
                {
                    return;
                }

                __result = envName;
            }
        }
    }
}