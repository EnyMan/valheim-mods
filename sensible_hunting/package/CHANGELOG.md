# Changelog

## 1.2.0
- Stage 4 now names the animal with its own gold head silhouette instead of borrowing the trophy icon. Species without art still fall back to the trophy, or to anything else the animal drops.
- Seagulls are sensed at last. `Seagal` is not a creature - it is a re-skinned crow with no Character component, so it could never show up however the config listed it; it now comes from the ambient bird registry instead, while it is on the ground.
- Young animals use the adult's head: Boar_piggy, Wolf_cub, Lox_Calf, Moose_calf, Asksvin_hatchling and Chicken.

## 1.1.0
- Fixed blip placement: positions were in screen pixels while Valheim's HUD canvas works in 1920x1080 units, so at any other resolution or GUI scale blips drifted away from their target and edge indicators on the right vanished off screen.
- Animals you can actually see now get a truthful blip: on screen, clear line of sight from your head, and not hidden by the Mistlands mist (a Wisplight opens the view back up). No bearing error on those, and the blip tracks the animal instead of the spot it was heard at.
- New `SightRadius`: the line-of-sight probe is swept with a radius instead of a hair-thin ray, so a tree trunk actually hides an animal. Raise it for more cover, 0 for the old behaviour.
- New `DaytimeErrorScale`: the bearing error is worse in broad daylight (2x at midday by default) and sharpest at night, ramping across dusk and dawn. Below skill 50, where the error exists at all.
- Wolf, Seagal (the seagull), Moose and Seal added to the default `GameAnimals`.
- Flying creatures are only sensed while on the ground - no blips on circling gulls. Hitting and killing them still pays XP.
- Fixed the stage 4 species icon being shared between creatures that have no name of their own.

## 1.0.0
- Initial release: crouch to sense, pulsing blips that lengthen as the skill rises, Hunting skill in four stages (rough bearing -> ballpark distance -> exact distance -> species icon), indicators for game on screen and off, XP from stalking, hitting and killing animals.
