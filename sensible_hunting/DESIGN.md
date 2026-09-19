# Sensible Hunting — design

Client-side mod adding a **Hunting** skill. While crouched, the player senses nearby wild game
as pulsing on-screen blips, including animals behind them. Higher skill means longer range,
a truer bearing, and more detail. It should feel like reading the woods, not a radar.

## Sensing

- **Crouch to sense.** Blips only appear while crouched (`RequireCrouch`, on by default).
- **Pulses.** The display flashes up, fades, and goes dark before the next pulse. All blips
  share one clock. Visible time grows with skill (0.8 s → 2.5 s), the dark gap shrinks
  (6 s → 1.5 s).
- **Pinned per pulse.** At the start of each pulse the mod records where every animal is. The
  blip stays on that spot for the whole pulse even if the animal moves; the next pulse picks up
  the new position. The distance label is measured from the player's current position.
- **Night is the hunter's hour.** The bearing error is multiplied by `DaytimeErrorScale` (2x by
  default) at midday, ramping back to 1x through dusk on EnvMan's own daylight curve. Below skill 50
  only - from stage 2 up the bearing is true and there is no error left to scale.
- **Nothing in the air.** A flying species (`Character.m_flying`) is only sensed while it is on the
  ground; ground animals are never affected, so a jumping boar does not blink out. Hits and kills on
  fliers still pay XP.
- **Seagulls are not creatures.** `Seagal` is a re-skinned crow - a `RandomFlyingBird` with no
  `Character` component - so it is picked up from `RandomFlyingBird.Instances` instead, while landed
  (`ZDOVars.s_landed`). It has no drop table to fall back on, so it needs its own art to get an icon,
  and killing one pays no hunting XP: the XP patches hook `Character`, which a bird never is.
- **Eyes beat instinct.** If an animal is on screen, unobstructed (terrain/buildings/trees raycast
  **from the player's head**, not the camera - the 3rd person camera sees round trunks you don't) and
  not hidden by the Mistlands mist (`ParticleMist.IsMistBlocked`, so a Wisplight counts), its blip
  drops the bearing error and tracks the animal live for the whole pulse. In the mist, or behind a
  rock, the pulse-pinned guess is all you get - which is exactly where the instinct should matter.
- **Range** grows with skill, 30 m → 90 m by default, config max 175 m. Only creatures loaded
  around the player can be sensed (every direction to ~128 m at the default Simulation Distance).

## The four stages

| Skill | Blip shows |
|---|---|
| 0–24 | `((•))` only, bearing up to 10° off |
| 25–49 | bearing up to ~3.5° off, ballpark distance (`~15 m`) |
| 50–74 | true bearing, exact distance (`12 m`) |
| 75–100 | the animal's head, in gold |

- The bearing error is **fixed per animal** (hashed from its ZDOID), so a blip points
  consistently wrong rather than jittering.
- **Size carries distance**: near blips are bigger (34 px → 16 px across the range) and the far
  quarter of the range fades out. No colour coding.
- **No custom art.** The blip is a text glyph (`((•))` over the animal, `•)))` rotated at the
  screen edge). The species icon is the mod's own white head silhouette from `art/icons`, shipped next
  to the DLL and tinted gold on screen; young animals borrow the adult's head. A species with no art
  falls back to the creature's own drop table - its trophy, or any other drop's icon.
- On screen the icon replaces the wave; at the screen edge the rotated wave stays and the icon
  sits above it, so the direction isn't lost.

## Indicator placement

Targets inside the view get the blip over the animal. Targets outside it, including behind the
camera, are clamped to the screen edge (inset by `EdgeMargin`) and the wave rotates to point at
them. Behind the camera the projection is mirrored, so it is flipped back before clamping; a
target dead astern is placed at the bottom edge.

## What counts as game

Wild (not tamed) creatures that either use the passive `AnimalAI` (deer, hares, chickens, young
animals) or whose prefab name is in `GameAnimals` (default `Boar,Neck,Lox,Asksvin,Wolf,Seagal,Moose,Seal`). Monsters,
players and tames never show.

## Experience

Weighting: kill > hit > stalk.

1. **Stalking.** Each vanilla sneak XP tick (once a second while crouched) also awards
   `StalkFactor` × that tick as Hunting XP, if game is in detection range. Sneaking near a target
   is worth 10× sneaking in an empty field, as in vanilla.
   **Anti-AFK:** it only pays again once the player has moved `StalkMoveDistance` or the nearest
   animal has changed.
2. **Hitting** a wild animal: `HitFactor` × the fraction of its max health removed.
3. **Killing** one: `KillFactor` × its star level.

## Client-side only

The skill is saved in the player profile, so there are no RPCs, no server sync, and nobody else
needs the mod. Jötunn is used only to register the skill.
