# MIDI USB Doctor

## Project summary

MIDI USB Doctor is a cross-platform desktop diagnostic tool for musicians using a CME H12MIDI Pro (and potentially other CME MIDI interfaces). It should make MIDI routing problems visible without requiring the user to understand raw MIDI or repeatedly inspect DAW settings. Version 1 will be developed on macOS with Avalonia and should preserve a clear path to Windows support.

The intended experience is:

> Plug everything in, press **Test**, play or adjust each instrument, and receive a clear green/red diagnosis plus the exact Ableton Live or Cubase settings to use.

The first release should answer three practical questions:

1. Which operating-system/CME virtual port is an instrument arriving on?
2. Which MIDI channel is it using?
3. Is valid MIDI data reaching the computer?

## Product principles

- Prefer plain-language diagnoses over MIDI jargon.
- Show live evidence rather than asking users to infer routing from configuration screens.
- Keep generic MIDI monitoring separate from CME-specific behaviour.
- Let users label physical or virtual ports once, because standard MIDI traffic usually does not identify the connected instrument.
- Do not make proprietary CME routing access a dependency of the first release.

## Proposed technology

- .NET 10
- Avalonia desktop UI
- A platform-neutral MIDI abstraction with macOS and Windows implementations
- CoreMIDI-compatible access during macOS development
- Windows MIDI APIs / Windows MIDI Services on Windows
- C# solution split into reusable core logic, platform MIDI adapters, and the Avalonia UI

Suggested solution structure:

```text
MidiUsbDoctor
├── MidiUsbDoctor.Core
│   ├── DeviceDiscovery
│   ├── MidiMonitoring
│   ├── MidiAnalysis
│   ├── Diagnostics
│   └── DawConfiguration
├── MidiUsbDoctor.Midi
│   ├── Abstractions
│   ├── MacOS
│   └── Windows
├── MidiUsbDoctor.Cme
│   ├── PortIdentification
│   └── Routing
├── MidiUsbDoctor.App
│   ├── Dashboard
│   ├── DeviceTest
│   ├── Results
│   └── Settings
└── MidiUsbDoctor.Tests
```

## MVP scope

### Device discovery

- Enumerate all MIDI input and output endpoints visible to the host operating system.
- Identify the H12MIDI Pro virtual input/output ports by their reported names.
- Display endpoint availability and connection state.
- Refresh safely when hardware is connected, disconnected, or restarted.

### Live monitoring

- Listen to all relevant input endpoints simultaneously.
- Show timestamp, source port, MIDI channel, message type, and useful decoded values.
- Decode at least:
  - Note On and Note Off
  - Control Change
  - Program Change
  - Pitch Bend
  - MIDI Clock and transport messages
  - SysEx as raw/summary data
- Provide obvious flashing input/output activity indicators.

Example event:

```text
10:46:23.112
H12 Port 1
NOTE ON
Channel: 1
Note: C3
Velocity: 91
```

### Port labelling

Allow the user to create and persist mappings such as:

```text
H12 Port 1 = Bass Station II
H12 Port 2 = Roland S-1
H12 Port 3 = Roland T-8
```

The app may attempt MIDI Device Inquiry via SysEx later, but manual labels must remain the dependable approach because device-query support varies.

### Diagnostics

For each labelled instrument, report:

- Configured operating-system input and output ports
- Detected MIDI channel or channels
- Whether notes, CC, clock, and SysEx have been observed
- Time of the most recent message
- Input test status
- Output test status, when an active test has been run
- Clear explanation of failures and likely fixes

Example result:

```text
BASS STATION II

Connected via: H12 Port 1
Input test:      Pass
Output test:     Pass
MIDI channel:    1
Notes:           Detected
CC data:         Detected
Clock:           Not detected
```

The diagnostic engine should distinguish between conditions such as:

- The instrument is not transmitting.
- MIDI reaches the CME hardware but not the expected operating-system virtual port.
- MIDI is arriving on a different port or channel than expected.
- Routing appears correct, but the DAW is probably listening to the wrong endpoint.
- The port is unavailable or held exclusively by another application.

### DAW configuration guidance

Generate settings the user can copy into Ableton Live or Cubase based on observed traffic and saved device labels.

Example:

```text
ABLETON LIVE
MIDI From: H12 Port 1
Channel: 1
Track: On
Sync: Off
Remote: Off

CUBASE
Input Routing: H12 Port 1
Output Routing: H12 Port 1
Channel: 1
```

The generated recommendation should explain when Track, Sync, or Remote should differ from the defaults rather than enabling every option.

## Initial UI concept

