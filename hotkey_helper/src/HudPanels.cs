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
            // Inventory/chest/crafting: the crafting panel covers the right-middle, so dock bottom-right and grow upward.
            bool inventory = contexts != null && contexts.Contains("inventory");
            bool wanted = inventory ? HotkeyHelperPlugin.ShowInventoryColumn.Value : HotkeyHelperPlugin.ShowColumn.Value;
            string key = contexts == null || !HotkeyHelperPlugin.Enabled.Value || !wanted || AllHotkeysOverlay.Visible
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

    /// <summary>Hold-to-show list of every mod hotkey in OverlayColumns columns, grouped by mod.</summary>
    internal static class AllHotkeysOverlay
    {
        private static GameObject _root;
        private static readonly List<TextMeshProUGUI> _columns = new List<TextMeshProUGUI>();

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

            var size = HotkeyHelperPlugin.OverlaySize.Value;
            var w = Mathf.Clamp(size.x, 0.2f, 1f) / 2f;
            var h = Mathf.Clamp(size.y, 0.2f, 1f) / 2f;
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = new Vector2(0.5f - w, 0.5f - h);
            rt.anchorMax = new Vector2(0.5f + w, 0.5f + h);
            var count = HotkeyHelperPlugin.OverlayColumns.Value;
            while (_columns.Count < count) _columns.Add(Column(rt, _columns[0].font));
            for (var i = 0; i < _columns.Count; i++)
            {
                _columns[i].gameObject.SetActive(i < count);
                Place(_columns[i].rectTransform, (float)i / count, (float)(i + 1) / count, top: 70, bottom: 24, fromTop: false);
            }

            var hotkeys = HotkeyScanner.Visible(ZInput.IsGamepadActive()).ToList();
            var keyWidthEm = hotkeys.Count == 0 ? 4f : hotkeys.Max(h => h.KeyText.Length) * 0.6f + 1f;
            var groups = hotkeys.GroupBy(h => h.PluginName).Select(g =>
                new[] { $"<color=#ffc34d><b>{g.Key}</b></color>" }
                    .Concat(g.Select(h => $"<color=#ffffff>{h.KeyText}</color><pos={keyWidthEm:0.#}em>{h.Label}  <size=75%><color=#8a8a8a>{string.Join(", ", h.Contexts)}</color></size>"))
                    .Concat(new[] { "" })
                    .ToList()).ToList();

            var lines = new List<string>[count];
            for (var i = 0; i < count; i++) lines[i] = new List<string>();
            var split = SplitColumns(groups.Select(g => g.Count).ToArray(), count);
            for (var i = 0; i < groups.Count; i++) lines[split[i]].AddRange(groups[i]);
            if (hotkeys.Count == 0) lines[0].Add("No mod hotkeys found.");
            for (var i = 0; i < count; i++) _columns[i].text = string.Join("\n", lines[i]);

            _root.SetActive(true);
            // Autosize each column, then use the smallest size for all so they match.
            var shown = _columns.Take(count).ToList();
            foreach (var t in shown)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = HotkeyHelperPlugin.OverlayFontSize.Value;
                t.ForceMeshUpdate();
            }
            var font = shown.Where(t => t.text != "").Select(t => t.fontSize).DefaultIfEmpty(HotkeyHelperPlugin.OverlayFontSize.Value).Min();
            foreach (var t in shown)
            {
                t.enableAutoSizing = false;
                t.fontSize = font;
            }
        }

        // Column index per group: whole groups, in order, each column taking about an even share of the lines.
        internal static int[] SplitColumns(int[] groupLines, int columns)
        {
            var result = new int[groupLines.Length];
            float share = (float)groupLines.Sum() / columns;
            int col = 0, used = 0;
            for (var i = 0; i < groupLines.Length; i++)
            {
                // Next column once this group would mostly land past the current column's share.
                if (col < columns - 1 && used > 0 && used + groupLines[i] / 2f > share * (col + 1)) col++;
                result[i] = col;
                used += groupLines[i];
            }
            return result;
        }

        private static bool Create()
        {
            if (!Ui.TryGetFont(out var font)) return false;

            _root = new GameObject("HotkeyHelperOverlay", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)_root.transform;
            rt.SetParent(Hud.instance.m_rootObject.transform, false);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

            var title = Ui.Text(rt, font);
            title.text = "<b>Mod hotkeys</b>";
            title.fontSize = 28;
            title.color = new Color(1f, 0.82f, 0.45f);
            Place(title.rectTransform, 0f, 1f, top: 16, bottom: -60, fromTop: true);

            _columns.Clear(); // the old ones died with the previous world's HUD
            _columns.Add(Column(rt, font));
            _root.SetActive(false);
            return true;
        }

        private static TextMeshProUGUI Column(RectTransform parent, TMP_FontAsset font)
        {
            var t = Ui.Text(parent, font);
            t.fontSizeMin = 10;
            t.overflowMode = TextOverflowModes.Truncate;
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
