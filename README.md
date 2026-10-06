# MIDI USB Doctor

A macOS-first desktop tool, built with .NET 10 and Avalonia, that shows musicians which CME H12MIDI Pro virtual port and MIDI channel each instrument is arriving on, and whether valid MIDI is reaching the computer.

See [PROJECT_BRIEF.md](PROJECT_BRIEF.md) for the product brief, roadmap, and the current list of known gaps.

## Status

Early working foundation, reviewed 2026-10-06:

- Lists CoreMIDI input and output endpoints and groups them into port rows. With the H12 connected this shows the eight `CME [H12] Port n` pairs.
- Monitors the selected port and decodes Note On/Off, Control Change, Program Change, Pitch Bend, pressure, Clock, transport and SysEx into a bounded live log.
- Not yet implemented: monitoring all ports at once, device labels, guided tests, diagnostics, DAW recommendations, hot-plug handling, Windows support.

Known gaps and the suggested order for tackling them are tracked in the brief under "Current status and known gaps".

## Requirements

- macOS. Tested on Apple Silicon; see the Intel note in the brief before relying on it on an Intel Mac.
- No system-wide .NET install or administrator access. The pinned SDK is installed into the repository by the setup script.

## Local development

Install or restore the pinned .NET 10 SDK:

```bash
./scripts/setup-dotnet.sh
```

Run .NET commands through the repository wrapper:

```bash
./scripts/dotnet --info
./scripts/dotnet build
./scripts/dotnet test
```

Run the app:

```bash
./scripts/dotnet run --project src/MidiUsbDoctor.App
```

The SDK is installed in `.dotnet/`, which is excluded from Git.

## Repository layout

| Path | Purpose |
|---|---|
| `src/MidiUsbDoctor.Core` | Platform-neutral MIDI endpoint and message records, plus the message decoder. No platform dependencies. |
| `src/MidiUsbDoctor.Midi` | The `IMidiService` abstraction and the macOS CoreMIDI adapter (hand-written P/Invoke, no third-party MIDI library). |
| `src/MidiUsbDoctor.App` | Avalonia desktop UI. Currently a single window with code-behind. |
| `tests/MidiUsbDoctor.Tests` | xUnit tests for Core and the CoreMIDI adapter. |
| `tools/diagnose-midi.swift` | Standalone CoreMIDI dump script, independent of the .NET app. |
| `assets/` | Source artwork, including the app icon. |
| `scripts/` | Local SDK bootstrap and the `dotnet` wrapper. |

## CoreMIDI diagnostic script

`tools/diagnose-midi.swift` opens every CoreMIDI source and prints raw bytes for 30 seconds. Use it to confirm that MIDI is reaching macOS at all, independently of the app:

```bash
swift tools/diagnose-midi.swift
```

## App icon

`assets/midi-usb-doctor-icon-flat.png` is embedded as an Avalonia resource (`Assets/AppIcon.png`) and set as the window icon in `MainWindow.axaml`.

- The resource is embedded and loads correctly; the app starts with it set.
- On Windows and Linux this icon will appear in the title bar and taskbar.
- On macOS Avalonia ignores `Window.Icon` (its macOS backend implements it as a no-op), and macOS has no title-bar icons. When run via `dotnet run` the Dock shows the generic executable icon. A real Dock icon needs the app packaged as a `.app` bundle with an `.icns` file referenced from `Info.plist`, which is part of the packaging milestone.
- The source PNG is 1254 px and about 1 MB, which roughly doubles the size of the app assembly. A 256 or 512 px copy is enough for the embedded icon; keep the full-size file for generating the `.icns`.
