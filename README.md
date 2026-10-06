# MIDI USB Doctor

A macOS-first desktop tool, built with .NET 10 and Avalonia, that shows musicians which CME H12MIDI Pro virtual port and MIDI channel each instrument is arriving on, and whether valid MIDI is reaching the computer.

See [PROJECT_BRIEF.md](PROJECT_BRIEF.md) for the product brief, roadmap, and the current list of known gaps.

## Status

Early working foundation, reviewed 2026-10-06:

- Lists CoreMIDI input and output endpoints and pairs them into port rows by name. H12 ports are shown first, other devices after.
- Listens on every input at once. Each row shows an activity dot, the channels heard, and the last message, so playing an instrument identifies its port.
- Decodes Note On/Off, Control Change, Program Change, Pitch Bend, pressure, Clock, transport and SysEx into a bounded live log. Selecting a row filters the log to that port, and a "Hide clock" toggle (on by default) keeps MIDI Clock out of the log while port rows still report that clock was seen.
- Select a port to record which instrument is on it, such as "Bass Station II", and which H12 socket it is plugged into, such as "USB host 3". Both are saved to disk, restored on launch, and shown in the port list; the instrument name also replaces the port name in the log.
- Select a port and press "Test this device". The app listens for ten seconds while you play, then reports in plain language: port, channel, whether notes, controls and clock arrived, and what to try if not. If the instrument showed up on a different port during the test, it says which.
- Not yet implemented: output (computer-to-instrument) tests, DAW recommendations, hot-plug handling.

The shipping target is Windows. macOS is the development platform. The MIDI layer sits behind a platform-neutral interface with a CoreMIDI adapter for macOS and a WinMM adapter for Windows. The Windows adapter compiles but has not yet been run on a PC.

Known gaps and the suggested order for tackling them are tracked in the brief under "Current status and known gaps".

## Requirements

- macOS for development. Tested on Apple Silicon; see the Intel note in the brief before relying on it on an Intel Mac.
- Windows 10 or 11 for the target build. Untested so far; the first run should follow the checklist in the brief.
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
| `src/MidiUsbDoctor.Core` | Platform-neutral MIDI endpoint and message records, the message decoder, port pairing, per-port activity tracking and the device label store. No platform dependencies. `Cme/` holds the H12-specific bits. |
| `src/MidiUsbDoctor.Midi` | The `IMidiService` abstraction, a platform factory, the macOS CoreMIDI adapter and the Windows WinMM adapter. Both adapters are hand-written P/Invoke with no third-party MIDI library. |
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

## Where labels are stored

Device names and socket notes live in `device-labels.json` under the application-data folder: `%APPDATA%\MidiUsbDoctor\` on Windows and `~/Library/Application Support/MidiUsbDoctor/` on macOS. Delete the file to reset them.

The socket is something you type, not something the app detects. The H12 presents only a standard USB MIDI interface to the computer, so its routing matrix (which USB host socket or DIN connector feeds which virtual port) is not visible to the operating system. See "CME routing awareness" in the brief for the options.

## App icon

`assets/midi-usb-doctor-icon-flat.png` is embedded as an Avalonia resource (`Assets/AppIcon.png`) and set as the window icon in `MainWindow.axaml`.

- The resource is embedded and loads correctly; the app starts with it set.
- The same image is shown in the window header to the left of the title, sized to the title block, so the branding is visible on every platform including macOS.
- On Windows and Linux this icon will appear in the title bar and taskbar.
- On macOS Avalonia ignores `Window.Icon` (its macOS backend implements it as a no-op), and macOS has no title-bar icons. When run via `dotnet run` the Dock shows the generic executable icon. A real Dock icon needs the app packaged as a `.app` bundle with an `.icns` file referenced from `Info.plist`, which is part of the packaging milestone.
- The source PNG is 1254 px and about 1 MB, which roughly doubles the size of the app assembly. A 256 or 512 px copy is enough for the embedded icon; keep the full-size file for generating the `.icns`.
