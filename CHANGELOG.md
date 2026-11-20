# MONOLITH changelog

## 1.1.9
- The kestrel line at the top of the README uses the same words as the menu.
- The match stats board on the victory screen stands in the victory room, above the rematch sign, like the game's own signs. If the sign isn't there, it shows on the screen as before.
- The dll's description says what MONOLITH is, and each mod in it prints its name, version and "Dorsth was here." once in the BepInEx console at start.

## 1.1.8
- Replaces 1.1.7, an older build uploaded by mistake: Mod Menu's API is back, so mods that need Mod Menu load again.
- Other mods: key settings of other mods (like Spawn Viewer's Toggle Key) take the key you press. Before, pressing a key did nothing.
- Camera, No sprint FOV (CameraTweaks 1.1.9): switch it off and your FOV widens when you sprint and slide, like in the game. On by default, as before.
- Mod Menu's API is back in MONOLITH (ModMenu.dll), credited to kestrel: mods that need Mod Menu, like Spawn Viewer 2.1, load and show under Other mods again.
- The Viewmodel page, the Other mods page and the top of the README credit kestrel for Minimal Viewmodels Reborn and Mod Menu.
- The menu title is just MONOLITH. The version shows on the General page.
- Shorter README, with what MONOLITH has and how to use it.
- The dll's description is just "Dorsth was here." now. It said it held every Dorsth mod, and STRAFTH and Straftat Tournaments are not in it.

## 1.1.4
- MONOLITH no longer comes with a stand-in for Mod Menu. It works next to Mod Menu: install Mod Menu itself if a mod needs it. If you update by hand, delete ModMenu.dll from the MONOLITH folder.
- Without Mod Menu, Other mods leaves out sections named Internal or Hidden, the settings mods usually hide.

## 1.1.3
- With an extra jump on one scroll direction, swap weapons keeps the other one: jump on scroll down, swap on scroll up. Before, a jump anywhere on the wheel took the whole wheel from swap.
- Other mods that keep a key as text (like Spawn Viewer's Toggle Key) take a key press in the menu, the same as any other key setting.

## 1.1.2
- Match stats start over at every new match for every player. Before, only the host's reset, and a rematch kept counting from the last match.
- The match stats board flies up into the victory screen, and gets smaller when it wouldn't fit, so it never fills the screen.

## 1.1.1
- The update line in the main menu now says to close the game first: a mod manager can't replace a mod's files while the game is open.

## 1.1.0
- E opens and takes in the menu, next to Q for back and WASD to move. The key bar shows these keys. Arrows, Enter and Backspace still work.

## 1.0.9
- The update line no longer updates by itself: a click opens the Thunderstore page, and installed with a mod manager it says to update there. Thunderstore leaves updating to the mod manager.

## 1.0.8
- When a newer MONOLITH is on Thunderstore, a line in the game's main menu says so. A click updates it, a second click restarts the game. Installed with a mod manager (r2modman, Thunderstore Mod Manager, Gale): the line says to update there.
- The menu's title shows the version you have.
- The Dorsth and Defaults presets say what they really leave out: sensitivity and keys, keys and outline colors.

## 1.0.7
- Friendly fire when I host: the game's lobby option for friendly fire, kept between launches like enemy outlines.
- Join message: up to 120 letters with color, bold, italic, underline and strikethrough tags, which do not count toward the limit (DorsthTweaks 1.3.7).
- The match board column first blood is now first k-d.

## 1.0.6
- Performance viewmodel is no longer experimental: an imported config now sets it like any other setting (ViewmodelTweaks 2.0.4).
- The Dorsth preset has Performance viewmodel on.
- Credits: NoCameraRoll by Kevnator, the idea CameraTweaks builds on (CameraTweaks 1.1.8).

## 1.0.5
- README: the Fewer UI animations row no longer mentions the logo screen, and SwordStance is named as the old name of ViewmodelTweaks. 15 old Russian texts that nothing used are gone.
- Readme fixes in DorsthTweaks 1.3.6 and CombatSounds 1.1.6.

## 1.0.4
- New icon, 256 x 256.
- Readme: plainer wording about Mod Menu.
- Hide hats is now Hide dropped hats (NoGore 1.3.2). Your setting is kept.
- Match stats make no garbage per hit any more.
- Loading a config now sets Reduce VFX, graphics quality, the HUD and the other game settings right away, and the game's settings menu shows them. Before, they only took effect after a restart.
- No more warnings in the log about Mod Menu, GunSounds or weapon sounds that are not there (CombatSounds 1.1.5, DorsthTweaks 1.3.5, NoGore 1.3.3).
- The log only gets problems now: the notes from the FPS work (DorsthTweaks 1.3.4, ViewmodelTweaks 2.0.3) are written only with Detailed Log on in their settings files.
- Fewer UI animations no longer claims to skip the logo screen at start: the game shows it before any mod loads (DorsthTweaks 1.3.3).
- The round board no longer causes a 7 ms hitch when a round ends: it is made while the map loads, and only the names are measured letter by letter.
- Forced hats show again: the copy kept the wardrobe's own scripts, which switched it off.
- Forced hats come off like real ones: on a player who wears a hat when that hat is shot, on a player without one when that player is killed.
- The Thunderstore zips of 1.0.0 to 1.0.3 held Mod Menu itself instead of MONOLITH's own copy of its API: Mod Menu's window showed and other mods were missing from MONOLITH's menu. Fixed.

## 1.0.3
- Configs page reordered and renamed: Undo load (Redo load after it), Save as new, Export to clipboard, Import from clipboard, Include keybinds, then your saved configs under Saved, then Presets.

## 1.0.2
- Configs hold the game's own settings too (graphics, HUD, crosshair, audio, toggles) and every keybind. Sensitivity and what belongs to one PC stay out.
- Load keybinds: off by default; on, loading a config also sets its keybinds.
- Undo last load: back to what you had before the last load or import, keybinds included.

## 1.0.1
- Description and readme: MONOLITH replaces Mod Menu and Minimal Viewmodels Reborn, the settings of other mods are in its menu, and your settings come along when you switch.

## 1.0.0
- First public release.
- The match board sits centered in the free floor between the rematch signs and the health line, so it covers neither the 1st/2nd panel nor the signs.
- Stats boards: "-" and "/" keep their normal width ("4-0" read as "4 - 0").

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
