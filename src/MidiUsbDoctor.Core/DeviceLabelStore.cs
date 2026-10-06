using System.Text.Json;

namespace MidiUsbDoctor.Core;

/// <summary>
/// User-assigned device names for ports, such as "CME [H12] Port 1" = "Bass Station II".
/// Labels are keyed by port name rather than platform endpoint ID so they survive
/// reconnects, USB port changes and a move between macOS and Windows. Labels for ports
/// that are not currently connected are kept.
/// </summary>
public sealed class DeviceLabelStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, string> _labels = new(StringComparer.OrdinalIgnoreCase);

    public DeviceLabelStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public IReadOnlyDictionary<string, string> Labels => _labels;

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MidiUsbDoctor",
        "device-labels.json");

    /// <summary>Loads labels from disk. A missing or unreadable file yields an empty store.</summary>
    public static DeviceLabelStore LoadFrom(string filePath)
    {
        var store = new DeviceLabelStore(filePath);
        if (!File.Exists(filePath))
        {
            return store;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var file = JsonSerializer.Deserialize<LabelFile>(stream, JsonOptions);
            if (file?.Labels is null)
            {
                return store;
            }

            foreach (var (portName, label) in file.Labels)
            {
                if (!string.IsNullOrWhiteSpace(portName) && !string.IsNullOrWhiteSpace(label))
                {
                    store._labels[portName.Trim()] = label.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Corrupt file: start empty rather than refuse to launch. Saving will overwrite it.
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return store;
    }

    public string? GetLabel(string portName) =>
        _labels.TryGetValue(portName.Trim(), out var label) ? label : null;

    public void SetLabel(string portName, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            _labels.Remove(portName.Trim());
            return;
        }

        _labels[portName.Trim()] = label.Trim();
    }

    public bool RemoveLabel(string portName) => _labels.Remove(portName.Trim());

    /// <summary>Writes the labels to disk, creating the folder if needed.</summary>
    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new LabelFile { Labels = new Dictionary<string, string>(_labels, StringComparer.OrdinalIgnoreCase) };
        var temporaryPath = FilePath + ".tmp";

        using (var stream = File.Create(temporaryPath))
        {
            JsonSerializer.Serialize(stream, file, JsonOptions);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private sealed class LabelFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, string>? Labels { get; set; }
    }
}
