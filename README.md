# MONOLITH

A competitive mod bundle for STRAFTAT. Press F2 for the menu.

Only your own screen and sound change, so it works in any lobby, with players who don't have it.

Mods made for [Mod Menu](https://github.com/kestrel-straftat/mod-menu) by kestrel work in MONOLITH's menu. The weapon camera works the way [Minimal Viewmodels Reborn](https://github.com/kestrel-straftat/minimal-viewmodels-reborn) by kestrel does it.

## Install

1. Put the `MONOLITH` folder from the zip into `BepInEx/plugins`, or install it with a mod manager.
2. Remove separate copies of my mods. You don't need Minimal Viewmodels Reborn or Mod Menu next to it, and Mod Menu can't be installed together with it.
3. Start the game and press **F2**.

Your old settings come along.

## What it has

| Page | What you get |
|---|---|
| Crosshair | style, color, size; add your own PNGs in `plugins/MONOLITH/Crosshairs` |
| Viewmodel | weapon FOV and position, per weapon if you want; sway, bob, left-handed, hide arms |
| Camera | no tilt, no landing or jump shake, better recoil |
| HUD | hitmarkers, prompts, hide FPS or ping, slide cooldown timer |
| Effects | tracers, casings, bodies, blood, vignette |
| Players | one outfit and hat for enemies, one for teammates, outline colors |
| Audio | hit and kill sounds, gun volume |
| Controls | sensitivity in cm/360, extra jump keys, swap and taunt keys |
| Performance | FPS cap with even frame pacing, Ultimate tweaks |
| Configs | save, load and share every setting as one line of text |
| Other mods | settings of the other mods you have installed |

Match stats show kills, deaths and damage between rounds and after the match.

## Menu

**F2** opens it. Arrows or WASD move, Enter changes, Q goes back, `/` searches every setting, Esc closes. The mouse works too. Settings save at once.

The menu is in English or Russian (General, Language).

## Updates

When a new version is out, a line shows in the main menu. Update with your mod manager, or from Thunderstore, with the game closed.

## Credits

- Menu colors inspired by the STRAFTAT theme for Far Manager by klammvs.
- Slide cooldown timer based on MovementPro by Glizzmn.
- Camera tilt and shake switches build on [NoCameraRoll](https://thunderstore.io/c/straftat/p/Kevnator/NoCameraRoll/) by Kevnator.
- The weapon camera works the way [Minimal Viewmodels Reborn](https://github.com/kestrel-straftat/minimal-viewmodels-reborn) by kestrel does it, under the MIT License. The license text is `mods/ViewmodelTweaks/LICENSE-MinimalViewmodelsReborn.txt`.
- Mods made for [Mod Menu](https://github.com/kestrel-straftat/mod-menu) by kestrel work in MONOLITH's menu through its API. `modmenu/` follows that API, under the MIT License. The license text is `modmenu/LICENSE-ModMenu.txt`. The two can't be installed together.
