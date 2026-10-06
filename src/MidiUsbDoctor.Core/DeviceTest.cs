namespace MidiUsbDoctor.Core;

public enum DeviceTestStatus
{
    /// <summary>Notes or control changes arrived: the instrument reaches the computer.</summary>
    Working,

    /// <summary>Something arrived (clock, SysEx, pressure) but no notes or controls.</summary>
    Partial,

    /// <summary>Nothing at all arrived on the port during the test.</summary>
    NothingReceived,
}

public sealed record DeviceTestLine(string Label, string Value);

public sealed record DeviceTestResult(
    DeviceTestStatus Status,
    string Headline,
    IReadOnlyList<DeviceTestLine> Summary,
    string Advice);

/// <summary>
/// Turns what was heard on one port during a timed test into a plain-language result.
/// Pure logic, no UI or platform dependencies, so it can be unit-tested.
/// </summary>
public static class DeviceTestEvaluator
{
    public static DeviceTestResult Evaluate(
        MidiPortActivity activity,
        string portName,
        string? deviceLabel,
        string? connection,
        TimeSpan duration,
        IReadOnlyCollection<string> otherPortsThatPlayed)
    {
        var device = string.IsNullOrWhiteSpace(deviceLabel) ? portName : deviceLabel.Trim();
        var via = string.IsNullOrWhiteSpace(connection) ? portName : $"{portName} ({connection.Trim()})";
        var channels = activity.Channels.Count == 0 ? "—" : string.Join(", ", activity.Channels);
        var seconds = Math.Max(1, (int)Math.Round(duration.TotalSeconds));

        var status = activity.NotesSeen || activity.ControlChangesSeen
            ? DeviceTestStatus.Working
            : activity.MessageCount > 0
                ? DeviceTestStatus.Partial
                : DeviceTestStatus.NothingReceived;

        var summary = new List<DeviceTestLine>
        {
            new("Connected via", via),
            new("MIDI channel", channels),
            new("Notes", Detected(activity.NotesSeen)),
            new("Controls (CC)", Detected(activity.ControlChangesSeen)),
            new("Clock", Detected(activity.ClockSeen)),
            new("Messages", activity.MessageCount.ToString()),
        };

        var (headline, advice) = status switch
        {
            DeviceTestStatus.Working => (
                $"{device}: Working",
                BuildWorkingAdvice(activity, device, portName)),
            DeviceTestStatus.Partial => (
                $"{device}: Connected, but no notes or controls",
                BuildPartialAdvice(activity, device, portName)),
            _ => (
                $"{device}: Nothing received",
                BuildNothingAdvice(device, portName, seconds, otherPortsThatPlayed)),
        };

        return new DeviceTestResult(status, headline, summary, advice);
    }

    private static string Detected(bool seen) => seen ? "Detected" : "Not detected";

    private static string BuildWorkingAdvice(MidiPortActivity activity, string device, string portName)
    {
        var parts = new List<string>
        {
            $"MIDI from {device} is reaching the computer on {portName}. In your DAW, choose {portName} as the MIDI input for this instrument.",
        };

        if (activity.Channels.Count == 1)
        {
            parts.Add($"It is sending on channel {activity.Channels.First()}.");
        }
        else if (activity.Channels.Count > 1)
        {
            parts.Add($"It sent on more than one channel ({string.Join(", ", activity.Channels)}). If you expected a single channel, check the instrument's MIDI channel setting.");
        }

        if (!activity.ControlChangesSeen)
        {
            parts.Add("No control changes were seen. If you moved a knob, the instrument may not be set to send CC.");
        }

        if (!activity.NotesSeen)
        {
            parts.Add("Controls arrived but no notes did. Play a key to confirm notes as well.");
        }

        if (activity.ClockSeen)
        {
            parts.Add("It also sends MIDI clock. Leave Sync off for this input in the DAW unless you want this instrument to set the tempo.");
        }

        return string.Join(" ", parts);
    }

    private static string BuildPartialAdvice(MidiPortActivity activity, string device, string portName)
    {
        var kinds = new List<string>();
        if (activity.ClockSeen)
        {
            kinds.Add("clock");
        }

        if (activity.SysExSeen)
        {
            kinds.Add("SysEx");
        }

        var what = kinds.Count > 0 ? string.Join(" and ", kinds) : "some MIDI";

        return $"{portName} received {what} from {device}, so the connection works, but no notes or control changes arrived in the test. " +
               "Play some keys or move a control and test again. If that still shows nothing, check the instrument's MIDI transmit settings (local/USB/DIN output, channel).";
    }

    private static string BuildNothingAdvice(string device, string portName, int seconds, IReadOnlyCollection<string> otherPortsThatPlayed)
    {
        if (otherPortsThatPlayed.Count > 0)
        {
            var others = string.Join(", ", otherPortsThatPlayed);
            return $"Nothing arrived on {portName} in {seconds} seconds, but {others} did receive notes or controls during the test. " +
                   $"{device} is probably plugged into a different H12 socket than expected, or the H12 routing sends it to a different port. Select that row to check, and update the label if so.";
        }

        return $"No MIDI arrived on {portName} in {seconds} seconds. Check that {device} is powered on, set to transmit MIDI on the connection you are using (USB or DIN), " +
               "and plugged firmly into the H12. If the H12 shows activity for that socket but this port stays silent, the H12 routing may send it elsewhere. Then test again.";
    }
}
