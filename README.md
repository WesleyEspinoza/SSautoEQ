# SteelSeries Auto EQ

A lightweight Windows tray app that automatically switches your **SteelSeries GG Sonar** game EQ profile to match whatever application currently has focus. Alt-tab into a game and the right EQ is already loaded; switch to another game and it follows you, all without opening the SteelSeries GG window.

> Unofficial project. Not affiliated with or endorsed by SteelSeries. "SteelSeries", "GG", and "Sonar" are trademarks of their respective owners.

## Features

- **Zero configuration to connect.** Finds the Sonar local API automatically, including its randomly assigned port.
- **Per-process EQ assignments.** Pick exactly which Sonar config each game or app should use.
- **Default profile fallback.** Anything without an assignment can fall back to a profile you choose.
- **Focus-driven.** Reacts instantly to Windows foreground events, backed by a lightweight one-second safety check that also catches tab and title changes within the same window.
- **Runs in the system tray.** Single instance; launching it again just reopens the window.
- **Auto-starts SteelSeries GG.** If GG isn't running when it's needed, the app launches it and waits for the Sonar API to come up.
- **Resilient.** If Sonar restarts and its port changes, the app rediscovers it on its own.

## Requirements

- Windows 10 or 11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to run, or the .NET 8 SDK to build
- SteelSeries GG with the Sonar app installed and running

## Getting started

Clone and build:

```powershell
git clone <your-fork-url> SteelSeriesAutoEq
cd SteelSeriesAutoEq
dotnet build src/SteelSeriesAutoEq/SteelSeriesAutoEq.csproj -c Release
```

Run it:

```powershell
dotnet run --project src/SteelSeriesAutoEq/SteelSeriesAutoEq.csproj -c Release
```

Or launch the built executable directly:

```
src\SteelSeriesAutoEq\bin\Release\net8.0-windows\SteelSeriesAutoEq.exe
```

The app opens a status window on first launch and keeps running in the tray. Closing the window leaves it running; use **Exit** from the tray menu to quit.

## Usage

1. Start SteelSeries GG (with Sonar) and then start SteelSeries Auto EQ.
2. The status window shows the connection state, the active Sonar profile, and the process currently in focus.
3. To map a game to an EQ profile:
   - Bring the game to the foreground once so the app records it.
   - Open Auto EQ from the tray. The **Process in focus** panel shows that game (focusing Auto EQ itself does not overwrite it).
   - Choose a Sonar config from the dropdown and click **Assign to process**.
4. From now on, whenever that process is focused the assigned config is selected. Anything without an assignment uses the default profile if you set one.

Assignments and the default profile are listed in the window and can be removed at any time.

### Tray menu

- Current game, current profile, connection status, and API endpoint
- Show Window
- Refresh Profiles
- Enable Auto Switching
- Settings
- Open Log
- Exit

## How it works

**API discovery.** SteelSeries GG exposes a local HTTP API on a port that changes every launch, so nothing is hardcoded. The app looks for the endpoint in three ways, in order:

1. Reads the SteelSeries `coreProps.json` and asks the GG engine (`/subApps`) for the Sonar web-server address.
2. Inspects localhost TCP listeners owned by SteelSeries processes.
3. Falls back to a short list of commonly used ports.

Each candidate is confirmed by calling `GET /configs` and checking for valid JSON.

**Auto-starting SteelSeries GG.** If discovery finds nothing and no GG process is running, the app locates the GG executable (via its Windows startup entry, the uninstall registry key, or the usual Program Files locations), launches it, and then polls until the Sonar API responds before continuing. If GG is already running but still booting, it just waits.

**Reading profiles.** Game EQ configs come from `GET /configs`. Only entries whose `virtualAudioDevice` is `game` are used; chat, media, and other channels are ignored.

**Detecting the focused app.** A `SetWinEventHook` subscription for `EVENT_SYSTEM_FOREGROUND` reports app switches instantly. A one-second poll runs alongside it as a safety net and to catch changes the hook never raises, such as switching tabs inside the same window (the foreground window stays the same, only its title changes). For each change the app reads the window title, process name, and executable name.

**Choosing a profile.** On every focus change the app resolves a target profile:

1. An explicit assignment for that executable, if one exists.
2. Otherwise the default profile, if configured.
3. Otherwise it leaves the current profile alone.

When you assign a profile in the UI, a built-in catalog of common game names (for example `cs2.exe` to a "CS2" config) is used only to pre-select a sensible suggestion. It never switches on its own.

**Switching.** Selecting a profile is a `PUT /configs/{id}/select`, then the app reads `GET /configs/selected` back to confirm the change.

## Configuration and data files

These are created next to the executable on first run and are safe to delete:

| File | Purpose |
| --- | --- |
| `settings.json` | Auto-switch toggle, default profile, and per-process assignments |
| `profiles.json` | Cached copy of your Sonar game profiles |
| `logs/app.log` | Rolling activity log |

`settings.json` assignments are keyed by executable name:

```json
{
  "autoSwitchEnabled": true,
  "defaultProfileId": "84888f22-3a7f-44a0-9479-dc8d639226b6",
  "processProfileMap": {
    "cs2.exe": "cc64bc5c-0663-4229-9b7c-4b32f579411b"
  }
}
```

## Troubleshooting

- **Status shows "SteelSeries GG not found".** The app tries to auto-start GG, but if it was installed to a non-standard location it may not be found. Start GG manually once, then use **Refresh Profiles**. The app retries discovery on its own as well.
- **A game does not switch.** Confirm it appears under **Process in focus**, then assign a profile to it. Some launchers run the game under a different executable name than you expect; assign the one shown.
- **Wrong config was picked automatically.** Automatic guessing only pre-fills the assignment dropdown. Assign the correct profile explicitly and it will be remembered.
- **Need details.** Use **Open Log** from the tray menu.

## Roadmap ideas

- Import and export of assignment sets
- Steam AppID based mapping
- Optional per-title volume presets

## Contributing

Issues and pull requests are welcome. The project targets .NET 8 and WPF; keep changes buildable with `dotnet build` and avoid adding hardcoded ports or paths.

## License

Released under the [MIT License](LICENSE).
