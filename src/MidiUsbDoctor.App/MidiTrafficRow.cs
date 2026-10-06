namespace MidiUsbDoctor.App;

public sealed record MidiTrafficRow(
    string Time,
    string Channel,
    string Type,
    string Data);
