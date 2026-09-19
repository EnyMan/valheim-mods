# Sensible Hunting

Adds a **Hunting** skill to Valheim. The better you hunt, the better you read the woods: nearby wild game shows up as an indicator on your screen — over the animal when it's in view, pinned to the edge pointing the right way when it isn't, including directly behind you.

**Crouch to sense.** The instinct only works while you're sneaking, and it comes in pulses: a blip flashes up where the animal was when you sensed it, fades, and the woods go quiet again for a few seconds. Low skill means one brief glimpse every seven seconds or so; at 100 the pulses are long and the gaps short.

This is not a radar. It reaches 90 metres by default, blips fade with distance, and it only ever shows wild animals — never monsters, never players, never your tames.

## What it looks like

Same spot, same evening, same two boars and a deer — only the Hunting skill changes.

![Hunting 10](https://raw.githubusercontent.com/EnyMan/valheim-mods/main/sensible_hunting/media/skill_10.jpg)
*Hunting 10 — pulses, no numbers, and the bearing is deliberately off. One blip is pinned to the left edge: that animal is behind you.*

![Hunting 40](https://raw.githubusercontent.com/EnyMan/valheim-mods/main/sensible_hunting/media/skill_40.jpg)
*Hunting 40 — the bearing tightens and you get a ballpark distance: `~20 m`, `~25 m`.*

![Hunting 80](https://raw.githubusercontent.com/EnyMan/valheim-mods/main/sensible_hunting/media/skill_80.jpg)
*Hunting 80 — you know what it is. Two boars at an exact 18 m and 27 m, and a deer off the left edge.*

**What the skill unlocks:**

| Hunting | What a blip tells you |
|---|---|
| 0–24 | roughly where — the blip can point as much as 10° off, double that at midday |
| 25–49 | closer to the truth, and about how far (`~15 m`) |
| 50–74 | the true bearing and the exact distance (`12 m`) |
| 75–100 | what it is — the animal's head, in gold |

Detection range grows from 30 m to 90 m as the skill rises, and near game shows as a bigger
blip, so you can read distance before you can read numbers.

**Your eyes beat your instinct.** If the animal is on screen and you have a clear line of sight to it — no terrain, building or tree trunk in the way, and not hidden by the Mistlands mist — the blip drops the bearing error and follows the animal itself. Behind a rock, or in the mist, you are back to the instinct, which is where it earns its keep. A Wisplight clears the mist for it, same as for your eyes.

**Night is the hunter's hour.** The bearing error is worst in broad daylight and sharpest after dusk.

**How you level it:**

- **Stalking** — sneaking while game is within your detection range. A slow trickle, and it only pays again once you've moved on or the nearest animal has changed, so camping a pen does nothing.
- **Hitting** an animal — scaled by how much of its health you took off.
- **Killing** one — the biggest share, multiplied by its star level.

Client-side only. Nobody else needs it, and neither does the server.

## Configuration
`BepInEx/config/com.mous.sensiblehunting.cfg` (or in-game with ConfigurationManager, changes apply live):

**Upgrading from an older version?** BepInEx never overwrites a setting that already exists in your
config file, so species added in a later release (Wolf, Seagal, Moose, Seal) do **not** appear in a
`GameAnimals` line written by an earlier version. Add them by hand, or delete the `GameAnimals` line
and restart to take the new default.

- `Enabled`, `GameAnimals` (prefab names of animals that fight back but still count as game — Boar, Neck, Lox, Asksvin, Wolf, Seagal (the seagull), Moose and Seal by default; flying creatures are only sensed while on the ground; passive animals always count), `RequireCrouch`, `MinRange`, `MaxRange`, `MaxBearingError`, `DaytimeErrorScale`, `SightRadius`, `BallparkStep`
- `VisibleAtSkill0`, `VisibleAtSkill100`, `GapAtSkill0`, `GapAtSkill100`
- `EdgeMargin`, `NearSize`, `FarSize`, `Opacity`, `Glyph`, `EdgeGlyph`
- `StalkFactor`, `HitFactor`, `KillFactor`, `StalkMoveDistance`

## License

MIT — source at https://github.com/EnyMan/valheim-mods
