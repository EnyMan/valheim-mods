# Simple Compass

A minimal compass for Valheim: a thin line across the top of the screen, in the style of modern adventure games.

- **N / E / S / W** only, plus small ticks between them. No clutter.
- **Your map pins** sit above the line at their real bearing, using the map's own icons. That includes your pins, pings, shouts, other players and bosses. Pins shrink and fade with distance.
- **Distance to what you're facing:** the pin nearest the centre shows its name and distance right on the line, in gold.
- Crossed-out pins are hidden. The compass hides while the big map is open, and when you hide the HUD.
- **Drag it:** open your inventory, then drag the compass anywhere. The position is saved.

Client-side only. Nobody else needs it, and neither does the server.

## Configuration
`BepInEx/config/com.mous.simplecompass.cfg` (or in-game with ConfigurationManager, changes apply live):

- `FieldOfView` (degrees visible across the bar), `IntercardinalTicks`
- `Position`, `Width`, `Opacity`, `FontSize`
- `Range` (metres, 0 = no pins), `IconSize`, `HideCheckedPins`, `ShowPinNames`

## License

MIT — source at https://github.com/EnyMan/valheim-mods
