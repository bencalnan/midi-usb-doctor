# MIDI USB Doctor: project brief

This document holds the product thinking behind MIDI USB Doctor: what it is for, the principles it follows, what has been delivered, what remains, and the platform knowledge gathered along the way. The [README](../README.md) covers how to build and use it.

## Summary

MIDI USB Doctor is a desktop diagnostic tool for musicians using a multi-port MIDI interface, first and foremost the CME H12MIDI Pro. It makes MIDI routing problems visible without requiring the user to understand raw MIDI or to inspect DAW settings repeatedly.

The intended experience:

> Plug everything in, press **Test**, play or adjust each instrument, and receive a clear diagnosis plus the exact Ableton Live or Cubase settings to use.

The first release answers three practical questions:

1. Which operating-system port is an instrument arriving on?
2. Which MIDI channel is it using?
3. Is valid MIDI reaching the computer, and getting back out to the instrument?

## Principles

- Prefer plain-language diagnoses over MIDI jargon.
- Show live evidence rather than asking users to infer routing from configuration screens.
- Keep generic MIDI monitoring separate from interface-specific behaviour.
- Let users label ports once, because standard MIDI traffic does not identify the connected instrument.
- Do not make proprietary interface routing access a dependency of the first release.
- Distinguish "not tested" from "failed", and never claim more than the evidence supports. A successful send proves the operating system accepted the data, not that the instrument received it.

## Architecture

.NET 10 with Avalonia, in three layers:

- **Core**: platform-neutral records and logic. Endpoints and messages, the decoder, port pairing and name normalisation, per-port activity tracking, the label store, the input and output test evaluators, and the DAW settings generator. No platform dependencies. Interface-specific code (currently only H12 port recognition) sits under `Core/Cme`.
- **Midi**: the `IMidiService` abstraction (enumerate, start and stop monitoring, send) and the platform adapters. Both adapters are hand-written P/Invoke with no third-party MIDI library: CoreMIDI on macOS, WinMM on Windows. A factory picks the adapter at runtime.
- **App**: the Avalonia window. Messages are queued off the MIDI thread and drained in batches on the UI thread so dense traffic cannot stall the window.

Anything that produces wording for the user lives in Core with a unit test, so the window stays thin and the verdicts are testable without hardware.

## Status

All milestones in the original plan except routing awareness are delivered and confirmed against the H12 on both macOS and Windows 11.

| Milestone | Status | Notes |
|---|---|---|
| 1. Endpoint discovery | Done | All endpoints listed and paired by name; interface ports first in numeric order. Hot-plug still needs a manual Refresh. |
| 2. Live MIDI monitor | Done | Every input open at once; per-port activity, channels and last message; bounded, filterable log; clock hidden by default. |
| 3. Labels and persistence | Done | Instrument name and socket per port, keyed by port name, stored as JSON under the application-data folder, kept for absent ports. |
| 4. Guided tests and diagnostics | Done | Ten-second input test with verdicts for working, connected-but-silent, nothing received, and instrument-on-another-port. Output test with guaranteed Note Off and a heard / not heard answer. |
| 5. DAW recommendations | Done | Ableton Live and Cubase settings from observed data, with notes on Sync and Remote and on whether the output is confirmed. |
| 6. Interface routing awareness | Investigation | See the H12 notes below. The socket is recorded manually in the meantime. |

Also delivered beyond the original plan: a Windows adapter confirmed on hardware, Pause / Resume listening for single-client MIDI stacks, clear "port held by another application" messaging, and legacy Windows port-name normalisation.

## Remaining work

In rough priority order.

1. **CoreMIDI packet splitting.** The macOS adapter emits one message per CoreMIDI packet and the decoder reads only the first status byte. A packet carrying several messages loses all but the first, and SysEx spanning packets appears as "Data" rows. Fix in the adapter with decoder tests; the same splitting logic will serve the Windows adapter's SysEx path.
2. **Hot-plug detection.** CoreMIDI offers a notification callback; WinMM needs polling. Add a device-changed event to `IMidiService` and refresh automatically.
3. **Routing investigation.** See the H12 notes below.
4. **Output Control Change test and SysEx Device Inquiry.** Optional extras from the original scope.
5. **Test coverage.** A fake `IMidiService` for window logic and high-volume tests; more decoder cases including system messages; CoreMIDI tests that skip rather than silently pass off macOS.
6. **Packaging.** Signed releases. On macOS a `.app` bundle with an `.icns` is needed for a Dock icon, because Avalonia's `Window.Icon` is a no-op there. The embedded icon PNG is 1254 px and about 1 MB; a 256 or 512 px copy is enough.
7. **Intel Macs.** The packet stride between CoreMIDI packets is 4-byte aligned on Apple Silicon and unpadded on Intel. The adapter currently assumes Apple Silicon.

