using MidiUsbDoctor.Core;
using MidiUsbDoctor.Midi.MacOS;

namespace MidiUsbDoctor.Tests;

public sealed class CoreMidiServiceTests
{
    [Fact]
    public async Task Endpoint_discovery_completes_without_connected_hardware()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await using var service = new CoreMidiService();

        var endpoints = await service.GetEndpointsAsync();

        Assert.NotNull(endpoints);
    }

    [Fact]
    public async Task Sending_a_note_off_to_the_first_output_is_accepted()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await using var service = new CoreMidiService();
        var output = (await service.GetEndpointsAsync())
            .FirstOrDefault(endpoint => endpoint.Direction == MidiEndpointDirection.Output);
        if (output is null)
        {
            return; // no MIDI output on this machine; nothing to exercise
        }

        // A Note Off for a note that is not sounding is harmless to any instrument.
        await service.SendAsync(output.Id, MidiTestNote.NoteOff(16, 0));
    }

    [Fact]
    public async Task Sending_to_an_unknown_endpoint_fails_clearly()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await using var service = new CoreMidiService();
        await service.GetEndpointsAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendAsync("coremidi:does-not-exist", MidiTestNote.NoteOff(1)));
    }
}
