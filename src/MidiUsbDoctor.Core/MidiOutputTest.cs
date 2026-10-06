namespace MidiUsbDoctor.Core;

/// <summary>Builds the messages for a safe outbound test: one short, moderate note.</summary>
public static class MidiTestNote
{
    /// <summary>Middle C.</summary>
    public const byte DefaultNote = 60;

    public const byte DefaultVelocity = 80;

    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(400);

    public static byte[] NoteOn(int channel, byte note = DefaultNote, byte velocity = DefaultVelocity)
    {
        ValidateChannel(channel);
        return [(byte)(0x90 | (channel - 1)), (byte)(note & 0x7F), (byte)(velocity & 0x7F)];
    }

    public static byte[] NoteOff(int channel, byte note = DefaultNote)
    {
        ValidateChannel(channel);
        return [(byte)(0x80 | (channel - 1)), (byte)(note & 0x7F), 0];
    }

    /// <summary>
    /// Instruments usually receive on the channel they send on, so if exactly one channel has
    /// been heard from this port, use it. Otherwise fall back to channel 1.
    /// </summary>
    public static int ChooseChannel(MidiPortActivity? activity) =>
        activity is { Channels.Count: 1 } ? activity.Channels.First() : 1;

    private static void ValidateChannel(int channel)
    {
        if (channel is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "MIDI channels run from 1 to 16.");
        }
    }
}

public enum OutputTestOutcome
{
    /// <summary>The note went out and the operating system accepted it; waiting for the user to say whether the instrument sounded.</summary>
    Sent,

    /// <summary>The user heard the instrument.</summary>
    Heard,

    /// <summary>The user heard nothing.</summary>
    NotHeard,

    /// <summary>The send itself failed.</summary>
    Failed,
}

public sealed record OutputTestResult(
    OutputTestOutcome Outcome,
    string Headline,
    IReadOnlyList<DeviceTestLine> Summary,
    string Advice);

/// <summary>Plain-language wording for each stage of the output test. Pure logic, unit-tested.</summary>
public static class OutputTestEvaluator
{
    public static OutputTestResult Describe(
        OutputTestOutcome outcome,
        string portName,
        string? deviceLabel,
        string? connection,
        int channel,
        byte note = MidiTestNote.DefaultNote,
        string? error = null)
    {
        var device = string.IsNullOrWhiteSpace(deviceLabel) ? portName : deviceLabel.Trim();
        var via = string.IsNullOrWhiteSpace(connection) ? portName : $"{portName} ({connection.Trim()})";

        var summary = new List<DeviceTestLine>
        {
            new("Sent via", via),
            new("MIDI channel", channel.ToString()),
            new("Test note", FormatNote(note)),
        };

        var (headline, advice) = outcome switch
        {
            OutputTestOutcome.Sent => (
                $"Test note sent to {device}",
                $"A short note went out on {portName}, channel {channel}, and the computer accepted it. Did {device} sound?"),
            OutputTestOutcome.Heard => (
                $"{device}: Output working",
                $"The computer can play {device} through {portName}. In your DAW, choose {portName} as the MIDI output (MIDI To) for this instrument, channel {channel}."),
            OutputTestOutcome.NotHeard => (
                $"{device}: Output not heard",
                $"The computer sent the note on {portName} and the system accepted it, so the break is beyond the computer. " +
                $"Check that {device} receives on channel {channel} (we used the channel it sends on, or channel 1), that its volume and local settings allow it to sound from MIDI, and the cable or socket. " +
                $"If a different instrument sounded, the H12 routes this port's output to a different socket than its input."),
            _ => (
                $"{device}: Could not send",
                $"The computer could not send on {portName}. {error ?? "Unknown error."} Another application may hold the output, or the port may have gone. Press Refresh devices and try again."),
        };

        return new OutputTestResult(outcome, headline, summary, advice);
    }

    private static string FormatNote(byte note)
    {
        string[] names = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];
        return $"{names[note % 12]}{(note / 12) - 1} ({note})";
    }
}