Use a single dashboard for the first release:

| CME/system port | Device label | Detected channel | Latest traffic | Status |
|---|---|---:|---|---|
| H12 Port 1 | Bass Station II | 1 | Note 54 | Receiving |
| H12 Port 2 | Roland S-1 | 2 | CC 74 | Receiving |
| H12 Port 3 | Roland T-8 | 10 | Clock | Receiving |

Selecting a row should reveal:

- Live message stream
- Input/output path
- Test controls
- Diagnostic findings
- Recommended DAW configuration

## Current status and known gaps

Reviewed 2026-10-06 against the code on `main`; updated the same day after the all-ports monitoring change.

### Verified

- The solution builds with no warnings and all tests pass (5 tests).
- Layering holds: `MidiUsbDoctor.Core` and the Avalonia app do not reference CoreMIDI types. The macOS adapter is hand-written P/Invoke with no third-party MIDI dependency, so a Windows adapter can be added behind `IMidiService` without redesign.
- With the H12 connected the app lists eight `CME [H12] Port n` input/output pairs, each with input and output ready.
- Every input is opened as soon as devices are discovered. Each port row shows an activity dot, the channels heard, the last message and its time, so playing an instrument identifies its port without clicking through ports.
- Ports are paired by name (`MidiPortPairer` in Core, unit-tested), so a device with only an input no longer shifts other rows. H12 ports are listed first in numeric order, other devices after them.
- Selecting a row filters the live log to that port; "Show all ports" clears the filter.
- Messages are queued off the CoreMIDI thread and drained in batches on the UI thread, so bursts of clock or dense traffic do not schedule one UI update per message.
- A "Hide clock" toggle (on by default) keeps MIDI Clock out of the log; port rows still report clock.
- Device labels: selecting a port shows an inline editor for the instrument name and the H12 socket it is plugged into ("USB host 3", "DIN 1"). Both are keyed by port name, stored as JSON under the user's application-data folder (`DeviceLabelStore` in Core, unit-tested), restored at startup, and kept for ports that are not currently connected.
- A Windows adapter (`WinMmMidiService`) and a platform factory exist and compile. They are untested on Windows; see item 10.
- The app icon asset is embedded and loads at startup (see item 9 below for what it does and does not affect).

### Gaps against the MVP scope

1. ~~Only one port is monitored at a time.~~ Resolved 2026-10-06: all inputs are monitored together and each row shows its own activity.
2. ~~Ports are paired by sorted index, not by name.~~ Resolved 2026-10-06: pairing is by name with H12 ports grouped first. Non-H12 devices are still shown, under the same list, rather than hidden.
3. **One message per CoreMIDI packet.** The adapter emits a single `MidiMessage` per packet and the decoder reads only the first status byte. A packet carrying several messages loses all but the first, and SysEx that spans packets shows as "Data" rows rather than one SysEx summary.
4. **Packet stride assumes Apple Silicon.** The 4-byte alignment applied between packets matches the ARM definition of `MIDIPacketNext`. On Intel Macs CoreMIDI does not pad between packets, so multi-packet lists would be misread. Not a problem on the development machine, but it needs a runtime check or a note before Intel is claimed as supported.
5. **No hot-plug.** The CoreMIDI client is created with no notification callback and `IMidiService` has no device-changed event, so connecting or disconnecting hardware requires a manual refresh.
6. **No output path.** `IMidiService` has no send method. Milestone 4's outbound Note/CC tests will need one, plus the matching Note Off cleanup.
10. **Windows adapter is unverified.** `WinMmMidiService` was written and compiled on macOS against the WinMM API (device enumeration, `midiInOpen` with a callback, short messages, SysEx buffers). It has never run on Windows. First run on a PC should check: device names, that the H12 ports pair up by name, that short messages and SysEx arrive, and that closing the app releases the ports. WinMM device IDs are indexes that can change between sessions, which is why labels are keyed by port name.
7. ~~Selecting a port with no input leaves the previous monitor running.~~ Resolved 2026-10-06: selection only filters the log and no longer starts or stops monitoring.
8. **Test coverage is thin** relative to the verification strategy below. Pairing and per-port activity tracking are now covered, but the decoder has three cases and no system messages, there is no fake `IMidiService` for discovery or high-volume tests, and the CoreMIDI test returns silently off macOS instead of being skipped.
9. **App icon.** `Window.Icon` is set from an embedded PNG. This will work on Windows and Linux. On macOS Avalonia implements `Window.Icon` as a no-op and the Dock shows the generic icon when run from `dotnet run`; a Dock icon needs a `.app` bundle with an `.icns` file, which belongs with the packaging work. The embedded PNG is 1254 px and about 1 MB; a 256 or 512 px copy is sufficient.

