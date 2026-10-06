using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MidiUsbDoctor.Core;
using MidiUsbDoctor.Midi;

namespace MidiUsbDoctor.App;

public partial class MainWindow : Window
{
    private const int MaximumVisibleRows = 250;
    private const int MaximumHistoryRows = 1000;
    private static readonly TimeSpan ActivityFlash = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(10);
    private static readonly IBrush IdleBrush = Brush.Parse("#484F58");
    private static readonly IBrush ListeningBrush = Brush.Parse("#D29922");
    private static readonly IBrush ReceivingBrush = Brush.Parse("#3FB950");
    private static readonly IBrush ErrorBrush = Brush.Parse("#F85149");
    private static readonly IBrush TextBrush = Brush.Parse("#F0F6FC");

    private readonly IMidiService? _midiService;
    private readonly DeviceLabelStore _labels;
    private readonly ObservableCollection<MidiPortPairViewModel> _ports = [];
    private readonly ObservableCollection<MidiTrafficRow> _traffic = [];
    private readonly ObservableCollection<DeviceTestLine> _testSummary = [];
    private readonly List<MidiTrafficRow> _history = [];
    private readonly Dictionary<string, MidiPortPairViewModel> _portsByInputId = [];
    private readonly List<string> _monitoredInputIds = [];
    private readonly ConcurrentQueue<MidiMessage> _pending = new();
    private readonly DispatcherTimer _activityTimer;
    private int _drainScheduled;
    private DateTimeOffset _lastMessageAt;
    private MidiPortPairViewModel? _filter;
    private bool _hideClock = true;
    private bool _isPaused;

    // Device test state. A test listens to one row for a fixed window and then explains what it heard.
    private MidiPortPairViewModel? _testRow;
    private MidiPortActivity? _testActivity;
    private readonly SortedSet<string> _testOtherPortsThatPlayed = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _testStartedAt;
    private int _testSecondsShown = -1;

    public MainWindow()
    {
        InitializeComponent();

        PortsList.ItemsSource = _ports;
        TrafficList.ItemsSource = _traffic;
        TestSummaryList.ItemsSource = _testSummary;
        _labels = DeviceLabelStore.LoadFrom(DeviceLabelStore.DefaultFilePath);
        _midiService = MidiServiceFactory.CreateForCurrentPlatform();

        if (_midiService is not null)
        {
            _midiService.MessageReceived += MidiService_OnMessageReceived;
        }

        _activityTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            ActivityTimer_OnTick);

