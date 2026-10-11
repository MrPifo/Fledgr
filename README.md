# Fledgr

A lightweight native music player for Windows (C#, .NET 10, Avalonia).
One player for your local music, YouTube Music and internet radio, built for low memory use.

<img width="1128" height="826" alt="image" src="https://github.com/user-attachments/assets/4ee381f3-071a-4ea4-80fc-a9e1763667f1" />

## Features

### Local music
- Library from your own folders, with live folder watching
- Fast search across file names and tags (title, artist, album)
- Views for artists, albums and folders, with embedded or folder cover art
- Playlists, favourites, multi-select, drag & drop of files and folders
- Weighted shuffle, queue saved between sessions
- Playback via LibVLC

### YouTube Music (YtConnect)
- Uses your own YouTube Music account in a hidden WebView2 (sign-in stays on Google's page)
- Home feed, search with filters and suggestions, artist pages, playlists and "Liked music"
- Like/dislike, create and edit playlists, radio mixes, lyrics

### Internet radio
- Station search via [radio-browser.info](https://www.radio-browser.info) (name, country, genre)
- Favourites, history, custom stream URLs (M3U/PLS)
- Live song titles (ICY metadata), automatic detection of dead or moved stations

### App
- Home page with "continue listening", recently played and module sections
- Floating player bar, mini player widget, tray menu
- Media keys and Windows media overlay (SMTC)
- Sleep timer, per-source volume, session restore (track and position)
- Modules can be switched on and off at runtime in the settings
- English and German UI
- Built-in diagnostics (RAM per process, operation timings, CSV export)

## Download

Get `Fledgr.exe` from the [Releases](../../releases) page. It is a single self-contained file: no .NET install needed, just run it.

**Requirements:** Windows 10/11 x64. YouTube Music needs the Microsoft Edge WebView2 Runtime (preinstalled on most systems).

## Project structure

```
src/
  YTSPlayer.Contracts            interfaces and models, no UI
  YTSPlayer.Ui                   design system (themes, metrics, components)
  YTSPlayer.Shell                main window, player service, plugin host, home page
  Fledgr.Audio                   shared LibVLC audio engine (local music and radio)
  YTSPlayer.Modules.MusicPlayer  local library
  YTSPlayer.Modules.YtConnect    YouTube Music (+ .Windows: WebView2 host)
  YTSPlayer.Modules.RadioPlayer  internet radio
  YTSPlayer.Platform.Windows     P/Invoke, SMTC
  YTSPlayer.Desktop              executable, composition root
tests/YTSPlayer.Tests            xunit
tools/                           architecture check, icon script
```

Dependencies only point downwards. Platform code lives only in `*.Windows` and `Desktop`. `tools/check-architecture.ps1` enforces this.

## Building

Requirements: .NET 10 SDK, Windows 10 (1809) or newer, WebView2 Runtime.

```powershell
dotnet run --project src\YTSPlayer.Desktop
dotnet test tests\YTSPlayer.Tests\YTSPlayer.Tests.csproj
.\publish.ps1        # single-file EXE to publish\Fledgr.exe
```

The EXE has built-in self-tests (`--selftest`, `--selftest-ytm`, `--selftest-login`, `--selftest-local`, `--selftest-radio`, `--selftest-ui`). Results are written next to the EXE.

## Notes

- YouTube content is never downloaded, stored or extracted from the web view. Ads are not blocked.
- Google sign-in happens on Google's own page. The app never stores passwords.
- Unofficial: the app uses interfaces of the YouTube Music website, which may change without notice. Not affiliated with Google.
- Low memory is a core goal. The web view is closed after a period of inactivity.
- Test new YouTube features with a secondary account first. Deleting or editing playlists affects your account directly.
