namespace MidiUsbDoctor.Core;

/// <summary>
/// Accumulates what has been seen on one input: channels, message kinds, last message.
/// This is the raw material for the per-port status row and, later, diagnostics.
/// Not thread-safe; callers should record from a single thread.
/// </summary>
public sealed class MidiPortActivity
{
    private readonly SortedSet<int> _channels = [];

    public IReadOnlyCollection<int> Channels => _channels;
    public int MessageCount { get; private set; }
    public DateTimeOffset? LastSeen { get; private set; }
    public DecodedMidiMessage? LastMessage { get; private set; }
    public bool NotesSeen { get; private set; }
    public bool ControlChangesSeen { get; private set; }
    public bool ClockSeen { get; private set; }
    public bool SysExSeen { get; private set; }

    public void Record(DateTimeOffset timestamp, DecodedMidiMessage message)
    {
        MessageCount++;
        LastSeen = timestamp;
        LastMessage = message;

        if (message.Channel is { } channel)
        {
            _channels.Add(channel);
        }

        switch (message.Type)
        {
            case "Note On":
            case "Note Off":
                NotesSeen = true;
                break;
            case "Control Change":
                ControlChangesSeen = true;
                break;
            case "Clock":
                ClockSeen = true;
                break;
            case "SysEx":
                SysExSeen = true;
                break;
        }
    }

    public void Reset()
    {
        _channels.Clear();
        MessageCount = 0;
        LastSeen = null;
        LastMessage = null;
        NotesSeen = false;
        ControlChangesSeen = false;
        ClockSeen = false;
        SysExSeen = false;
    }
}
