<p align="center">
  <img src="docs/icon.png" width="96" alt="TuneBar icon">
</p>

<h1 align="center">TuneBar</h1>

<p align="center">A tiny Spotify mini-player that lives on your Windows taskbar.<br>
Free, open source, no login and no API key needed.</p>

<!-- Add a screenshot once you've got one:
<p align="center"><img src="docs/screenshot.png" alt="TuneBar on the taskbar"></p>
-->

## Features

- **Album art, song title and artist** right inside your taskbar, bottom-left
- **Previous, Play/Pause and Next** buttons
- **Volume slider** that controls Spotify's own volume (not your whole PC). Click the speaker to mute.
- **Works with an auto-hiding taskbar**: it slides away and comes back together with the taskbar
- **Settings app** built in: colors, see-through background, accent color (or match the album art), position, width
- **Starts with Windows** automatically, and runs quietly with no command window
- **Scroll the mouse wheel** over it to change the volume, **drag it** sideways to move it
- **Hides itself** when a game or video goes fullscreen

## Download

1. Go to the [**Releases**](../../releases/latest) page.
2. Download `TuneBar-win-x64.exe` (or `TuneBar-win-arm64.exe` on ARM PCs like Snapdragon laptops).
3. Put it somewhere it can stay (for example a `TuneBar` folder in Documents) and double-click it.
   Settings opens the first time so you can pick your colors.

> **"Windows protected your PC"?** That shows up for any new app that isn't code-signed.
> Click **More info → Run anyway**. The full source code is right here if you want to check it.

## Using it

| Do this | What happens |
|---|---|
| Click the album art or song name | Opens Spotify |
| Drag the widget sideways | Moves it along the taskbar (remembered) |
| Scroll the wheel over it | Volume up / down |
| Click the speaker icon | Mute / unmute Spotify |
| **Right-click** it | Settings, Open Spotify, Exit |
| Open TuneBar again (e.g. from Start) | Opens Settings |

## Settings

- **General:** start with Windows (on by default), also follow other players like YouTube in a browser
- **Position:** inside the taskbar or floating just above it, distance from the left edge, width
- **Colors:** background (match Windows / dark / light / any custom color), background opacity
  (0% blends right into the taskbar), accent color for the play button and volume bar, or
  *match the album art*

## Requirements

- Windows 10 (version 2004 or newer) or Windows 11, with the taskbar at the bottom or top
- The **Spotify desktop app** (the normal installer or the Microsoft Store version)
- Works with Spotify Free and Premium

## How it works

TuneBar reads what's playing from **Windows' built-in media controls**, the same place the
volume pop-up and lock screen get it from. Spotify already sends its song info there, so TuneBar
never needs your Spotify account. Volume is set through the normal Windows volume mixer, just
for Spotify.

Settings are saved in `%AppData%\TuneBar\settings.json`.

## Tips

- **Windows 11:** the Widgets (weather) button also sits in the bottom-left corner. Drag TuneBar to the
  right, or turn Widgets off in *Settings → Personalization → Taskbar*.
- If your taskbar is on the left or right side of the screen, TuneBar floats above the bottom of the screen instead.

## Build it yourself

**Easiest:** install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then double-click
**`install.bat`**. It builds TuneBar, installs it to `%LocalAppData%\Programs\TuneBar`, adds it to the
Start menu and starts it. `uninstall.bat` removes everything again.

For development:

```powershell
dotnet run --project src/TuneBar
```

Publishing a release on GitHub builds both `.exe` files and attaches them to it automatically
(see `.github/workflows/build.yml`).

## Contributing

Bug reports and pull requests are welcome. Please open an issue first for big changes.

## License

[MIT](LICENSE). Free to use, change and share.

---

*TuneBar is a fan-made project and isn't affiliated with or endorsed by Spotify.
Spotify is a trademark of Spotify AB.*
