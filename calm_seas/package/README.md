# Calm Seas

Scales ocean wave height: calm near land, rougher out at sea (or the other way round). Applies to both the water you see and the water your boat rides on.

## ⚠️ Work in progress

This mod is still being tuned. Expect some visual glitches around boats in multiplayer:

- Wave height follows each player's own position, and the boat rides the waves of the player whose game is simulating it. Another player can see that boat sit slightly above or sink slightly into the water.
- It is most noticeable when **watching someone else's boat from a distance** (for example from the shore while they sail at sea), and minor for players on the same boat near the coast.
- It is only visual — boats handle normally for the person steering. Out on open sea, where everyone uses the sea value, it goes away.

Feedback and screenshots are welcome on [GitHub](https://github.com/EnyMan/valheim-mods/issues).

## Configuration (server-synced)

`BepInEx/config/com.mous.calmseas.cfg`, section `[Waves]`. On multiplayer servers the server's value is pushed to all clients (admins only):

| Setting | Default | Description |
| --- | --- | --- |
| `LandWaveScale` | 0.3 | Wave multiplier while you are on land or in shallow water (0–5, 1 = vanilla) |
| `SeaWaveScale` | 0.7 | Wave multiplier over open sea (0–5, 1 = vanilla) |
| `OpenSeaDepth` | 20 | Water depth in metres under you where the sea value fully applies; shallower water blends the two |

## Details

- The scale follows **your** position, so all the water around you uses the same value.
- Only waves change. Wind strength, sailing speed and weather are vanilla.
- Must be installed on the server **and** every client, so everyone's boat sits on the same wave.

## Installation

Install via the Thunderstore Mod Manager or r2modman (dependencies are pulled automatically), or manually place `CalmSeas.dll` into `BepInEx/plugins/`.

## License

MIT — source at https://github.com/EnyMan/valheim-mods
