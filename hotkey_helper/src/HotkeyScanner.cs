using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace HotkeyHelper
{
    internal sealed class Hotkey
    {
        public string Guid, PluginName, Section, Key, Description, AutoContexts;
        public ConfigEntryBase Entry;
        public ConfigEntry<string> Override;
        public HashSet<string> Contexts = new HashSet<string>();
        public string Label;

        public string KeyText => HotkeyScanner.FormatBinding(Entry.BoxedValue);
        public bool IsGamepad => HotkeyScanner.MainKey(Entry.BoxedValue).ToString().StartsWith("Joystick");
        public bool IsBound => HotkeyScanner.MainKey(Entry.BoxedValue) != KeyCode.None;
    }

    internal static class HotkeyScanner
    {
        public static readonly string[] AllContexts = { "build", "buildmenu", "combat", "inventory", "container", "fishing", "radial", "general" };

        public static readonly List<Hotkey> Hotkeys = new List<Hotkey>();
        public static bool Dirty = true;

        private static readonly HashSet<ConfigFile> Subscribed = new HashSet<ConfigFile>();

        // Bundled defaults for known mods: (guid, key regex, contexts). First match wins, before the heuristic.
        private static readonly (string guid, Regex key, string contexts)[] KnownMods =
        {
            ("shudnal.ExtraSlots", new Regex("^(Quickslot|Ammo|Food)", RegexOptions.IgnoreCase), "hidden"), // already labeled on its own hotbar
            ("Snapheim.Valheim", new Regex(".*"), "build"),
            ("Azumatt.AzuAutoStore", new Regex(".*"), "inventory,container"),
            ("com.morda.storeandcraft", new Regex(".*"), "inventory,container"),
            ("com.nopetrides.valheim.crop-utils", new Regex(".*"), "build"),
            ("MK_BetterUI", new Regex(".*"), "general"), // HUD edit mode keys; description mentions "rotation"
        };

        // ponytail: keyword heuristic, the per-hotkey override config is the escape hatch
        private static readonly (string context, Regex words)[] Keywords =
        {
            ("fishing", Words("fish")),
            ("build", Words("build", "piece", "snap", "place", "rotat", "grid", "hammer", "terrain", "plant", "crop", "hoe", "cultivat")),
            ("container", Words("container", "chest", "store", "dump", "take")),
            ("inventory", Words("inventory", "craft", "recipe", "favo", "sort", "stack", "equip")),
            ("combat", Words("attack", "weapon", "block", "dodge", "shield", "ammo", "bow")),
        };

        private static Regex Words(params string[] w) => new Regex(@"\b(" + string.Join("|", w) + ")", RegexOptions.IgnoreCase);

        public static void Scan()
        {
            Hotkeys.Clear();
            var config = HotkeyHelperPlugin.Instance.Config;
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Metadata.GUID == HotkeyHelperPlugin.PluginGuid) continue;
                var file = info.Instance.Config;
                if (Subscribed.Add(file)) file.SettingChanged += (_, _) => Dirty = true;

                foreach (var pair in file.ToList())
                {
                    var entry = pair.Value;
                    if (entry.SettingType != typeof(KeyboardShortcut) && entry.SettingType != typeof(KeyCode)) continue;

                    var hk = new Hotkey
                    {
                        Guid = info.Metadata.GUID,
                        PluginName = info.Metadata.Name,
                        Section = pair.Key.Section,
                        Key = pair.Key.Key,
                        Description = entry.Description?.Description ?? "",
                        Entry = entry,
                    };
                    hk.AutoContexts = AutoResolve(hk);
                    hk.Override = config.Bind(Sanitize("Hotkeys - " + hk.PluginName), Sanitize(hk.Section + " - " + hk.Key), "auto",
                        $"Where to show '{hk.Key}'. auto = {hk.AutoContexts}. Use auto, hidden, or a comma list of: {string.Join(", ", AllContexts)}. Optional custom label after ';' (e.g. build;Cycle snap).");
                    Resolve(hk);
                    Hotkeys.Add(hk);
                }
            }
            if (Subscribed.Add(config)) config.SettingChanged += (_, _) => Dirty = true;
            Dirty = true;

            var showAll = FormatBinding(HotkeyHelperPlugin.ShowAllKey.Value);
            foreach (var hk in Hotkeys.Where(h => h.IsBound && h.KeyText == showAll))
                HotkeyHelperPlugin.Warn($"ShowAllKey ({showAll}) is also bound by {hk.PluginName}: {hk.Key}");
        }

        public static IEnumerable<Hotkey> Visible(bool gamepad) =>
            Hotkeys.Where(h => h.IsBound && h.IsGamepad == gamepad && !h.Contexts.Contains("hidden"));

        /// <summary>Re-apply overrides (cheap; called whenever any config changes).</summary>
        public static void ResolveAll()
        {
            foreach (var hk in Hotkeys) Resolve(hk);
        }

        private static void Resolve(Hotkey hk)
        {
            var value = hk.Override.Value ?? "auto";
            var split = value.Split(new[] { ';' }, 2);
            var contexts = split[0].Trim();
            if (contexts == "" || contexts.Equals("auto", System.StringComparison.OrdinalIgnoreCase)) contexts = hk.AutoContexts;
            hk.Contexts = new HashSet<string>(contexts.ToLowerInvariant().Split(',').Select(c => c.Trim()).Where(c => c != ""));
            hk.Label = split.Length > 1 && split[1].Trim() != "" ? split[1].Trim() : Humanize(hk.Key, hk.PluginName);
        }

        internal static string AutoResolve(Hotkey hk)
        {
            foreach (var (guid, key, contexts) in KnownMods)
                if (guid == hk.Guid && key.IsMatch(hk.Key)) return contexts;

            // Name-ish text first; description only if the name says nothing (descriptions are noisy).
            foreach (var text in new[] { hk.Section + " " + hk.Key + " " + hk.PluginName, hk.Description })
            {
                var hits = Keywords.Where(k => k.words.IsMatch(text)).Select(k => k.context).ToList();
                if (hits.Count > 0) return string.Join(",", hits);
            }
            return "general";
        }

        /// <summary>"increaseRangeKeyGamepad" -> "Increase Range"; a name that is only "HotKey" falls back to the plugin name.</summary>
        internal static string Humanize(string key, string fallback)
        {
            var s = Regex.Replace(key, "(?<=[a-z])(?=[A-Z])", " ");
            s = Regex.Replace(s, @"(\s*(hot\s*key|short\s*cut|key\s*bind\d*|key|gamepad))+\s*$", "", RegexOptions.IgnoreCase).Trim();
            if (s == "") return fallback;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static string Sanitize(string s) => Regex.Replace(s, "[=\\n\\t\\\\\"'\\[\\]]", " ");

        internal static KeyCode MainKey(object binding) => binding switch
        {
            KeyboardShortcut ks => ks.MainKey,
            KeyCode kc => kc,
            _ => KeyCode.None,
        };

        internal static string FormatBinding(object binding)
        {
            if (binding is KeyboardShortcut ks)
                return string.Join(" + ", ks.Modifiers.Select(KeyName).Concat(new[] { KeyName(ks.MainKey) }));
            return binding is KeyCode kc ? KeyName(kc) : "";
        }

        internal static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "MMB";
                case KeyCode.Period: return ".";
                case KeyCode.Comma: return ",";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Plus: return "+";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.BackQuote: return "`";
            }
            var s = k.ToString();
            if (s.StartsWith("Alpha")) return s.Substring(5);
            if (s.StartsWith("Keypad")) return "Num" + s.Substring(6);
            if (s.StartsWith("Joystick")) return "Pad " + s.Substring(s.IndexOf("Button") + 6);
            return s.Replace("Left", "L").Replace("Right", "R").Replace("Control", "Ctrl");
        }
    }
}
