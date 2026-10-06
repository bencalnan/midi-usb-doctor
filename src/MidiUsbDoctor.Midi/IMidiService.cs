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
}
