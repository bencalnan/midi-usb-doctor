namespace MidiUsbDoctor.Core;

public static class MidiMessageLength
{
    /// <summary>
    /// Total byte count of a MIDI message that begins with <paramref name="status"/>,
    /// or 0 when the length is not fixed (SysEx, data bytes, undefined status).
    /// </summary>
    public static int ForStatus(byte status) => status switch
    {
        < 0x80 => 0,            // data byte, not a status
        < 0xC0 => 3,            // Note Off, Note On, Poly Pressure, Control Change
        < 0xE0 => 2,            // Program Change, Channel Pressure
        < 0xF0 => 3,            // Pitch Bend
        0xF0 => 0,              // SysEx start: runs until 0xF7
        0xF1 => 2,              // MIDI Time Code quarter frame
        0xF2 => 3,              // Song Position
        0xF3 => 2,              // Song Select
        0xF4 or 0xF5 => 0,      // undefined
        _ => 1                  // Tune Request, SysEx end, realtime
    };
}
