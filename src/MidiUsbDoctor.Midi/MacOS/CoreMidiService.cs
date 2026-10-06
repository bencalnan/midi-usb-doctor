using System.Runtime.InteropServices;
using System.Text;
using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Midi.MacOS;

public sealed class CoreMidiService : IMidiService
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, uint> _endpointReferences = [];
    private readonly Dictionary<string, Connection> _connections = [];
    private uint _client;
    private uint _inputPort;
    private uint _outputPort;
    private bool _disposed;

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

        var discoveredEndpoints = new List<(MidiEndpoint Endpoint, uint Reference)>();
        AddEndpoints(discoveredEndpoints, MidiEndpointDirection.Input);
        AddEndpoints(discoveredEndpoints, MidiEndpointDirection.Output);

        lock (_sync)
        {
            foreach (var item in discoveredEndpoints)
            {
                _endpointReferences[item.Endpoint.Id] = item.Reference;
            }
        }

        return Task.FromResult<IReadOnlyList<MidiEndpoint>>(
            discoveredEndpoints.Select(item => item.Endpoint).ToArray());
    }

    public async Task StartMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("CoreMIDI monitoring is available only on macOS.");
        }

        if (!_endpointReferences.ContainsKey(endpointId))
        {
            await GetEndpointsAsync(cancellationToken);
        }

        lock (_sync)
        {
            if (_connections.ContainsKey(endpointId))
            {
                return;
            }

            if (!_endpointReferences.TryGetValue(endpointId, out var endpointReference))
            {
                throw new ArgumentException("The MIDI input endpoint is no longer available.", nameof(endpointId));
            }

            EnsureClient();
            EnsureInputPort();

            var context = new CallbackContext(this, endpointId);
            var contextHandle = GCHandle.Alloc(context);
            var contextPointer = GCHandle.ToIntPtr(contextHandle);
            var status = NativeMethods.MIDIPortConnectSource(
                _inputPort,
                endpointReference,
                contextPointer);

            if (status != 0)
            {
                contextHandle.Free();
                throw new InvalidOperationException($"CoreMIDI could not connect to the input (status {status}).");
            }

            _connections.Add(endpointId, new Connection(endpointReference, contextHandle));
        }
    }

    public Task StopMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (!_connections.Remove(endpointId, out var connection))
            {
                return Task.CompletedTask;
            }

            if (_inputPort != 0)
            {
                NativeMethods.MIDIPortDisconnectSource(_inputPort, connection.EndpointReference);
            }

            connection.ContextHandle.Free();
        }

        return Task.CompletedTask;
    }

    public Task SendAsync(
        string endpointId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("CoreMIDI output is available only on macOS.");
        }

        if (data.IsEmpty)
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            if (!_endpointReferences.TryGetValue(endpointId, out var destination))
            {
                throw new ArgumentException("The MIDI output endpoint is no longer available.", nameof(endpointId));
            }

            EnsureClient();
            EnsureOutputPort();

            var bytes = data.ToArray();
            var listSize = (nuint)(4 + 8 + 2 + bytes.Length + 64);
            var list = Marshal.AllocHGlobal((int)listSize);
            try
            {
                var packet = NativeMethods.MIDIPacketListInit(list);
                var added = NativeMethods.MIDIPacketListAdd(list, listSize, packet, 0, (nuint)bytes.Length, bytes);
                if (added == nint.Zero)
                {
                    throw new InvalidOperationException("CoreMIDI could not build the outgoing packet.");
                }

                ThrowForStatus(NativeMethods.MIDISend(_outputPort, destination, list), "send to the MIDI output");
            }
            finally
            {
                Marshal.FreeHGlobal(list);
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;

            foreach (var connection in _connections.Values)
            {
                if (_inputPort != 0)
                {
                    NativeMethods.MIDIPortDisconnectSource(_inputPort, connection.EndpointReference);
                }

                connection.ContextHandle.Free();
            }

            _connections.Clear();

            if (_inputPort != 0)
            {
                NativeMethods.MIDIPortDispose(_inputPort);
                _inputPort = 0;
            }

            if (_outputPort != 0)
            {
                NativeMethods.MIDIPortDispose(_outputPort);
                _outputPort = 0;
            }

            if (_client != 0)
            {
                NativeMethods.MIDIClientDispose(_client);
                _client = 0;
            }

            MessageReceived = null;
        }

        return ValueTask.CompletedTask;
    }

    private static void AddEndpoints(
        ICollection<(MidiEndpoint Endpoint, uint Reference)> endpoints,
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

            endpoints.Add((new MidiEndpoint(id, name, direction, manufacturer), endpointReference));
        }
    }

    private void EnsureClient()
    {
        if (_client != 0)
        {
            return;
        }

        var clientName = NativeMethods.CreateString("MIDI USB Doctor");
        try
        {
            var status = NativeMethods.MIDIClientCreate(
                clientName,
                nint.Zero,
                nint.Zero,
                out _client);
            ThrowForStatus(status, "create a CoreMIDI client");
        }
        finally
        {
            NativeMethods.CFRelease(clientName);
        }
    }

    private void EnsureInputPort()
    {
        if (_inputPort != 0)
        {
            return;
        }

        var portName = NativeMethods.CreateString("MIDI USB Doctor Input");
        try
        {
            var status = NativeMethods.MIDIInputPortCreate(
                _client,
                portName,
                NativeMethods.ReadCallback,
                nint.Zero,
                out _inputPort);
            ThrowForStatus(status, "create a CoreMIDI input port");
        }
        finally
        {
            NativeMethods.CFRelease(portName);
        }
    }

    private void EnsureOutputPort()
    {
        if (_outputPort != 0)
        {
            return;
        }

        var portName = NativeMethods.CreateString("MIDI USB Doctor Output");
        try
        {
            var status = NativeMethods.MIDIOutputPortCreate(_client, portName, out _outputPort);
            ThrowForStatus(status, "create a CoreMIDI output port");
        }
        finally
        {
            NativeMethods.CFRelease(portName);
        }
    }

    private static void ThrowForStatus(int status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"CoreMIDI could not {operation} (status {status}).");
        }
    }

    private static void ReceivePackets(
        nint packetList,
        nint readCallbackReference,
        nint sourceConnectionReference)
    {
        if (packetList == nint.Zero || sourceConnectionReference == nint.Zero)
        {
            return;
        }

        var handle = GCHandle.FromIntPtr(sourceConnectionReference);
        if (handle.Target is not CallbackContext context)
        {
            return;
        }

        var packetCount = Marshal.ReadInt32(packetList);

        // CoreMIDI declares MIDIPacketList inside #pragma pack(push, 4), so the
        // first MIDIPacket begins immediately after the UInt32 packet count.
        var packet = nint.Add(packetList, 4);

        for (var index = 0; index < packetCount; index++)
        {
            var length = (ushort)Marshal.ReadInt16(packet, 8);
            if (length > 0)
            {
                var data = new byte[length];
                Marshal.Copy(nint.Add(packet, 10), data, 0, length);
                context.Service.MessageReceived?.Invoke(
                    context.Service,
                    new MidiMessage(context.EndpointId, DateTimeOffset.UtcNow, data));
            }

            var packetSize = (10 + length + 3) & ~3;
            packet = nint.Add(packet, packetSize);
        }
    }

    private sealed record CallbackContext(CoreMidiService Service, string EndpointId);

    private sealed record Connection(uint EndpointReference, GCHandle ContextHandle);

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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void MidiReadProc(
            nint packetList,
            nint readProcReference,
            nint sourceConnectionReference);

        internal static readonly MidiReadProc ReadCallback = ReceivePackets;

        [DllImport(CoreMidi)]
        internal static extern int MIDIClientCreate(
            nint name,
            nint notificationCallback,
            nint notificationReference,
            out uint client);

        [DllImport(CoreMidi)]
        internal static extern int MIDIClientDispose(uint client);

        [DllImport(CoreMidi)]
        internal static extern int MIDIInputPortCreate(
            uint client,
            nint portName,
            MidiReadProc readCallback,
            nint readCallbackReference,
            out uint port);

        [DllImport(CoreMidi)]
        internal static extern int MIDIPortDispose(uint port);

        [DllImport(CoreMidi)]
        internal static extern int MIDIOutputPortCreate(uint client, nint portName, out uint port);

        [DllImport(CoreMidi)]
        internal static extern int MIDISend(uint port, uint destination, nint packetList);

        [DllImport(CoreMidi)]
        internal static extern nint MIDIPacketListInit(nint packetList);

        [DllImport(CoreMidi)]
        internal static extern nint MIDIPacketListAdd(
            nint packetList,
            nuint listSize,
            nint currentPacket,
            ulong timestamp,
            nuint dataLength,
            byte[] data);

        [DllImport(CoreMidi)]
        internal static extern int MIDIPortConnectSource(
            uint port,
            uint source,
            nint sourceConnectionReference);

        [DllImport(CoreMidi)]
        internal static extern int MIDIPortDisconnectSource(uint port, uint source);

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
