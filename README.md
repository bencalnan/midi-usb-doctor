# MIDI USB Doctor

A desktop tool for musicians that shows where your MIDI is going, and where it stops.

Plug in your instruments, launch the app, play each one, and it tells you which MIDI port and channel the instrument arrived on, whether MIDI is reaching the computer and coming back out again, and exactly which settings to pick in Ableton Live or Cubase. No MIDI knowledge needed.

Built around the CME H12MIDI Pro multi-port interface, but it works with any MIDI device the operating system can see. Runs on macOS and Windows 11.

## What it does

- **Listens to every MIDI input at once.** Each port has a row with an activity light, the channels heard, and the last message. Play an instrument and its row lights up.
- **Decodes traffic in plain terms.** Note On and Off with note names, Control Change, Program Change, Pitch Bend, pressure, Clock, transport and SysEx, in a live log you can filter to one port. Clock is hidden by default so it does not swamp everything else.
- **Lets you name things.** Give each port the instrument's name and note which socket on the interface it is plugged into. Names are saved and come back on the next launch.
- **Tests the input.** Press *Test input*, play for ten seconds, and get a plain-language verdict: working, connected but not sending notes, or nothing received, with advice. If the instrument turned up on a different port, it says which one.
- **Tests the output.** Press *Test output* and the app sends one short note on the channel the instrument uses, then asks whether you heard it. The matching Note Off is always sent.
- **Writes your DAW settings.** Press *DAW settings* for copy-ready Ableton Live and Cubase settings for that port, worked out from what was actually observed, with notes on when Sync or Remote should differ from the defaults.
- **Plays nicely with other MIDI software.** *Pause listening* releases every port so a DAW or the interface's own configuration tool can use them, then takes them back.

## Platform support

| Platform | MIDI stack | Notes |
|---|---|---|
| macOS (Apple Silicon) | CoreMIDI | Development platform. Shared port access, so the app can run alongside a DAW. |
| Windows 11 25H2 and later | WinMM via Windows MIDI Services | Shared port access and real port names once Windows MIDI Services reaches the machine. |
| Windows 11 23H2 and 24H2 | WinMM, legacy stack | Only one application can hold a port at a time. Use *Pause listening* before opening a DAW or the interface's configuration tool. The app recognises the legacy "MIDIIN2 (device)" port names and pairs them correctly. |
| Intel Macs | CoreMIDI | Untested. See the packet alignment note in the project brief before relying on it. |

Linux is not supported: there is no ALSA adapter yet, though the architecture allows one.

## Getting started

You need the .NET 10 SDK. On macOS the repository can install a private copy so nothing is needed system-wide; on Windows install the SDK from Microsoft.

**macOS**

```bash
./scripts/setup-dotnet.sh          # one-off: installs the pinned SDK into .dotnet/
./scripts/dotnet run --project src/MidiUsbDoctor.App
```

**Windows**

```powershell
dotnet run --project src\MidiUsbDoctor.App
```

Build and test from the repository root with `dotnet build` and `dotnet test` (prefix with `./scripts/` on macOS if you used the private SDK).

## Using it

1. Connect your interface and instruments, then launch the app. Every port appears in the left-hand list, interface ports first, and the app starts listening on all of them.
2. Play each instrument. Its row lights up and shows the channel and the last message. The live log on the right shows everything arriving.
3. Select a row to name the instrument and note which socket it is on. Press *Save*. The log filters to that port while a row is selected; *Show all* clears the filter.
4. Press *Test input* and play for ten seconds for a verdict and advice.
5. Press *Test output*, listen for the note, and answer the question.
6. Press *DAW settings* and copy the block for your DAW.
7. Before opening a DAW or the interface's configuration tool on an older Windows, press *Pause listening*. Press *Resume listening* afterwards.

### Where your names are stored

Instrument names and socket notes live in `device-labels.json`:

- Windows: `%APPDATA%\MidiUsbDoctor\`
- macOS: `~/Library/Application Support/MidiUsbDoctor/`

They are keyed by port name, so they survive reconnects, USB socket changes, and moving between a Mac and a PC. Delete the file to reset them.

### What the app cannot tell you

The socket an instrument is plugged into on the interface is something you type, not something the app detects. The H12 presents only a standard USB MIDI interface to the computer, so its internal routing matrix is invisible to the operating system. Likewise, a successful output test proves the operating system accepted the note, not that the instrument received it, which is why the app asks whether you heard it.

## How it is built

.NET 10 and Avalonia, in three layers with no third-party MIDI library:

| Project | Purpose |
|---|---|
| `src/MidiUsbDoctor.Core` | Platform-neutral logic: endpoint and message records, the decoder, port pairing and name normalisation, per-port activity tracking, the label store, the input and output test evaluators, and the DAW settings generator. Everything here is unit-tested. `Cme/` holds the only interface-specific code. |
| `src/MidiUsbDoctor.Midi` | The `IMidiService` abstraction, a platform factory, and two hand-written P/Invoke adapters: CoreMIDI for macOS and WinMM for Windows. |
| `src/MidiUsbDoctor.App` | The Avalonia window. A single view with code-behind and small view models. |
| `tests/MidiUsbDoctor.Tests` | xUnit tests for Core and the adapters, including a live CoreMIDI send when hardware is present. |
| `tools/diagnose-midi.swift` | Standalone script that dumps raw bytes from every CoreMIDI source for 30 seconds, for checking that MIDI reaches macOS independently of the app. |
| `scripts/` | Private SDK bootstrap and the `dotnet` wrapper for macOS. |

Messages arrive on the MIDI stack's own thread, are queued, and are drained in batches on the UI thread, so a flood of clock from eight ports never stalls the window.

The product brief, current status, roadmap and platform notes are in [docs/PROJECT_BRIEF.md](docs/PROJECT_BRIEF.md).

## Roadmap

Done: discovery, live monitoring, labels, input and output tests, DAW settings, macOS and Windows support.

Next, in rough order:

- Split multi-message CoreMIDI packets and reassemble long SysEx.
- Hot-plug detection so the list updates without pressing Refresh.
- Investigate whether the H12 routing matrix can be read, so the socket does not have to be typed.
- Output Control Change test and SysEx Device Inquiry.
- Signed, packaged releases with a proper Dock and taskbar icon.
- A fake MIDI service so the window logic can be tested without hardware.

## Contributing

Issues and pull requests are welcome. The useful things to know:

- Keep Core free of platform types. Anything that talks to CoreMIDI or WinMM lives in `MidiUsbDoctor.Midi`.
- Logic that produces text for the user, such as test verdicts and DAW settings, belongs in Core with a unit test, not in the window.
- Run `dotnet test` before opening a pull request. The suite runs in under a second.
- If you have an interface other than the H12, reports of how its ports are named on each platform are especially helpful.

## Licence

[MIT](LICENSE).
