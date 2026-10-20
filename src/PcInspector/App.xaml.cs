using System.Windows;

namespace PcInspector;

public partial class App : Application
{
    /// <summary>Version affichée (v1.0.N, N = numéro de compilation GitHub Actions), pour ne pas confondre deux .exe.</summary>
    public static string Version => AppInfo.Version;
}
