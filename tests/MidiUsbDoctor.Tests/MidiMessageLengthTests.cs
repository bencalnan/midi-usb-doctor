using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Tests;

public sealed class MidiMessageLengthTests
{
    [Theory]
    [InlineData(0x80, 3)]
    [InlineData(0x9F, 3)]
    [InlineData(0xB0, 3)]
    [InlineData(0xC5, 2)]
    [InlineData(0xD0, 2)]
    [InlineData(0xE0, 3)]
    [InlineData(0xF1, 2)]
    [InlineData(0xF2, 3)]
    [InlineData(0xF3, 2)]
    [InlineData(0xF6, 1)]
    [InlineData(0xF8, 1)]
    [InlineData(0xFE, 1)]
    public void Fixed_length_messages_report_their_size(byte status, int expected) =>
        Assert.Equal(expected, MidiMessageLength.ForStatus(status));

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x7F)]
    [InlineData(0xF0)]
    [InlineData(0xF4)]
    public void Variable_or_invalid_statuses_report_zero(byte status) =>
        Assert.Equal(0, MidiMessageLength.ForStatus(status));
}
