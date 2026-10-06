using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class DawSettingsGeneratorTests
{
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
    public void Single_channel_device_with_confirmed_output_gets_straightforward_settings()
    {
        var activity = Activity([0x92, 60, 100], [0xB2, 74, 10]);

        var result = DawSettingsGenerator.Generate("CME [H12] Port 2", "Bass Station II", activity, hasOutput: true, outputConfirmed: true);

        Assert.Equal("DAW settings for Bass Station II", result.Title);
        Assert.Contains("MIDI From: CME [H12] Port 2    Ch. 3", result.AbletonText);
        Assert.Contains("MIDI To:   CME [H12] Port 2    Ch. 3", result.AbletonText);
        Assert.Contains("Track On   Sync Off   Remote Off (see note)", result.AbletonText);
        Assert.Contains("Input Routing:  CME [H12] Port 2", result.CubaseText);
        Assert.Contains("Output Routing: CME [H12] Port 2", result.CubaseText);
        Assert.Contains("Channel: 3", result.CubaseText);
        Assert.Contains(result.Notes, note => note.StartsWith("Remote:"));
        Assert.DoesNotContain(result.Notes, note => note.Contains("Output not tested"));
        Assert.StartsWith("Bass Station II via CME [H12] Port 2", result.ClipboardText);
    }

    [Fact]
    public void Clock_sender_gets_sync_warning()
    {
        var activity = Activity([0x90, 60, 100], [0xF8]);

        var result = DawSettingsGenerator.Generate("CME [H12] Port 4", "Roland T-8", activity, hasOutput: true, outputConfirmed: null);

        Assert.Contains("Sync Off (see note)", result.AbletonText);
        Assert.Contains(result.Notes, note => note.Contains("sends MIDI clock") && note.Contains("set the DAW's tempo"));
        Assert.Contains(result.Notes, note => note.Contains("Output not tested"));
    }

    [Fact]
    public void Multiple_channels_fall_back_to_all_channels()
    {
        var activity = Activity([0x90, 60, 100], [0x99, 36, 100]);

        var result = DawSettingsGenerator.Generate("CME [H12] Port 1", null, activity, hasOutput: true, outputConfirmed: false);

        Assert.Contains("All Channels", result.AbletonText);
        Assert.Contains("Channel: Any", result.CubaseText);
        Assert.Contains(result.Notes, note => note.Contains("channels 1, 10"));
        Assert.Contains(result.Notes, note => note.Contains("was not heard"));
    }

    [Fact]
    public void Nothing_observed_yet_says_channel_is_a_guess()
    {
        var result = DawSettingsGenerator.Generate("CME [H12] Port 5", "Volca", new MidiPortActivity(), hasOutput: false, outputConfirmed: null);

        Assert.Contains("Ch. 1", result.AbletonText);
        Assert.DoesNotContain("MIDI To", result.AbletonText);
        Assert.Contains("Output Routing: (none", result.CubaseText);
        Assert.Contains(result.Notes, note => note.Contains("channel 1 is a guess"));
        Assert.Contains(result.Notes, note => note.Contains("no output"));
    }
}
