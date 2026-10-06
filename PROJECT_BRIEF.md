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

## Feature roadmap

### Now: working foundation

The first technical version is working on macOS:

- Avalonia desktop app running on .NET 10
- H12 discovery through CoreMIDI
- Eight virtual input/output port pairs displayed
- Automatic monitoring when a port is selected
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

The current version monitors the selected input automatically and displays a bounded live message history.

### Milestone 3: labels and profiles — next

- Let users name instruments and associate them with port pairs.
- Persist the setup locally.
- Restore it while tolerating missing or renamed endpoints.

**Success:** device labels survive an app restart and remain useful after reconnecting the hardware.

### Milestone 4: guided tests and diagnostics — planned

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
