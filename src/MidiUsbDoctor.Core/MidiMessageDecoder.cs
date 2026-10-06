namespace MidiUsbDoctor.Core;

public static class MidiMessageDecoder
{
    private static readonly string[] NoteNames =
        ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    public static DecodedMidiMessage Decode(MidiMessage message)
    {
        var data = message.Data.Span;
        if (data.IsEmpty)
        {
            return new DecodedMidiMessage(null, "Empty", string.Empty, string.Empty);
        }

        var status = data[0];
        var raw = Convert.ToHexString(data);

        if (status < 0x80)
        {
            return new DecodedMidiMessage(null, "Data", raw, raw);
        }

        if (status >= 0xF0)
        {
            return DecodeSystemMessage(status, data, raw);
        }

        var channel = (status & 0x0F) + 1;
        var messageType = status & 0xF0;

        return messageType switch
        {
            0x80 when data.Length >= 3 =>
                new(channel, "Note Off", $"{FormatNote(data[1])}  velocity {data[2]}", raw),
            0x90 when data.Length >= 3 && data[2] == 0 =>
                new(channel, "Note Off", $"{FormatNote(data[1])}  velocity 0", raw),
            0x90 when data.Length >= 3 =>
                new(channel, "Note On", $"{FormatNote(data[1])}  velocity {data[2]}", raw),
            0xA0 when data.Length >= 3 =>
                new(channel, "Poly Pressure", $"{FormatNote(data[1])}  pressure {data[2]}", raw),
            0xB0 when data.Length >= 3 =>
                new(channel, "Control Change", $"CC {data[1]}  value {data[2]}", raw),
            0xC0 when data.Length >= 2 =>
                new(channel, "Program Change", $"Program {data[1]}", raw),
            0xD0 when data.Length >= 2 =>
                new(channel, "Channel Pressure", $"Pressure {data[1]}", raw),
            0xE0 when data.Length >= 3 =>
                new(channel, "Pitch Bend", $"Value {(data[2] << 7) | data[1]}", raw),
            _ => new(channel, "MIDI", raw, raw)
        };
    }

    private static DecodedMidiMessage DecodeSystemMessage(
        byte status,
        ReadOnlySpan<byte> data,
        string raw) => status switch
        {
            0xF0 => new(null, "SysEx", $"{data.Length} bytes", raw),
            0xF1 => new(null, "Time Code", raw, raw),
            0xF2 => new(null, "Song Position", raw, raw),
            0xF3 => new(null, "Song Select", raw, raw),
            0xF6 => new(null, "Tune Request", raw, raw),
            0xF8 => new(null, "Clock", string.Empty, raw),
            0xFA => new(null, "Start", string.Empty, raw),
            0xFB => new(null, "Continue", string.Empty, raw),
            0xFC => new(null, "Stop", string.Empty, raw),
            0xFE => new(null, "Active Sensing", string.Empty, raw),
            0xFF => new(null, "System Reset", string.Empty, raw),
            _ => new(null, "System", raw, raw)
        };

    private static string FormatNote(byte note)
    {
        var octave = (note / 12) - 1;
        return $"{NoteNames[note % 12]}{octave} ({note})";
    }
}

public sealed record DecodedMidiMessage(
    int? Channel,
    string Type,
    string Description,
    string RawData);
