using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class DeviceTestEvaluatorTests
{
    private static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);

    private static MidiPortActivity Activity(params byte[][] messages)
    {
        var activity = new MidiPortActivity();
        foreach (var bytes in messages)
        {
            activity.Record(DateTimeOffset.UtcNow, MidiMessageDecoder.Decode(new MidiMessage("in", DateTimeOffset.UtcNow, bytes)));
        }

        return activity;
    }

    [Fact]
    public void Notes_and_controls_mean_working()
    {
        var activity = Activity([0x92, 60, 100], [0x92, 60, 0], [0xB2, 74, 64]);

        var result = DeviceTestEvaluator.Evaluate(activity, "CME [H12] Port 2", "Bass Station II", "USB host 3", TenSeconds, []);

        Assert.Equal(DeviceTestStatus.Working, result.Status);
        Assert.Equal("Bass Station II: Working", result.Headline);
        Assert.Contains(new DeviceTestLine("Connected via", "CME [H12] Port 2 (USB host 3)"), result.Summary);
        Assert.Contains(new DeviceTestLine("MIDI channel", "3"), result.Summary);
        Assert.Contains(new DeviceTestLine("Notes", "Detected"), result.Summary);
        Assert.Contains(new DeviceTestLine("Controls (CC)", "Detected"), result.Summary);
        Assert.Contains(new DeviceTestLine("Clock", "Not detected"), result.Summary);
        Assert.Contains("choose CME [H12] Port 2 as the MIDI input", result.Advice);
        Assert.Contains("channel 3", result.Advice);
    }

    [Fact]
    public void Notes_without_controls_is_still_working_but_says_so()
    {
        var activity = Activity([0x90, 60, 100]);

        var result = DeviceTestEvaluator.Evaluate(activity, "CME [H12] Port 1", null, null, TenSeconds, []);

        Assert.Equal(DeviceTestStatus.Working, result.Status);
        Assert.Equal("CME [H12] Port 1: Working", result.Headline);
        Assert.Contains("No control changes were seen", result.Advice);
    }

    [Fact]
    public void Multiple_channels_are_called_out()
    {
        var activity = Activity([0x90, 60, 100], [0x99, 36, 100]);

        var result = DeviceTestEvaluator.Evaluate(activity, "CME [H12] Port 3", "Roland T-8", null, TenSeconds, []);

        Assert.Contains(new DeviceTestLine("MIDI channel", "1, 10"), result.Summary);
        Assert.Contains("more than one channel", result.Advice);
    }

    [Fact]
    public void Clock_alone_is_partial()
    {
        var activity = Activity([0xF8], [0xF8], [0xF8]);

        var result = DeviceTestEvaluator.Evaluate(activity, "CME [H12] Port 3", "Roland T-8", null, TenSeconds, []);

        Assert.Equal(DeviceTestStatus.Partial, result.Status);
        Assert.Equal("Roland T-8: Connected, but no notes or controls", result.Headline);
        Assert.Contains("received clock", result.Advice);
        Assert.Contains(new DeviceTestLine("Clock", "Detected"), result.Summary);
    }

    [Fact]
    public void Silence_is_nothing_received_with_basic_checks()
    {
        var result = DeviceTestEvaluator.Evaluate(new MidiPortActivity(), "CME [H12] Port 4", "Roland S-1", null, TenSeconds, []);

        Assert.Equal(DeviceTestStatus.NothingReceived, result.Status);
        Assert.Equal("Roland S-1: Nothing received", result.Headline);
        Assert.Contains("in 10 seconds", result.Advice);
        Assert.Contains("powered on", result.Advice);
        Assert.Contains(new DeviceTestLine("Messages", "0"), result.Summary);
    }

    [Fact]
    public void Silence_while_another_port_played_points_at_that_port()
    {
        var result = DeviceTestEvaluator.Evaluate(
            new MidiPortActivity(), "CME [H12] Port 4", "Roland S-1", "USB host 4", TenSeconds, ["CME [H12] Port 6"]);

        Assert.Equal(DeviceTestStatus.NothingReceived, result.Status);
        Assert.Contains("CME [H12] Port 6 did receive", result.Advice);
        Assert.Contains("different H12 socket", result.Advice);
    }
}
