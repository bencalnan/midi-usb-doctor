using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class MidiPortPairerTests
{
    private static MidiEndpoint Input(string id, string name) =>
        new(id, name, MidiEndpointDirection.Input);

    private static MidiEndpoint Output(string id, string name) =>
        new(id, name, MidiEndpointDirection.Output);

    [Fact]
    public void Pairs_input_and_output_that_share_a_name()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Output("out1", "CME [H12] Port 1"),
            Input("in1", "CME [H12] Port 1"),
        ]);

        var pair = Assert.Single(pairs);
        Assert.Equal("in1", pair.Input?.Id);
        Assert.Equal("out1", pair.Output?.Id);
        Assert.Equal(1, pair.PortNumber);
        Assert.True(pair.IsH12);
    }

    [Fact]
    public void Input_only_device_does_not_shift_other_pairs()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Input("kbd", "Arturia KeyStep"),
            Input("in1", "CME [H12] Port 1"),
            Input("in2", "CME [H12] Port 2"),
            Output("out1", "CME [H12] Port 1"),
            Output("out2", "CME [H12] Port 2"),
        ]);

        Assert.Equal(3, pairs.Count);
        Assert.Equal(("in1", "out1"), (pairs[0].Input?.Id, pairs[0].Output?.Id));
        Assert.Equal(("in2", "out2"), (pairs[1].Input?.Id, pairs[1].Output?.Id));

        var keyboard = pairs[2];
        Assert.Equal("kbd", keyboard.Input?.Id);
        Assert.Null(keyboard.Output);
        Assert.False(keyboard.IsH12);
    }

    [Fact]
    public void H12_ports_come_first_in_numeric_order()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Input("iac", "IAC Driver Bus 1"),
            Input("in10", "CME [H12] Port 10"),
            Input("in2", "CME [H12] Port 2"),
            Input("in1", "CME [H12] Port 1"),
        ]);

        Assert.Equal(
            ["CME [H12] Port 1", "CME [H12] Port 2", "CME [H12] Port 10", "IAC Driver Bus 1"],
            pairs.Select(pair => pair.Name).ToArray());
    }

    [Fact]
    public void Duplicate_names_produce_one_row_per_endpoint()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Input("a", "USB MIDI"),
            Input("b", "USB MIDI"),
            Output("c", "USB MIDI"),
        ]);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(("a", "c"), (pairs[0].Input?.Id, pairs[0].Output?.Id));
        Assert.Equal(("b", null), (pairs[1].Input?.Id, pairs[1].Output?.Id));
    }

    [Fact]
    public void Legacy_windows_names_pair_and_number_like_other_platforms()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Input("in1", "CME [H12]"),
            Input("in2", "MIDIIN2 (CME [H12])"),
            Input("in3", "MIDIIN3 (CME [H12])"),
            Output("out1", "CME [H12]"),
            Output("out2", "MIDIOUT2 (CME [H12])"),
            Output("out3", "MIDIOUT3 (CME [H12])"),
        ]);

        Assert.Equal(3, pairs.Count);
        Assert.Equal(["CME [H12] Port 1", "CME [H12] Port 2", "CME [H12] Port 3"], pairs.Select(p => p.Name).ToArray());
        Assert.Equal(("in1", "out1"), (pairs[0].Input?.Id, pairs[0].Output?.Id));
        Assert.Equal(("in2", "out2"), (pairs[1].Input?.Id, pairs[1].Output?.Id));
        Assert.Equal(("in3", "out3"), (pairs[2].Input?.Id, pairs[2].Output?.Id));
        Assert.All(pairs, pair => Assert.True(pair.IsH12));
        Assert.Equal([1, 2, 3], pairs.Select(p => p.PortNumber).ToArray());
    }

    [Fact]
    public void Windows_instance_prefix_is_ignored()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Input("in", "2- MIDIIN2 (CME [H12])"),
            Output("out", "2- MIDIOUT2 (CME [H12])"),
        ]);

        var pair = Assert.Single(pairs);
        Assert.Equal("CME [H12] Port 2", pair.Name);
        Assert.NotNull(pair.Input);
        Assert.NotNull(pair.Output);
    }

    [Fact]
    public void A_bare_name_without_numbered_siblings_is_left_alone()
    {
        var pairs = MidiPortPairer.Pair([Input("in", "Arturia KeyStep"), Output("out", "Arturia KeyStep")]);

        Assert.Equal("Arturia KeyStep", Assert.Single(pairs).Name);
    }

    [Fact]
    public void Names_are_matched_ignoring_case_and_surrounding_whitespace()
    {
        var pairs = MidiPortPairer.Pair(
        [
            Input("in", "Roland S-1 "),
            Output("out", "roland s-1"),
        ]);

        var pair = Assert.Single(pairs);
        Assert.NotNull(pair.Input);
        Assert.NotNull(pair.Output);
        Assert.Null(pair.PortNumber);
    }
}
