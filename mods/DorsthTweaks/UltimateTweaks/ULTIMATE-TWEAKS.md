# Ultimate Tweaks

Fixes to the game's own code, all behind one setting in DorsthTweaks: **Ultimate Tweaks**. None of them changes what you see or hear, hit registration, damage, or what is sent to other players.

Measured on one PC (uncapped, 2026-11-18): with all of them off the frame rate was 28.7% lower on an empty map. In real matches the worker thread limit alone was worth 16% (2 threads against the game's own 15).

Forward rendering and the soft fill light are not in this list: they come with **No Post-Processing**.

## The tweaks

**PreCompile** (started as an experiment)  
The game's code is compiled the first time each piece runs; the first shot with a weapon type waits 13 to 30 ms (measured in a match), and compiling is the likely reason. With this on, a helper thread compiles the game's code shortly after the game starts. Code is only compiled, never run; classes with start-up code of their own are left out. Takes effect at the next start of the game. Measured: the first grenade throw took 0.01 ms with it against 5.2 and 5.4 ms without (FINDINGS).

**FadedLinesEndEarly**  
The thin fragment lines of a frag grenade and a shrapnel shell fade out in about 0.7 seconds, but the game keeps every one of them for 2.5 seconds: an invisible object that is still updated and handed to the graphics card every frame (51 fading lines alive on average in a 4-player match, 16.5 microseconds a frame, 85 in the slowest frames; measured). With it on a line is removed when it has faded to nothing (at most 0.15% of its brightness left, under what a screen can show) instead of after 2.5 seconds. Nothing visible changes; the explosion itself, its damage and what other players get are not touched. Bullet trails are not touched.

**NoMouseEvents**  
Remove Unity's legacy OnMouseDown/OnMouseOver step from the frame loop (a raycast from every camera each frame). The game has no such handlers; if a live one is ever found the step is put back.

**CrosshairFix**  
Two game scripts switch the crosshair image off and on again every frame, which rebuilds the HUD canvas every frame. Apply only the final value, once.

**LobbyListThrottle**  
The lobby player list is rebuilt every frame, also during a match. Rebuild it a few times a second while not in the main menu.

**SteamRunningCheckThrottle**  
A Steam helper asks Windows whether Steam is running every frame. Ask twice a second.

**PreviewDollIdle**  
The lobby preview character keeps animating and skinning during a match although no camera draws it. Pause it until its camera is on again.

**OwnNameTagCanvasOff**  
Your own player carries a name-tag canvas that is empty for you but follows you around, so Unity re-lays it out every frame. Switch that one canvas off. Other players' name tags are untouched.

**KillCamLean**  
The kill camera asks the engine for every player body every frame, also while you are alive, to notice that you joined mid-match. That costs time and makes garbage (14 us and 630 bytes a frame measured in a match, together with the rest of the kill camera). While your own player exists the game throws the answer away, so the question is skipped then. Nothing else changes.

**WeaponNoInputCopy**  
Every weapon that spawns builds its own copy of the game's whole key binding set (read from text, about 40 actions) and never uses it or gives it back: time and garbage on every weapon spawn, and the copies pile up for the whole session (7 to 21 more per round in a recorded match, none freed). With it on a weapon does not build that copy. Nothing reads it, so nothing else changes.

**DeadInputCallbacks**  
Every player body and the kill camera add mouse callbacks to the game's input set and never take them off. After a round their objects are destroyed but the callbacks stay for the whole session: each is still called on every mouse movement and keeps its dead object in memory (about 10 more per round). With this on, callbacks whose object is destroyed are taken off when a scene loads. Callbacks of living objects are not touched.

**TrickShotLineNoGarbage**  
The aiming arc of the launcher weapons (the line that shows where the shot will bounce) builds a new list and a new array of its points every frame, for every such weapon in the map: about 2 KB of garbage a frame together with the prediction itself (measured in a match). With this on, the points go into one array that is reused. The prediction is the game's own, untouched: the same call, the same points, the same line.

**GunSoundsNoScanInPlay**  
Only matters with the GunSounds mod (1ntrospective) installed. GunSounds looks at every sound the game plays. For a sound that no weapon plays (footsteps, jumps, vaults, ladders: yours and everyone else's) it searches through every loaded weapon again to see whether it is a gun sound after all, and it remembers the answer for 5 seconds only. So about every 5 seconds while anyone moves the game spends 2 to 3 ms on that search (measured: most of the frames of 4 ms or more while you could move). With this on, the search is not made while your own player can move. It is still made at every map load (GunSounds' own), during every take's countdown (added here), and whenever you cannot move. Your custom gun sounds play as before.

**DecalSmallBuffers**  
Every scorch mark an explosion leaves (rockets, grenades, mines) is drawn by the game's decal library. For each mark the library reserves room for 4048 corner points in six lists, and it does that twice per mark: about 580 KB of memory per explosion that is thrown away again (counted in the library's code: 2 x 4048 x 72 bytes; measured 576 and 580 KB on two explosions). With it on it reserves room for 256 points at first, about 37 KB per explosion, and the lists still grow by themselves if a mark needs more. The mark looks the same. Nothing about the explosion itself is touched.

