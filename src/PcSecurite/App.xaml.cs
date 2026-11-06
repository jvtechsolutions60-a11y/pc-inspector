using System.Windows;

namespace PcSecurite;

public partial class App : Application
{
    public App() => PcInspector.CrashGuard.Install(this, "PC Sécurité");

}
