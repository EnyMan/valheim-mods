# Friendly Clock

A day/night dial for Valheim that tells you *when* it is without ruining the mood with exact numbers.

- **Painted dial:** sunrise at the top, noon on the right, sunset at the bottom, midnight on the left. The hand sweeps through the day.
- **Time-of-day words** under the dial: Midnight, Before Dawn, Dawn, Morning, Midday, Afternoon, Evening, Dusk, Night…
- **Optional day counter.**
- Part of the vanilla HUD: hides when you hide the HUD, and menus and the inventory draw over it.
- **Drag it:** open your inventory, then drag the clock anywhere. The position is saved.

Client-side only. Nobody else needs it, and neither does the server.

## Configuration
`BepInEx/config/com.mous.friendlyclock.cfg` (or in-game with ConfigurationManager, changes apply live):

- `Style`: `Dial`, `Words` or `DialAndWords`
- `ShowDayCounter`
- `Size`, `FontSize`, `Opacity`, `Position`
- `DayPhases`: comma-separated names spread evenly over the day, starting at midnight. Keep 12 entries to match the 12 wedges of the painted dial.

## Custom art
The dial is made of three 512×512 PNG layers next to `FriendlyClock.dll`: `clock_face.png`, `clock_hand.png` (pointing up, rotating around the image centre) and `clock_cap.png`. Replace them with your own and they reload while the game is running. If a file is deleted, a simple built-in dial is drawn in its place.

## License

MIT — source at https://github.com/EnyMan/valheim-mods
