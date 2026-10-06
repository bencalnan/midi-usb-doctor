using System.Text.Json;
using System.Text.Json.Serialization;

namespace MidiUsbDoctor.Core;

/// <summary>What the user has told us about one port: the instrument and where it is plugged in.</summary>
public sealed record DevicePortInfo(string? Label, string? Connection)
{
    public bool IsEmpty => Label is null && Connection is null;

    public static DevicePortInfo Normalize(string? label, string? connection) => new(
        string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
        string.IsNullOrWhiteSpace(connection) ? null : connection.Trim());
}

/// <summary>
/// User-assigned details for ports, such as "CME [H12] Port 1" = "Bass Station II" on "USB host 3".
/// Entries are keyed by port name rather than platform endpoint ID so they survive
/// reconnects, USB port changes and a move between macOS and Windows. Entries for ports
/// that are not currently connected are kept.
/// </summary>
public sealed class DeviceLabelStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, DevicePortInfo> _ports = new(StringComparer.OrdinalIgnoreCase);

    public DeviceLabelStore(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public IReadOnlyDictionary<string, DevicePortInfo> Ports => _ports;

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MidiUsbDoctor",
        "device-labels.json");

    /// <summary>Loads from disk. A missing or unreadable file yields an empty store.</summary>
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
            if (file is null)
            {
                return store;
            }

            // Version 1 files held a flat name -> label map.
            if (file.Labels is not null)
            {
                foreach (var (portName, label) in file.Labels)
                {
                    store.Set(portName, DevicePortInfo.Normalize(label, null));
                }
            }

            if (file.Ports is not null)
            {
                foreach (var (portName, entry) in file.Ports)
                {
                    store.Set(portName, DevicePortInfo.Normalize(entry?.Label, entry?.Connection));
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

    public DevicePortInfo? Get(string portName) =>
        _ports.TryGetValue(portName.Trim(), out var info) ? info : null;

    public string? GetLabel(string portName) => Get(portName)?.Label;

    public string? GetConnection(string portName) => Get(portName)?.Connection;

    /// <summary>Replaces the entry for a port. An entry with nothing in it is removed.</summary>
    public void Set(string portName, DevicePortInfo info)
    {
        var key = portName.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var normalized = DevicePortInfo.Normalize(info.Label, info.Connection);
        if (normalized.IsEmpty)
        {
            _ports.Remove(key);
        }
        else
        {
            _ports[key] = normalized;
        }
    }

    public void SetLabel(string portName, string? label) =>
        Set(portName, new DevicePortInfo(label, Get(portName)?.Connection));

    public void SetConnection(string portName, string? connection) =>
        Set(portName, new DevicePortInfo(Get(portName)?.Label, connection));

    public bool Remove(string portName) => _ports.Remove(portName.Trim());

    /// <summary>Writes to disk, creating the folder if needed.</summary>
    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var file = new LabelFile
        {
            Version = 2,
            Ports = _ports.ToDictionary(
                pair => pair.Key,
                pair => new PortEntry { Label = pair.Value.Label, Connection = pair.Value.Connection },
                StringComparer.OrdinalIgnoreCase),
        };
        var temporaryPath = FilePath + ".tmp";

        using (var stream = File.Create(temporaryPath))
        {
            JsonSerializer.Serialize(stream, file, JsonOptions);
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private sealed class LabelFile
    {
        public int Version { get; set; } = 2;

        /// <summary>Version 1 format only; never written any more.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Labels { get; set; }

        public Dictionary<string, PortEntry>? Ports { get; set; }
    }

    private sealed class PortEntry
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Label { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Connection { get; set; }
    }
}
