using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        public const string PluginVersion = "1.1.2";

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
        private ConfigEntry<float> _tipWidth;
        private ConfigEntry<float> _tipFontSize;
        private ConfigEntry<Color> _tipBackground;

        // BepInEx/config/FriendlyClock.phases.md: each "## Word" heading is a phase, the markdown under it its hover tooltip.
        private const string PhaseFileName = "FriendlyClock.phases.md";
        private DateTime _phaseStamp;
        private string[] _fileWords, _fileTips;

        private RectTransform _root;
        private RectTransform _tip;
        private TextMeshProUGUI _tipText;
        private readonly List<Image> _rules = new List<Image>();
        private const float TipPadding = 10f;
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
                "Comma-separated phase names spread evenly across the day, starting at midnight. The dial draws one segment per phase (12 = two hours each; use a count divisible by 4 so sunrise/sunset land on segment edges). " +
                $"Ignored while BepInEx/config/{PhaseFileName} exists: there each '## Word' heading is a phase and the markdown below it shows on hover (inventory, map or game menu open).");
            _tipWidth = Config.Bind("4 - Tooltip", "MaxWidth", 380f,
                new ConfigDescription($"Maximum width of the {PhaseFileName} hover tooltip.", new AcceptableValueRange<float>(150f, 1000f)));
            _tipFontSize = Config.Bind("4 - Tooltip", "FontSize", 18f,
                new ConfigDescription("Base font size of the tooltip (headings scale from it).", new AcceptableValueRange<float>(10f, 40f)));
            _tipBackground = Config.Bind("4 - Tooltip", "Background", new Color(0.05f, 0.04f, 0.03f, 0.9f), "Tooltip background color (RGBA).");
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
            CheckFiles();
            var words = _fileWords ?? _words.Value.Split(',');
            var phase = PhaseIndex(fraction, words.Length);
            _face.SetSegments(words.Length);
            _hand.localRotation = Quaternion.Euler(0f, 0f, -(fraction - 0.25f) * 360f); // sunrise (0.25) at the top

            var text = style == ClockStyle.Dial ? "" : words[phase].Trim();
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
            Tooltip(editing || Minimap.IsOpen() || Menu.IsVisible() ? _fileTips?[phase] : null);
        }

        // Shown while the cursor is free and over the clock; follows the mouse, opening away from the nearest screen edges.
        private void Tooltip(string tip)
        {
            var mouse = Input.mousePosition;
            var cam = UiCamera();
            var show = !string.IsNullOrEmpty(tip) && !_dragging &&
                       ((_face.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_face.rectTransform, mouse, cam)) ||
                        OverText(_label, mouse, cam));
            if (_tip.gameObject.activeSelf != show)
            {
                _tip.gameObject.SetActive(show);
                if (show)
                {
                    // Nested canvas so it draws over the inventory; only sticks while active.
                    var canvas = _tip.GetComponent<Canvas>();
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = 30000;
                }
            }
            if (!show) return;

            _tip.GetComponent<Image>().color = _tipBackground.Value;
            _tipText.fontSize = _tipFontSize.Value;
            var max = _tipWidth.Value - 2f * TipPadding;
            // Each rule is a blank line (a no-break space) with a drawn line laid over it after layout.
            var shown = tip.Replace(RuleMark, " ");
            var w = Mathf.Min(max, _tipText.GetPreferredValues(shown, max, 0f).x);
            // Last GetPreferredValues call must be on the shown string: TMP builds the mesh from the string it last parsed.
            var h = _tipText.GetPreferredValues(shown, w, 0f).y;
            if (_tipText.text != shown) _tipText.text = shown;
            _tip.sizeDelta = new Vector2(w, h) + 2f * TipPadding * Vector2.one;
            PlaceRules(tip);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, mouse, cam, out var local);
            var right = mouse.x < Screen.width * 0.5f;
            var up = mouse.y < Screen.height * 0.5f;
            _tip.pivot = new Vector2(right ? 0f : 1f, up ? 0f : 1f);
            _tip.anchoredPosition = local + new Vector2(right ? 16f : -16f, up ? 16f : -16f);
        }

        // One full-width line per RuleMark, centered on the line TMP laid its no-break space out on.
        private void PlaceRules(string tip)
        {
            var marks = new HashSet<int>();
            for (int i = tip.IndexOf(RuleMark, StringComparison.Ordinal), k = 0; i >= 0; i = tip.IndexOf(RuleMark, i + 1, StringComparison.Ordinal), k++)
                marks.Add(i - k * (RuleMark.Length - 1)); // index in the shown string, where each mark is one character
            var used = 0;
            if (marks.Count > 0)
            {
                _tipText.ForceMeshUpdate(); // layout for this frame's size, not last frame's
                var info = _tipText.textInfo;
                var rect = _tipText.rectTransform.rect;
                for (var i = 0; i < info.characterCount; i++)
                {
                    if (!marks.Contains(info.characterInfo[i].index)) continue;
                    var line = info.lineInfo[info.characterInfo[i].lineNumber];
                    if (used == _rules.Count) _rules.Add(Child<Image>("Rule", _tipText.transform));
                    var rule = _rules[used++];
                    rule.gameObject.SetActive(true);
                    rule.color = new Color(0.54f, 0.51f, 0.45f, 0.9f);
                    var rt = rule.rectTransform;
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(rect.width, 2f);
                    rt.anchoredPosition = new Vector2(rect.center.x, (line.ascender + line.descender) * 0.5f);
                }
            }
            for (var i = used; i < _rules.Count; i++) _rules[i].gameObject.SetActive(false);
        }

        // The drawn glyphs (plus a small margin), not the fixed 240x60 label box, so pins next to a words-only clock stay reachable.
        private static bool OverText(TMP_Text text, Vector2 screen, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(text.rectTransform, screen, cam, out var local)) return false;
            var b = text.textBounds;
            return b.size.x > 0f && Rect.MinMaxRect(b.min.x - 6f, b.min.y - 6f, b.max.x + 6f, b.max.y + 6f).Contains(local);
        }

        private Camera UiCamera()
        {
            var canvas = _root.GetComponentInParent<Canvas>().rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        // Drag with the inventory open (free cursor); saved to config on release.
        private void Drag(bool editing)
        {
            var mouse = new Vector2(Input.mousePosition.x / Screen.width, Input.mousePosition.y / Screen.height);
            if (!_dragging && editing && Input.GetMouseButtonDown(0))
            {
                var cam = UiCamera();
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

        internal static int PhaseIndex(float dayFraction, int count) =>
            Mathf.Clamp((int)(count * Mathf.Repeat(dayFraction, 1f)), 0, count - 1);

        // "## Word" starts a phase; everything up to the next "## " is its tooltip. Text before the first heading is ignored.
        internal static List<KeyValuePair<string, string>> ParsePhases(string md)
        {
            var phases = new List<KeyValuePair<string, string>>();
            string word = null;
            var body = new StringBuilder();
            foreach (var line in md.Replace("\r", "").Split('\n').Append("## "))
            {
                if (!line.StartsWith("## ")) { body.AppendLine(line); continue; }
                if (word != null) phases.Add(new KeyValuePair<string, string>(word, MarkdownToTmp(body.ToString())));
                word = line.Substring(3).Trim();
                body.Clear();
            }
            return phases;
        }

        private static readonly int[] HeadingSizes = { 150, 130, 115 };

        // A --- line; drawn as a full-width line once the tooltip is laid out (PlaceRules).
        internal const string RuleMark = "\u001Frule\u001F";

        // Markdown subset -> TMP rich text: # headings, **bold**, *italic*/_italic_, ~~strike~~, - lists, > quotes, --- rules.
        // TMP tags written directly in the file pass through untouched.
        internal static string MarkdownToTmp(string md)
        {
            var lines = new List<string>();
            foreach (var raw in md.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                var quote = false;
                while (line.StartsWith(">")) { quote = true; line = line.Substring(1).TrimStart(); }
                var indent = quote ? 1 : 0; // em, not %: TMP takes % of the current rect width, which the tooltip sizes from the text

                Match m;
                if ((m = Regex.Match(line, @"^(#{1,6})\s+(.*)$")).Success)
                    line = $"<size={(m.Groups[1].Length <= 3 ? HeadingSizes[m.Groups[1].Length - 1] : 100)}%><b>{Inline(m.Groups[2].Value)}</b></size>";
                else if (Regex.IsMatch(line, @"^([-*_])( *\1){2,}$"))
                    line = RuleMark;
                else if ((m = Regex.Match(line, @"^[-*+]\s+(.*)$")).Success)
                    line = $"<indent={indent}em>•<indent={indent + 1}em>{Inline(m.Groups[1].Value)}</indent>"; // hanging indent
                else
                    line = indent > 0 ? $"<indent={indent}em>{Inline(line)}</indent>" : Inline(line);

                lines.Add(quote && line != RuleMark ? $"<color=#F2E0B3>{line}</color>" : line);
            }
            return string.Join("\n", lines).Trim('\n');
        }

        private static string Inline(string s)
        {
            s = Regex.Replace(s, @"\*\*(.+?)\*\*|__(.+?)__", m => $"<b>{m.Groups[1].Value}{m.Groups[2].Value}</b>");
            s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)",
                m => $"<i>{m.Groups[1].Value}{m.Groups[2].Value}</i>");
            return Regex.Replace(s, @"~~(.+?)~~", "<s>$1</s>");
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
            Array.Clear(_assetStamps, 0, _assetStamps.Length); // fresh parts (new world) need the art loaded again
            _nextAssetCheck = 0f;

            _label = Child<TextMeshProUGUI>("Label", _root);
            _label.font = vanilla.font;
            _label.fontSharedMaterial = vanilla.fontSharedMaterial;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.color = new Color(0.95f, 0.88f, 0.7f);
            _label.rectTransform.sizeDelta = new Vector2(240f, 60f);

            var tip = new GameObject("Tooltip", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image));
            _tip = (RectTransform)tip.transform;
            _tip.SetParent(_root, false);
            _tip.anchorMin = _tip.anchorMax = new Vector2(0.5f, 0.5f);
            var tipGroup = tip.GetComponent<CanvasGroup>();
            tipGroup.ignoreParentGroups = true; // full opacity regardless of the clock's Opacity
            tipGroup.blocksRaycasts = false;
            tip.GetComponent<Image>().raycastTarget = false;
            _tipText = Child<TextMeshProUGUI>("Text", _tip);
            _rules.Clear(); // the old ones died with the previous world's HUD
            _tipText.rectTransform.offsetMin = Vector2.one * TipPadding;
            _tipText.rectTransform.offsetMax = -Vector2.one * TipPadding;
            _tipText.font = vanilla.font;
            _tipText.fontSharedMaterial = vanilla.fontSharedMaterial;
            _tipText.alignment = TextAlignmentOptions.TopLeft;
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            _tipText.color = new Color(0.93f, 0.93f, 0.9f);
            tip.SetActive(false);
            return true;
        }

        private DialGraphic Part(string name, Transform parent, DialGraphic.Part part)
        {
            var g = Child<DialGraphic>(name, parent);
            g.part = part;
            g.SetVerticesDirty();
            return _parts[(int)part] = g;
        }

        private void CheckFiles()
        {
            if (Time.unscaledTime < _nextAssetCheck) return;
            _nextAssetCheck = Time.unscaledTime + 1f;

            var phaseFile = Path.Combine(Paths.ConfigPath, PhaseFileName);
            var phaseStamp = File.Exists(phaseFile) ? File.GetLastWriteTimeUtc(phaseFile) : default;
            if (phaseStamp != _phaseStamp)
            {
                _phaseStamp = phaseStamp;
                _fileWords = _fileTips = null;
                if (phaseStamp != default)
                {
                    try
                    {
                        var phases = ParsePhases(File.ReadAllText(phaseFile));
                        if (phases.Count == 0) Logger.LogWarning($"{phaseFile} has no '## Word' headings; using DayPhases");
                        else
                        {
                            _fileWords = phases.Select(p => p.Key).ToArray();
                            _fileTips = phases.Select(p => p.Value).ToArray();
                            Logger.LogInfo($"Loaded {phases.Count} phases from {phaseFile}");
                        }
                    }
                    catch (IOException e)
                    {
                        Logger.LogWarning($"Could not read {phaseFile}: {e.Message}"); // mid-save; retried on the next change
                    }
                }
            }

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
