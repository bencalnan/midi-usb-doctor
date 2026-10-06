using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.App;

public sealed record MidiPortPairViewModel(
    int Number,
    string DisplayName,
    MidiEndpoint? Input,
    MidiEndpoint? Output)
{
    public string InputStatus => Input is null ? "Input missing" : "Input ready";
    public string OutputStatus => Output is null ? "Output missing" : "Output ready";
}
