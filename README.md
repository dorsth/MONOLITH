# MONOLITH

Every Dorsth mod for STRAFTAT in one file, with one menu: CameraTweaks, CombatSounds, DorsthTweaks (with Ultimate Tweaks), ezCrosshair, NoGore, TrueSens and ViewmodelTweaks. It adds match stats, an FPS cap, forced outfits, extra keys and configs. STRAFTH is not in it.

Only your own screen and sound change. Nothing about hit registration, damage or what other players receive does, so it works in lobbies with players who do not have it.

Each mod in it is the same source as its own release, loads under its own name and keeps its own settings file.

## Menu

**F2** opens it. Left: what a setting is about. Right: the settings. Drawn only while open; closed, it costs one key check per frame.

| Key | Mouse | Does |
|---|---|---|
| Up, Down or W, S | | move |
| Left, Right or A, D | click or drag a bar | change a value (Shift: ten steps) |
| Enter or Space | click | flip a switch, open a page, a list or the colors, type a number |
| | move over a row | read what it does |
| R | Reset in the box under the page | back to default (shown when the setting is changed) |
| Q or Backspace | click `..` | back |
| Tab | | other panel |
| `/` | | search every setting |
| Esc | | close |

Every key of it is a letter, Enter, Esc, Tab or `/`: nothing a small keyboard lacks. While typing a name or number, Left deletes the last character.

* A value with `▼` opens a list on Enter. What the cursor is on applies at once, so you see or hear it. Enter keeps it, Esc puts back what was there. Type the first letters of a name to jump to it.
* A color opens a palette. Arrows or the mouse pick one; typing six hex digits sets one exactly.
* A number is a slider: the knob is the value, the small tick is the default. Drag it or click on the line; near the tick it snaps to the default.
* Crosshair, Hitmarkers and Players show what you picked in a box beside the rows.
* A row with no effect right now is not shown.
* Not every setting has a row. The search finds all of them.
* Saved at once, in each mod's own `.cfg`.

## Replaces Mod Menu

