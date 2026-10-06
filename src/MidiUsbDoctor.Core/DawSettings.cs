using System.Text;

namespace MidiUsbDoctor.Core;

public sealed record DawRecommendation(
    string Title,
    string AbletonText,
    string CubaseText,
    IReadOnlyList<string> Notes,
    string ClipboardText);

/// <summary>
/// Produces copy-ready Ableton Live and Cubase settings from what has been observed on a
/// port, plus the reasons any default should differ. Pure logic, unit-tested.
/// </summary>
public static class DawSettingsGenerator
{
    /// <param name="outputConfirmed">true = output test heard, false = not heard, null = not tested.</param>
    public static DawRecommendation Generate(
        string portName,
        string? deviceLabel,
        MidiPortActivity activity,
        bool hasOutput,
        bool? outputConfirmed)
    {
        var device = string.IsNullOrWhiteSpace(deviceLabel) ? portName : deviceLabel.Trim();
        var notes = new List<string>();

        string liveChannel;
        string cubaseChannel;
        switch (activity.Channels.Count)
        {
            case 1:
                var channel = activity.Channels.First();
                liveChannel = $"Ch. {channel}";
                cubaseChannel = channel.ToString();
                break;
            case 0:
                liveChannel = "Ch. 1";
                cubaseChannel = "1";
                notes.Add($"No MIDI channel has been seen from {device} yet, so channel 1 is a guess. Play the instrument and open these settings again.");
                break;
            default:
                var list = string.Join(", ", activity.Channels);
                liveChannel = "All Channels";
                cubaseChannel = "Any";
                notes.Add($"{device} sent on channels {list}. Pick the one you want to record, or leave the track on All Channels / Any.");
                break;
        }

        var inputSync = activity.ClockSeen ? "Off (see note)" : "Off";
        var inputRemote = activity.ControlChangesSeen ? "Off (see note)" : "Off";

        var ableton = new StringBuilder();
        ableton.AppendLine("ABLETON LIVE");
        ableton.AppendLine("Preferences > Link, Tempo & MIDI > MIDI Ports");
        ableton.AppendLine($"  Input  \"{portName}\":  Track On   Sync {inputSync}   Remote {inputRemote}");
        if (hasOutput)
        {
            ableton.AppendLine($"  Output \"{portName}\":  Track On   Sync Off   Remote Off");
        }

        ableton.AppendLine("MIDI track");
        ableton.AppendLine($"  MIDI From: {portName}    {liveChannel}");
        if (hasOutput)
        {
            ableton.AppendLine($"  MIDI To:   {portName}    {liveChannel}");
        }

        ableton.AppendLine("  Monitor:   Auto");

        var cubase = new StringBuilder();
        cubase.AppendLine("CUBASE");
        cubase.AppendLine($"Studio > Studio Setup > MIDI Port Setup: \"{portName}\" is Active");
        cubase.AppendLine("MIDI track");
        cubase.AppendLine($"  Input Routing:  {portName}");
        cubase.AppendLine(hasOutput ? $"  Output Routing: {portName}" : "  Output Routing: (none, this port has no output)");
        cubase.AppendLine($"  Channel: {cubaseChannel}");

        if (activity.ClockSeen)
        {
            notes.Add($"Sync: {device} sends MIDI clock. Leave Sync off for its input unless you want {device} to set the DAW's tempo. " +
                      "In Live that is Sync On for the input port; in Cubase it is Transport > Project Synchronization Setup > MIDI Clock source.");
        }
        else if (hasOutput)
        {
            notes.Add($"Sync: to have the DAW drive {device}'s arpeggiator or sequencer, turn Sync On for the output port in Live, " +
                      "or tick the port under Transport > Project Synchronization Setup > MIDI Clock Destinations in Cubase. Otherwise leave it off.");
        }

        if (activity.ControlChangesSeen)
        {
            notes.Add($"Remote: {device} sends control changes. Leave Remote Off to record them into clips as automation. " +
                      "Turn Remote On only if you want to MIDI-map its knobs to Live's own controls, in which case Live will not record them.");
        }

        if (!activity.NotesSeen)
        {
            notes.Add($"No notes have been seen from {device} yet. Track On is still the right setting for recording once it plays.");
        }

        if (!hasOutput)
        {
            notes.Add("This port has no output, so there is nothing to set for MIDI To / Output Routing.");
        }
        else if (outputConfirmed is null)
        {
            notes.Add("Output not tested yet. Run Test output to confirm MIDI To / Output Routing reaches the instrument.");
        }
        else if (outputConfirmed is false)
        {
            notes.Add("The output test was not heard, so MIDI To / Output Routing may not reach the instrument yet. See the output test advice before relying on it.");
        }

        var clipboard = new StringBuilder();
        clipboard.AppendLine($"{device} via {portName}");
        clipboard.AppendLine();
        clipboard.Append(ableton);
        clipboard.AppendLine();
        clipboard.Append(cubase);
        if (notes.Count > 0)
        {
            clipboard.AppendLine();
            clipboard.AppendLine("NOTES");
            foreach (var note in notes)
            {
                clipboard.AppendLine($"- {note}");
            }
        }

        return new DawRecommendation(
            $"DAW settings for {device}",
            ableton.ToString().TrimEnd(),
            cubase.ToString().TrimEnd(),
            notes,
            clipboard.ToString().TrimEnd());
    }
}
