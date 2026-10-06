namespace MidiUsbDoctor.Core;

public sealed record MidiMessage(
    string EndpointId,
    DateTimeOffset Timestamp,
    ReadOnlyMemory<byte> Data);