### Suggested order

The shipping target is Windows; the Mac is the development machine. That favours platform-neutral work and brings the Windows adapter forward.

1. ~~Monitor all inputs at once and pair ports by name.~~ Done 2026-10-06.
2. Fix multi-message packets and multi-packet SysEx in the CoreMIDI adapter, with decoder tests for each. The same packet-splitting logic will be needed by the Windows adapter.
3. ~~Windows `IMidiService` adapter.~~ Written 2026-10-06 on WinMM; needs its first run on a PC (item 10).
4. ~~Milestone 3 (labels and persistence).~~ Done 2026-10-06.
5. Milestone 4: guided input test and plain-language result per labelled device, then outbound tests (needs a send method on `IMidiService` for both adapters).
6. Mac-only items (Intel packet alignment, `.app` bundle and Dock icon) last.

## Feature roadmap

### Now: working foundation

The first technical version is working on macOS:

- Avalonia desktop app running on .NET 10
- Endpoint discovery through CoreMIDI (every endpoint, not yet filtered to the H12)
- Input/output port pairs displayed; with the H12 connected this is the eight `CME [H12] Port n` pairs
- All inputs monitored at once, with per-port activity, detected channels and last message shown in the port list
- Port pairing by endpoint name, H12 ports first
- Device labels per port, saved to disk and restored on launch
- Windows adapter on WinMM, compiled but not yet run on Windows
- Live Note On, Note Off, CC, program change, pitch bend, clock, transport, and SysEx display
- MIDI channel, note, velocity, controller, and value decoding
- Bounded traffic history so busy MIDI streams do not freeze the UI
- Manual device refresh and clear connected/empty states

### Next: names and simple test results

Turn the live monitor into something a musician can understand without reading individual MIDI messages:

1. Let the user give each port a device name, such as `Bass Station II`.
2. Save those names and restore them when the app restarts.
3. Add a **Test this device** action.
4. Ask the user to play notes and move controls for a short period.
5. Display a plain-language result:

```text
Bass Station II

H12 virtual port: 2
Detected channel: 3
Notes received: Yes
Controls received: Yes
Clock received: No
Status: Working
```

### After that: diagnosis

- Explain when no MIDI is arriving.
- Warn when traffic is on a different channel or port than expected.
- Distinguish an inactive instrument from a likely routing problem.
- Show the most recent message and last activity time.
- Add reconnect and device-disconnection handling.

### Later: output tests and DAW help

- Send a safe test note and always send the matching Note Off.
- Test Control Change output.
- Attempt SysEx Device Inquiry where supported.
- Generate recommended Ableton Live settings.
- Generate recommended Cubase settings.
- Provide copy-friendly setup instructions.

### Future investigation

- Determine whether the CME routing matrix, filters, and mappings can be read safely.
- Add Windows MIDI support behind the existing platform-neutral interface.
- Package signed macOS and Windows releases.

## Delivery milestones

### Milestone 1: endpoint discovery — substantially complete

- Create the .NET 10 solution and Avalonia shell on macOS.
- Define the platform-neutral MIDI endpoint and message interfaces.
- Implement the first MIDI adapter for macOS development, without leaking platform types into the core or UI.
- Enumerate MIDI endpoints.
- Identify and group H12 input/output port pairs.
- Handle connection and disconnection events.

**Success:** the app accurately lists the H12 endpoints exposed on macOS, and the UI/core can use a Windows adapter without redesign.

The app currently supports manual refresh. Automatic hot-plug and disconnect notifications remain to be implemented.

### Milestone 2: live MIDI monitor — working

- Open multiple input endpoints.
- Capture, timestamp, and decode messages.
- Display live activity by port and channel.
- Add bounded buffering so sustained MIDI clock or dense traffic cannot freeze the UI.

**Success:** playing each connected synth immediately identifies its system MIDI port, MIDI channel, and message type.

The current version monitors every input at once, shows per-port activity and channels in the port list, and keeps a bounded live message history that can be filtered to one port.

### Milestone 3: labels and profiles — done 2026-10-06

- Let users name instruments and associate them with port pairs.
- Persist the setup locally.
- Restore it while tolerating missing or renamed endpoints.

**Success:** device labels survive an app restart and remain useful after reconnecting the hardware.

Labels are keyed by port name and stored in `device-labels.json` under the application-data folder. Ports that are absent keep their labels. Profiles (named sets of labels) were not needed yet and remain open.

### Milestone 4: guided tests and diagnostics — next

