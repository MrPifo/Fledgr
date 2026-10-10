# Fledgr

Musikplayer für Windows (C#, .NET 10, Avalonia). Spielt lokale Dateien und YouTube Music.

## Funktionen

- Lokale Bibliothek: Ordner, Interpreten, Alben, Playlists, Favoriten. Suche über Pfad und Tags, Cover, Ordnerüberwachung. Wiedergabe mit LibVLC.
- YouTube Music (Premium-Konto): Wiedergabe über eine versteckte WebView2. Playlists, Suche mit Filter und Vorschlägen, Startseite, Künstlerseiten, Songtexte, Playlists bearbeiten.
- Zufallswiedergabe mit Gewichtung, Sleep-Timer, Medientasten und Medienanzeige (SMTC), Widget, Tray.
- Module (lokal, YouTube Music, Radio) lassen sich in den Einstellungen ein- und ausschalten.

## Aufbau

```
src/
  YTSPlayer.Contracts            Schnittstellen und Modelle, kein UI
  YTSPlayer.Ui                   Design-System (Farben, Maße, Komponenten)
  YTSPlayer.Shell                Hauptfenster, Player-Dienst, Plugin-Host
  YTSPlayer.Modules.MusicPlayer  lokale Bibliothek
  YTSPlayer.Modules.YtConnect    YouTube Music (+ .Windows: WebView2)
  YTSPlayer.Modules.RadioPlayer  Platzhalter
  YTSPlayer.Platform.Windows     P/Invoke, SMTC
  YTSPlayer.Desktop              EXE, Verdrahtung
tests/YTSPlayer.Tests            xunit
tools/                           Architektur-Check, Icon-Skript
```

Abhängigkeiten zeigen nur nach unten. Plattformcode steht nur in `*.Windows` und `Desktop`. `tools/check-architecture.ps1` prüft das.

## Bauen

Voraussetzungen: .NET 10 SDK, Windows 10 (1809) oder neuer, WebView2 Runtime.

```powershell
dotnet run --project src\YTSPlayer.Desktop
dotnet test tests\YTSPlayer.Tests\YTSPlayer.Tests.csproj
.\publish.ps1        # Single-File-EXE nach publish\Fledgr.exe
```

Die EXE hat Selbsttests (`--selftest`, `--selftest-ytm`, `--selftest-login`, `--selftest-local`, `--selftest-ui`). Das Ergebnis liegt neben der EXE.

## Hinweise

- YouTube-Inhalte werden nicht heruntergeladen, gespeichert oder aus der Web-Ansicht herausgelöst. Werbung wird nicht blockiert.
- Der Google-Login läuft auf der Seite von Google. Die App speichert keine Passwörter.
- Inoffiziell: Die App nutzt Schnittstellen der YouTube-Music-Webseite. Sie können sich ohne Ankündigung ändern. Kein Produkt von Google.
- Ziel ist wenig Arbeitsspeicher. Im Leerlauf sind es etwa 50 MB, die Web-Ansicht wird nach Ruhe beendet.
- Neue YouTube-Funktionen zuerst mit einem Zweitkonto testen. Playlists löschen und bearbeiten wirkt direkt auf das Konto.
