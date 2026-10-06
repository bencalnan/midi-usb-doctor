namespace MidiUsbDoctor.Core;

public sealed record MidiEndpoint(
    string Id,
    string Name,
    MidiEndpointDirection Direction,
    string? Manufacturer = null);

public enum MidiEndpointDirection
{
    Input,
    Output
}
