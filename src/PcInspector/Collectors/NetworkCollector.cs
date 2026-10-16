using System.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class NetworkCollector : ICollector
{
    public string Title => "Réseau";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        var configs = Wmi.Query("SELECT * FROM Win32_NetworkAdapterConfiguration")
            .Where(c => c.ULong("Index") is not null)
            .GroupBy(c => c.ULong("Index")!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var adapters = Wmi.Query("SELECT * FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")
            .OrderByDescending(a => a.Bool("NetEnabled") == true);

        foreach (var adapter in adapters)
        {
            var connected = adapter.Bool("NetEnabled") == true;
            var group = new InfoGroup(adapter.Str("Name") ?? "Carte réseau")
                .Add("Connexion", adapter.Str("NetConnectionID"))
                .Add("État", connected ? "Connecté" : "Déconnecté")
                .Add("Débit", connected ? Speed(adapter.ULong("Speed")) : null)
                .Add("Adresse MAC", adapter.Str("MACAddress"))
                .Add("Fabricant", adapter.Str("Manufacturer"));

            if (connected && adapter.ULong("Index") is { } index && configs.TryGetValue(index, out var config))
            {
                group.Add("Adresses IP", config.Str("IPAddress"))
                     .Add("Passerelle", config.Str("DefaultIPGateway"))
                     .Add("DNS", config.Str("DNSServerSearchOrder"))
                     .Add("DHCP", Wmi.YesNo(config.Bool("DHCPEnabled")));
            }

            section.Groups.Add(group);
        }

        return section;
    }

    private static string? Speed(ulong? bitsPerSecond) => bitsPerSecond switch
    {
        null or 0 => null,
        >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gbit/s",
        _ => $"{bitsPerSecond / 1_000_000} Mbit/s",
    };
}
