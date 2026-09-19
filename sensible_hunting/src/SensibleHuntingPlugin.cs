using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SensibleHunting
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class SensibleHuntingPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mous.sensiblehunting";
        public const string PluginName = "Sensible Hunting";
        public const string PluginVersion = "1.2.0";
        internal const string SkillIdentifier = PluginGuid + ".hunting";

        internal static Skills.SkillType Hunting;
        internal static SensibleHuntingPlugin Instance;

        private static readonly Color Gold = new Color(1f, 0.79f, 0.25f);
        private const float PulseFade = 0.3f; // seconds of ramp at each end of a pulse

        internal ConfigEntry<bool> Enabled;
        internal ConfigEntry<float> MinRange;
        internal ConfigEntry<float> MaxRange;
        internal ConfigEntry<float> EdgeMargin;
        internal ConfigEntry<float> NearSize;
        internal ConfigEntry<float> FarSize;
        internal ConfigEntry<float> Opacity;
        internal ConfigEntry<bool> RequireCrouch;
        internal ConfigEntry<string> GameAnimals;
        internal ConfigEntry<float> MaxBearingError;
        internal ConfigEntry<float> DaytimeErrorScale;
        internal ConfigEntry<float> SightRadius;
        internal ConfigEntry<float> PulseOnAtZero;
        internal ConfigEntry<float> PulseOnAtHundred;
        internal ConfigEntry<float> PulseGapAtZero;
        internal ConfigEntry<float> PulseGapAtHundred;
        internal ConfigEntry<float> BallparkStep;
        internal ConfigEntry<string> Glyph;
        internal ConfigEntry<string> EdgeGlyph;
        internal ConfigEntry<float> StalkFactor;
        internal ConfigEntry<float> HitFactor;
        internal ConfigEntry<float> KillFactor;
        internal ConfigEntry<float> StalkMoveDistance;

        private Harmony _harmony;
        private static int _viewBlockMask;
        private RectTransform _root;
        private readonly List<BlipView> _blips = new List<BlipView>();
        private readonly List<Character> _scratch = new List<Character>();

        /// One indicator: the wave glyph (rotates to point off-screen), the species icon, the distance.
        private class BlipView
        {
            public RectTransform Root;
            public TextMeshProUGUI Wave;
            public TextMeshProUGUI Label;
            public Image Icon;

            public void SetActive(bool on)
            {
                if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            }
        }

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("1 - General", "Enabled", true, "Show hunting indicators.");
            MinRange = Config.Bind("1 - General", "MinRange", 30f,
                new ConfigDescription("Detection radius at skill 0 (metres).", new AcceptableValueRange<float>(5f, 100f)));
            MaxRange = Config.Bind("1 - General", "MaxRange", 90f,
                new ConfigDescription("Detection radius at skill 100 (metres). Only creatures loaded around you can be sensed: " +
                    "at the default Simulation Distance that is every direction to ~128 m and some directions to ~270 m.",
                    new AcceptableValueRange<float>(10f, 175f)));
            GameAnimals = Config.Bind("1 - General", "GameAnimals", "Boar,Neck,Lox,Asksvin,Wolf,Seagal,Moose,Seal",
                "Creatures that fight back but still count as game, plus anything that is not a plain ground animal " +
                "(prefab names, comma separated - Seagal is the seagull, spelled that way in the game files). " +
                "Anything with the passive animal AI - deer, hares, chickens, young animals - always counts.");
            GameAnimals.SettingChanged += (_, __) => _gameNames = null;
            RequireCrouch = Config.Bind("1 - General", "RequireCrouch", true,
                "Only sense game while crouched. Standing up stops the instinct.");
            MaxBearingError = Config.Bind("1 - General", "MaxBearingError", 10f,
                new ConfigDescription("How far off a blip may point at skill 0, in degrees. Shrinks as the skill rises, gone by 50.",
                    new AcceptableValueRange<float>(0f, 90f)));
            DaytimeErrorScale = Config.Bind("1 - General", "DaytimeErrorScale", 2f,
                new ConfigDescription("Multiplier on MaxBearingError at midday, ramping down to 1x through dusk and back up through dawn. " +
                    "Set to 1 for no day/night difference. Only bites below skill 50, where the bearing error exists at all.",
                    new AcceptableValueRange<float>(0.1f, 5f)));
            SightRadius = Config.Bind("1 - General", "SightRadius", 0.3f,
                new ConfigDescription("Thickness of the line-of-sight probe, in metres. A hair-thin ray (0) slips past tree trunks; " +
                    "wider counts more things as cover, but an animal standing tight against the ground or a wall starts reading as hidden.",
                    new AcceptableValueRange<float>(0f, 2f)));
            BallparkStep = Config.Bind("1 - General", "BallparkStep", 5f,
                new ConfigDescription("Rounding of the rough distance shown at skill 25-49 (metres).", new AcceptableValueRange<float>(1f, 25f)));
            PulseOnAtZero = Config.Bind("2 - Pulse", "VisibleAtSkill0", 0.8f,
                new ConfigDescription("Seconds a blip stays up per pulse at skill 0.", new AcceptableValueRange<float>(0.1f, 10f)));
            PulseOnAtHundred = Config.Bind("2 - Pulse", "VisibleAtSkill100", 2.5f,
                new ConfigDescription("Seconds a blip stays up per pulse at skill 100.", new AcceptableValueRange<float>(0.1f, 10f)));
            PulseGapAtZero = Config.Bind("2 - Pulse", "GapAtSkill0", 6f,
                new ConfigDescription("Seconds of nothing between pulses at skill 0.", new AcceptableValueRange<float>(0f, 30f)));
            PulseGapAtHundred = Config.Bind("2 - Pulse", "GapAtSkill100", 1.5f,
                new ConfigDescription("Seconds of nothing between pulses at skill 100.", new AcceptableValueRange<float>(0f, 30f)));
            EdgeMargin = Config.Bind("2 - Layout", "EdgeMargin", 64f,
                new ConfigDescription("Keep edge indicators this far inside the screen, in HUD units (screen is 1920x1080 units at GUI scale 1).", new AcceptableValueRange<float>(0f, 300f)));
            NearSize = Config.Bind("2 - Layout", "NearSize", 34f,
                new ConfigDescription("Size of a blip right next to you.", new AcceptableValueRange<float>(8f, 96f)));
            FarSize = Config.Bind("2 - Layout", "FarSize", 16f,
                new ConfigDescription("Size of a blip at the edge of your detection range.", new AcceptableValueRange<float>(8f, 96f)));
            Opacity = Config.Bind("2 - Layout", "Opacity", 0.85f,
                new ConfigDescription("Overall opacity.", new AcceptableValueRange<float>(0.1f, 1f)));
            Glyph = Config.Bind("2 - Layout", "Glyph", "((•))", "Blip drawn over an animal you can see.");
            EdgeGlyph = Config.Bind("2 - Layout", "EdgeGlyph", "•)))", "Blip pinned to the screen edge; it is rotated to point at the animal.");
            StalkFactor = Config.Bind("3 - Experience", "StalkFactor", 0.25f,
                new ConfigDescription("Fraction of the sneak XP tick also awarded as hunting XP while an animal is detected.",
                    new AcceptableValueRange<float>(0f, 2f)));
            HitFactor = Config.Bind("3 - Experience", "HitFactor", 1f,
                new ConfigDescription("XP for hitting a wild animal, scaled by the fraction of its health removed.",
                    new AcceptableValueRange<float>(0f, 10f)));
            KillFactor = Config.Bind("3 - Experience", "KillFactor", 4f,
                new ConfigDescription("XP for killing a wild animal, multiplied by its star level.",
                    new AcceptableValueRange<float>(0f, 50f)));
            StalkMoveDistance = Config.Bind("3 - Experience", "StalkMoveDistance", 5f,
                new ConfigDescription("Anti-AFK: stalking only pays again once you have moved this far or the nearest animal changed.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Hunting = SkillManager.Instance.AddSkill(new SkillConfig
            {
                Identifier = SkillIdentifier,
                Name = "Hunting",
                Description = "Reading the woods. Higher hunting senses wild game further away, points at it more truly, and tells you what it is.",
                IncreaseStep = 1f,
            });

            // Same layers BaseAI blocks vision with, minus "viewblock" - that one is there to blind
            // monsters, not you.
            _viewBlockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

            SpeciesIcons.Load(Path.GetDirectoryName(Info.Location), Logger);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(HuntingXp));
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private static HashSet<string> _gameNames;

        /// Is this something you hunt? Not by faction: Boar, Neck and Lox are MonsterAI creatures outside
        /// AnimalsVeg, so a faction test silently rejects the most common game in the game. Passive
        /// AnimalAI creatures always count; animals that fight back come from the GameAnimals list.
        internal static bool IsGame(Character c)
        {
            if (c == null || c.IsPlayer() || c.IsTamed()) return false;
            if (c.GetBaseAI() is AnimalAI) return true;
            return IsGameName(c.gameObject);
        }

        /// The prefab-name half of IsGame, for game that is not a Character at all.
        internal static bool IsGameName(GameObject go)
        {
            if (_gameNames == null) _gameNames = HuntMath.ParseNames(Instance.GameAnimals.Value);
            return _gameNames.Contains(Utils.GetPrefabName(go));
        }

        /// Game that is still worth sensing or stalking.
        internal static bool IsWildGame(Character c) => IsGame(c) && !c.IsDead();

        /// Game the instinct can actually read. Nothing in the air: there are no tracks in the sky, and a
        /// blip hanging over open water on a circling gull is noise. `Character.m_flying` is a prefab flag
        /// ("this species flies"), so only fliers get the on-the-ground test - a boar mid-jump never blinks
        /// out. Hitting and killing still pay XP: shooting a gull out of the air is hunting, just not tracking.
        internal static bool CanSense(Character c) => IsWildGame(c) && (!c.IsFlying() || c.IsOnGround());

        /// Nearest wild animal to the player within the current detection range, or null.
        internal Character NearestGame(Player player, float range)
        {
            _scratch.Clear();
            Character.GetCharactersInRange(player.transform.position, range, _scratch);
            Character best = null;
            var bestSqr = float.MaxValue;
            foreach (var c in _scratch)
            {
                if (!CanSense(c)) continue;
                var sqr = (c.transform.position - player.transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = c;
            }
            return best;
        }

        internal float CurrentRange(Player player) =>
            HuntMath.BySkill(player.GetSkillFactor(Hunting), MinRange.Value, MaxRange.Value);

        /// Sensing is something you do, not something you have on: you have to be crouched and listening.
        internal bool IsSensing(Player player) => !RequireCrouch.Value || player.IsCrouching();

        /// Where an animal was when a pulse sensed it. The blip stays on this spot for the whole pulse
        /// even if the animal walks off: you heard something there, not a tracker on its back. Unless
        /// you can see the animal - then the blip tracks it, because pretending not to would look broken.
        private struct Sighting
        {
            public Vector3 Position;  // already skewed by the bearing error
            public Sprite Icon;       // captured now: the animal may be gone by the time we draw
            public bool Gold;         // our own white art takes the gold tint; a trophy icon must not
            public Character Seen;    // set when you can actually see it: then the blip follows it live
        }

        /// Can the player see this point with their own eyes? Terrain, buildings and tree trunks block
        /// it, and so does the Mistlands mist - by the game's own test, so a wisplight opens the view
        /// back up. Swept with a radius, the way projectiles are (Projectile uses SphereCastAll with
        /// m_rayRadius): a hair-thin ray threads straight past a trunk that plainly hides the animal.
        /// The sweep stops one radius short of the target so the sphere does not clip the ground the
        /// animal is standing on and call clear ground cover.
        internal static bool CanSee(Vector3 eye, Vector3 point, float radius)
        {
            var delta = point - eye;
            var distance = delta.magnitude;
            if (distance < 0.01f) return true;
            var dir = delta / distance;
            if (radius <= 0f)
            {
                if (Physics.Raycast(eye, dir, distance, _viewBlockMask)) return false;
            }
            else if (Physics.SphereCast(eye, radius, dir, out _, Mathf.Max(0.01f, distance - radius), _viewBlockMask))
            {
                return false;
            }
            return !ParticleMist.IsMistBlocked(eye, point);
        }

        private readonly List<Sighting> _sightings = new List<Sighting>();
        private int _pulseIndex = -1;

        private void Update()
        {
            var player = Player.m_localPlayer;
            var cam = Camera.main;
            if (Hud.instance == null || player == null || cam == null) return;
            if (_root == null && !TryCreate()) return;

            var visible = Enabled.Value && !Minimap.IsOpen() && !player.IsDead() && IsSensing(player);
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible)
            {
                _pulseIndex = -1; // crouching again starts a fresh listen instead of replaying a stale one
                return;
            }

            var skill = player.GetSkillFactor(Hunting);
            var range = CurrentRange(player);
            var tier = HuntMath.DetailTier(skill);
            var on = HuntMath.BySkill(skill, PulseOnAtZero.Value, PulseOnAtHundred.Value);
            var gap = HuntMath.BySkill(skill, PulseGapAtZero.Value, PulseGapAtHundred.Value);
            var eye = player.transform.position;

            // New pulse: listen once, remember where everything was.
            var index = HuntMath.PulseIndex(Time.time, on, gap);
            if (index != _pulseIndex)
            {
                _pulseIndex = index;
                Sense(eye, player.m_eye.position, cam, range, tier);
            }

            // The whole display pulses: up for a moment, then gone. Skip the work while it is dark.
            var pulse = HuntMath.PulseAlpha(Time.time, on, gap, PulseFade);
            if (pulse <= 0f)
            {
                for (var i = 0; i < _blips.Count; i++) _blips[i].SetActive(false);
                return;
            }

            // Canvas units, NOT Screen pixels: Valheim's GUI canvas has a CanvasScaler whose factor is
            // min(width/1920, height/1080) x the GuiScale setting, so anything but 1920x1080 at scale 1
            // pushed every blip away from the bottom-left corner and threw right-side ones off screen.
            var rect = _root.rect;
            float w = rect.width, h = rect.height;
            var used = 0;
            foreach (var sighting in _sightings)
            {
                // Distance to the spot from where you are now: walking toward it reads as closing in.
                var spot = sighting.Seen != null ? sighting.Seen.GetCenterPoint() : sighting.Position;
                var distance = Vector3.Distance(spot, eye);
                var v = cam.WorldToViewportPoint(spot);
                var blip = HuntMath.Project(v.x, v.y, v.z, w, h, EdgeMargin.Value);
                var size = HuntMath.BlipScale(distance, range, FarSize.Value, NearSize.Value);
                var alpha = Opacity.Value * pulse * HuntMath.DistanceFade(distance, range);

                var view = ViewAt(used++);
                view.SetActive(true);
                view.Root.anchoredPosition = new Vector2(blip.X, blip.Y);

                // The species icon replaces the wave when the spot is on screen, an arrow adds nothing.
                // Off screen the wave stays and rotates, or the direction is lost.
                var icon = sighting.Icon;
                var showWave = icon == null || blip.OffScreen;
                view.Wave.gameObject.SetActive(showWave);
                if (showWave)
                {
                    view.Wave.text = blip.OffScreen ? EdgeGlyph.Value : Glyph.Value;
                    view.Wave.fontSize = size;
                    view.Wave.alpha = alpha;
                    view.Wave.rectTransform.localEulerAngles = blip.OffScreen ? new Vector3(0f, 0f, blip.Angle) : Vector3.zero;
                }

                view.Icon.gameObject.SetActive(icon != null);
                if (icon != null)
                {
                    view.Icon.sprite = icon;
                    view.Icon.color = sighting.Gold ? new Color(Gold.r, Gold.g, Gold.b, alpha) : new Color(1f, 1f, 1f, alpha);
                    view.Icon.rectTransform.sizeDelta = new Vector2(size * 1.6f, size * 1.6f);
                    view.Icon.rectTransform.anchoredPosition = new Vector2(0f, showWave ? size * 1.3f : 0f);
                }

                var label = tier == 0 ? "" : tier == 1 ? HuntMath.Ballpark(distance, BallparkStep.Value) : HuntMath.Exact(distance);
                view.Label.gameObject.SetActive(label.Length > 0);
                view.Label.text = label;
                view.Label.fontSize = size * 0.7f;
                view.Label.alpha = alpha;
                view.Label.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.9f);
            }
            for (var i = used; i < _blips.Count; i++) _blips[i].SetActive(false);
        }

        /// One listen: snapshot every animal in range, with this stage's bearing error baked in.
        private void Sense(Vector3 eye, Vector3 head, Camera cam, float range, int tier)
        {
            _sightings.Clear();
            // Broad daylight is the worst time to read the woods: the instinct is sharpest at night.
            var daylight = EnvMan.instance == null ? 0f : HuntMath.Daylight(EnvMan.instance.GetDayFraction());
            var maxError = MaxBearingError.Value * Mathf.Lerp(1f, DaytimeErrorScale.Value, daylight);
            var error = HuntMath.ErrorForTier(tier, maxError);
            _scratch.Clear();
            Character.GetCharactersInRange(eye, range, _scratch);
            foreach (var c in _scratch)
            {
                if (!CanSense(c)) continue;
                Add(eye, head, cam, error, c.GetZDOID().GetHashCode(), c.GetCenterPoint(), c,
                    tier >= 3 ? SpeciesIcons.For(c.gameObject, c) : null);
            }

            // Seagulls are not creatures. Seagal is a re-skinned crow: a RandomFlyingBird with no
            // Character component, so GetCharactersInRange can never return one however the config
            // lists it. Its own static registry can. Landed only, the same rule flying creatures get,
            // and that state lives in the ZDO rather than in a field we can read.
            foreach (var updater in RandomFlyingBird.Instances)
            {
                if (!(updater is RandomFlyingBird bird) || !IsGameName(bird.gameObject)) continue;
                var pos = bird.transform.position;
                if ((pos - eye).sqrMagnitude > range * range) continue;
                var nview = bird.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.GetZDO().GetBool(ZDOVars.s_landed)) continue;
                // No Character means no drop table to fall back on and nothing to follow live: the blip
                // stays where the pulse heard it, which is fine for a bird sitting still.
                Add(eye, head, cam, error, bird.GetInstanceID(), pos, null,
                    tier >= 3 ? SpeciesIcons.For(bird.gameObject, null) : null);
            }
        }

        /// One sighting. If it is right there in front of you, your eyes win: no bearing error, and the
        /// blip rides the animal instead of the spot - guessing at something you are looking at reads as
        /// a bug. On screen is a camera question; "is a tree in the way" is a head question, because the
        /// 3rd person camera sits metres behind and above you and sees round trunks you do not.
        private void Add(Vector3 eye, Vector3 head, Camera cam, float error, int seed, Vector3 target,
            Character live, Sprite icon)
        {
            var view = cam.WorldToViewportPoint(target);
            var seen = HuntMath.InView(view.x, view.y, view.z) && CanSee(head, target, SightRadius.Value);
            // Low skill points you the wrong way on purpose: a fixed error per animal, not per pulse.
            if (error > 0f && !seen)
            {
                var skew = Quaternion.AngleAxis(HuntMath.BearingErrorDegrees(seed, error), Vector3.up);
                target = eye + skew * (target - eye);
            }
            _sightings.Add(new Sighting
            {
                Position = target,
                Icon = icon,
                Gold = SpeciesIcons.IsOurs(icon),
                Seen = seen ? live : null,
            });
        }

        private BlipView ViewAt(int index)
        {
            while (_blips.Count <= index)
            {
                var go = new GameObject($"blip{_blips.Count}", typeof(RectTransform));
                go.transform.SetParent(_root, false);
                var root = go.GetComponent<RectTransform>();
                root.anchorMin = root.anchorMax = Vector2.zero;
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = Vector2.zero;
                _blips.Add(new BlipView
                {
                    Root = root,
                    Wave = MakeText(root, "wave"),
                    Label = MakeText(root, "label"),
                    Icon = MakeIcon(root),
                });
            }
            return _blips[index];
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = Hud.instance.m_healthText.font;
            text.fontSharedMaterial = Hud.instance.m_healthText.fontSharedMaterial;
            text.color = Gold;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(300f, 48f);
            return text;
        }

        private static Image MakeIcon(RectTransform parent)
        {
            var go = new GameObject("icon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        private bool TryCreate()
        {
            if (Hud.instance == null || Hud.instance.m_rootObject == null) return false;
            var go = new GameObject("SensibleHuntingRoot", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(Hud.instance.m_rootObject.transform, false);
            _root = go.GetComponent<RectTransform>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            // Same trick as Hotkey Helper: InventoryGui draws over anything under m_rootObject.
            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 10;
            return true;
        }
    }
}
