# Sensible Hunting — design notes

Client-side mod adding a **Hunting** skill. The skill passively reveals nearby wild
animals as full-screen directional indicators (a sound-wave blip), including animals
*behind* the player. Higher skill = longer range and more detail per blip.

Not a radar: 90 m default cap (200 m configurable max), no minimap pins, no through-terrain guarantees. It should feel
like reading the woods, not like a sonar sweep.

## Scope decisions (settled)

- **Sensing is an action, not a state.** Blips only appear while **crouched** (`RequireCrouch`,
  on by default). Always-on made the skill feel like a HUD element rather than something you do.
- **The display pulses.** Blips flash up, fade, and go dark for a few seconds before the next
  pulse. A constant readout is a tracker; an intermittent one is an impression you have to act on.

- **Own mod**, not a Simple Compass option — indicators live on the whole screen, not on
  a forward-facing compass bar, and must resolve directions outside the camera frustum.
- Client-side only. Custom skills live in the player profile → no RPCs, no
  `EveryoneMustHaveMod`, no server sync. Plain BepInEx + Jötunn (Jötunn needed only for
  `SkillManager`).
- Range 30 → 90 m by default, config allows up to 175 m. See "Range ceiling" below — past the
  loaded zones a larger number simply finds nothing.

## Verified game APIs (build 1.0.12)

| Need | API | Notes |
|---|---|---|
| Nearby creatures | `Character.GetCharactersInRange(Vector3 point, float radius, List<Character> outList)` | public static; also `Character.GetAllCharacters()` returning the live `s_characters` list |
| Is it game? | `c.GetBaseAI() is AnimalAI` **or** `Utils.GetPrefabName(c.gameObject)` in `GameAnimals` config (default `Boar,Neck,Lox,Asksvin`), and `!IsTamed() && !IsPlayer()` | **Not faction.** The first in-game test gated on `AnimalsVeg` and showed nothing, gave no XP — Boar/Neck/Lox are `MonsterAI` outside that faction. See MODDING.md |
| Display name / level / hp | `c.GetHoverName()`, `c.GetLevel()`, `c.GetHealth()` / `GetMaxHealth()` | `GetLevel()` = 1★/2★/3★ star level |
| Register the skill | Jötunn `SkillManager.Instance.AddSkill(SkillConfig)` or `AddSkill(identifier, name, description, increaseStep, icon)` → returns `Skills.SkillType` | shows up in the vanilla skills panel, saved per character |
| Read skill | `Player.m_localPlayer.GetSkillFactor(uid)` → 0..1, `GetSkillLevel(uid)` → 0..100 | |
| Raise skill | `player.GetSkills().RaiseSkill(uid, factor)` | `GetSkills()` is public (`Player.m_skills` itself is private). Going through `Skills` directly avoids re-entering `Player.RaiseSkill` — important, see XP hooks |
| Sneak XP cadence | `Player.OnSneaking(float dt)` (protected override) raises `Skills.SkillType.Sneak` once per second: factor `1f` when `BaseAI.InStealthRange(this)`, else `0.1f` | |
| Kill/hit detection | `Character.OnDeath()` + `protected HitData m_lastHit` → `m_lastHit.GetAttacker()`; damage path sets `m_lastHit` in `Character.Damage(HitData)` | |

Ground truth for anything else: `ilspycmd -t <Type> <Valheim install>/valheim_Data/Managed/assembly_valheim.dll` (see repo `MODDING.md` §2).

## XP model

Three sources, all client-side Harmony patches. Numbers are starting guesses — all of
them belong in config so they can be tuned in-game.

1. **Stalking (passive).** Postfix `Player.RaiseSkill`; when `skill == Skills.SkillType.Sneak`,
   raise Hunting by `value * StalkFactor` **only if** a wild animal is within detection
   range. This inherits vanilla's 1 s cadence for free, and inherits its
   `InStealthRange` distinction (value `1f` near a target, `0.1f` otherwise) so
   sneaking *at something* is already worth 10× sneaking in an empty field.
   - Must raise via `player.GetSkills().RaiseSkill(...)`, not `player.RaiseSkill(...)`,
     or the postfix re-enters itself.
2. **Hitting a wild animal.** Postfix `Character.Damage`; attacker is the local player and
   victim passes the wild-animal test → raise by `HitFactor` scaled by damage dealt
   relative to victim max HP (so a bow volley into a deer isn't worth the same as
   plinking a boar 40 times).
3. **Killing a wild animal.** Postfix `Character.OnDeath`; `m_lastHit.GetAttacker()` is the
   local player and victim is a wild animal → raise by `KillFactor`, scaled by the
   victim's `GetLevel()` (starred animals are worth more).

Rough intended weighting: kill >> hit >> stalk. Stalking alone should level the skill
slowly enough that it's a trickle, not an AFK-in-a-bush exploit.

**Anti-AFK:** stalking XP should require the target set to actually change, or at least
require player movement, otherwise crouching next to a penned boar farms the skill.
Simplest guard: only award stalk XP when the nearest detected animal's ZDOID differs
from the one credited last tick, or the player has moved > N metres since.

## Detection & the four stages

`skill = GetSkillFactor(uid)` (0..1). Range `Mathf.Lerp(30f, 90f, skill)`.

| Skill | Stage | Blip shows |
|---|---|---|
| 0–24 | 0 | `((•))` only, pointing up to **10° off** |
| 25–49 | 1 | error down to ~9°, + ballpark distance (`~15 m`) |
| 50–74 | 2 | true bearing, + exact distance (`12 m`) |
| 75–100 | 3 | + **which animal** — its own trophy icon |

