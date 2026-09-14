using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HotkeyHelper
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class HotkeyHelperPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mous.hotkeyhelper";
        public const string PluginName = "Hotkey Helper";
        public const string PluginVersion = "1.0.0";

        internal static HotkeyHelperPlugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> MaxRows;
        internal static ConfigEntry<Vector2> ColumnPosition;
        internal static ConfigEntry<Vector2> InventoryColumnPosition;
        internal const int ColumnSortingOrder = 1000; // ponytail: fixed; make configurable if another UI mod draws above it
        internal static ConfigEntry<float> ColumnFontSize;
        internal static ConfigEntry<float> OverlayFontSize;
        internal static ConfigEntry<KeyboardShortcut> ShowAllKey;
        internal static ConfigEntry<bool> DebugDump;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("1 - General", "Enabled", true, "Show the mod hotkey column next to the vanilla key hints.");
            MaxRows = Config.Bind("1 - General", "MaxRows", 15,
                new ConfigDescription("Max hotkeys listed in the side column.", new AcceptableValueRange<int>(1, 40)));
            ColumnPosition = Config.Bind("2 - Layout", "ColumnPosition", new Vector2(0.995f, 0.4f),
                "Screen position of the column's right-middle point (0..1, x from left, y from bottom) while building/fighting.");
            InventoryColumnPosition = Config.Bind("2 - Layout", "InventoryColumnPosition", new Vector2(0.995f, 0.09f),
                "Screen position of the column's bottom-right corner (column grows upward) while inventory, chest or crafting is open.");
            ColumnFontSize = Config.Bind("2 - Layout", "ColumnFontSize", 14f,
                new ConfigDescription("Font size of the side column.", new AcceptableValueRange<float>(8f, 32f)));
            OverlayFontSize = Config.Bind("2 - Layout", "OverlayFontSize", 20f,
                new ConfigDescription("Max font size of the show-all list (shrinks to fit).", new AcceptableValueRange<float>(10f, 36f)));
            ShowAllKey = Config.Bind("1 - General", "ShowAllKey", new KeyboardShortcut(KeyCode.H, KeyCode.LeftAlt),
                "Hold to show every discovered mod hotkey.");
            DebugDump = Config.Bind("9 - Debug", "DumpOnWorldLoad", false,
                "Log the KeyHints UI hierarchy and all discovered hotkeys with their resolved contexts.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void Update()
        {
            if (Hud.instance == null) return;
            AllHotkeysOverlay.SetVisible(Enabled.Value && ShowAllKey.Value.IsPressed());
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }

        internal static void Log(string msg) => Instance.Logger.LogInfo(msg);
        internal static void Warn(string msg) => Instance.Logger.LogWarning(msg);
    }
}