        Opened += async (_, _) =>
        {
            _activityTimer.Start();
            await RefreshEndpointsAsync();
        };
        Closed += async (_, _) =>
        {
            _activityTimer.Stop();
            if (_midiService is not null)
            {
                _midiService.MessageReceived -= MidiService_OnMessageReceived;
                await _midiService.DisposeAsync();
            }
        };
    }

    private async void RefreshDevices_OnClick(object? sender, RoutedEventArgs e) =>
        await RefreshEndpointsAsync();

    private async void PauseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        PauseButton.IsEnabled = false;
        try
        {
            if (_isPaused)
            {
                await ResumeListeningAsync();
            }
            else
            {
                await PauseListeningAsync();
            }
        }
        finally
        {
            PauseButton.IsEnabled = _midiService is not null && _ports.Count > 0;
        }
    }

    /// <summary>Releases every port so another application (a DAW, UxMIDI Tools) can open them.</summary>
    private async Task PauseListeningAsync()
    {
        CancelTest();
        await StopAllMonitoringAsync();
        _isPaused = true;

        foreach (var row in _ports)
        {
            row.SetListening(false);
        }

        PauseButton.Content = "Resume listening";
        EndpointStatusText.Text = $"{DescribePortCounts()} · paused, ports released";
        SetMonitorState("Paused", IdleBrush);
        UpdateTrafficHelpText();
        UpdateTestButton();
    }

    private async Task ResumeListeningAsync()
    {
        _isPaused = false;
        foreach (var row in _ports)
        {
            row.SetListening(true);
        }

        var listening = await StartMonitoringAllAsync();
        PauseButton.Content = "Pause listening";
        EndpointStatusText.Text = DescribePorts(listening);
        SetMonitorState(listening > 0 ? "Listening" : "Waiting", listening > 0 ? ListeningBrush : IdleBrush);
        UpdateTrafficHelpText();
        UpdateTestButton();
    }

    private void ShowAll_OnClick(object? sender, RoutedEventArgs e) =>
        PortsList.SelectedItem = null;

    private void HideClockToggle_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _hideClock = HideClockToggle.IsChecked == true;
        RebuildVisibleTraffic();
        UpdateTrafficHelpText();
    }

    private void PortsList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var previous = _filter;
        _filter = PortsList.SelectedItem as MidiPortPairViewModel;
        if (_testRow is not null && !ReferenceEquals(_testRow, _filter) && previous is not null)
        {
            CancelTest();
        }

        RebuildVisibleTraffic();
        UpdateTrafficHelpText();
        ShowAllButton.IsEnabled = _filter is not null;
        ShowAllButton.Content = _filter is null ? "Showing all ports" : "Show all ports";
        UpdateLabelEditor();
        UpdateTestButton();
    }

    private void TestButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_testRow is not null)
        {
            FinishTest();
        }
        else if (_filter?.Input is not null)
        {
            StartTest(_filter);
        }
    }

    private void TestDismiss_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_testRow is not null)
        {
            CancelTest();
        }

        TestCard.IsVisible = false;
    }

    private void StartTest(MidiPortPairViewModel row)
    {
        _testRow = row;
        _testActivity = new MidiPortActivity();
        _testOtherPortsThatPlayed.Clear();
        _testStartedAt = DateTimeOffset.UtcNow;
        _testSecondsShown = -1;

        _testSummary.Clear();
        TestHeadline.Foreground = TextBrush;
        TestHeadline.Text = $"Testing {row.Title}…";
        TestAdviceText.Text = string.Empty;
        TestCard.IsVisible = true;
        UpdateTestCountdown(DateTimeOffset.UtcNow);
        UpdateTestButton();
    }

    private void CancelTest()
    {
        _testRow = null;
        _testActivity = null;
        TestCard.IsVisible = false;
        UpdateTestButton();
    }

    private void FinishTest()
    {
        if (_testRow is null || _testActivity is null)
        {
            return;
        }

        var row = _testRow;
        var result = DeviceTestEvaluator.Evaluate(
            _testActivity,
            row.Pair.Name,
            row.Label,
            row.Connection,
            DateTimeOffset.UtcNow - _testStartedAt,
            _testOtherPortsThatPlayed);

        _testRow = null;
        _testActivity = null;

        TestHeadline.Text = result.Headline;
        TestHeadline.Foreground = result.Status switch
        {
            DeviceTestStatus.Working => ReceivingBrush,
            DeviceTestStatus.Partial => ListeningBrush,
            _ => ErrorBrush,
        };

        _testSummary.Clear();
        foreach (var line in result.Summary)
        {
            _testSummary.Add(line);
        }

        TestAdviceText.Text = result.Advice;
        TestCard.IsVisible = true;
        UpdateTestButton();
    }

    private void UpdateTestCountdown(DateTimeOffset now)
    {
        var remaining = TestDuration - (now - _testStartedAt);
        if (remaining <= TimeSpan.Zero)
        {
            FinishTest();
            return;
        }

        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        if (seconds == _testSecondsShown)
        {
            return;
        }

        _testSecondsShown = seconds;
        var heard = _testActivity?.MessageCount ?? 0;
        TestAdviceText.Text = heard == 0
            ? $"Play a few notes and move a control on the instrument. {seconds}s left."
            : $"Hearing it. Keep playing, and move a control too. {seconds}s left.";
    }

    private void UpdateTestButton()
    {
        if (_testRow is not null)
        {
            TestButton.Content = "Finish test now";
            TestButton.IsEnabled = true;
            return;
        }

        TestButton.Content = "Test this device";
        TestButton.IsEnabled = _filter?.Input is not null && _monitoredInputIds.Count > 0;
    }

    private void SaveLabel_OnClick(object? sender, RoutedEventArgs e) => SaveDetails();

    private void ClearLabel_OnClick(object? sender, RoutedEventArgs e)
    {
        LabelTextBox.Text = string.Empty;
        ConnectionTextBox.Text = string.Empty;
        SaveDetails();
    }

    private void LabelTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveDetails();
            e.Handled = true;
        }
    }

    private void UpdateLabelEditor()
    {
        LabelEditor.IsVisible = _filter is not null;
        if (_filter is null)
        {
            return;
        }

        LabelEditorTitle.Text = $"What is on {_filter.DisplayName}?";
        LabelTextBox.Text = _filter.Label ?? string.Empty;
        ConnectionTextBox.Text = _filter.Connection ?? string.Empty;
        LabelStatusText.Text = _filter.HasSubtitle
            ? "Saved with this port. The app cannot read the H12 routing, so the socket is what you tell it."
            : "Name the instrument and note which H12 socket it is plugged into. The app cannot read that from the H12 itself.";
    }

    private void SaveDetails()
    {
        if (_filter is null)
        {
            return;
        }

        var info = DevicePortInfo.Normalize(LabelTextBox.Text, ConnectionTextBox.Text);
        _filter.Label = info.Label;
        _filter.Connection = info.Connection;
        _labels.Set(_filter.Pair.Name, info);

        try
        {
            _labels.Save();
            LabelStatusText.Text = info.IsEmpty
                ? "Cleared."
                : $"Saved. {_filter.Pair.Name} will show as {_filter.Title}{(info.Connection is null ? string.Empty : $" on {info.Connection}")}.";
        }
        catch (Exception exception)
        {
            LabelStatusText.Text = $"Could not save: {exception.Message}";
        }

        UpdateTrafficHelpText();
    }

    private async Task RefreshEndpointsAsync()
    {
        if (_midiService is null)
        {
            EndpointStatusText.Text = "MIDI is not supported on this operating system yet.";
            RefreshButton.IsEnabled = false;
            return;
        }

        RefreshButton.IsEnabled = false;
        EndpointStatusText.Text = $"Checking {MidiServiceFactory.PlatformApiName}…";

        try
        {
            await StopAllMonitoringAsync();

            var endpoints = await _midiService.GetEndpointsAsync();
            var pairs = MidiPortPairer.Pair(endpoints);

            PortsList.SelectedItem = null;
            _ports.Clear();
            _portsByInputId.Clear();
            _history.Clear();
            _traffic.Clear();

            foreach (var pair in pairs)
            {
                var saved = _labels.Get(pair.Name);
                var row = new MidiPortPairViewModel(pair) { Label = saved?.Label, Connection = saved?.Connection };
                _ports.Add(row);
                if (pair.Input is not null)
                {
                    _portsByInputId[pair.Input.Id] = row;
                }
            }

            var hasPorts = _ports.Count > 0;
            EmptyEndpointsPanel.IsVisible = !hasPorts;
            PortsList.IsVisible = hasPorts;

            CancelTest();
            if (_isPaused)
            {
                foreach (var row in _ports)
                {
                    row.SetListening(false);
                }

                EndpointStatusText.Text = $"{DescribePortCounts()} · paused, ports released";
                SetMonitorState("Paused", IdleBrush);
            }
            else
            {
                var listening = await StartMonitoringAllAsync();
                EndpointStatusText.Text = DescribePorts(listening);
                SetMonitorState(listening > 0 ? "Listening" : "Waiting", listening > 0 ? ListeningBrush : IdleBrush);
            }

            PauseButton.IsEnabled = _ports.Count > 0;
            UpdateTestButton();
            UpdateTrafficHelpText();
        }
        catch (Exception exception)
        {
            EmptyEndpointsPanel.IsVisible = true;
            PortsList.IsVisible = false;
            EndpointStatusText.Text = $"MIDI discovery failed: {exception.Message}";
            SetMonitorState("Error", ErrorBrush);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task<int> StartMonitoringAllAsync()
    {
        if (_midiService is null)
        {
            return 0;
        }

        var listening = 0;
        foreach (var row in _ports)
        {
            if (row.Input is null)
            {
                continue;
            }

            try
            {
                await _midiService.StartMonitoringAsync(row.Input.Id);
                _monitoredInputIds.Add(row.Input.Id);
                listening++;
            }
            catch (MidiPortInUseException)
            {
                row.SetInputError(
                    "Another application has this port. Close it, or pause it, then press Refresh devices.",
                    inUseElsewhere: true);
            }
            catch (Exception exception)
            {
                row.SetInputError(exception.Message);
            }
        }

        return listening;
    }

    private async Task StopAllMonitoringAsync()
    {
        if (_midiService is not null)
        {
            foreach (var inputId in _monitoredInputIds)
            {
                try
                {
                    await _midiService.StopMonitoringAsync(inputId);
                }
                catch
                {
                    // The endpoint may already be gone; nothing useful to do.
                }
            }
        }

        _monitoredInputIds.Clear();
        _pending.Clear();
        SetMonitorState("Waiting", IdleBrush);
    }

    private string DescribePorts(int listening)
    {
        if (_ports.Count == 0)
        {
            return "No endpoints detected — it is safe to connect the H12 later.";
        }

        var inUse = _ports.Count(row => row.InputStatus == "Input in use elsewhere");
        var suffix = inUse > 0 ? $", {inUse} held by another app" : string.Empty;
        return $"{DescribePortCounts()} · listening on {listening} input{(listening == 1 ? string.Empty : "s")}{suffix}";
    }

    private string DescribePortCounts()
    {
        var h12Count = _ports.Count(row => row.Pair.IsH12);
        var otherCount = _ports.Count - h12Count;
        var parts = new List<string>();
        if (h12Count > 0)
        {
            parts.Add($"{h12Count} H12 port{(h12Count == 1 ? string.Empty : "s")}");
        }

        if (otherCount > 0)
        {
            parts.Add($"{otherCount} other");
        }

        return string.Join(", ", parts);
    }

    private void UpdateTrafficHelpText()
    {
        var clockNote = _hideClock ? " Clock messages are hidden." : string.Empty;

        if (_isPaused)
        {
            TrafficHelpText.Text = "Listening is paused and every port is released, so a DAW or UxMIDI Tools can use them. Press Resume listening when you are done.";
        }
        else if (_monitoredInputIds.Count == 0)
        {
            TrafficHelpText.Text = "Connect a device and refresh to start listening.";
        }
        else if (_filter is null)
        {
            TrafficHelpText.Text = "Listening on every input. Play a note and watch which port lights up." + clockNote;
        }
        else
        {
            TrafficHelpText.Text = $"Showing only {_filter.Title}.{clockNote}";
        }

        EmptyTrafficText.Text = _filter is null
            ? "Play a note to begin"
            : $"No MIDI yet on {_filter.Title}";
    }

    private void SetMonitorState(string text, IBrush brush)
    {
        if (MonitorStatusText.Text != text)
        {
            MonitorStatusText.Text = text;
        }

        if (!ReferenceEquals(ActivityIndicator.Background, brush))
        {
            ActivityIndicator.Background = brush;
        }
    }

    // Called on CoreMIDI's thread. Queue the message and make sure one drain is scheduled.
    private void MidiService_OnMessageReceived(object? sender, MidiMessage message)
    {
        _pending.Enqueue(message);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(DrainPendingMessages, DispatcherPriority.Background);
        }
    }

    private void DrainPendingMessages()
    {
        Volatile.Write(ref _drainScheduled, 0);

        var received = false;
        while (_pending.TryDequeue(out var message))
        {
            received = true;
            var decoded = MidiMessageDecoder.Decode(message);
            _portsByInputId.TryGetValue(message.EndpointId, out var row);
            row?.Record(message, decoded);

            if (_testRow is not null && row is not null)
            {
                if (ReferenceEquals(row, _testRow))
                {
                    _testActivity?.Record(message.Timestamp, decoded);
                }
                else if (decoded.Type is "Note On" or "Control Change")
                {
                    _testOtherPortsThatPlayed.Add(row.Title);
                }
            }

            var trafficRow = new MidiTrafficRow(
                message.EndpointId,
                message.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff"),
                row?.Title ?? message.EndpointId,
                decoded.Channel?.ToString() ?? "—",
                decoded.Type,
                string.IsNullOrWhiteSpace(decoded.Description) ? decoded.RawData : decoded.Description);

            _history.Insert(0, trafficRow);
            if (_history.Count > MaximumHistoryRows)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            if (PassesFilters(trafficRow))
            {
                _traffic.Insert(0, trafficRow);
                while (_traffic.Count > MaximumVisibleRows)
                {
                    _traffic.RemoveAt(_traffic.Count - 1);
                }
            }

            _lastMessageAt = message.Timestamp;
        }

        if (received)
        {
            ShowTrafficListIfNeeded();
            SetMonitorState("Receiving", ReceivingBrush);
        }
    }

    private void ActivityTimer_OnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in _ports)
        {
            row.Tick(now, ActivityFlash);
        }

        if (_testRow is not null)
        {
            UpdateTestCountdown(now);
        }

        if (_monitoredInputIds.Count > 0 &&
            MonitorStatusText.Text == "Receiving" &&
            now - _lastMessageAt > ActivityFlash)
        {
            SetMonitorState("Listening", ListeningBrush);
        }
    }

    private void RebuildVisibleTraffic()
    {
        _traffic.Clear();
        foreach (var row in _history.Where(PassesFilters).Take(MaximumVisibleRows))
        {
            _traffic.Add(row);
        }

        ShowTrafficListIfNeeded();
    }

    private bool PassesFilters(MidiTrafficRow row)
    {
        if (_hideClock && row.Type == "Clock")
        {
            return false;
        }

        return _filter is null || _filter.Input?.Id == row.EndpointId;
    }

    private void ShowTrafficListIfNeeded()
    {
        var hasRows = _traffic.Count > 0;
        if (TrafficList.IsVisible != hasRows)
        {
            TrafficList.IsVisible = hasRows;
            EmptyTrafficText.IsVisible = !hasRows;
        }
    }
}
