using MidiUsbDoctor.Midi.MacOS;
using MidiUsbDoctor.Midi.Windows;

namespace MidiUsbDoctor.Midi;

public static class MidiServiceFactory
{
    /// <summary>
    /// Returns the MIDI adapter for the running operating system, or null when none exists.
    /// </summary>
    public static IMidiService? CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return new CoreMidiService();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WinMmMidiService();
        }

        return null;
    }

    public static string PlatformApiName =>
        OperatingSystem.IsMacOS() ? "CoreMIDI"
        : OperatingSystem.IsWindows() ? "Windows MIDI (WinMM)"
        : "no MIDI backend";
}
