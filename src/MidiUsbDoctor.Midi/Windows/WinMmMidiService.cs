using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Midi.Windows;

/// <summary>
/// Windows MIDI adapter built on the classic WinMM (multimedia) API.
///
/// WinMM was chosen for the first Windows release because it is available on every
/// supported Windows version, needs no SDK packages beyond P/Invoke, and because
/// Windows MIDI Services (in-box from Windows 11 25H2) transparently serves WinMM clients,
/// including multi-client access. A native Windows MIDI Services adapter can be added
/// behind <see cref="IMidiService"/> later without touching the UI or core.
///
/// Written and compiled on macOS; not yet exercised on Windows hardware.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinMmMidiService : IMidiService
{
    private const int SysExBufferSize = 4096;
    private const int SysExBufferCount = 4;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, uint> _inputDeviceIds = [];
    private readonly Dictionary<string, uint> _outputDeviceIds = [];
    private readonly Dictionary<string, OpenInput> _openInputs = [];
    private readonly NativeMethods.MidiInProc _callback;
    private bool _disposed;

    public WinMmMidiService()
    {
        // Keep one delegate alive for the lifetime of the service so the native side
        // never calls into a collected delegate.
        _callback = OnMidiInMessage;
    }

    public event EventHandler<MidiMessage>? MessageReceived;

    public Task<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var endpoints = new List<MidiEndpoint>();
        var inputIds = new Dictionary<string, uint>();
        var outputIds = new Dictionary<string, uint>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        var inputCount = NativeMethods.midiInGetNumDevs();
        for (uint index = 0; index < inputCount; index++)
        {
            var size = (uint)Marshal.SizeOf<NativeMethods.MidiInCaps>();
            if (NativeMethods.midiInGetDevCapsW(index, out var caps, size) != 0)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(caps.Name) ? $"MIDI Input {index + 1}" : caps.Name.Trim();
            var id = MakeUniqueId("winmm:in:" + name, usedIds);
            endpoints.Add(new MidiEndpoint(id, name, MidiEndpointDirection.Input));
            inputIds[id] = index;
        }

        var outputCount = NativeMethods.midiOutGetNumDevs();
        for (uint index = 0; index < outputCount; index++)
        {
            var size = (uint)Marshal.SizeOf<NativeMethods.MidiOutCaps>();
            if (NativeMethods.midiOutGetDevCapsW(index, out var caps, size) != 0)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(caps.Name) ? $"MIDI Output {index + 1}" : caps.Name.Trim();
            var id = MakeUniqueId("winmm:out:" + name, usedIds);
            endpoints.Add(new MidiEndpoint(id, name, MidiEndpointDirection.Output));
            outputIds[id] = index;
        }

        lock (_sync)
        {
            _inputDeviceIds.Clear();
            foreach (var pair in inputIds)
            {
                _inputDeviceIds[pair.Key] = pair.Value;
            }

            _outputDeviceIds.Clear();
            foreach (var pair in outputIds)
            {
                _outputDeviceIds[pair.Key] = pair.Value;
            }
        }

        return Task.FromResult<IReadOnlyList<MidiEndpoint>>(endpoints);
    }

    public async Task StartMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        bool known;
        lock (_sync)
        {
            known = _inputDeviceIds.ContainsKey(endpointId);
        }

        if (!known)
        {
            await GetEndpointsAsync(cancellationToken);
        }

        lock (_sync)
        {
            if (_openInputs.ContainsKey(endpointId))
            {
                return;
            }

            if (!_inputDeviceIds.TryGetValue(endpointId, out var deviceId))
            {
                throw new ArgumentException("The MIDI input endpoint is no longer available.", nameof(endpointId));
            }

            var input = new OpenInput(endpointId, this);
            var contextHandle = GCHandle.Alloc(input);
            input.ContextHandle = contextHandle;

            var status = NativeMethods.midiInOpen(
                out var handle,
                deviceId,
                _callback,
                GCHandle.ToIntPtr(contextHandle),
                NativeMethods.CallbackFunction);

            if (status != 0)
            {
                contextHandle.Free();
                if (status == NativeMethods.MmsyserrAllocated)
                {
                    throw new MidiPortInUseException(endpointId);
                }

                throw new InvalidOperationException($"Windows could not open the MIDI input: {DescribeError(status)}");
            }

            input.Handle = handle;

            try
            {
                for (var index = 0; index < SysExBufferCount; index++)
                {
                    input.Headers.Add(AddSysExBuffer(handle));
                }

                ThrowForStatus(NativeMethods.midiInStart(handle), "start the MIDI input");
            }
            catch
            {
                CloseInput(input);
                throw;
            }

            _openInputs.Add(endpointId, input);
        }
    }

    public Task StopMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            if (_openInputs.Remove(endpointId, out var input))
            {
                CloseInput(input);
            }
        }

        return Task.CompletedTask;
    }

    public async Task SendAsync(
        string endpointId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (data.IsEmpty)
        {
            return;
        }

        bool known;
        lock (_sync)
        {
            known = _outputDeviceIds.ContainsKey(endpointId);
        }

        if (!known)
        {
            await GetEndpointsAsync(cancellationToken);
        }

        lock (_sync)
        {
            if (!_outputDeviceIds.TryGetValue(endpointId, out var deviceId))
            {
                throw new ArgumentException("The MIDI output endpoint is no longer available.", nameof(endpointId));
            }

            // Open, send, close. On the legacy Windows stack a held output would lock a DAW out
            // of it, and a single short message does not justify keeping the handle.
            var openStatus = NativeMethods.midiOutOpen(out var handle, deviceId, nint.Zero, nint.Zero, 0);
            if (openStatus == NativeMethods.MmsyserrAllocated)
            {
                throw new MidiPortInUseException(endpointId);
            }

            ThrowForStatus(openStatus, "open the MIDI output");

            try
            {
                var bytes = data.Span;
                if (bytes[0] == 0xF0 || bytes.Length > 3)
                {
                    SendLongMessage(handle, bytes);
                }
                else
                {
                    uint packed = bytes[0];
                    if (bytes.Length > 1)
                    {
                        packed |= (uint)bytes[1] << 8;
                    }

                    if (bytes.Length > 2)
                    {
                        packed |= (uint)bytes[2] << 16;
                    }

                    ThrowForStatus(NativeMethods.midiOutShortMsg(handle, packed), "send to the MIDI output");
                }
            }
            finally
            {
                NativeMethods.midiOutClose(handle);
            }
        }
    }

    private static void SendLongMessage(nint handle, ReadOnlySpan<byte> bytes)
    {
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.MidiHeader>();
        var headerPointer = Marshal.AllocHGlobal((int)headerSize);
        var dataPointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes.ToArray(), 0, dataPointer, bytes.Length);
            var header = new NativeMethods.MidiHeader
            {
                Data = dataPointer,
                BufferLength = (uint)bytes.Length,
                BytesRecorded = (uint)bytes.Length,
                ReservedArray = new nint[8],
            };
            Marshal.StructureToPtr(header, headerPointer, false);

            ThrowForStatus(NativeMethods.midiOutPrepareHeader(handle, headerPointer, headerSize), "prepare the outgoing SysEx");
            try
            {
                ThrowForStatus(NativeMethods.midiOutLongMsg(handle, headerPointer, headerSize), "send SysEx to the MIDI output");

                // The driver sets MHDR_DONE when it has finished with the buffer. Short messages
                // complete in well under a millisecond; give slow drivers up to a second.
                var deadline = Environment.TickCount64 + 1000;
                while ((Marshal.PtrToStructure<NativeMethods.MidiHeader>(headerPointer).Flags & NativeMethods.MhdrDone) == 0 &&
                       Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(1);
                }
            }
            finally
            {
                NativeMethods.midiOutUnprepareHeader(handle, headerPointer, headerSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPointer);
            Marshal.FreeHGlobal(headerPointer);
        }
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

            foreach (var input in _openInputs.Values)
            {
                CloseInput(input);
            }

            _openInputs.Clear();
            MessageReceived = null;
        }

        return ValueTask.CompletedTask;
    }

    private static string MakeUniqueId(string baseId, HashSet<string> usedIds)
    {
        var id = baseId;
        var suffix = 2;
        while (!usedIds.Add(id))
        {
            id = $"{baseId}#{suffix++}";
        }

        return id;
    }

    private static nint AddSysExBuffer(nint handle)
    {
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.MidiHeader>();
        var headerPointer = Marshal.AllocHGlobal((int)headerSize);
        var dataPointer = Marshal.AllocHGlobal(SysExBufferSize);

        var header = new NativeMethods.MidiHeader
        {
            Data = dataPointer,
            BufferLength = SysExBufferSize,
            ReservedArray = new nint[8],
        };
        Marshal.StructureToPtr(header, headerPointer, false);

        var status = NativeMethods.midiInPrepareHeader(handle, headerPointer, headerSize);
        if (status == 0)
        {
            status = NativeMethods.midiInAddBuffer(handle, headerPointer, headerSize);
        }

        if (status != 0)
        {
            Marshal.FreeHGlobal(dataPointer);
            Marshal.FreeHGlobal(headerPointer);
            throw new InvalidOperationException($"Windows could not prepare a SysEx buffer: {DescribeError(status)}");
        }

        return headerPointer;
    }

    private static void CloseInput(OpenInput input)
    {
        input.IsClosing = true;

        if (input.Handle != nint.Zero)
        {
            NativeMethods.midiInStop(input.Handle);
            NativeMethods.midiInReset(input.Handle); // returns all pending SysEx buffers

            var headerSize = (uint)Marshal.SizeOf<NativeMethods.MidiHeader>();
            foreach (var headerPointer in input.Headers)
            {
                NativeMethods.midiInUnprepareHeader(input.Handle, headerPointer, headerSize);
                var header = Marshal.PtrToStructure<NativeMethods.MidiHeader>(headerPointer);
                Marshal.FreeHGlobal(header.Data);
                Marshal.FreeHGlobal(headerPointer);
            }

            input.Headers.Clear();
            NativeMethods.midiInClose(input.Handle);
            input.Handle = nint.Zero;
        }

        if (input.ContextHandle.IsAllocated)
        {
            input.ContextHandle.Free();
        }
    }

    // Runs on a WinMM thread. Keep it short and never block.
    private static void OnMidiInMessage(nint handle, uint message, nint instance, nint parameter1, nint parameter2)
    {
        if (instance == nint.Zero || GCHandle.FromIntPtr(instance).Target is not OpenInput input)
        {
            return;
        }

        switch (message)
        {
            case NativeMethods.MimData:
                HandleShortMessage(input, parameter1);
                break;
            case NativeMethods.MimLongData:
                HandleLongMessage(input, handle, parameter1);
                break;
        }
    }

    private static void HandleShortMessage(OpenInput input, nint packed)
    {
        var value = (uint)packed.ToInt64();
        var status = (byte)(value & 0xFF);
        var length = MidiMessageLength.ForStatus(status);
        if (length == 0)
        {
            length = 1;
        }

        var data = new byte[length];
        data[0] = status;
        if (length > 1)
        {
            data[1] = (byte)((value >> 8) & 0x7F);
        }

        if (length > 2)
        {
            data[2] = (byte)((value >> 16) & 0x7F);
        }

        input.Service.MessageReceived?.Invoke(
            input.Service,
            new MidiMessage(input.EndpointId, DateTimeOffset.UtcNow, data));
    }

    private static void HandleLongMessage(OpenInput input, nint handle, nint headerPointer)
    {
        var header = Marshal.PtrToStructure<NativeMethods.MidiHeader>(headerPointer);

        if (header.BytesRecorded > 0)
        {
            var data = new byte[header.BytesRecorded];
            Marshal.Copy(header.Data, data, 0, data.Length);
            input.Service.MessageReceived?.Invoke(
                input.Service,
                new MidiMessage(input.EndpointId, DateTimeOffset.UtcNow, data));
        }

        // midiInReset hands every buffer back through this callback; do not requeue then.
        if (!input.IsClosing)
        {
            NativeMethods.midiInAddBuffer(handle, headerPointer, (uint)Marshal.SizeOf<NativeMethods.MidiHeader>());
        }
    }

    private static void ThrowForStatus(uint status, string operation)
    {
        if (status != 0)
        {
            throw new InvalidOperationException($"Windows could not {operation}: {DescribeError(status)}");
        }
    }

    private static string DescribeError(uint status)
    {
        var text = new StringBuilder(256);
        return NativeMethods.midiInGetErrorTextW(status, text, (uint)text.Capacity) == 0 && text.Length > 0
            ? $"{text} (error {status})"
            : $"error {status}";
    }

    private sealed class OpenInput(string endpointId, WinMmMidiService service)
    {
        public string EndpointId { get; } = endpointId;
        public WinMmMidiService Service { get; } = service;
        public nint Handle { get; set; }
        public GCHandle ContextHandle { get; set; }
        public List<nint> Headers { get; } = [];
        public volatile bool IsClosing;
    }

    private static class NativeMethods
    {
        private const string WinMm = "winmm.dll";

        internal const uint CallbackFunction = 0x00030000;
        internal const uint MmsyserrAllocated = 4; // "The specified device is already in use."
        internal const uint MhdrDone = 0x00000001;
        internal const uint MimData = 0x3C3;
        internal const uint MimLongData = 0x3C4;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void MidiInProc(nint handle, uint message, nint instance, nint parameter1, nint parameter2);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MidiInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Name;
            public uint Support;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MidiOutCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Name;
            public ushort Technology;
            public ushort Voices;
            public ushort Notes;
            public ushort ChannelMask;
            public uint Support;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MidiHeader
        {
            public nint Data;
            public uint BufferLength;
            public uint BytesRecorded;
            public nint User;
            public uint Flags;
            public nint Next;
            public nint Reserved;
            public uint Offset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public nint[] ReservedArray;
        }

        [DllImport(WinMm)]
        internal static extern uint midiInGetNumDevs();

        [DllImport(WinMm, CharSet = CharSet.Unicode)]
        internal static extern uint midiInGetDevCapsW(nuint deviceId, out MidiInCaps caps, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiOutGetNumDevs();

        [DllImport(WinMm, CharSet = CharSet.Unicode)]
        internal static extern uint midiOutGetDevCapsW(nuint deviceId, out MidiOutCaps caps, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiInOpen(out nint handle, uint deviceId, MidiInProc callback, nint instance, uint flags);

        [DllImport(WinMm)]
        internal static extern uint midiInClose(nint handle);

        [DllImport(WinMm)]
        internal static extern uint midiInStart(nint handle);

        [DllImport(WinMm)]
        internal static extern uint midiInStop(nint handle);

        [DllImport(WinMm)]
        internal static extern uint midiInReset(nint handle);

        [DllImport(WinMm)]
        internal static extern uint midiInPrepareHeader(nint handle, nint header, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiInUnprepareHeader(nint handle, nint header, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiInAddBuffer(nint handle, nint header, uint size);

        [DllImport(WinMm, CharSet = CharSet.Unicode)]
        internal static extern uint midiInGetErrorTextW(uint error, StringBuilder text, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiOutOpen(out nint handle, uint deviceId, nint callback, nint instance, uint flags);

        [DllImport(WinMm)]
        internal static extern uint midiOutClose(nint handle);

        [DllImport(WinMm)]
        internal static extern uint midiOutShortMsg(nint handle, uint message);

        [DllImport(WinMm)]
        internal static extern uint midiOutLongMsg(nint handle, nint header, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiOutPrepareHeader(nint handle, nint header, uint size);

        [DllImport(WinMm)]
        internal static extern uint midiOutUnprepareHeader(nint handle, nint header, uint size);
    }
}
