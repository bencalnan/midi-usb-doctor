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

    [Theory]
    [InlineData(0x90, 60, 100, 1, "Note On", "C4 (60)  velocity 100")]
    [InlineData(0x91, 60, 0, 2, "Note Off", "C4 (60)  velocity 0")]
    [InlineData(0xB9, 74, 127, 10, "Control Change", "CC 74  value 127")]
    public void Decoder_describes_channel_messages(
        byte status,
        byte data1,
        byte data2,
        int expectedChannel,
        string expectedType,
        string expectedDescription)
    {
        var message = new MidiMessage(
            "test",
            DateTimeOffset.UtcNow,
            new byte[] { status, data1, data2 });

        var decoded = MidiMessageDecoder.Decode(message);

        Assert.Equal(expectedChannel, decoded.Channel);
        Assert.Equal(expectedType, decoded.Type);
        Assert.Equal(expectedDescription, decoded.Description);
    }
}
