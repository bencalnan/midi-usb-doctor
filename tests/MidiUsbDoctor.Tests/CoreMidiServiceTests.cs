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
}
