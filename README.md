# VeloxBoost

Automatic PC optimizer for Windows 10/11.

### [⬇ Download VeloxBoost](https://github.com/minipulgajss-maker/VeloxBoost/releases/latest/download/VeloxBoost-win-Setup.exe)

## Features
- **394 reversible optimizations** across 11 categories: Gaming & FPS, Network & Ping, Privacy, Performance, Interface, Ads & Suggestions, Power, Services, Scheduled Tasks, Apps & Browsers, Cleanup
- Only optimizations supported by your PC are shown
- **Game Booster**: automatic high CPU priority for games and background app throttling
- **Network tools**: real DNS speed test and one-click fastest DNS
- Safe / Balanced / Extreme profiles, restore point before optimizing, full backup and revert
- English (default), Português, Español, Français
- **Automatic updates**: when a new version is out, the app asks you to update on launch

## Releasing a new version
Push any change to `main`. GitHub Actions builds the app, creates the installer and publishes a new release automatically.

## Safety
Every change stores the original value (`%LOCALAPPDATA%\VeloxBoostData\backup.json`) and can be reverted. Defender, UAC, Firewall, SmartScreen and Windows Update are never turned off.
