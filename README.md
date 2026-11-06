# VeloxBoost

Automatic PC optimizer for Windows 10/11.

- **442 reversible optimizations** across 12 categories: Gaming & FPS, Per-Game, Network & Ping, Privacy, Performance, Interface, Ads & Suggestions, Power, Services, Scheduled Tasks, Apps & Browsers, Cleanup
- **Per-game tuning** for CS2, Valorant, Fortnite, League of Legends, Apex, Rocket League, Dota 2, Rust, PUBG, Rainbow Six Siege, GTA V, Overwatch 2, Marvel Rivals, Roblox, plus Discord, Steam and OBS
- **Game Booster**: automatic high CPU priority for games and background app throttling
- **Network tools**: real DNS speed test and one-click fastest DNS
- Safe / Balanced / Extreme profiles, restore point before optimizing, full backup and revert
- English, Português, Español, Français
- **Automatic updates** (like Discord) powered by Velopack

## Install
Download `VeloxBoost-win-Setup.exe` from the [latest release](https://github.com/minipulgajss-maker/VeloxBoost/releases/latest).

## Release a new version
```
git tag v1.2.0
git push origin v1.2.0
```
GitHub Actions builds the app, creates the installer and publishes the release. Installed copies download the update in the background and install it on restart.

## Safety
Every change stores the original value (`%LOCALAPPDATA%\VeloxBoostData\backup.json`) and can be reverted. Defender, UAC, Firewall, SmartScreen and Windows Update are never turned off.