**ItemNameNoGarbage**  
Every weapon and item in the map asks the engine for its own name every frame, only to compare it with one fixed name. Each answer is a new piece of text that is thrown away: 580 bytes of garbage a frame in a recorded match (19 items), all of that script's garbage. With it on the name is asked once per item and remembered. The game never renames an item, so the comparison gives the same result.

**SceneNameNoGarbage**  
Six of the game's scripts ask the engine for the name of the current map every frame, 14 times a frame in all (pause menu, music player, settings, crosshair, scene loader, scoreboard), and each answer is a new piece of text that is thrown away. With it on the name is remembered until the map changes.

**HudNumbersNoGarbage**  
The health, ping and frame-rate texts of the minimal HUD, the XP text and the ammo counter are put together from numbers again every frame, also when the number is the same and also when the text is hidden: new pieces of text every frame (about 430 bytes a frame in a recorded match). With it on a text that was already built for the same number is used again. What is shown is identical.

**LobbyRowsNotInPlay**  
When somebody joins the lobby during a take, the game builds that player's two rows (lobby list and Tab scoreboard, each with avatar and buttons) at once: about 3 ms in the middle of your fight (measured once, inside a 14.8 ms frame). With it on the rows are built as soon as you cannot move (dead, between takes), or the moment you hold Tab or open the pause menu, so you never see a list without them. Nothing else about the lobby changes.

**MyceliumPollRate**  
Only matters with Mycelium Networking installed (the library ChatCommands sends its commands through). It asks Steam for new mod messages every frame: 5 us a frame in a recorded match, the most of any plugin's own per-frame code. With it on it asks 60 times a second. Messages wait in Steam until asked for, so none is lost; a chat command can arrive up to 17 ms later. The game's own networking is not involved.

**OtherArcsAtScreenRate**  
Every launcher that somebody holds gets its aiming arc worked out on your PC every frame (sphere casts along the path). For your own launcher that result goes out with your shot, so it is left exactly as it is. For a launcher that is in another player's hands, nothing on your PC uses the result except the line that is drawn. Measured in a match: up to 67 us a frame for other players' launchers. With it on those arcs are worked out once per screen refresh (never less than 120 times a second) instead of once per frame. The line you see is the same; it changes as often as your screen can show. A launcher that lies on the ground, or that is in your own hands, is worked out every frame as the game does (since 0.8.26): you could pick one up and fire it within one frame, and then the game sends the arc of the frame before.

**FovTweensNoPileUp**  
While your field of view is changing (sprint, slide, aim: nearly always in play) the game starts a new field-of-view animation every frame and lets all the earlier ones run on: about 250 at once, all updated every frame (25 to 31 us a frame measured). Each frame only the newest two decide what you see: the newest sets the picture and takes its starting point from the one before; the older ones are overwritten before anything reads them. With this on, an older one is stopped as soon as a newer one will outlast it. Reasoned from how the animation library runs, NOT yet proven by measurement: if sprint, slide or aim zoom ever feels different, switch this off and tell me.

**SpawnViewerNoSearch**  
Only matters with the SpawnViewer plugin installed. Every time players are spawned it searches through every object the game has loaded, twice, only to find itself and set one flag: 3.3 ms each time, four times in a nine-minute match while you could move (measured). With it on the flag is set on the plugin directly, without the search. What SpawnViewer shows and when is unchanged.

**WeaponLayerNoGarbage**  
A weapon that another player holds is put on the 'held weapon' layer again every frame, part by part, and to do that the game asks the engine for a new list of all its parts every frame: about 120 bytes of garbage per held weapon per frame (217 bytes a frame in a match, measured: all of the weapon scripts' garbage). With it on the same parts are put on the same layer every frame through one list that is reused, and a part that is already on that layer is not written again. The layers end up exactly the same.

**OnGuiNoLayout**  
A script with an old-style GUI method makes the engine do a layout pass for it every frame and create two layout objects each time, also when the method draws nothing. Three such scripts run in a match: the player script (its method is empty), the network library's ping script (the game's version only works out the ping number, it draws nothing) and SpawnViewer if installed (draws nothing while its panel is hidden). With it on the layout pass is switched off for the first two and for the game's FPS counter (one label at a fixed position), and for SpawnViewer only while its panel is hidden. Nothing that is drawn changes and the ping number is worked out as before. Suspected source of the 730 bytes of garbage a frame at the end of rendering (the engine code creates two layout objects per script per frame); to be proven by the next measurement.