**The bearing error must be fixed per animal, not per frame.** A wobble that re-rolls every
frame averages out to the true position and reads as jitter; a stable offset derived from the
animal's `ZDOID` hash reads as "you think it's over there". `HuntMath.BearingErrorDegrees`
does the hashing; the plugin rotates the player→animal vector by it before projecting.

**The pulse** (`HuntMath.PulseAlpha`) runs on one clock shared by every blip, so they breathe
together like a single sweep of attention instead of a field of independently blinking lights.
Visible time grows with the skill (0.8 s → 2.5 s) and the dark gap shrinks (6 s → 1.5 s), so a
novice gets one glimpse every seven seconds and an expert gets something close to continuous.
Both ends of both ranges are config. When the pulse is dark the whole update loop early-outs.

**Blips are pinned per pulse.** At the start of each pulse the mod listens once and records
where every animal was (bearing error already applied). For the rest of that pulse the blip
stays on that world spot even if the animal walks off; the next pulse picks up the new position.
A blip that slides along with the animal is a tracker. The distance label is measured from where
you are now, so creeping toward the spot shows as closing in. First in-game test had live-following
blips and it read wrong.

**Size carries distance before the number does** (mockup panel 1: the near blip is visibly
bigger). `HuntMath.BlipScale` lerps 34 px → 16 px across the detection range, and
`DistanceFade` takes the far quarter down to nothing.

**Colour: not used.** Considered encoding distance or animal size in colour, but size already
carries distance and Valheim's gold-on-green is the readable combination — a second channel
for the same fact is noise. If colour earns a job later it should be a *different* fact
(dangerous vs harmless game, say).

**No custom art.** The wave is a text glyph (`((•))` on screen, `•)))` rotated at the edge,
both configurable). The species icon comes from the game: `TrophyIcons.For()` walks the
creature's own `CharacterDrop.m_drops` and takes the icon of the first drop whose
`m_itemType == Trophy`, falling back to any other drop's icon for creatures without one.
No lookup table to maintain — new or modded creatures work for free.

On screen, the icon **replaces** the wave (you can see the animal, an arrow adds nothing);
at the screen edge the rotated wave stays and the icon sits above it, or the direction is lost.

## Full-screen indicator (the part Simple Compass can't do)

Standard off-screen indicator math, one static helper class (`HuntMath`) so
`tools/selfcheck.ps1` can reflect-invoke and assert it (repo convention — keep all pure
logic in static methods):

- `Camera.main.WorldToScreenPoint(target)`; if `z < 0` the target is **behind** the
  camera → negate x/y before clamping, otherwise blips mirror to the wrong side.
- Clamp the resulting point into the screen rect inset by a margin; when clamping had to
  move the point, the target is off-screen → draw the wave icon rotated to point outward
  along the direction from screen centre.
- When the point is inside the rect untouched, the animal is visible on screen → draw the
  blip at the projected position instead of at the edge.

Assert in selfcheck: a point directly behind the camera lands on the correct screen half;
a point dead ahead lands near centre; clamping never escapes the margin.

## Range ceiling (known limitation, document it in the README)

Verified in code, see MODDING.md "Creature loading radius". Short version: your client only
instantiates creatures around **your own** position, radius set by the Simulation Distance
graphics setting. Default = 5×5 zones: full coverage to 128-160 m, lucky diagonals to ~271 m.
Other players' loaded zones do **not** extend it: no 480 m via overlap. Config max is 175 m.

`Character.s_characters` only contains creatures **instantiated on this client** — i.e.
inside the active zone area. Animals further out exist only as ZDOs and are invisible to
this mod. At the 90 m default this rarely bites; at 200 m it will, but the mod must not promise long-range detection,
and the config max should stay well under the active-area radius.

## Status

Scaffolded and building; `tools/selfcheck.ps1` passes (binding probe + 27 math asserts,
including the behind-the-camera and dead-astern projection cases).

Mockups (`mockups.png`) are implemented: gold wave glyphs, number underneath, size by
distance, species icon at the top stage. Never seen in-game — everything below the
selfcheck line is unverified against a running client.

No `package/icon.png` yet (256×256, required by the packager before `package.ps1` will
produce a usable zip).

## Prior art: Hunter's Instinct (in `references/`)

Radamanto's mod solves the same problem with far more machinery: hold-to-charge activation with a
stamina cost and a fill bar, spectral halos on the creatures themselves, persistent footprint
trails, ServerSync'd config, per-posture charge multipliers, prefab exclusion lists.

What it gets right and we should steal: **footprints**. A trail on the ground is information you
have to read and follow, which is what hunting actually is — not a marker that tells you the answer.

What we deliberately don't copy: it renders a halo **on the creature**, which pinpoints it exactly.
That is the cheating feeling. Our blip is a bearing with an error bar, and the error only goes away
once the skill has earned it.

Not implemented here, deliberately: no footprints yet (see below), no charge bar, no stamina cost,
no server sync — the skill lives in the player profile, so there is nothing to sync.

## Open questions

- **Footprints are the obvious next feature.** Wild animals leave tracks; following them is the
  part of hunting a blip cannot express. Sketch: sample each detected animal's position on a timer,
  drop a decal/quad at ground height, fade each over its own lifetime, scale count and lifetime with
  the skill. Needs a ground-height query and a sprite — the first thing in this mod that might
  actually need art, unless a vanilla decal can be reused.

- Do the trophy icons actually read at 24-54 px over grass? If not, a dark outline or a
  small backing plate is the fix, not bigger icons.
- Should tamed animals be excluded entirely (current plan) or shown in a different colour?
- Does stage 0's 10° error feel like a skill or like a bug? (First test at 25° was "kinda far off".) It is the one number most likely
  to need tuning after the first play session (`MaxBearingError`).
- Does the skill do anything beyond detection — sneak-damage bonus, better drops? Out of
  scope for v1.
