using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace CalmSeas
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class CalmSeasPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mous.calmseas";
        public const string PluginName = "Calm Seas";
        public const string PluginVersion = "1.1.0";

        private static ConfigEntry<float> _landScale;
        private static ConfigEntry<float> _seaScale;
        private static ConfigEntry<float> _openSeaDepth;

        // Scale for this frame, picked from where the local player stands.
        internal static float Scale = 1f;

        private Harmony _harmony;

        private void Awake()
        {
            var adminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };
            var range = new AcceptableValueRange<float>(0f, 5f);
            _landScale = Config.Bind("Waves", "LandWaveScale", 0.3f,
                new ConfigDescription("Wave height multiplier while you are on land or in shallow water. 1 = vanilla, 0 = flat.", range, adminOnly));
            _seaScale = Config.Bind("Waves", "SeaWaveScale", 0.7f,
                new ConfigDescription("Wave height multiplier while you are over open sea. 1 = vanilla, 0 = flat.", range, adminOnly));
            _openSeaDepth = Config.Bind("Waves", "OpenSeaDepth", 20f,
                new ConfigDescription("Water depth (m) under you at which SeaWaveScale fully applies. Between the shore and this depth the two scales blend.",
                    new AcceptableValueRange<float>(1f, 200f), adminOnly));
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private void Update()
        {
            var player = Player.m_localPlayer;
            // No player (menu, dedicated server) or no terrain hit: open-sea value.
            var depth = player && ZoneSystem.instance && ZoneSystem.instance.GetGroundHeight(player.transform.position, out var ground)
                ? ZoneSystem.instance.m_waterLevel - ground
                : float.MaxValue;
            Scale = ScaleFor(depth, _landScale.Value, _seaScale.Value, _openSeaDepth.Value);
        }

        internal static float ScaleFor(float depth, float land, float sea, float openSeaDepth) =>
            Mathf.Lerp(land, sea, Mathf.Clamp01(depth / openSeaDepth));

        internal static Vector4 ScaleWind(Vector4 wind, float scale) => new(wind.x, wind.y, wind.z, wind.w * scale);
    }

    // Physics: the water height boats, swimmers and floating items use. Result is linear in wind intensity.
    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.CalcWave), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float))]
    internal static class CalcWavePatch
    {
        private static void Postfix(ref float __result) => __result *= CalmSeasPlugin.Scale;
    }

    // Visuals: the water shader reads wind intensity from these globals. EnvMan's own wind (sailing, cloth) stays untouched.
    [HarmonyPatch(typeof(EnvMan), "UpdateWindTransition")]
    internal static class WindShaderPatch
    {
        private static readonly int GlobalWind1 = Shader.PropertyToID("_GlobalWind1");
        private static readonly int GlobalWind2 = Shader.PropertyToID("_GlobalWind2");

        private static void Postfix(EnvMan __instance)
        {
            __instance.GetWindData(out var wind1, out var wind2, out _);
            Shader.SetGlobalVector(GlobalWind1, CalmSeasPlugin.ScaleWind(wind1, CalmSeasPlugin.Scale));
            Shader.SetGlobalVector(GlobalWind2, CalmSeasPlugin.ScaleWind(wind2, CalmSeasPlugin.Scale));
        }
    }
}
