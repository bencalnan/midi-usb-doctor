using System.Runtime.InteropServices;
using System.Text;
using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Midi.MacOS;

public sealed class CoreMidiService : IMidiService
{
    public event EventHandler<MidiMessage>? MessageReceived;

    public Task<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(
                "CoreMIDI endpoint discovery is available only on macOS.");
        }

        var endpoints = new List<MidiEndpoint>();
        AddEndpoints(endpoints, MidiEndpointDirection.Input);
        AddEndpoints(endpoints, MidiEndpointDirection.Output);

        return Task.FromResult<IReadOnlyList<MidiEndpoint>>(endpoints);
    }

    public Task StartMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "CoreMIDI message monitoring will be implemented after endpoint discovery.");

    public Task StopMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        MessageReceived = null;
        return ValueTask.CompletedTask;
    }

    private void RaiseMessageReceived(MidiMessage message) =>
        MessageReceived?.Invoke(this, message);

    private bool _disposed;

    private static void AddEndpoints(
        ICollection<MidiEndpoint> endpoints,
        MidiEndpointDirection direction)
    {
        var count = direction == MidiEndpointDirection.Input
            ? NativeMethods.MIDIGetNumberOfSources()
            : NativeMethods.MIDIGetNumberOfDestinations();

        for (nuint index = 0; index < count; index++)
        {
            var endpointReference = direction == MidiEndpointDirection.Input
                ? NativeMethods.MIDIGetSource(index)
                : NativeMethods.MIDIGetDestination(index);

            if (endpointReference == 0)
            {
                continue;
            }

            var uniqueId = GetIntegerProperty(endpointReference, "uniqueID");
            var name = GetStringProperty(endpointReference, "displayName")
                ?? GetStringProperty(endpointReference, "name")
                ?? $"MIDI {direction} {index + 1}";
            var manufacturer = GetStringProperty(endpointReference, "manufacturer");
            var id = uniqueId is not null
                ? $"coremidi:{uniqueId.Value}"
                : $"coremidi:ref-{endpointReference}";

            endpoints.Add(new MidiEndpoint(id, name, direction, manufacturer));
        }
    }

    private static int? GetIntegerProperty(uint midiObject, string propertyName)
    {
        var property = NativeMethods.CreateString(propertyName);
        try
        {
            return NativeMethods.MIDIObjectGetIntegerProperty(midiObject, property, out var value) == 0
                ? value
                : null;
        }
        finally
        {
            NativeMethods.CFRelease(property);
        }
    }

    private static string? GetStringProperty(uint midiObject, string propertyName)
    {
        var property = NativeMethods.CreateString(propertyName);
        try
        {
            if (NativeMethods.MIDIObjectGetStringProperty(midiObject, property, out var value) != 0 ||
                value == nint.Zero)
            {
                return null;
            }

            try
            {
                var length = NativeMethods.CFStringGetLength(value);
                var capacity = checked((length * 4) + 1);
                var buffer = new byte[capacity];

                return NativeMethods.CFStringGetCString(
                    value,
                    buffer,
                    buffer.Length,
                    NativeMethods.Utf8Encoding)
                    ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))
                    : null;
            }
            finally
            {
                NativeMethods.CFRelease(value);
            }
        }
        finally
        {
            NativeMethods.CFRelease(property);
        }
    }

    private static class NativeMethods
    {
        private const string CoreMidi = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        internal const uint Utf8Encoding = 0x08000100;

        [DllImport(CoreMidi)]
        internal static extern nuint MIDIGetNumberOfSources();

        [DllImport(CoreMidi)]
        internal static extern uint MIDIGetSource(nuint sourceIndex);

        [DllImport(CoreMidi)]
        internal static extern nuint MIDIGetNumberOfDestinations();

        [DllImport(CoreMidi)]
        internal static extern uint MIDIGetDestination(nuint destinationIndex);

        [DllImport(CoreMidi)]
        internal static extern int MIDIObjectGetStringProperty(
            uint midiObject,
            nint propertyId,
            out nint value);

        [DllImport(CoreMidi)]
        internal static extern int MIDIObjectGetIntegerProperty(
            uint midiObject,
            nint propertyId,
            out int value);

        [DllImport(CoreFoundation)]
        private static extern nint CFStringCreateWithCString(
            nint allocator,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
            uint encoding);

        [DllImport(CoreFoundation)]
        internal static extern nint CFStringGetLength(nint value);

        [DllImport(CoreFoundation)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool CFStringGetCString(
            nint value,
            byte[] buffer,
            nint bufferSize,
            uint encoding);

        [DllImport(CoreFoundation)]
        internal static extern void CFRelease(nint value);

        internal static nint CreateString(string value)
        {
            var result = CFStringCreateWithCString(nint.Zero, value, Utf8Encoding);
            return result != nint.Zero
                ? result
                : throw new InvalidOperationException("CoreFoundation could not create a string.");
        }
    }
}
