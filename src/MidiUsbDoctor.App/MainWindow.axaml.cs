using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MidiUsbDoctor.Core;
using MidiUsbDoctor.Midi;
using MidiUsbDoctor.Midi.MacOS;

namespace MidiUsbDoctor.App;

public partial class MainWindow : Window
{
    private const int MaximumTrafficRows = 250;
    private static readonly Regex TrailingNumber = new(@"(\d+)(?!.*\d)", RegexOptions.Compiled);

    private readonly IMidiService? _midiService;
    private readonly ObservableCollection<MidiPortPairViewModel> _ports = [];
    private readonly ObservableCollection<MidiTrafficRow> _traffic = [];
    private string? _monitoredEndpointId;

    public MainWindow()
    {
        InitializeComponent();

        PortsList.ItemsSource = _ports;
        TrafficList.ItemsSource = _traffic;
        _midiService = OperatingSystem.IsMacOS() ? new CoreMidiService() : null;

        if (_midiService is not null)
        {
            _midiService.MessageReceived += MidiService_OnMessageReceived;
        }

        Opened += async (_, _) => await RefreshEndpointsAsync();
        Closed += async (_, _) =>
        {
            if (_midiService is not null)
            {
                _midiService.MessageReceived -= MidiService_OnMessageReceived;
                await _midiService.DisposeAsync();
            }
        };
    }

    private async void RefreshDevices_OnClick(object? sender, RoutedEventArgs e) =>
        await RefreshEndpointsAsync();

    private void PortsList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selectedPort = PortsList.SelectedItem as MidiPortPairViewModel;
        MonitorButton.IsEnabled = selectedPort?.Input is not null;
        MonitorButton.Content = selectedPort?.Input is null
            ? "This port has no input"
            : selectedPort.Input.Id == _monitoredEndpointId
                ? "Stop monitoring"
                : $"Monitor {selectedPort.DisplayName}";
    }

    private async void MonitorButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_midiService is null ||
            PortsList.SelectedItem is not MidiPortPairViewModel selectedPort ||
            selectedPort.Input is null)
        {
            return;
        }

        MonitorButton.IsEnabled = false;

        try
        {
            if (_monitoredEndpointId == selectedPort.Input.Id)
            {
                await StopMonitoringAsync();
                return;
            }

            await StopMonitoringAsync();
            _traffic.Clear();
            TrafficList.IsVisible = false;
            EmptyTrafficText.IsVisible = true;

            await _midiService.StartMonitoringAsync(selectedPort.Input.Id);
            _monitoredEndpointId = selectedPort.Input.Id;
            MonitorButton.Content = "Stop monitoring";
            MonitorStatusText.Text = "Listening";
            TrafficHelpText.Text = $"Listening to {selectedPort.Input.Name}.";
            ActivityIndicator.Background = Brush.Parse("#D29922");
        }
        catch (Exception exception)
        {
            MonitorStatusText.Text = "Error";
            TrafficHelpText.Text = exception.Message;
            ActivityIndicator.Background = Brush.Parse("#F85149");
        }
        finally
        {
            MonitorButton.IsEnabled = selectedPort.Input is not null;
        }
    }

    private async Task RefreshEndpointsAsync()
    {
        if (_midiService is null)
        {
            EndpointStatusText.Text = "CoreMIDI discovery is available on macOS.";
            RefreshButton.IsEnabled = false;
            return;
        }

        RefreshButton.IsEnabled = false;
        EndpointStatusText.Text = "Checking CoreMIDI…";

        try
        {
            await StopMonitoringAsync();
            var discoveredEndpoints = await _midiService.GetEndpointsAsync();
            var pairedPorts = PairEndpoints(discoveredEndpoints);
            _ports.Clear();

            foreach (var port in pairedPorts)
            {
                _ports.Add(port);
            }

            var hasEndpoints = _ports.Count > 0;
            EmptyEndpointsPanel.IsVisible = !hasEndpoints;
            PortsList.IsVisible = hasEndpoints;
            EndpointStatusText.Text = hasEndpoints
                ? $"{_ports.Count} port pair{(_ports.Count == 1 ? string.Empty : "s")} available"
                : "No endpoints detected — it is safe to connect the H12 later.";
        }
        catch (Exception exception)
        {
            EmptyEndpointsPanel.IsVisible = true;
            PortsList.IsVisible = false;
            EndpointStatusText.Text = $"CoreMIDI discovery failed: {exception.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task StopMonitoringAsync()
    {
        if (_midiService is not null && _monitoredEndpointId is not null)
        {
            await _midiService.StopMonitoringAsync(_monitoredEndpointId);
        }

        _monitoredEndpointId = null;
        MonitorStatusText.Text = "Waiting";
        TrafficHelpText.Text = "Select a port, then start monitoring.";
        ActivityIndicator.Background = Brush.Parse("#484F58");

        if (PortsList.SelectedItem is MidiPortPairViewModel selectedPort)
        {
            MonitorButton.Content = selectedPort.Input is null
                ? "This port has no input"
                : $"Monitor {selectedPort.DisplayName}";
        }
    }

    private void MidiService_OnMessageReceived(object? sender, MidiMessage message)
    {
        var decoded = MidiMessageDecoder.Decode(message);
        Dispatcher.UIThread.Post(() =>
        {
            var description = string.IsNullOrWhiteSpace(decoded.Description)
                ? decoded.RawData
                : decoded.Description;

            _traffic.Insert(0, new MidiTrafficRow(
                message.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
                decoded.Channel?.ToString() ?? "—",
                decoded.Type,
                description));

            while (_traffic.Count > MaximumTrafficRows)
            {
                _traffic.RemoveAt(_traffic.Count - 1);
            }

            EmptyTrafficText.IsVisible = false;
            TrafficList.IsVisible = true;
            MonitorStatusText.Text = "Receiving";
            ActivityIndicator.Background = Brush.Parse("#3FB950");
        });
    }

    private static MidiPortPairViewModel[] PairEndpoints(
        System.Collections.Generic.IReadOnlyList<MidiEndpoint> endpoints)
    {
        var inputs = endpoints
            .Where(endpoint => endpoint.Direction == MidiEndpointDirection.Input)
            .OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var outputs = endpoints
            .Where(endpoint => endpoint.Direction == MidiEndpointDirection.Output)
            .OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pairCount = Math.Max(inputs.Length, outputs.Length);
        var pairs = new MidiPortPairViewModel[pairCount];

        for (var index = 0; index < pairCount; index++)
        {
            var input = index < inputs.Length ? inputs[index] : null;
            var output = index < outputs.Length ? outputs[index] : null;
            var name = input?.Name ?? output?.Name ?? $"MIDI Port {index + 1}";
            var numberMatch = TrailingNumber.Match(name);
            var number = numberMatch.Success && int.TryParse(numberMatch.Value, out var parsedNumber)
                ? parsedNumber
                : index + 1;

            pairs[index] = new MidiPortPairViewModel(
                number,
                $"Port {number} — {name}",
                input,
                output);
        }

        return pairs.OrderBy(pair => pair.Number).ToArray();
    }
}