**FovTweensReuse**  
The field-of-view animation that the game starts every frame is a new object every time (about 430 bytes per frame you are alive, measured). With it on those animation objects go back to the animation library's own pool when they end or are stopped and are used again for the next one, which is a standard feature of that library that the game has switched off. Only the field-of-view animations are marked for it. Needs FovTweensNoPileUp. How much it saves is not measured yet.

**ArcPathNoGarbage**  
Working out a launcher's aiming arc builds the path point by point in a new list every time, lets that list grow step by step, copies it into the result and throws it away. For your own launcher that is about 19 KB of garbage per frame you hold it (measured in a match: two thirds of all garbage of that match came from arcs). With it on the point list is one list that is reused. The arc that comes out is the same, point for point: the copy that the game keeps and sends is made exactly as before.

**ExplosionLinesOneMaterial**  
A frag grenade, a shrapnel shell and a claymore draw one thin line per fragment when they go off. For every line the game looks the line shader up by name and creates a new material, and never destroys it (they pile up until the next map). One such explosion took 35 ms in a match (measured once; 3 ms in an earlier one). With it on all those lines use one material that is made once. The lines look the same: their colour and fade are set on the line itself, not on the material. How much time it saves is not measured yet.

**KillCamNoGarbage**  
While you are dead or watching, the kill camera builds the same three texts again every frame (the help line, the health and the name of the player you watch), reads an object's name and starts two new camera animations: 250 to 460 bytes of garbage a frame averaged over a match (measured), about 1 KB per frame you are dead. That garbage brings the next collection forward, which can then land when you are alive again. With it on a text is only built again when what it says changes, the name is remembered, and the two animation objects go back to the animation library's pool to be used again. What you see while dead is the same.

**DeadNetworkObjects**  
The network library keeps lists of networked objects: per player the objects that player owns, and as host a table of despawned objects waiting to be destroyed. Objects that were destroyed with the last round's scene stay on those lists, so the players and weapons of every earlier round are kept in memory with their texts, input bindings and bones (measured: 23,019 destroyed objects still held after 11 minutes, 6 to 9 MB more memory every minute). With this on, destroyed objects are taken off those lists when a scene loads. Living objects are not touched and nothing is sent.

**GcSmallSteps**  
The game's garbage collector may stop a frame for up to 3 ms at a time. Give it 0.25 ms per frame instead, so its work is spread over more frames. Measured on an empty map: 0.1% low 240 to 446 FPS, 1% low 680 to 772, average about 1.5% lower.

**VoiceKeyOffThread** (started as an experiment)  
Pressing or releasing the voice key makes Steam open or close the microphone, and the game waits for that: 14 to 64 ms measured in a match. Let a helper thread do the waiting. The microphone opens and closes exactly as before.

**JobWorkerLimit** (started as an experiment)  
At this frame rate the engine's small per-frame jobs (changed transforms, animation, culling) cost more in waking worker threads and waiting for them than in the work itself: about a quarter of the main thread's frame in a sample profile. Limit the number of job worker threads to JobWorkerCount. Loading screens can get slower with few workers.

## Tried and removed

Kept here so they are not tried again without a new reason.

- **DeadSteamCallbacks** (2026-11-15): A handful of callbacks per session, no measured gain, and the one suspect of the two lobby drops that was never cleared.
- **SoundPacketGuard** (2026-11-16): Measured as host: what the game throws away behind the failing sound message is only more sound messages for the same destroyed sound sources (5 bytes each, down to 0), so the error loses nothing.
- **StrafthGreetKnownOnly** (2026-11-16): the fix is in STRAFTH itself from 2.1.1.
- **StrafthReceiveOffThread** (2026-11-15): measured: the call moved to the helper thread and the frame froze all the same.
- **NoRealtimeLights** (2026-11-15): no measurable gain in two leave-one-out runs (-0.3% and -1.5% +/-1.8) once it left the HUD and gun lights alone
- **MovementQueriesNoSync** (2026-11-18): measured on Canliferous, off 1053 FPS average against on 1024, the same 1% low
- **MovementCastRate** (2026-11-18): no gain under a frame cap, and it changed when vault, wall jump and slide are detected
- **StaticMeshMerge** (2026-11-15): the graphics card hands back empty buffers for the map's combined meshes, so it switched itself off on every map
- **SteamCallbackRate** (2026-11-18): 0.0% in the full benchmark (69 setups, 6 rounds), and it delayed Steam's connection events
- **ArcStepsReuse** (2026-11-16): reused the arrays of a launcher's arc. Fired shells stayed where they were made and the game threw 20,454 errors in one match.
- **OwnBodyCull** (2026-11-15): stopped lean-based movement tech such as fast falling.
- **HudMenuParking** (2026-11-15): removed at the author's request.
