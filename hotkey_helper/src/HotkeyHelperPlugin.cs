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
        public const string PluginVersion = "1.1.0";

        internal static HotkeyHelperPlugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowColumn;
        internal static ConfigEntry<bool> ShowInventoryColumn;
        internal static ConfigEntry<int> MaxRows;
        internal static ConfigEntry<Vector2> ColumnPosition;
        internal static ConfigEntry<Vector2> InventoryColumnPosition;
        internal const int ColumnSortingOrder = 1000; // ponytail: fixed; make configurable if another UI mod draws above it
        internal static ConfigEntry<float> ColumnFontSize;
        internal static ConfigEntry<float> OverlayFontSize;
        internal static ConfigEntry<int> OverlayColumns;
        internal static ConfigEntry<Vector2> OverlaySize;
        internal static ConfigEntry<KeyboardShortcut> ShowAllKey;
        internal static ConfigEntry<bool> DebugDump;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("1 - General", "Enabled", true, "Master switch: the side column and the show-all list.");
            ShowColumn = Config.Bind("1 - General", "ShowColumn", true,
                "Show the side column while building, fighting, fishing or in the radial menu.");
            ShowInventoryColumn = Config.Bind("1 - General", "ShowInventoryColumn", true,
                "Show the side column while the inventory, a chest or a crafting station is open.");
            MaxRows = Config.Bind("1 - General", "MaxRows", 15,
                new ConfigDescription("Max hotkeys listed in the side column.", new AcceptableValueRange<int>(1, 40)));
            ColumnPosition = Config.Bind("2 - Layout", "ColumnPosition", new Vector2(0.995f, 0.4f),
                "Screen position of the column's right-middle point (0..1, x from left, y from bottom) while building/fighting.");
            InventoryColumnPosition = Config.Bind("2 - Layout", "InventoryColumnPosition", new Vector2(0.995f, 0.09f),
                "Screen position of the column's bottom-right corner (column grows upward) while inventory, chest or crafting is open.");
            ColumnFontSize = Config.Bind("2 - Layout", "ColumnFontSize", 14f,
                new ConfigDescription("Font size of the side column.", new AcceptableValueRange<float>(8f, 32f)));
            OverlayFontSize = Config.Bind("2 - Layout", "OverlayFontSize", 20f,
                new ConfigDescription("Font size of the show-all list (shrinks only if the list doesn't fit).", new AcceptableValueRange<float>(10f, 36f)));
            OverlayColumns = Config.Bind("2 - Layout", "OverlayColumns", 2,
                new ConfigDescription("Number of columns in the show-all list; more columns fit more rows at a larger font.", new AcceptableValueRange<int>(1, 4)));
            OverlaySize = Config.Bind("2 - Layout", "OverlaySize", new Vector2(0.76f, 0.84f),
                "Width and height of the show-all list as screen fractions (0..1, centered). Taller fits more rows.");
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
