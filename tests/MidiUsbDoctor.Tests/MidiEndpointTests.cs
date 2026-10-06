using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class MidiEndpointTests
{
    [Fact]
    public void Endpoint_preserves_platform_identity_and_direction()
    {
        var endpoint = new MidiEndpoint(
            "coremidi:42",
            "H12MIDI Pro Port 1",
            MidiEndpointDirection.Input,
            "CME");

        Assert.Equal("coremidi:42", endpoint.Id);
        Assert.Equal("H12MIDI Pro Port 1", endpoint.Name);
        Assert.Equal(MidiEndpointDirection.Input, endpoint.Direction);
        Assert.Equal("CME", endpoint.Manufacturer);
    }
}
