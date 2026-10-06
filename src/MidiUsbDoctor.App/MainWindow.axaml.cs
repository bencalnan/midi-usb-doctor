using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MidiUsbDoctor.Core;
using MidiUsbDoctor.Midi;
using MidiUsbDoctor.Midi.MacOS;

namespace MidiUsbDoctor.App;

public partial class MainWindow : Window
{
    private readonly IMidiService? _midiService;
    private readonly ObservableCollection<MidiEndpoint> _endpoints = [];

    public MainWindow()
    {
        InitializeComponent();

        EndpointsList.ItemsSource = _endpoints;
        _midiService = OperatingSystem.IsMacOS() ? new CoreMidiService() : null;

        Opened += async (_, _) => await RefreshEndpointsAsync();
        Closed += async (_, _) =>
        {
            if (_midiService is not null)
            {
                await _midiService.DisposeAsync();
            }
        };
    }

    private async void RefreshDevices_OnClick(object? sender, RoutedEventArgs e) =>
        await RefreshEndpointsAsync();

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
            var discoveredEndpoints = await _midiService.GetEndpointsAsync();
            _endpoints.Clear();

            foreach (var endpoint in discoveredEndpoints)
            {
                _endpoints.Add(endpoint);
            }

            var hasEndpoints = _endpoints.Count > 0;
            EmptyEndpointsPanel.IsVisible = !hasEndpoints;
            EndpointsList.IsVisible = hasEndpoints;
            EndpointStatusText.Text = hasEndpoints
                ? $"{_endpoints.Count} endpoint{(_endpoints.Count == 1 ? string.Empty : "s")} available"
                : "No endpoints detected — it is safe to connect the H12 later.";
        }
        catch (Exception exception)
        {
            EmptyEndpointsPanel.IsVisible = true;
            EndpointsList.IsVisible = false;
            EndpointStatusText.Text = $"CoreMIDI discovery failed: {exception.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }
}
