using System.Windows;

namespace PcMigration;

public partial class App : Application
{
    public App() => PcInspector.CrashGuard.Install(this, "PC Migration");
}
