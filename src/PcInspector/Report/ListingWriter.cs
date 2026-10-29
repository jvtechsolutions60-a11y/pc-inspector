using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PcInspector.Collectors;

namespace PcInspector.Report;

/// <summary>Ce que le logiciel ne peut pas lire : saisi à la main dans la fenêtre du texte d'annonce.</summary>
public sealed class ListingExtras
{
    public string Case { get; set; } = "";
    public string PowerSupply { get; set; } = "";
    public string Cooler { get; set; } = "";
    public string Condition { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>Composants du PC relevés une fois, pour composer le titre et le descriptif d'une annonce.</summary>
public sealed class ListingSpecs
{
    private const double Gb = 1024d * 1024 * 1024;

    public bool IsLaptop { get; private set; }
    public string Kind { get; private set; } = "PC";
    public string Model { get; private set; } = "";
    public bool IsCustomBuild { get; private set; }
    public string Cpu { get; private set; } = "";
    public string CpuDetail { get; private set; } = "";
    public string Board { get; private set; } = "";
    public string Ram { get; private set; } = "";
    public string RamShort { get; private set; } = "";
    public List<string> Ramsticks { get; } = new();
    public List<string> Gpus { get; } = new();
    public string GpuShort { get; private set; } = "";
    public List<string> Disks { get; } = new();
    public string StorageShort { get; private set; } = "";
    public string Screen { get; private set; } = "";
    public string Battery { get; private set; } = "";
    public List<string> Connectivity { get; } = new();
    public string Windows { get; private set; } = "";

    public static ListingSpecs Collect()
    {
        var s = new ListingSpecs();
        Try(() => s.ReadSystem());
        Try(() => s.ReadCpu());
        Try(() => s.ReadRam());
        Try(() => s.ReadGpu());
        Try(() => s.ReadDisks());
        Try(() => s.ReadScreen());
        Try(() => s.ReadBattery());
        Try(() => s.ReadConnectivity());
        Try(() => s.ReadWindows());
        return s;
    }

    // Une lecture qui échoue laisse sa ligne vide au lieu de bloquer tout le texte.
    private static void Try(Action read)
    {
        try { read(); }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException
                                       or InvalidCastException or System.Runtime.InteropServices.COMException) { }
    }

    private void ReadSystem()
    {
        var chassis = Wmi.QueryOptional("SELECT ChassisTypes FROM Win32_SystemEnclosure").FirstOrDefault()?["ChassisTypes"] as ushort[];
        var type = chassis?.FirstOrDefault() ?? 0;
        IsLaptop = type is 8 or 9 or 10 or 11 or 14 or 30 or 31 or 32;
        Kind = type switch
        {
            8 or 9 or 10 or 14 => "PC portable",
            30 or 31 or 32 => "PC portable 2 en 1",
            13 => "PC tout-en-un",
            35 or 36 => "Mini PC",
            3 or 4 or 5 or 6 or 7 or 15 or 16 => "PC fixe",
            _ => "PC",
        };
        Model = MachineIdentity.Model();
        IsCustomBuild = Model.StartsWith("PC monté");
        var board = Wmi.QueryOptional("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        Board = $"{MachineIdentity.Clean(board?.Str("Manufacturer"))} {MachineIdentity.Clean(board?.Str("Product"))}".Trim();
    }

    private void ReadCpu()
    {
        var cpu = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
        Cpu = LabelWriter.Shorten(cpu?.Str("Name")) ?? "";
        var parts = new List<string>();
        if (cpu?.ULong("NumberOfCores") is > 0 and var cores) parts.Add($"{cores} cœurs");
        if (cpu?.ULong("NumberOfLogicalProcessors") is > 0 and var threads) parts.Add($"{threads} threads");
        if (cpu?.ULong("MaxClockSpeed") is > 0 and var mhz && !Cpu.Contains("GHz")) parts.Add($"{mhz / 1000.0:0.0#} GHz");
        CpuDetail = string.Join(", ", parts);
    }

    private void ReadRam()
    {
        var modules = Wmi.Query("SELECT Capacity, SMBIOSMemoryType, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
        var total = modules.Aggregate(0UL, (sum, m) => sum + (m.ULong("Capacity") ?? 0));
        if (total == 0) return;
        var type = modules.Select(m => MemoryType(m.ULong("SMBIOSMemoryType"))).FirstOrDefault(t => t is not null);
        var speed = modules.Select(m => m.ULong("Speed") is > 0 ? m.ULong("Speed") : m.ULong("ConfiguredClockSpeed")).Max();
        var sizes = modules.Select(m => Math.Round((m.ULong("Capacity") ?? 0) / Gb)).ToList();
        var layout = sizes.Distinct().Count() == 1 ? $"{sizes.Count} × {sizes[0]} Go" : string.Join(" + ", sizes.Select(g => $"{g} Go"));
        RamShort = $"{Math.Round(total / Gb)} Go" + (type is null ? "" : " " + type);
        Ram = RamShort + (speed is > 0 ? $" {speed} MHz" : "") + $" ({layout})";
        foreach (var group in modules.GroupBy(m => $"{Clean(m.Str("Manufacturer"))} {Clean(m.Str("PartNumber"))}".Trim()).Where(g => g.Key.Length > 0))
            Ramsticks.Add((group.Count() > 1 ? $"{group.Count()} × " : "") + group.Key);
    }

    private void ReadGpu()
    {
        foreach (var gpu in Wmi.Query("SELECT Name, AdapterRAM FROM Win32_VideoController"))
        {
            var name = gpu.Str("Name");
            if (string.IsNullOrWhiteSpace(name) || name!.Contains("Basic") || name.Contains("Remote") || name.Contains("Virtual")) continue;
            var memory = GpuCollector.VideoMemory(name) ?? gpu.ULong("AdapterRAM");
            var clean = name.Replace("(R)", "").Replace("(TM)", "").Trim();
            var dedicated = !(clean.StartsWith("Intel") && !clean.Contains("Arc")) && !clean.Contains("Radeon(TM) Graphics") && clean != "AMD Radeon Graphics";
            Gpus.Add(clean + (dedicated && memory is > 512UL * 1024 * 1024 ? $" {Math.Round(memory.Value / Gb)} Go" : ""));
        }
        GpuShort = Gpus.FirstOrDefault(g => !g.StartsWith("Intel") || g.Contains("Arc")) ?? "";
    }

    private void ReadDisks()
    {
        var reliability = Wmi.QueryOptional("SELECT DeviceId, Wear FROM MSFT_StorageReliabilityCounter", Wmi.StorageScope)
            .ToDictionary(r => r.Str("DeviceId") ?? "", r => r.ULong("Wear"));
        var sizes = new List<string>();
        foreach (var disk in Wmi.QueryOptional("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", Wmi.StorageScope))
        {
            var bus = disk.ULong("BusType");
            if (bus is 7 or 12 or 13 or 15) continue; // USB, carte SD, virtuel : pas vendus avec le PC
            var type = bus == 17 ? "SSD NVMe" : disk.ULong("MediaType") == 3 ? "HDD" : bus == 11 ? "SSD SATA" : "SSD";
            var size = Size(disk.ULong("Size") ?? 0);
            var health = disk.ULong("HealthStatus") switch { 0 => "bon état", 1 => "à surveiller", 2 => "défaillant", _ => null };
            var wear = reliability.TryGetValue(disk.Str("DeviceId") ?? "", out var w) && w is > 0 and <= 100 ? $"usure {w} %" : null;
            var state = string.Join(", ", new[] { health, wear }.Where(x => x is not null));
            Disks.Add($"{type} {size} ({disk.Str("FriendlyName")?.Trim()})" + (state.Length > 0 ? $", {state}" : ""));
            sizes.Add($"{(type == "HDD" ? "HDD" : "SSD")} {size}");
        }
        StorageShort = string.Join(" + ", sizes);
    }

    private void ReadScreen()
    {
        if (!IsLaptop && !Kind.Contains("tout-en-un")) return;
        var display = Wmi.QueryOptional("SELECT MaxHorizontalImageSize, MaxVerticalImageSize FROM WmiMonitorBasicDisplayParams", Wmi.WmiScope).FirstOrDefault();
        var w = display?.ULong("MaxHorizontalImageSize") ?? 0;
        var h = display?.ULong("MaxVerticalImageSize") ?? 0;
        var inches = w > 0 && h > 0 ? Math.Sqrt(w * w + h * h) / 2.54 : 0;
        var video = Wmi.Query("SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController")
            .Select(v => (W: v.ULong("CurrentHorizontalResolution") ?? 0, H: v.ULong("CurrentVerticalResolution") ?? 0))
            .OrderByDescending(r => r.W).FirstOrDefault();
        var parts = new List<string>();
        if (inches > 9) parts.Add($"{Math.Round(inches * 2) / 2:0.#} pouces");
        if (video.W > 0) parts.Add($"{video.W} × {video.H}" + (video.W == 1920 && video.H == 1080 ? " (Full HD)" : ""));
        Screen = string.Join(", ", parts);
    }

    private void ReadBattery()
    {
        if (!IsLaptop) return;
        Battery = BatteryCollector.WearPercent() is { } wear ? $"{Math.Max(0, Math.Round(100 - wear))} % de sa capacité d'origine" : "";
    }

    private void ReadConnectivity()
    {
        var adapters = Wmi.Query("SELECT Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE").Select(a => a.Str("Name") ?? "").ToList();
        var wifi = adapters.FirstOrDefault(n => n.Contains("Wi-Fi") || n.Contains("Wireless") || n.Contains("802.11") || n.Contains("WLAN") || n.Contains("WiFi"));
        if (wifi is not null)
        {
            var standard = Regex.IsMatch(wifi, @"\bBE\d|Wi-?Fi 7") ? "Wi-Fi 7"
                : wifi.Contains("6E") ? "Wi-Fi 6E"
                : Regex.IsMatch(wifi, @"\bAX\d|Wi-?Fi 6|802\.11ax", RegexOptions.IgnoreCase) ? "Wi-Fi 6"
                : Regex.IsMatch(wifi, @"-AC\b|\bAC\s?\d|802\.11ac", RegexOptions.IgnoreCase) ? "Wi-Fi 5"
                : "Wi-Fi";
            Connectivity.Add($"{standard} ({wifi.Replace("(R)", "").Replace("(TM)", "").Trim()})");
        }
        if (Wmi.QueryOptional("SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth'").Count > 0) Connectivity.Add("Bluetooth");
        var ethernet = adapters.FirstOrDefault(n => n != wifi && !Regex.IsMatch(n, "Bluetooth|Virtual|VPN|TAP|Hyper-V|WAN Miniport|Wi-?Fi|Wireless", RegexOptions.IgnoreCase));
        if (ethernet is not null) Connectivity.Add("Ethernet" + (ethernet.Contains("2.5") ? " 2,5 Gb/s" : ""));
    }

    private void ReadWindows()
    {
        var caption = Wmi.Query("SELECT Caption FROM Win32_OperatingSystem").FirstOrDefault()?.Str("Caption")?.Replace("Microsoft ", "").Trim() ?? "";
        Windows = caption + (SecurityCollector.IsWindowsActivated() == true ? " activé" : "");
    }

    // ---------- Texte ----------

    /// <summary>Titre court pour l'annonce, par exemple « PC fixe i9-9900K · RTX 2060 · 16 Go · SSD 750 Go ».</summary>
    public string Title()
    {
        var model = IsCustomBuild || Model.Length == 0 ? Kind : $"{Kind} {Model}";
        var cpu = Cpu.Replace("Intel Core ", "").Replace("AMD ", "").Replace("Processor", "").Trim();
        var gpu = GpuShort.Replace("NVIDIA GeForce ", "").Replace("AMD Radeon ", "Radeon ").Trim();
        var ram = string.Join(" ", RamShort.Split(' ').Take(2)); // « 16 Go », sans le type
        var parts = new[] { model, cpu, gpu, ram, StorageShort };
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    public string Description(ListingExtras extras)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Title());
        sb.AppendLine();
        if (extras.Condition.Trim().Length > 0) { sb.AppendLine("État : " + extras.Condition.Trim()); sb.AppendLine(); }
        sb.AppendLine("Configuration :");
        void Line(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"- {label} : {value!.Trim()}");
        }
        if (!IsCustomBuild) Line("Modèle", Model);
        Line("Processeur", Cpu + (CpuDetail.Length > 0 ? $" ({CpuDetail})" : ""));
        if (IsCustomBuild || !IsLaptop) Line("Carte mère", Board);
        Line("Mémoire", Ram + (Ramsticks.Count > 0 ? $", {string.Join(" + ", Ramsticks)}" : ""));
        Line(Gpus.Count > 1 ? "Cartes graphiques" : "Carte graphique", string.Join(" + ", Gpus));
        foreach (var disk in Disks) Line("Stockage", disk);
        Line("Écran", Screen);
        Line("Batterie", Battery);
        Line("Boîtier", extras.Case);
        Line("Alimentation", extras.PowerSupply);
        Line("Refroidissement", extras.Cooler);
        Line("Connectivité", string.Join(", ", Connectivity));
        Line("Système", Windows);
        if (extras.Notes.Trim().Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine(extras.Notes.Trim());
        }
        sb.AppendLine();
        sb.AppendLine("Configuration relevée par PC Inspector, PC contrôlé et préparé par JVTECH Solutions.");
        return sb.ToString();
    }

    private static string? Clean(string? value)
    {
        var text = MachineIdentity.Clean(value);
        return text is null || text.Trim('0').Length == 0 || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : text;
    }

    private static string Size(ulong bytes)
    {
        var gb = bytes / 1e9;
        return gb >= 1000 ? $"{gb / 1000:0.#} To" : $"{Math.Round(gb)} Go";
    }

    private static string? MemoryType(ulong? smbiosType) => smbiosType switch
    {
        24 => "DDR3", 26 => "DDR4", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => null,
    };
}
