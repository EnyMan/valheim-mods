using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimpleCompass
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class SimpleCompassPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mous.simplecompass";
        public const string PluginName = "Simple Compass";
        public const string PluginVersion = "1.0.1";

        private const float PinDeadZone = 5f; // closer than this the bearing jitters wildly
        private const float FocusDegrees = 10f; // how close to the centre a pin must be to get its label
        private static readonly string[] Letters = { "N", "E", "S", "W" };
        // m_pins holds everything the map shows: player pins, pings, shouts, other players, bosses...
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> Pins =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<Vector2> _position;
        private ConfigEntry<float> _width;
        private ConfigEntry<float> _fov;
        private ConfigEntry<float> _opacity;
        private ConfigEntry<bool> _intercardinalTicks;
        private ConfigEntry<float> _fontSize;
        private ConfigEntry<float> _pinRange;
        private ConfigEntry<float> _iconSize;
        private ConfigEntry<bool> _hideCheckedPins;
        private ConfigEntry<bool> _showPinNames;

        private RectTransform _root;
        private CompassLineGraphic _line;
        private readonly TextMeshProUGUI[] _letters = new TextMeshProUGUI[4];
        private readonly List<Image> _icons = new List<Image>();
        private TextMeshProUGUI _focusLabel;
        private TMP_Text _vanillaText;
        private bool _dragging;
        private Vector2 _dragOffset;

        private void Awake()
        {
            _enabled = Config.Bind("1 - General", "Enabled", true, "Show the compass.");
            _fov = Config.Bind("1 - General", "FieldOfView", 160f,
                new ConfigDescription("Degrees of heading visible across the bar.", new AcceptableValueRange<float>(60f, 360f)));
            _intercardinalTicks = Config.Bind("1 - General", "IntercardinalTicks", true, "Small ticks at NE/SE/SW/NW.");
            _position = Config.Bind("2 - Layout", "Position", new Vector2(0.5f, 0.955f),
                "Bar centre on screen (0..1, x from left, y from bottom). Or open the inventory and drag the compass.");
            _width = Config.Bind("2 - Layout", "Width", 640f,
                new ConfigDescription("Bar width in HUD units.", new AcceptableValueRange<float>(200f, 1600f)));
            _opacity = Config.Bind("2 - Layout", "Opacity", 0.9f,
                new ConfigDescription("Overall opacity.", new AcceptableValueRange<float>(0.2f, 1f)));
            _fontSize = Config.Bind("2 - Layout", "FontSize", 18f,
                new ConfigDescription("Size of the direction letters and pin label.", new AcceptableValueRange<float>(10f, 36f)));
            _pinRange = Config.Bind("3 - Pins", "Range", 300f,
                new ConfigDescription("Map pins further than this (metres) are not shown. 0 = no pins.", new AcceptableValueRange<float>(0f, 5000f)));
            _iconSize = Config.Bind("3 - Pins", "IconSize", 22f,
                new ConfigDescription("Icon size of a nearby pin; far pins shrink to half.", new AcceptableValueRange<float>(8f, 64f)));
            _hideCheckedPins = Config.Bind("3 - Pins", "HideCheckedPins", true, "Skip pins you crossed out on the map.");
            _showPinNames = Config.Bind("3 - Pins", "ShowPinNames", true, "Show the name next to the distance of the pin closest to the centre.");
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (Hud.instance == null || player == null || GameCamera.instance == null) return;
            if (_root == null && !TryCreate()) return;

            var visible = _enabled.Value && !Minimap.IsOpen();
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible) return;

            var fov = _fov.Value;
            var half = _width.Value * 0.5f;
            var heading = GameCamera.instance.transform.eulerAngles.y;
            _root.sizeDelta = new Vector2(_width.Value, 40f);
            _root.GetComponent<CanvasGroup>().alpha = _opacity.Value;

            for (var i = 0; i < 4; i++)
            {
                var pos = CompassMath.BarPosition(heading, i * 90f, fov);
                var letter = _letters[i];
                letter.enabled = Mathf.Abs(pos) <= 1f;
                if (!letter.enabled) continue;
                letter.fontSize = _fontSize.Value;
                letter.alpha = CompassMath.EdgeFade(pos);
                letter.rectTransform.anchoredPosition = new Vector2(pos * half, -11f);
            }

            var gapHalf = UpdatePins(player.transform.position, heading, fov, half, out var gapCenter);
            if (_line.heading != heading || _line.fov != fov || _line.intercardinalTicks != _intercardinalTicks.Value
                || _line.gapCenter != gapCenter || _line.gapHalfWidth != gapHalf)
            {
                _line.heading = heading;
                _line.fov = fov;
                _line.intercardinalTicks = _intercardinalTicks.Value;
                _line.gapCenter = gapCenter;
                _line.gapHalfWidth = gapHalf;
                _line.SetVerticesDirty();
            }
            Drag(InventoryGui.IsVisible());
        }

        // Icons sit above the line; the focused pin's distance sits on the line. Returns the label's half width (0 = none).
        private float UpdatePins(Vector3 from, float heading, float fov, float half, out float gapCenter)
        {
            gapCenter = 0f;
            var used = 0;
            Minimap.PinData focus = null;
            Image focusIcon = null;
            var focusOffset = FocusDegrees;
            var range = _pinRange.Value;
            var pins = range > 0f && Minimap.instance != null ? Pins(Minimap.instance) : null;

            if (pins != null)
            {
                foreach (var pin in pins)
                {
                    if (pin.m_icon == null || pin.m_type == Minimap.PinType.EventArea || (pin.m_checked && _hideCheckedPins.Value)) continue;
                    var delta = pin.m_pos - from;
                    var dist = new Vector2(delta.x, delta.z).magnitude;
                    if (dist < PinDeadZone || dist > range) continue;
                    var pos = CompassMath.BarPosition(heading, CompassMath.Bearing(delta.x, delta.z), fov);
                    if (Mathf.Abs(pos) > 1f) continue;

                    var t = dist / range;
                    var icon = Icon(used++);
                    icon.sprite = pin.m_icon;
                    icon.color = new Color(1f, 1f, 1f, CompassMath.EdgeFade(pos) * (1f - Mathf.InverseLerp(0.8f, 1f, t)));
                    icon.rectTransform.sizeDelta = Vector2.one * (_iconSize.Value * Mathf.Lerp(1f, 0.5f, t));
                    icon.rectTransform.anchoredPosition = new Vector2(pos * half, icon.rectTransform.sizeDelta.y * 0.5f + 3f);

                    var offset = Mathf.Abs(pos) * fov * 0.5f;
                    if (offset < focusOffset)
                    {
                        focusOffset = offset;
                        focus = pin;
                        focusIcon = icon;
                    }
                }
            }
            for (var i = used; i < _icons.Count; i++)
                if (_icons[i].enabled) _icons[i].enabled = false;

            _focusLabel.enabled = focus != null;
            if (focus == null) return 0f;
            var d = focus.m_pos - from;
            // Vanilla pin names can hold tokens (death pins are "$hud_mapday 430"); Localize is cached.
            var name = _showPinNames.Value ? Localization.instance.Localize(focus.m_name)?.Trim() : null;
            var text = CompassMath.FormatDistance(new Vector2(d.x, d.z).magnitude);
            _focusLabel.text = string.IsNullOrEmpty(name) ? text : $"{name}  {text}";
            _focusLabel.fontSize = _fontSize.Value * 0.85f;
            gapCenter = focusIcon.rectTransform.anchoredPosition.x;
            _focusLabel.rectTransform.anchoredPosition = new Vector2(gapCenter, 0f);
            focusIcon.transform.SetAsLastSibling();
            return _focusLabel.GetPreferredValues(_focusLabel.text).x * 0.5f + 5f;
        }

        private Image Icon(int index)
        {
            if (index == _icons.Count)
            {
                var go = new GameObject("Pin", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_root, false);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                _icons.Add(img);
            }
            var icon = _icons[index];
            icon.enabled = true;
            return icon;
        }

        // Drag with the inventory open (free cursor); saved to config on release.
        private void Drag(bool editing)
        {
            var mouse = new Vector2(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height);
            if (!_dragging && editing && Input.GetMouseButtonDown(0))
            {
                var canvas = _root.GetComponentInParent<Canvas>().rootCanvas;
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.RectangleContainsScreenPoint(_root, Input.mousePosition, cam))
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

        private bool TryCreate()
        {
            // Same bold serif + outline material as the status effect names ("Rested").
            _vanillaText = Hud.instance.m_statusEffectTemplate ? Hud.instance.m_statusEffectTemplate.GetComponentInChildren<TMP_Text>(true) : null;
            if (_vanillaText == null) return false;

            // Under the vanilla HUD root: hides with the HUD and stays below inventory/menus.
            var go = new GameObject("SimpleCompass", typeof(RectTransform), typeof(CanvasGroup));
            _root = (RectTransform)go.transform;
            _root.SetParent(Hud.instance.m_rootObject.transform, false);
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var lineGo = new GameObject("Line", typeof(RectTransform), typeof(CompassLineGraphic));
            var lineRt = (RectTransform)lineGo.transform;
            lineRt.SetParent(_root, false);
            lineRt.anchorMin = new Vector2(0f, 0.5f);
            lineRt.anchorMax = new Vector2(1f, 0.5f);
            lineRt.sizeDelta = new Vector2(0f, 20f);
            _line = lineGo.GetComponent<CompassLineGraphic>();
            _line.raycastTarget = false;
            _line.color = new Color(0.95f, 0.93f, 0.88f, 0.9f);

            for (var i = 0; i < 4; i++) _letters[i] = Text(Letters[i], new Vector2(0.5f, 1f));
            _focusLabel = Text("", new Vector2(0.5f, 0.5f));
            _focusLabel.color = new Color(1f, 0.8f, 0.4f); // gold, so it doesn't melt into the white line
            return true;
        }

        private TextMeshProUGUI Text(string text, Vector2 pivot)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root, false);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(300f, 30f);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = _vanillaText.font;
            t.fontSharedMaterial = _vanillaText.fontSharedMaterial;
            t.text = text;
            t.raycastTarget = false;
            t.richText = true;
            t.alignment = pivot.y > 0.5f ? TextAlignmentOptions.Top : TextAlignmentOptions.Midline;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = new Color(0.95f, 0.93f, 0.88f);
            return t;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
