using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class MidiOutputTestTests
{
    [Fact]
    public void Note_on_and_off_encode_the_channel_in_the_status_byte()
    {
        Assert.Equal(new byte[] { 0x90, 60, 80 }, MidiTestNote.NoteOn(1));
        Assert.Equal(new byte[] { 0x9F, 60, 80 }, MidiTestNote.NoteOn(16));
        Assert.Equal(new byte[] { 0x82, 60, 0 }, MidiTestNote.NoteOff(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public void Channels_outside_1_to_16_are_rejected(int channel) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MidiTestNote.NoteOn(channel));

    [Fact]
    public void Channel_follows_the_single_channel_heard_otherwise_1()
    {
        var one = new MidiPortActivity();
        one.Record(DateTimeOffset.UtcNow, MidiMessageDecoder.Decode(new MidiMessage("in", DateTimeOffset.UtcNow, new byte[] { 0x92, 60, 100 })));

        var two = new MidiPortActivity();
        two.Record(DateTimeOffset.UtcNow, MidiMessageDecoder.Decode(new MidiMessage("in", DateTimeOffset.UtcNow, new byte[] { 0x90, 60, 100 })));
        two.Record(DateTimeOffset.UtcNow, MidiMessageDecoder.Decode(new MidiMessage("in", DateTimeOffset.UtcNow, new byte[] { 0x99, 36, 100 })));

        Assert.Equal(3, MidiTestNote.ChooseChannel(one));
        Assert.Equal(1, MidiTestNote.ChooseChannel(two));
        Assert.Equal(1, MidiTestNote.ChooseChannel(null));
    }

    [Fact]
    public void Sent_asks_the_question()
    {
        var result = OutputTestEvaluator.Describe(OutputTestOutcome.Sent, "CME [H12] Port 2", "Bass Station II", "USB host 1", 3);

        Assert.Equal("Test note sent to Bass Station II", result.Headline);
        Assert.Contains("Did Bass Station II sound?", result.Advice);
        Assert.Contains(new DeviceTestLine("Sent via", "CME [H12] Port 2 (USB host 1)"), result.Summary);
        Assert.Contains(new DeviceTestLine("Test note", "C4 (60)"), result.Summary);
    }

    [Fact]
    public void Heard_gives_the_daw_output_setting()
    {
        var result = OutputTestEvaluator.Describe(OutputTestOutcome.Heard, "CME [H12] Port 2", "Bass Station II", null, 3);

        Assert.Equal("Bass Station II: Output working", result.Headline);
        Assert.Contains("choose CME [H12] Port 2 as the MIDI output", result.Advice);
        Assert.Contains("channel 3", result.Advice);
    }

    [Fact]
    public void Not_heard_points_beyond_the_computer()
    {
        var result = OutputTestEvaluator.Describe(OutputTestOutcome.NotHeard, "CME [H12] Port 2", "Bass Station II", null, 1);

        Assert.Equal("Bass Station II: Output not heard", result.Headline);
        Assert.Contains("beyond the computer", result.Advice);
        Assert.Contains("receives on channel 1", result.Advice);
        Assert.Contains("different socket", result.Advice);
    }

    [Fact]
    public void Failed_includes_the_error()
    {
        var result = OutputTestEvaluator.Describe(OutputTestOutcome.Failed, "CME [H12] Port 2", null, null, 1, error: "Another application already has this MIDI port open.");

        Assert.Equal("CME [H12] Port 2: Could not send", result.Headline);
        Assert.Contains("Another application already has this MIDI port open.", result.Advice);
    }
}
