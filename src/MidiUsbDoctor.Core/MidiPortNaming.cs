using System.Text.RegularExpressions;

namespace MidiUsbDoctor.Core;

/// <summary>
/// Normalises endpoint names so that an input and an output belonging to the same port
/// share one name on every platform.
///
/// The legacy Windows MIDI stack (before Windows MIDI Services) names a multi-port device's
/// endpoints "CME [H12]", "MIDIIN2 (CME [H12])", "MIDIIN3 (CME [H12])" for inputs and
/// "CME [H12]", "MIDIOUT2 (CME [H12])" for outputs, and may prefix a second device instance
/// with "2- ". These become "CME [H12] Port 1", "CME [H12] Port 2" and so on, which is also
/// what macOS and Windows MIDI Services report, so labels line up across platforms.
/// </summary>
public static partial class MidiPortNaming
{
    [GeneratedRegex(@"^(?:\d+-\s*)?MIDI(?:IN|OUT)(\d+)\s*\((.+)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyWindowsNumbered();

    [GeneratedRegex(@"^\d+-\s*(.+)$")]
    private static partial Regex InstancePrefix();

    public static string Normalize(string name)
    {
        var trimmed = name.Trim();

        var numbered = LegacyWindowsNumbered().Match(trimmed);
        if (numbered.Success)
        {
            return $"{numbered.Groups[2].Value.Trim()} Port {numbered.Groups[1].Value}";
        }

        var instance = InstancePrefix().Match(trimmed);
        return instance.Success ? instance.Groups[1].Value.Trim() : trimmed;
    }

    /// <summary>
    /// On the legacy Windows stack the first port carries the bare device name while the rest
    /// are numbered. Given all normalised names, returns the bare names that should be read as
    /// "Port 1" of a numbered family.
    /// </summary>
    public static IReadOnlySet<string> BareNamesWithNumberedSiblings(IEnumerable<string> normalizedNames)
    {
        var names = normalizedNames.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var match = PortSuffix().Match(name);
            if (match.Success)
            {
                stems.Add(match.Groups[1].Value.Trim());
            }
        }

        return names.Where(stems.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^(.+?)\s+Port\s+\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex PortSuffix();
}
