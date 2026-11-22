# MONOLITH

A competitive mod bundle for STRAFTAT. Press F2 for the menu.

Only your own screen and sound change, so it works in any lobby, with players who don't have it.

Weapon camera based on [Minimal Viewmodels Reborn](https://github.com/kestrel-straftat/minimal-viewmodels-reborn) by kestrel. Works with mods made for [Mod Menu](https://github.com/kestrel-straftat/mod-menu) by kestrel.

## Install

1. Put the `MONOLITH` folder from the zip into `BepInEx/plugins`, or install it with a mod manager.
2. Remove separate copies of my mods. Use either Minimal Viewmodels Reborn or MONOLITH, since both move the weapon camera. Mod Menu and MONOLITH can't be installed at the same time, since they share a plugin id.
3. Start the game and press **F2**.

Your old settings come along.

## What it has

| Page | What you get |
|---|---|
| Crosshair | style, color, size; add your own PNGs in `plugins/MONOLITH/Crosshairs` |
| Viewmodel | weapon FOV and position, per weapon if you want; sway, bob, left-handed, hide arms |
| Camera | no tilt, no landing or jump shake, sprint FOV amount, better recoil |
| HUD | hitmarkers, prompts, hide FPS or ping, slide cooldown timer |
| Effects | tracers, casings, bodies, blood, vignette |
| Players | one outfit and hat for enemies, one for teammates, outline colors |
| Audio | hit and kill sounds, gun volume |
| Controls | sensitivity in cm/360, extra jump keybinds, swap and taunt keybinds |
| Performance | FPS cap with even frame pacing, Ultimate tweaks |
| Configs | save, load and share every setting as one line of text |
| Other mods | settings of the other mods you have installed |

Match stats show kills, deaths and damage between rounds and after the match. They are also on their own as [MatchStats](https://thunderstore.io/c/straftat/p/Dorsth/MatchStats/).

## Menu

**F2** opens it. Arrows or WASD move, Enter changes, Q goes back, `/` searches every setting, Esc closes. The mouse works too. Settings save at once.

The menu is in English or Russian (General, Language).

## Updates

When a new version is out, a line shows in the main menu. Update with your mod manager, or from Thunderstore, with the game closed.

## Credits

- Menu inspired by the STRAFTAT theme for Far Manager by [klammvs](https://github.com/vsklamm).
- Slide cooldown timer based on MovementPro by Glizzmn.
- Camera tilt and shake switches build on [NoCameraRoll](https://thunderstore.io/c/straftat/p/Kevnator/NoCameraRoll/) by Kevnator.
- Weapon camera based on [Minimal Viewmodels Reborn](https://github.com/kestrel-straftat/minimal-viewmodels-reborn) by kestrel, under the MIT License. The license text is `mods/ViewmodelTweaks/LICENSE-MinimalViewmodelsReborn.txt`.
- Mod Menu API by kestrel, so mods made for [Mod Menu](https://github.com/kestrel-straftat/mod-menu) work in MONOLITH's menu. `modmenu/` follows that API, under the MIT License. The license text is `modmenu/LICENSE-ModMenu.txt`. The two can't be installed together.
