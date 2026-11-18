# MONOLITH changelog

## 1.0.1
- The match board sits below the 1st/2nd panel again, so it covers neither the panel nor the rematch signs.
- Stats boards: "-" and "/" keep their normal width ("4-0" read as "4 - 0").

## 1.0.0
- First public release.

## 0.10.11
- The match board is in the middle of the screen.

## 0.10.10
- Stats boards: columns are measured on the letters as drawn, so the gap after the names is the same as between the other columns.

## 0.10.9
- Stats boards: every digit is as wide as a 0, so numbers line up exactly on their right edge.

## 0.10.8
- Fixed a player in the lobby and Tab screen having no name row when an old row from your last lobby still held their player number (DorsthTweaks 1.3.2).

## 0.10.7
- Match stats: a kill right after you join mid match goes to the player who made it (it went to nobody).

## 0.10.6
- Fixed players showing up in the lobby without a name, picture or ready box after you left or got kicked from a lobby (DorsthTweaks 1.3.1).

## 0.10.5
- Stats boards: names left aligned, numbers right aligned so their digits line up. The name column is as wide as the longest name.
- The match board is centered under the podium and writes out kills, deaths and first blood.
- Letters the game's font can't draw are left out of names instead of showing as boxes.
- Dorsth profile updated: outline colors, gun sound volume, melee kill volume 150, frame cap off.
- Removed log lines that only helped during development.

## 0.10.4
- Stats boards: every column is left aligned under its header, and the padding is the same on both sides.
- The match board sits bottom right, clear of chat.
- Your own kills, deaths and damage show on the Tab screen too.
- The per-hit log line is gone: the counting matches the kill feed.

## 0.10.3
- Match stats: the kill that ends a take or round is counted before the round board is shown (its damage and kill were missing from the board).

## 0.10.2
- Stats boards: fixed column widths worked out from the text, the same gap between every column (no measuring, which squeezed the columns).
- Each counted hit is written to the BepInEx log ("Stats hit: ..."), to check the counting.

## 0.10.1
- Fixed the stats boards squeezing all columns together: columns are measured on the board's own text, and never narrower than their longest entry.

## 0.10.0
- Match board: no damage taken column; multi is called 2k again.

## 0.9.99
- Match stats: a hit is no longer missed when it is the first health change of a player (a one-shot kill could count nothing). Who dealt a hit is decided a frame later, so another player's damage no longer goes to whoever hit that player before.
- Stats boards: every column as wide as its widest entry plus the same gap; names take only the room they need.
- The match board is smaller and sits bottom left, clear of the podium and its sign.
- The game's FPS and ping texts are off while a board is up.

## 0.9.98
- Tab screen: kills, deaths and damage of the match so far over each name.
- Round-end screen: only the round board, no labels over the names (one set of numbers per screen).

## 0.9.97
- Board text sits on one baseline per row, so headers like "player" and "first kills" line up with "k" and "d".

## 0.9.96
- Board headers all lowercase, like the game's own round-end and Tab screens.

## 0.9.95
- Round board headers in whole words: kills, deaths, damage. Match board: k, d and adr stay (what every FPS scoreboard uses); open, 2k and alive are now first kills, multi and survived.

## 0.9.94
- The label over each name says kills and deaths ("kills 7   deaths 3"), in the color of the Tab screen's map line, a little bigger, and shows for every player (also one with nothing counted yet).

## 0.9.93
- The kill labels only move when their name moves (no canvas rebuild every frame), and the list of Tab rows they keep is emptied on every map.

## 0.9.92
- "kills: 7" over each name on the round-end screen and the Tab screen: a small label of its own, the game's names and layout stay as they are.

## 0.9.91
- Stats boards: names show without the game's text tags (<gradient=...>, <#FFF> and the like), the same rule as strafth.com. A long name gets smaller to fit before it is cut with "…".
- The match board sits on the left, clear of the podium on the victory screen.

## 0.9.90
- No more kill counts added to names on the round-end screen and the Tab screen: they broke the game's own layout. The round board shows the kills.

## 0.9.89
- The stats panel fits its text, with the same padding on every side. The round board sits the same distance from the left and top of the screen.

## 0.9.88
- Tab screen: the kills are added to the name shown, not to the player's name itself. Rows keep their avatars, buttons and places again.

## 0.9.87
- Match stats is one switch on the HUD page (the round board and match board no longer have their own).

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
