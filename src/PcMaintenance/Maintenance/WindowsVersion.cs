namespace PcMaintenance.Maintenance;

/// <summary>Version de Windows (lecture commune à tous les logiciels JVTECH).</summary>
internal static class WindowsVersion
{
    public static PcInspector.WindowsVersionInfo Read() => PcInspector.WindowsState.Version();
}
