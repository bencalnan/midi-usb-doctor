using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.Midi;

public interface IMidiService : IAsyncDisposable
{
    event EventHandler<MidiMessage>? MessageReceived;

    Task<IReadOnlyList<MidiEndpoint>> GetEndpointsAsync(
        CancellationToken cancellationToken = default);

    Task StartMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    Task StopMonitoringAsync(
        string endpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one complete MIDI message to an output endpoint. Completion means the operating
    /// system accepted the data, not that the instrument received or acted on it.
    /// </summary>
    Task SendAsync(
        string endpointId,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default);
}
