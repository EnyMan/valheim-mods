using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HotkeyHelper
{
    /// <summary>Small right-side column listing mod hotkeys for whatever vanilla key hint panel is active.</summary>
    [HarmonyPatch(typeof(KeyHints))]
    internal static class ContextColumn
    {
        private static GameObject _root;
        private static TextMeshProUGUI _text;
        private static string _lastKey;

        [HarmonyPostfix, HarmonyPatch("Start")]
        private static void Start()
        {
            var config = HotkeyHelperPlugin.Instance.Config;
            config.SaveOnConfigSet = false;
            HotkeyScanner.Scan();
            config.Save();
            config.SaveOnConfigSet = true;
            _lastKey = null;

            if (HotkeyHelperPlugin.DebugDump.Value)
                HotkeyHelperPlugin.Log("Discovered hotkeys:\n" + string.Join("\n", HotkeyScanner.Hotkeys.Select(hk =>
                    $"  {hk.PluginName} | [{hk.Section}] {hk.Key} = {hk.KeyText} | auto={hk.AutoContexts} | shown={string.Join(",", hk.Contexts)} | label={hk.Label}")));
        }

        [HarmonyPostfix, HarmonyPatch("UpdateHints")]
        private static void UpdateHints(KeyHints __instance)
        {
            var contexts = ActiveContexts(__instance);
            bool gamepad = ZInput.IsGamepadActive();
            string key = contexts == null || !HotkeyHelperPlugin.Enabled.Value || AllHotkeysOverlay.Visible
                ? null
                : string.Join(",", contexts) + gamepad;
            if (key == _lastKey && !HotkeyScanner.Dirty) return;
            _lastKey = key;
            HotkeyScanner.Dirty = false;
            HotkeyScanner.ResolveAll();

            var rows = key == null
                ? new List<Hotkey>()
                : HotkeyScanner.Visible(gamepad).Where(h => contexts.Any(h.Contexts.Contains)).Take(HotkeyHelperPlugin.MaxRows.Value).ToList();
            if (rows.Count == 0)
            {
                if (_root) _root.SetActive(false);
                return;
            }
            if (!_root && !Create()) return;

            // Inventory/chest/crafting: the crafting panel covers the right-middle, so dock bottom-right and grow upward.
            bool inventory = contexts.Contains("inventory");
            var rt = (RectTransform)_root.transform;
            rt.pivot = inventory ? new Vector2(1f, 0f) : new Vector2(1f, 0.5f);
            rt.anchorMin = rt.anchorMax = (inventory ? HotkeyHelperPlugin.InventoryColumnPosition : HotkeyHelperPlugin.ColumnPosition).Value;
            _text.fontSize = HotkeyHelperPlugin.ColumnFontSize.Value;
            _text.text = string.Join("\n", rows.Select(h => $"{h.Label}  <color=#ffd27f>{h.KeyText}</color>"));
            _root.SetActive(true);
        }

        // Reads vanilla's decision instead of re-deriving it; null = no panel (key hints off, dead, paused, Jötunn custom hint...).
        private static string[] ActiveContexts(KeyHints kh)
        {
            if (kh.m_buildHints.activeSelf) return Hud.IsPieceSelectionVisible() ? new[] { "build", "buildmenu" } : new[] { "build" };
            if (kh.m_combatHints.activeSelf) return new[] { "combat" };
            if (kh.m_inventoryWithContainerHints.activeSelf) return new[] { "inventory", "container" };
            if (kh.m_inventoryHints.activeSelf) return new[] { "inventory" };
            if (kh.m_fishingHints.activeSelf) return new[] { "fishing" };
            if (kh.m_radialHints.activeSelf) return new[] { "radial" };
            return null;
        }

        private static bool Create()
        {
            if (!Ui.TryGetFont(out var font)) return false;

            _root = new GameObject("HotkeyHelperColumn", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)_root.transform;
            rt.SetParent(Hud.instance.m_rootObject.transform, false);
            // Own sorting layer so InventoryGui (drawn after the HUD) can't cover it. Must be set while active in hierarchy.
            var canvas = _root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = HotkeyHelperPlugin.ColumnSortingOrder;
            _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var layout = _root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.childControlWidth = layout.childControlHeight = true;
            var fitter = _root.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _text = Ui.Text(rt, font);
            _text.alignment = TextAlignmentOptions.Right;
            return true;
        }
    }

    /// <summary>Hold-to-show list of every mod hotkey, two columns, grouped by mod.</summary>
    internal static class AllHotkeysOverlay
    {
        private static GameObject _root;
        private static TextMeshProUGUI _left, _right;

        public static bool Visible => _root && _root.activeSelf;

        public static void SetVisible(bool visible)
        {
            if (!visible)
            {
                if (_root) _root.SetActive(false);
                return;
            }
            if (Visible || (!_root && !Create())) return;
            HotkeyScanner.ResolveAll();

            var hotkeys = HotkeyScanner.Visible(ZInput.IsGamepadActive()).ToList();
            var keyWidthEm = hotkeys.Count == 0 ? 4f : hotkeys.Max(h => h.KeyText.Length) * 0.6f + 1f;
            var groups = hotkeys.GroupBy(h => h.PluginName).Select(g =>
                new[] { $"<color=#ffc34d><b>{g.Key}</b></color>" }
                    .Concat(g.Select(h => $"<color=#ffffff>{h.KeyText}</color><pos={keyWidthEm:0.#}em>{h.Label}  <size=75%><color=#8a8a8a>{string.Join(", ", h.Contexts)}</color></size>"))
                    .Concat(new[] { "" })
                    .ToList()).ToList();

            // Greedy split: whole groups go left until about half the lines are used.
            int total = groups.Sum(g => g.Count), used = 0;
            var left = new List<string>();
            var right = new List<string>();
            foreach (var g in groups)
            {
                if (right.Count == 0 && used + g.Count / 2 <= total / 2) { left.AddRange(g); used += g.Count; }
                else right.AddRange(g);
            }
            _left.text = hotkeys.Count == 0 ? "No mod hotkeys found." : string.Join("\n", left);
            _right.text = string.Join("\n", right);

            _root.SetActive(true);
            // Autosize each column, then use the smaller size for both so they match.
            foreach (var t in new[] { _left, _right })
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = HotkeyHelperPlugin.OverlayFontSize.Value;
                t.ForceMeshUpdate();
            }
            var size = Mathf.Min(_left.fontSize, _right.text == "" ? _left.fontSize : _right.fontSize);
            foreach (var t in new[] { _left, _right })
            {
                t.enableAutoSizing = false;
                t.fontSize = size;
            }
        }

        private static bool Create()
        {
            if (!Ui.TryGetFont(out var font)) return false;

            _root = new GameObject("HotkeyHelperOverlay", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)_root.transform;
            rt.SetParent(Hud.instance.m_rootObject.transform, false);
            rt.anchorMin = new Vector2(0.12f, 0.08f);
            rt.anchorMax = new Vector2(0.88f, 0.92f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

            var title = Ui.Text(rt, font);
            title.text = "<b>Mod hotkeys</b>";
            title.fontSize = 28;
            title.color = new Color(1f, 0.82f, 0.45f);
            Place(title.rectTransform, 0f, 1f, top: 16, bottom: -60, fromTop: true);

            _left = Column(rt, font, 0f, 0.5f);
            _right = Column(rt, font, 0.5f, 1f);
            _root.SetActive(false);
            return true;
        }

        private static TextMeshProUGUI Column(RectTransform parent, TMP_FontAsset font, float xMin, float xMax)
        {
            var t = Ui.Text(parent, font);
            t.fontSizeMin = 10;
            t.overflowMode = TextOverflowModes.Truncate;
            Place(t.rectTransform, xMin, xMax, top: 70, bottom: 24, fromTop: false);
            return t;
        }

        // Horizontal anchors xMin..xMax with 32px side padding; vertical: full height minus top/bottom, or a fixed top strip.
        private static void Place(RectTransform rt, float xMin, float xMax, float top, float bottom, bool fromTop)
        {
            rt.anchorMin = new Vector2(xMin, fromTop ? 1f : 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = new Vector2(32, bottom);
            rt.offsetMax = new Vector2(-32, -top);
        }
    }

    internal static class Ui
    {
        public static bool TryGetFont(out TMP_FontAsset font)
        {
            font = KeyHints.instance && KeyHints.instance.m_buildMenuKey ? KeyHints.instance.m_buildMenuKey.font : null;
            return Hud.instance != null && font != null;
        }

        public static TextMeshProUGUI Text(Transform parent, TMP_FontAsset font)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = font;
            t.richText = true;
            t.color = new Color(0.9f, 0.9f, 0.9f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }
    }
}
