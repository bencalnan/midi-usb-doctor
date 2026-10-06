namespace MidiUsbDoctor.App;

public sealed record MidiTrafficRow(
    string EndpointId,
    string Time,
    string Port,
    string Channel,
    string Type,
    string Data);
