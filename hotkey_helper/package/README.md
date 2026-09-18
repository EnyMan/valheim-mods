# Hotkey Helper

Mods add lots of hotkeys and you forget them. Hotkey Helper finds every hotkey in your installed mods' configs and shows the relevant ones in a small column on the right side of the screen, next to Valheim's own key hints:

| Situation | Mod hotkeys shown |
|---|---|
| Holding a hammer/hoe/cultivator | build (plus build-menu keys while the piece menu is open) |
| Holding a weapon | combat |
| Inventory open | inventory |
| Chest open | inventory + container |
| Fishing rod | fishing |
| Radial menu | radial |

Hold **Left Alt + H** to see a list of every mod hotkey, grouped by mod.

Client-side only. No Jötunn needed. The column follows the vanilla *Key hints* setting.

## How it decides where a hotkey belongs
1. Your override (see below)
2. Built-in defaults for some popular mods
3. A guess from the hotkey's name/section/description (e.g. "snap", "build" → build; "chest", "dump" → container)

Anything it can't place only appears in the Alt+H list.

## Configuration
`BepInEx/config/com.mous.hotkeyhelper.cfg` (or in-game with ConfigurationManager):

- `Enabled`, `MaxRows`, `ShowAllKey`
- `ColumnPosition` (while building/fighting) and `InventoryColumnPosition` (while inventory, chest or crafting is open) — screen fractions, 0..1
- `ColumnFontSize`, `OverlayFontSize`
- For every discovered hotkey there is an entry under `Hotkeys - <Mod name>`:
  - `auto` (default) – use the guess (shown in the entry's description)
  - `hidden` – never show
  - `build,combat` – comma list of: build, buildmenu, combat, inventory, container, fishing, radial, general
  - add `;Label` to rename it, e.g. `build;Cycle snap point`

Entries appear after you load into a world once. Rebinding a mod's key updates the hints right away.

## Notes
Only hotkeys a mod stores in its BepInEx config (`KeyboardShortcut` or `KeyCode`) can be found; hardcoded keys cannot.

## License

MIT — source at https://github.com/EnyMan/valheim-mods
