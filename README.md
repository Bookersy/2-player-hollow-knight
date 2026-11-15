# 2 Player Hollow Knight (Local Coop mod)

A same-screen, local 2-player mod for Hollow Knight. Player 2 is a second, blue-tinted Knight on the same screen, using their own controller.

> **Version 0.1.6 is experimental.** Please report bugs in [Issues](../../issues) and attach the lines mentioning "Local Coop" from your `ModLog.txt`.

## Install (no building needed)
1. Install [Lumafly](https://themulhima.github.io/Lumafly/) and use it to install any mod (for example HKMP). This sets up the Modding API. The game needs to be on a moddable version (Steam/GOG 1.5.78).
2. Download [`LocalCoop-0.1.6.zip`](download/LocalCoop-0.1.6.zip?raw=1) (click it, then the download button).
3. Copy the `LocalCoop` folder from the zip into
   `...\Hollow Knight\hollow_knight_Data\Managed\Mods\`
   (to find it in Steam: right-click Hollow Knight → Manage → Browse local files).
4. Start the game. "Local Coop" appears in the top-left mod list on the title screen.

## Versions
All versions stay in the [`download`](download) folder. The newest is at the top.

| Version | Download | Changes |
|---|---|---|
| **0.1.6** (latest) | [LocalCoop-0.1.6.zip](download/LocalCoop-0.1.6.zip?raw=1) | Experimental: enemies notice and go after Player 2 when Player 2 is closer. Turn off with `EnemiesTargetP2`. |
| 0.1.5 | [LocalCoop-0.1.5.zip](download/LocalCoop-0.1.5.zip?raw=1) | Player 2 gets a soul meter and can Focus to heal (hold the game's Focus button). Player 2 can pick up geo. |
| 0.1.4 | [LocalCoop-0.1.4.zip](download/LocalCoop-0.1.4.zip?raw=1) | Player 2 matches the Knight's actual running speed, including walk zones and Sprintmaster. |
| 0.1.3 | [LocalCoop-0.1.3.zip](download/LocalCoop-0.1.3.zip?raw=1) | Player 2's attacks show the white nail-slash effect, like the Knight's. |
| 0.1.2 | [LocalCoop-0.1.2.zip](download/LocalCoop-0.1.2.zip?raw=1) | Player 2 uses the game's controller bindings (Options → Controller), including rebinds. |
| 0.1.1 | [LocalCoop-0.1.1.zip](download/LocalCoop-0.1.1.zip?raw=1) | Player 2 is no longer faded or hidden behind fog and scenery, and moves at the Knight's real speeds. |
| 0.1.0 | [LocalCoop-0.1.0.zip](download/LocalCoop-0.1.0.zip?raw=1) | First release. |

## Controls
Player 1 uses the keyboard as usual. Player 2 uses a controller with **the same button layout as the game's controller settings** (Options → Controller), so rebinding there changes Player 2's buttons too. Player 2 can move, jump, attack (hold up or down to aim), dash and Focus (hold to heal 1 mask for 33 soul; soul comes from hitting enemies).

Press **F9** to turn co-op on or off.

**Both players on controllers:** run the game once, then set `"P1UsesKeyboard": false` in
`%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight\LocalCoop.GlobalSettings.json`.
Player 1 gets the first controller and Player 2 the next.

## Features
- Player 2 can run, jump, attack (side, up and down with pogo) and take damage. They have their own masks, shown in the top-right corner.
- Player 2 gets dash, double jump and wall jump once Player 1 has unlocked them, and does the same nail damage as Player 1.
- The camera follows Player 1. If Player 2 is off screen for 1.5 s, or hits spikes, acid or a pit, they're put back next to Player 1.
- Player 2 respawns 5 s after running out of masks. They heal fully at benches and when Player 1 respawns, and can Focus to heal.

## Settings
`LocalCoop.GlobalSettings.json` includes: `P1UsesKeyboard`, `P2ControllerIndex`, `P2MaxMasks`, `RespawnSeconds`, `NailHitsPerHeal`, `LeashSeconds`, `EnemiesTargetP2`, tint colour (`TintR/G/B`) and `ToggleKey`.

## Known limitations
- Player 2 can't use spells, open doors, sit on benches, talk to NPCs or go through room exits. Player 1 does all of that, and Player 2 follows.
- Player 2's nail damages enemies but can't break walls or hit levers.
- Enemies going after Player 2 is experimental and works best on enemies whose behaviour is a PlayMaker FSM. Some enemies may still only chase Player 1.
- Only Player 1 can use the menus.

## Building from source
Requires the [.NET SDK](https://dotnet.microsoft.com/download) and a modded Hollow Knight install.
Run `build.bat`, or:
```
dotnet build -c Release -p:HollowKnightRefs="C:\...\Hollow Knight\hollow_knight_Data\Managed"
```
The build copies `LocalCoop.dll` into the game's `Mods\LocalCoop` folder.
