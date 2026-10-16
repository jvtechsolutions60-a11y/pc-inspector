using System.Management;

namespace PcInspector.Collectors;

/// <summary>Petites aides pour interroger WMI sans répéter le même code partout.</summary>
internal static class Wmi
{
    public static List<ManagementObject> Query(string wql, string scope = @"root\CIMV2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    public static string? Str(this ManagementBaseObject obj, string property)
    {
        try { return obj[property]?.ToString(); }
        catch (ManagementException) { return null; }
    }

    public static ulong? ULong(this ManagementBaseObject obj, string property)
    {
        try { return obj[property] is { } v ? Convert.ToUInt64(v) : null; }
        catch (ManagementException) { return null; }
    }

    public static string FormatBytes(ulong? bytes)
    {
        if (bytes is null) return "—";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double value = bytes.Value;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