## Platform notes

### macOS

CoreMIDI gives shared access to ports, so the app can run alongside a DAW and the interface's configuration tool without conflict. Endpoint names come from the device, so a multi-port interface appears as "CME [H12] Port 1" to "Port 8", each with an input and an output half. Unique IDs are stable across reconnects.

### Windows: two MIDI stacks

Windows 11 has two very different MIDI stacks, and the app has to behave well on both.

**Legacy stack** (Windows 10, Windows 11 up to 24H2, and 25H2 until Windows MIDI Services reaches it):

- One application per port. Whoever opens a port first has it; everyone else gets "device already in use". An app that holds every input, as this one does, locks a DAW and the interface's configuration tool out entirely. Hence *Pause listening*, and the typed port-in-use error that gives the rows a clear message.
- Port names are generated rather than taken from the device: "CME [H12]" for the first port, then "MIDIIN2 (CME [H12])", "MIDIOUT2 (CME [H12])" and so on, with a "2- " prefix if the device is plugged into a different USB socket. The app normalises these to "CME [H12] Port n" so inputs and outputs pair up and labels match across platforms.
- Device IDs are list positions that shift when devices appear or disappear. The app never stores them.

**Windows MIDI Services** (Windows 11 25H2 and later, delivered through Windows Update; Microsoft's stated plan at the time of writing is from late November 2026, with preview packages available earlier):

- A Windows service owns the devices. Multiple applications share a port. Existing WinMM applications are routed through the service automatically and gain this without changes, which is why the Windows adapter is written against WinMM rather than the new API.
- Port names come from the device, as on macOS, and are stable.
- The mode is controlled by a `UseLegacyMidi` value under `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Drivers32`: 0 or absent for the new service, 1 for legacy. Microsoft documents a MIDI Settings app and console in a separate tools package.
- Windows 11 24H2 and earlier will not receive it, and older versions need the hardware to meet Windows 11's requirements (TPM 2.0, Secure Boot) to upgrade.

### The CME H12MIDI Pro

- To the computer the H12 is a single USB device with two standard interfaces: an audio-control stub and one USB MIDI streaming interface carrying eight virtual ports. There is no HID or vendor-specific interface.
- Each virtual port is a lane on the USB cable with an input and an output direction, not a physical socket. The H12's internal routing matrix decides which USB host socket or DIN connector feeds which lane, and the computer cannot see that matrix. On the unit used for development the mapping was not one-to-one: ports 2, 3 and 4 carried USB host sockets 1, 2 and 3.
- With default routing an instrument on a USB host socket uses the same lane in both directions, so one label covers input and output. Custom routing can split them; the output test exposes this when the note comes out of a different instrument.
- Because there is no side channel, CME's UxMIDI Tools must configure the device over MIDI SysEx on one of the virtual ports. That makes the routing exchange observable with this app's own monitor while UxMIDI Tools reads the device. The suggested experiment: run both side by side on macOS, trigger a routing read, and note which port carries SysEx and how many bytes. Listen only. If UxMIDI Tools is the browser version, its JavaScript documents the format directly. Asking CME for the SysEx specification is the clean route and worth doing in parallel. Only proceed with protocol observation where it is legally and technically appropriate, and keep any result behind the interface-specific abstraction.

## Verification strategy

- Unit-test decoding, pairing, activity tracking, test verdicts and DAW settings with synthetic messages. All of this runs without hardware.
- Exercise the real adapters where hardware is present: the test suite sends a harmless Note Off to the first available output on macOS.
- Integration-test with the H12 and each connected instrument on every supported platform. Compare the enumerated endpoints with Audio MIDI Setup on macOS and with Windows' own tooling.
- Verify outbound tests always send Note Off, including on failure and on app close.
- Validate the generated settings in current Ableton Live and Cubase.
- Test alongside a DAW on each platform, on both Windows stacks.

## Risks and open questions

- Endpoint names and identifiers differ between platforms and stacks. Pairing and labels are keyed by normalised name to absorb this, but a device with an unexpected naming scheme would need a new normalisation rule.
- Single-client MIDI stacks require the user to pause the app before other software can use the ports. Automatic release on focus loss was considered and rejected as surprising.
- The interface's routing may never be readable. Manual socket entry must remain a first-class path.
- SysEx Device Inquiry is optional and cannot be the only identification mechanism, because support varies.
- MIDI clock volume: handled by batching and the clock filter, but a very large number of ports could still stress the log.

## Definition of a useful first release

A user can launch the app, play each instrument, and see within seconds which port and channel carried the data, whether MIDI is arriving and getting back out, and which Ableton Live or Cubase settings to select. That definition is met.

## Origin

This brief began as a planning discussion before any code existed and has been updated as the project progressed. It is a working document, not a specification.
