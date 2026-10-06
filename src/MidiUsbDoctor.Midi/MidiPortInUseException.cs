namespace MidiUsbDoctor.Midi;

/// <summary>
/// Thrown when the operating system refuses to open a MIDI port because another
/// application already has it. Common on the legacy Windows MIDI stack, where a port
/// can be held by only one application at a time.
/// </summary>
public sealed class MidiPortInUseException(string endpointId)
    : InvalidOperationException("Another application already has this MIDI port open.")
{
    public string EndpointId { get; } = endpointId;
}