- Add a step-by-step input test.
- Add safe outbound Note/CC tests with explicit port selection.
- Track expected versus observed results.
- Present actionable diagnoses.

**Success:** a user can determine whether a failure is at the instrument, CME/system routing, channel selection, or DAW configuration layer.

### Milestone 5: DAW recommendations — planned

- Generate Ableton Live settings.
- Generate Cubase settings.
- Add copy-friendly summaries and warnings for common misconfiguration.

**Success:** recommendations are derived from observed port/channel data rather than guesses.

### Milestone 6: CME routing awareness — future investigation

- Research how CME's UxMIDI Tools reads the device routing matrix, mappings, and filters.
- Look for a supported API or documented SysEx/USB protocol first.
- Only consider protocol observation or reverse engineering if legally and technically appropriate.
- Keep this work behind a CME-specific abstraction.

**Success:** if feasible, show paths such as `USB Host 1 -> Virtual Port 1` and `Virtual Port 1 -> MIDI Out 1` without requiring manual entry.

This milestone is deliberately outside the MVP because no public CME programming API was identified in the source discussion.

Findings so far (2026-10-06, on macOS): the H12 exposes exactly two USB interfaces, a standard audio-control stub and one USB MIDI streaming interface carrying the eight virtual ports. There is no HID or vendor-specific interface, so UxMIDI Tools must configure the device over MIDI SysEx on one of those ports. That makes the routing exchange observable with this app's own monitor (SysEx rows with clock hidden) while UxMIDI Tools reads the device. Until that is investigated, the socket a device is plugged into is recorded manually in the label editor.

Suggested experiment: run the app and UxMIDI Tools side by side, trigger a routing read in UxMIDI Tools, and note which port carries SysEx and how many bytes. Listen only; do not send. If UxMIDI Tools is the browser version, its JavaScript documents the format directly. Asking CME for the SysEx specification is the clean route and worth doing in parallel.

## Data model sketch

```csharp
public sealed class MidiDeviceTest
{
    public required string DeviceName { get; init; }
    public required string InputPortId { get; init; }
    public string? OutputPortId { get; init; }
    public int? DetectedChannel { get; set; }
    public bool ReceivingNotes { get; set; }
    public bool ReceivingControlChanges { get; set; }
    public bool ReceivingClock { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public MidiTestStatus Status { get; set; }
}
```

The production model should support multiple detected channels and distinguish “not tested” from a genuine failed test.

## Verification strategy

- Unit-test MIDI byte/packet decoding with captured and synthetic messages.
- Unit-test diagnostic rules independently of physical hardware.
- Use fake endpoint adapters for discovery, disconnect, and high-volume traffic tests.
- Integration-test with the H12 and each connected instrument.
- Compare enumerated endpoints with macOS Audio MIDI Setup, Windows MIDI tooling, and CME tooling as each platform adapter is introduced.
- Verify input detection for notes, CC, clock, transport, and SysEx.
- Verify outbound tests use the explicitly selected destination and send Note Off cleanup.
- Validate generated instructions in current Ableton Live and Cubase versions.
- Test with a DAW open at the same time on macOS and Windows, especially when Windows MIDI Services multi-client support is available.

## Risks and open questions

- The exact cross-platform MIDI package strategy needs a small technical spike. The core must not depend directly on either CoreMIDI or Windows MIDI types.
- macOS is the v1 development environment, but Windows behaviour cannot be fully validated without a Windows test environment or CI runner.
- Endpoint names and stable identifiers may differ between macOS and Windows.
- Endpoint names and identifiers may change across drivers, USB ports, or reconnects.
- Older MIDI stacks or drivers may allow only one client to open an endpoint.
- MIDI clock can generate enough traffic to overwhelm an unbounded UI log.
- A successful outbound send confirms the operating system accepted data for the port, not necessarily that the physical instrument received or acted on it.
- Device Inquiry SysEx is optional and cannot be the only identification mechanism.
- CME routing/filter configuration may use an undocumented protocol.
- The H12's USB-host topology and its virtual ports should be verified on the actual target hardware on each supported operating system before hard-coding assumptions.

## Definition of a useful first release

The first release is successful when a user can launch the app, play each synth, and see—in seconds—which H12 virtual port and MIDI channel carried the data, whether valid MIDI is arriving, and which Ableton Live or Cubase input settings to select.

## Source

This brief summarizes the planning discussion in the shared ChatGPT conversation [Build MIDI Dashboard](https://chatgpt.com/share/6ac4fa0f-d960-83ed-9257-43ebc2165ed0). It is a project brief, not a verbatim transcript.
