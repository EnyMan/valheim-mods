using BepInEx;
using HarmonyLib;
using Jotunn;
using Jotunn.Utils;
using UnityEngine;

namespace WeatherAltar
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class WeatherAltarPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mous.weatheraltar";
        public const string PluginName = "Weather Altar";
        public const string PluginVersion = "1.0.1";

        private Harmony _harmony;

        private void Awake()
        {
            AltarSettings.Init(Config);
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            OfferingRpc.Register();
            WeatherAltarPiece.Register();
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            AltarMenu.Shutdown();
            OfferingRpc.Shutdown();
            WeatherService.Shutdown();
        }

        private void Update()
        {
            WeatherService.Update();
            AltarMenu.Update();
        }
    }
}