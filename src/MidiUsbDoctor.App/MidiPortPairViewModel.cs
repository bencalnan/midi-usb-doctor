using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using MidiUsbDoctor.Core;

namespace MidiUsbDoctor.App;

/// <summary>
/// One row in the port list: a port pair plus what has been heard on its input.
/// Updated on the UI thread only.
/// </summary>
public sealed class MidiPortPairViewModel : INotifyPropertyChanged
{
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#484F58"));
    private static readonly IBrush SeenBrush = new SolidColorBrush(Color.Parse("#238636"));
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#3FB950"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F85149"));

    private readonly MidiPortActivity _activity = new();
    private string? _inputError;
    private bool _inputInUse;
    private bool _isListening = true;
    private bool _isActive;
    private string? _label;
    private string? _connection;
    private OutputTestOutcome? _outputOutcome;

    public MidiPortPairViewModel(MidiPortPair pair)
    {
        Pair = pair;
        DisplayName = pair.Name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MidiPortPair Pair { get; }
    public MidiEndpoint? Input => Pair.Input;
    public MidiEndpoint? Output => Pair.Output;
    public string DisplayName { get; }
    public MidiPortActivity Activity => _activity;

    /// <summary>User-assigned device name, such as "Bass Station II". Null when unset.</summary>
    public string? Label
    {
        get => _label;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_label == normalized)
            {
                return;
            }

            _label = normalized;
            Raise(nameof(Label));
            Raise(nameof(HasLabel));
            Raise(nameof(HasSubtitle));
            Raise(nameof(Title));
            Raise(nameof(Subtitle));
        }
    }

    /// <summary>Where the instrument is plugged in, such as "USB host 3" or "DIN 1". Null when unset.</summary>
    public string? Connection
    {
        get => _connection;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_connection == normalized)
            {
                return;
            }

            _connection = normalized;
            Raise(nameof(Connection));
            Raise(nameof(HasSubtitle));
            Raise(nameof(Subtitle));
        }
    }

    public bool HasLabel => _label is not null;

    public bool HasSubtitle => _label is not null || _connection is not null;

    /// <summary>What the user sees first: the label when set, otherwise the port name.</summary>
    public string Title => _label ?? DisplayName;

    /// <summary>
    /// Shown under the title: the port name when a label hides it, and the connection when known.
    /// For example "CME [H12] Port 2 · USB host 3".
    /// </summary>
    public string Subtitle
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(2);
            if (_label is not null)
            {
                parts.Add(DisplayName);
            }

            if (_connection is not null)
            {
                parts.Add(_connection);
            }

            return string.Join(" · ", parts);
        }
    }

    public string InputStatus =>
        Input is null ? "Input missing"
        : _inputInUse ? "Input in use elsewhere"
        : _inputError is not null ? "Input error"
        : !_isListening ? "Input paused"
        : _activity.MessageCount > 0 ? "Input receiving"
        : "Input ready";

    public string OutputStatus =>
        Output is null ? "Output missing"
        : _outputOutcome switch
        {
            OutputTestOutcome.Sent => "Output: note sent",
            OutputTestOutcome.Heard => "Output working",
            OutputTestOutcome.NotHeard => "Output not heard",
            OutputTestOutcome.Failed => "Output failed",
            _ => "Output not tested",
        };

    /// <summary>true = output heard, false = not heard, null = not tested or not answered.</summary>
    public bool? OutputConfirmed => _outputOutcome switch
    {
        OutputTestOutcome.Heard => true,
        OutputTestOutcome.NotHeard => false,
        _ => null,
    };

    public void SetOutputOutcome(OutputTestOutcome? outcome)
    {
        _outputOutcome = outcome;
        Raise(nameof(OutputStatus));
    }

    public string ChannelText =>
        _activity.Channels.Count == 0
            ? "—"
            : "Ch " + string.Join(", ", _activity.Channels);

    public string LastMessageText
    {
        get
        {
            if (_inputError is not null)
            {
                return _inputError;
            }

            var last = _activity.LastMessage;
            if (last is null)
            {
                return Input is null ? "Nothing to listen to"
                    : _isListening ? "No MIDI yet"
                    : "Released";
            }

            return string.IsNullOrWhiteSpace(last.Description)
                ? last.Type
                : $"{last.Type} · {last.Description}";
        }
    }

    public string LastSeenText =>
        _activity.LastSeen?.ToLocalTime().ToString("HH:mm:ss") ?? string.Empty;

    public IBrush ActivityBrush =>
        _inputError is not null ? ErrorBrush
        : !_isListening ? IdleBrush
        : _isActive ? ActiveBrush
        : _activity.MessageCount > 0 ? SeenBrush
        : IdleBrush;

    public void Record(MidiMessage message, DecodedMidiMessage decoded)
    {
        var hadMessages = _activity.MessageCount > 0;
        var channelsBefore = _activity.Channels.Count;

        _activity.Record(message.Timestamp, decoded);

        Raise(nameof(LastMessageText));
        Raise(nameof(LastSeenText));
        if (_activity.Channels.Count != channelsBefore)
        {
            Raise(nameof(ChannelText));
        }

        if (!hadMessages)
        {
            Raise(nameof(InputStatus));
        }

        SetActive(true);
    }

    /// <summary>Called on a timer so the activity dot settles back after a flash.</summary>
    public void Tick(DateTimeOffset now, TimeSpan flashDuration)
    {
        if (_isActive && (_activity.LastSeen is null || now - _activity.LastSeen > flashDuration))
        {
            SetActive(false);
        }
    }

    public void SetInputError(string message, bool inUseElsewhere = false)
    {
        _inputError = message;
        _inputInUse = inUseElsewhere;
        Raise(nameof(InputStatus));
        Raise(nameof(LastMessageText));
        Raise(nameof(ActivityBrush));
    }

    /// <summary>Marks whether the app currently holds this port's input open.</summary>
    public void SetListening(bool isListening)
    {
        _inputError = null;
        _inputInUse = false;
        _isListening = isListening;
        if (!isListening)
        {
            _isActive = false;
        }

        Raise(nameof(InputStatus));
        Raise(nameof(LastMessageText));
        Raise(nameof(ActivityBrush));
    }

    private void SetActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        Raise(nameof(ActivityBrush));
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
