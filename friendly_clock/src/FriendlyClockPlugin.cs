using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FriendlyClock
{
    public enum ClockStyle { Dial, Words, DialAndWords }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class FriendlyClockPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mous.friendlyclock";
        public const string PluginName = "Friendly Clock";
        public const string PluginVersion = "1.0.0";

        // Twelve two-hour phases from midnight; the dial draws one wedge per entry, so 00-06 and 18-24 are night.
        internal const string DefaultWords = "Midnight,Early Morning,Before Dawn,Dawn,Morning,Late Morning,Midday,Afternoon,Evening,Dusk,Night,Late Night";

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<ClockStyle> _style;
        private ConfigEntry<bool> _showDay;
        private ConfigEntry<float> _size;
        private ConfigEntry<float> _fontSize;
        private ConfigEntry<float> _opacity;
        private ConfigEntry<Vector2> _position;
        private ConfigEntry<string> _words;

        private RectTransform _root;
        private DialGraphic _face;
        // Painted art dropped next to the DLL replaces the drawn part (index = DialGraphic.Part); reloaded live when the file changes.
        private static readonly string[] AssetFiles = { "clock_face.png", "clock_hand.png", "clock_cap.png" };
        private readonly DialGraphic[] _parts = new DialGraphic[3];
        private readonly DateTime[] _assetStamps = new DateTime[3];
        private float _nextAssetCheck;
        private RectTransform _hand;
        private TextMeshProUGUI _label;
        private bool _dragging;
        private Vector2 _dragOffset;

        private void Awake()
        {
            _enabled = Config.Bind("1 - General", "Enabled", true, "Show the clock.");
            _style = Config.Bind("1 - General", "Style", ClockStyle.DialAndWords,
                "Dial = day/night dial only, Words = fuzzy time of day only, DialAndWords = both.");
            _showDay = Config.Bind("1 - General", "ShowDayCounter", false, "Show the current day number.");
            _size = Config.Bind("2 - Layout", "Size", 110f,
                new ConfigDescription("Dial diameter in HUD units.", new AcceptableValueRange<float>(48f, 240f)));
            _fontSize = Config.Bind("2 - Layout", "FontSize", 20f,
                new ConfigDescription("Font size of the words / day counter.", new AcceptableValueRange<float>(10f, 40f)));
            _opacity = Config.Bind("2 - Layout", "Opacity", 0.95f,
                new ConfigDescription("Overall opacity.", new AcceptableValueRange<float>(0.2f, 1f)));
            _position = Config.Bind("2 - Layout", "Position", new Vector2(0.17f, 0.12f),
                "Clock center on screen (0..1, x from left, y from bottom). Or open the inventory and drag the clock.");
            _words = Config.Bind("3 - Words", "DayPhases", DefaultWords,
                "Comma-separated phase names spread evenly across the day, starting at midnight. The dial draws one segment per phase (12 = two hours each; use a count divisible by 4 so sunrise/sunset land on segment edges).");
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void Update()
        {
            if (Hud.instance == null || EnvMan.instance == null) return;
            if (_root == null && !TryCreate()) return;

            var visible = _enabled.Value;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible) return;

            var style = _style.Value;
            var size = _size.Value;
            var fraction = Mathf.Repeat(EnvMan.instance.GetDayFraction(), 1f);
            var editing = InventoryGui.IsVisible();

            _root.sizeDelta = new Vector2(size, size);
            _root.GetComponent<CanvasGroup>().alpha = _opacity.Value;
            _face.gameObject.SetActive(style != ClockStyle.Words);
            _face.SetEditMode(editing);
            CheckAssets();
            _face.SetSegments(_words.Value.Split(',').Length);
            _hand.localRotation = Quaternion.Euler(0f, 0f, -(fraction - 0.25f) * 360f); // sunrise (0.25) at the top

            var text = style == ClockStyle.Dial ? "" : WordFor(fraction, _words.Value);
            if (_showDay.Value)
                text += (text == "" ? "" : "\n") + $"<size=80%>Day {EnvMan.instance.GetDay()}</size>";
            if (_label.text != text) _label.text = text;
            _label.fontSize = _fontSize.Value;
            var labelRt = _label.rectTransform;
            var below = style != ClockStyle.Words;
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(0.5f, below ? 0f : 0.5f);
            labelRt.pivot = new Vector2(0.5f, below ? 1f : 0.5f);
            labelRt.anchoredPosition = new Vector2(0f, below ? -4f : 0f);
            _label.verticalAlignment = below ? VerticalAlignmentOptions.Top : VerticalAlignmentOptions.Middle;

            Drag(editing);
        }

        // Drag with the inventory open (free cursor); saved to config on release.
        private void Drag(bool editing)
        {
            var mouse = new Vector2(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height);
            if (!_dragging && editing && Input.GetMouseButtonDown(0))
            {
                var canvas = _root.GetComponentInParent<Canvas>().rootCanvas;
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var hit = _face.gameObject.activeSelf ? _face.rectTransform : _label.rectTransform;
                if (RectTransformUtility.RectangleContainsScreenPoint(hit, Input.mousePosition, cam))
                {
                    _dragging = true;
                    _dragOffset = _position.Value - mouse;
                }
            }

            var pos = _position.Value;
            if (_dragging)
            {
                pos = new Vector2(Mathf.Clamp01(mouse.x + _dragOffset.x), Mathf.Clamp01(mouse.y + _dragOffset.y));
                if (!editing || !Input.GetMouseButton(0))
                {
                    _dragging = false;
                    _position.Value = pos;
                }
            }
            _root.anchorMin = _root.anchorMax = pos;
            _root.anchoredPosition = Vector2.zero;
        }

        internal static string WordFor(float dayFraction, string words)
        {
            var list = words.Split(',');
            var i = Mathf.Clamp((int)(list.Length * Mathf.Repeat(dayFraction, 1f)), 0, list.Length - 1);
            return list[i].Trim();
        }

        private bool TryCreate()
        {
            // Same bold serif + outline material as the status effect names ("Rested").
            var vanilla = Hud.instance.m_statusEffectTemplate ? Hud.instance.m_statusEffectTemplate.GetComponentInChildren<TMP_Text>(true) : null;
            if (vanilla == null) return false;

            // Under the vanilla HUD root: hides with the HUD and stays below inventory/menus.
            var go = new GameObject("FriendlyClock", typeof(RectTransform), typeof(CanvasGroup));
            _root = (RectTransform)go.transform;
            _root.SetParent(Hud.instance.m_rootObject.transform, false);
            _root.pivot = new Vector2(0.5f, 0.5f);
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            _face = Part("Face", _root, DialGraphic.Part.Face);
            _hand = Part("Hand", _face.transform, DialGraphic.Part.Hand).rectTransform;
            Part("Cap", _face.transform, DialGraphic.Part.Cap); // after the hand so it draws on top, and doesn't rotate

            _label = Child<TextMeshProUGUI>("Label", _root);
            _label.font = vanilla.font;
            _label.fontSharedMaterial = vanilla.fontSharedMaterial;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.color = new Color(0.95f, 0.88f, 0.7f);
            _label.rectTransform.sizeDelta = new Vector2(240f, 60f);
            return true;
        }

        private DialGraphic Part(string name, Transform parent, DialGraphic.Part part)
        {
            var g = Child<DialGraphic>(name, parent);
            g.part = part;
            g.SetVerticesDirty();
            return _parts[(int)part] = g;
        }

        private void CheckAssets()
        {
            if (Time.unscaledTime < _nextAssetCheck) return;
            _nextAssetCheck = Time.unscaledTime + 1f;
            var dir = Path.GetDirectoryName(Info.Location);
            for (var i = 0; i < AssetFiles.Length; i++)
            {
                var path = Path.Combine(dir, AssetFiles[i]);
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
                if (stamp == _assetStamps[i]) continue;
                _assetStamps[i] = stamp;
                Texture2D tex = null;
                if (stamp != default)
                {
                    try
                    {
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                        if (!LoadImage(tex, File.ReadAllBytes(path))) throw new InvalidDataException("not a PNG/JPG");
                        Logger.LogInfo($"Loaded {AssetFiles[i]} ({tex.width}x{tex.height})");
                    }
                    catch (Exception e)
                    {
                        // Usually the file is still being saved; the next save changes the timestamp and retries.
                        Logger.LogWarning($"Could not load {path}: {e.Message}");
                        if (tex != null) Destroy(tex);
                        tex = null;
                    }
                }
                _parts[i].SetTexture(tex);
            }
        }

        // ImageConversionModule targets netstandard 2.1, which a net48 build can't reference; bind it at runtime instead.
        private static readonly Func<Texture2D, byte[], bool> LoadImage = (Func<Texture2D, byte[], bool>)Delegate.CreateDelegate(
            typeof(Func<Texture2D, byte[], bool>),
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true)
                .GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }));

        private static T Child<T>(string name, Transform parent) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(T));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var g = go.GetComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
