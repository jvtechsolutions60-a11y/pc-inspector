using Microsoft.Win32;

namespace PcMaintenance.Maintenance;

/// <summary>Version de Windows lue dans le registre (aucune requête WMI : affichage instantané).</summary>
internal sealed record WindowsVersion(string Name, string? DisplayVersion, int Build)
{
    public bool IsWindows11 => Build >= 22000;

    public static WindowsVersion Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var name = key?.GetValue("ProductName") as string ?? "Windows";
        int.TryParse(key?.GetValue("CurrentBuild") as string, out var build);
        // Windows 11 garde « Windows 10 » dans ProductName : seul le numéro de build fait foi.
        if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
        return new WindowsVersion(name, key?.GetValue("DisplayVersion") as string, build);
    }
}