MONOLITH does what [Mod Menu](https://github.com/kestrel-straftat/mod-menu) does, in its own menu. `ModMenu.dll` in the MONOLITH folder has Mod Menu's plugin id and API, so mods made for Mod Menu see it as installed and work the same: settings they hide are hidden, the description they set is shown, and the rows they add with a content builder show on their page under Other mods, where Mod Menu would put them.

It loads no asset bundles, so it also runs on macOS ARM64, where Mod Menu does not.

Have Mod Menu installed as well? Only one of the two loads, so remove one: MONOLITH needs neither.

## Language

General, Language: English or Russian. The first start takes Russian when Windows is in Russian. Everything the menu draws is translated: names, lines under the page, lists, messages, the key bar. A setting no page has (found through the search) keeps its English name and description. Settings files and configs are the same in both languages.

The Russian texts are in `lang/ru.py`. `lang/make.py` (run by `build.sh`) checks each one against the cells it has in the menu and stops the build when one would be cut, then writes `Russian.cs`.

## What one switch stands for

| Switch | Stands for |
|---|---|
| No post-processing | no screen effects at all, in maps and in the menus (no menu blur; the game's brightness slider does nothing then), forward rendering, and a soft matte fill light (0.1) for the brightness the effects gave |
| Fewer UI animations | no menu and HUD animations, no logo screen at start |
| Ultimate tweaks | about 30 fixes to the game's own code (garbage and needless work every frame), 2 worker threads, the voice key off the main thread, code compiled at start. Listed in `mods/DorsthTweaks/UltimateTweaks/ULTIMATE-TWEAKS.md` |
| Performance viewmodel (off by default) | your weapon drawn by the main camera, second camera off; its muzzle effect, ejected case and launcher smoke not drawn |

## Pages

Crosshair, Viewmodel, Camera, HUD, Effects, Players, Audio, Controls, Performance, Configs, General. **Other mods**, last and dimmer, is not part of MONOLITH: it lists the settings of the other mods you have, the way Mod Menu does (see above).

## Controls

**Mouse**: sensitivity in cm/360 with your mouse DPI, and multipliers for ADS and scopes.

**Keys** the game's own settings do not have. Enter on a row, then press the key, a mouse button or scroll the wheel. R puts a row back to its default; Enter a second time while it asks sets none.

* **Second jump key**, **Third jump key**: next to your own. Scroll up and scroll down are two inputs, so one on each puts jump on the whole wheel.
* **Swap weapons**: the game's own Swap binding, the same one as in its settings. Set it in either menu; either reset puts it back on the scroll wheel. Scroll while it asks for a key to do that from here.
* **Taunts**: each on another key, or off.

Each one calls the game's own routine, one press for one press. The game's saved bindings are not touched. With a jump of yours on the wheel, the wheel no longer swaps weapons: set a key for Swap weapons then. Keys are not part of a config.

## Crosshairs

Every PNG in `plugins/MONOLITH/Crosshairs` is a choice under Crosshair, Shape, named after its file. The twelve that come with it are written there at the first start; add your own next to them, delete the ones you do not want. Read at start.

Size 1 is one picture pixel per screen pixel at 1080p; at most 256 pixels. The picture is scaled as it is, with sharp pixels.

## Players

One outfit and one hat for enemies, one for teammates, with the game's own wardrobe picture of each. Only on your screen, from the next round.

**Enemy outlines when I host** is the game's own lobby option for enemy outlines, which the game forgets at every start. MONOLITH remembers it and sets it for you. It counts in lobbies you host, for every player in them; in somebody else's lobby the host decides.

**Outline color** for enemies and for teammates is the game's own setting, not a second one: change it here or in the game's settings and both show it. Whether enemies have an outline at all stays the lobby's rule. Part of a config.

The hat you picked is drawn on the hat a player wears, which is only not drawn any more; it flies off with it when shot. A player with no hat gets a drawn one that stays on. The real hat is not replaced: a shot that passes a hat sends a bullet trail point to every player and counts for the Hats Off achievement, so a hat only you have would change both.

## Match stats

Kills, deaths and damage for every player, counted from what the game already sends everyone. Shown only between rounds and after the match, never while anyone can play.

* Victory screen: kills, deaths, damage per take, damage taken, opening kills and deaths, trades, takes with two or more kills, clutches and takes survived. Also written to the BepInEx log.

Damage is on the HUD's 0 to 100 scale and stops at the health a player had left.

## FPS cap

Replaces the game's FPS cap while it is on. The game's cap asks Windows to sleep and wakes up to a millisecond early or late; this one waits on a high resolution timer and then watches the clock, so frames are evenly spaced. The main menu keeps the game's own cap. With V-Sync on it does nothing.

## Configs

A config under a name: every setting except sensitivity, keys and the join message.

* **Copy my config to clipboard**: one line of text, `MONOLITH:name:code`. Paste it to a friend.
* **Import from clipboard**: copy such a line, then Enter. Your config is kept as "previous config" first. Experimental settings are not taken from an imported line.
* **Save my config as**: type a name.
* A saved config: Load, Copy to clipboard, Overwrite, Delete.
* Presets: **Dorsth**, **Defaults**.
* Every load keeps what you had as "previous config".
* The two outline colors are the game's own setting. A config sets them only when it holds them: Defaults leaves yours as they are. Nothing in the game's settings is written when MONOLITH is first installed.

Saved in `BepInEx/config/dorsth.straftat.monolith.profiles.txt`.

## Install

* Take the mods above (and Ultimate Tweaks, if you had it) out of `BepInEx/plugins`.
* Put the `MONOLITH` folder from the zip into `BepInEx/plugins`.
* Hit and kill sounds: `plugins/CombatSounds` is used if it is still there (your own sounds), else the sound folders in `plugins/MONOLITH`.
* Minimal Viewmodels Reborn and Mod Menu are not needed. Remove Mod Menu: MONOLITH has its own copy of its API.

A mod that is installed twice is loaded once, and which copy is not up to you: do not leave an old copy next to MONOLITH.

## Credits

The menu look is inspired by the STRAFTAT color theme for Far Manager by klammvs.

The slide cooldown timer and bar (DorsthTweaks) are based on MovementPro by Glizzmn.

The viewmodel's weapon camera (its offset, FOV and mirroring) is adapted from [Minimal Viewmodels Reborn](https://github.com/kestrel-straftat/minimal-viewmodels-reborn) by kestrel, under the MIT License. The license text is `mods/ViewmodelTweaks/LICENSE-MinimalViewmodelsReborn.txt`.

`modmenu/` follows the public API of [Mod Menu](https://github.com/kestrel-straftat/mod-menu) by kestrel, under the MIT License. The license text is `modmenu/LICENSE-ModMenu.txt`.

## Building

`build.sh` compiles everything under `mods/` into `MONOLITH.dll`. It needs the .NET SDK and two paths: `LIB` (the game's `STRAFTAT_Data/Managed`) and `BEP` (`BepInEx/core`). It builds `modmenu/ModMenu.dll` first. The mods' sources are used as they are; only their `[assembly: ...]` lines are left out.

| File | Holds |
|---|---|
| `Bundle.cs` | the plugin |
| `modmenu/` | the stand-in for Mod Menu's API |
| `Menu.cs` | how the menu is drawn and used |
| `Layout.cs` | what is on which page |
| `Profiles.cs` | configs |
| `Players.cs` | forced outfits and hats |
| `MenuPop.cs` | the list and color pop-ups |
| `Previews.cs` | the crosshair, hitmarker and outfit previews |
| `Keys.cs` | extra jump, swap and taunt keys |
| `Stats.cs` | match stats |
| `FrameCap.cs` | the FPS cap |
| `Lang.cs`, `Russian.cs`, `lang/` | the menu's language |

Code in a mod that is only for MONOLITH, or left out of it, sits behind `#if DORSTH_BUNDLE` in the mod's own source: where CombatSounds looks for sounds, the crosshair folder, the switches above, and the mods' own Mod Menu pages and windows (ezCrosshair's window is not compiled in at all).

At start the log says which mods and versions the file holds.
