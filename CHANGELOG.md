# MONOLITH changelog

## 0.9.86
- The Dorsth config no longer forces suits or hats.

## 0.9.85
- The stats boards have one dark see-through panel behind them, like the game's own boxes, and a lighter text shadow.

## 0.9.84
- The round board is smaller and sits higher, clear of the round-end screen.
- The Tab screen shows each player's kills this match next to their name, like the round-end screen.
- The Dorsth config is his current one.
- New melee kill sound: sword cut.

## 0.9.83
- Hide default playlists moved from Players to General.

## 0.9.82
- Reset works while a setting is being edited (a key being set, a list or the colors open): it stops the edit and resets.

## 0.9.81
- Reset sits right after the default in the box under the page. Key rows show their default too.
- Descriptions rewritten as plain sentences ("Scroll: back to the wheel" and similar shorthand is gone). Configs are called configs everywhere.

## 0.9.80
- The Menu page is General. Fewer UI animations moved there from HUD.
- No near miss vignette moved from Camera to Effects, Screen.
- Descriptions no longer repeat the default (the box shows it already). "From the next round" is said once, on the Players page.

## 0.9.79
- A changed setting shows Reset in the box under the page, next to its default. Click it, or press R.
- Reset this page is gone.

## 0.9.78
- R resets the setting under the mouse, else the selected one.

## 0.9.77
- The ↺ mark on changed rows is gone.

## 0.9.76
- One way to reset: R. The key bar shows "R reset" while the selected setting is changed. Right-click no longer resets.
- Reset this page: shown at the end of a page while something on it is changed. Enter twice.

## 0.9.75
- Swap weapons is the game's own Swap binding: both menus show the same key, and resetting it in either one puts it back on the scroll wheel. A key set in an older version moves over by itself.
- A changed setting has a ↺ at the end of its row. Click it to put that setting back to its default.

## 0.9.74
- Replaces Mod Menu. `ModMenu.dll` in the MONOLITH folder gives other mods Mod Menu's API, so a mod made for Mod Menu works the same way: hidden settings stay hidden, its description shows, and the rows it adds itself (headers, text, buttons, values) are on its page under Other mods, in the same order.
- Works on macOS ARM64: no asset bundles.

## 0.9.73
- Swap weapons shows the game's own binding (the scroll wheel) and a key set there replaces it. Scroll while it asks for a key: back to the wheel.
- Extra jump and Extra jump 2 are named Second jump key and Third jump key.

## 0.9.72
- Pages reordered and renamed: Sound is Audio, Mouse and Keys are one page (Controls), Profiles is Configs, Frame cap is FPS cap. Other mods is last and dimmer: it is not part of MONOLITH.
- Hide default playlists moved to Players, Lobby.
- Match stats: the round board shows that round only, plain text in the game's font, and stays 3 seconds longer. Each name on the round-end screen has that player's kills this match next to it.
- Ultimate Tweaks is part of DorsthTweaks. The Performance row is the same; `dorsth.straftat.ultimatetweaks.cfg` is no longer read.
- Removed: the Steam callback rate and the two movement check rates (no measured gain), and Test frame times (Ultimate Diag prints frame pacing instead).
- Without Mod Menu installed, searches through the loaded code no longer log a warning.

## 0.9.59 to 0.9.63
- Match stats, built from Kill Tracker: kills, deaths, damage, opening kills, trades, clutches. Shown between rounds and after the match.

## 0.9.52 to 0.9.58
- FPS cap with evenly spaced frames.
- The Mods page lists other mods the way Mod Menu does.
- One click acts on a row; the row under the mouse is shaded and described.
