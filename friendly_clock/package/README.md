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
- `MaxWidth`, `FontSize`, `Background` of the phase tooltip (see below)

## Phase file with hover tooltips
Create `BepInEx/config/FriendlyClock.phases.md` to replace `DayPhases`. Each `## ` heading is one phase, in order from midnight; the markdown under it shows as a tooltip when you hover the clock with the inventory or map open. Supports headings, **bold**, *italic*, lists, `>` quotes, `---` rules and raw TextMeshPro tags. The file reloads while the game runs. A full example (Old Norse phases with pronunciations) ships next to the DLL as `FriendlyClock.phases.md`; copy it to `BepInEx/config/` to use it. Excerpt:

```markdown
## Ótta
> ### The deep night before dawn
> - **Pronunciation:** **OHT-tah**
> - **Sounds like:** _"Oht"_ (rhymes with _boat_) + _"tah"_
```

## Custom art
The dial is made of three 512×512 PNG layers next to `FriendlyClock.dll`: `clock_face.png`, `clock_hand.png` (pointing up, rotating around the image centre) and `clock_cap.png`. Replace them with your own and they reload while the game is running. If a file is deleted, a simple built-in dial is drawn in its place.

## License

MIT — source at https://github.com/EnyMan/valheim-mods
