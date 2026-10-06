namespace MidiUsbDoctor.Core.Cme;

/// <summary>
/// Recognises the virtual ports exposed by the CME H12MIDI Pro. Kept separate from the
/// generic MIDI logic so CME-specific behaviour has one home.
/// </summary>
public static class H12Ports
{
    public static bool IsH12Port(string endpointName) =>
        endpointName.Contains("H12", StringComparison.OrdinalIgnoreCase);
}
