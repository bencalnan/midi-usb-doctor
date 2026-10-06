using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class MidiPortActivityTests
{
    private static DecodedMidiMessage Decode(params byte[] bytes) =>
        MidiMessageDecoder.Decode(new MidiMessage("in", DateTimeOffset.UtcNow, bytes));

    [Fact]
    public void Tracks_channels_message_kinds_and_last_message()
    {
        var activity = new MidiPortActivity();
        var first = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);

        activity.Record(first, Decode(0x90, 60, 100));            // Note On, channel 1
        activity.Record(first.AddSeconds(1), Decode(0xB9, 74, 1)); // CC, channel 10
        activity.Record(first.AddSeconds(2), Decode(0xF8));        // Clock, no channel

        Assert.Equal([1, 10], activity.Channels);
        Assert.Equal(3, activity.MessageCount);
        Assert.True(activity.NotesSeen);
        Assert.True(activity.ControlChangesSeen);
        Assert.True(activity.ClockSeen);
        Assert.False(activity.SysExSeen);
        Assert.Equal(first.AddSeconds(2), activity.LastSeen);
        Assert.Equal("Clock", activity.LastMessage?.Type);
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var activity = new MidiPortActivity();
        activity.Record(DateTimeOffset.UtcNow, Decode(0xF0, 0x7E, 0xF7));

        activity.Reset();

        Assert.Empty(activity.Channels);
        Assert.Equal(0, activity.MessageCount);
        Assert.Null(activity.LastSeen);
        Assert.Null(activity.LastMessage);
        Assert.False(activity.SysExSeen);
    }
}
