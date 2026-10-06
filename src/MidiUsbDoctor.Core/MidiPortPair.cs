using System.Text.RegularExpressions;
using MidiUsbDoctor.Core.Cme;

namespace MidiUsbDoctor.Core;

/// <summary>
/// An input endpoint and an output endpoint that share a name, such as the two halves
/// of "CME [H12] Port 1". Either side may be missing.
/// </summary>
public sealed record MidiPortPair(
    string Name,
    MidiEndpoint? Input,
    MidiEndpoint? Output,
    int? PortNumber,
    bool IsH12);

public static class MidiPortPairer
{
    private static readonly Regex TrailingNumber = new(@"\s(\d+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Groups endpoints into pairs by name. Endpoints are matched only with endpoints of
    /// the same name, so a device that has only an input never shifts other pairs.
    /// Names are normalised first (see <see cref="MidiPortNaming"/>) so legacy Windows
    /// "MIDIIN2 (device)" / "MIDIOUT2 (device)" pairs line up.
    /// H12 ports are listed first, then everything else, each in natural name order.
    /// </summary>
    public static IReadOnlyList<MidiPortPair> Pair(IEnumerable<MidiEndpoint> endpoints)
    {
        var pairs = new List<MidiPortPair>();
        var endpointList = endpoints.ToList();

        var normalized = endpointList.ToDictionary(
            endpoint => endpoint,
            endpoint => MidiPortNaming.Normalize(endpoint.Name));
        var bareFirstPorts = MidiPortNaming.BareNamesWithNumberedSiblings(normalized.Values);

        string NameFor(MidiEndpoint endpoint)
        {
            var name = normalized[endpoint];
            return bareFirstPorts.Contains(name) ? $"{name} Port 1" : name;
        }

        foreach (var group in endpointList.GroupBy(NameFor, StringComparer.OrdinalIgnoreCase))
        {
            var inputs = group.Where(e => e.Direction == MidiEndpointDirection.Input).ToList();
            var outputs = group.Where(e => e.Direction == MidiEndpointDirection.Output).ToList();
            var name = group.Key;
            var rowCount = Math.Max(inputs.Count, outputs.Count);

            for (var index = 0; index < rowCount; index++)
            {
                pairs.Add(new MidiPortPair(
                    name,
                    index < inputs.Count ? inputs[index] : null,
                    index < outputs.Count ? outputs[index] : null,
                    GetTrailingNumber(name),
                    H12Ports.IsH12Port(name)));
            }
        }

        return pairs
            .OrderByDescending(pair => pair.IsH12)
            .ThenBy(pair => GetStem(pair.Name), StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.PortNumber ?? -1)
            .ToArray();
    }

    private static int? GetTrailingNumber(string name)
    {
        var match = TrailingNumber.Match(name);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : null;
    }

    private static string GetStem(string name) =>
        TrailingNumber.Replace(name, string.Empty).TrimEnd();
}
