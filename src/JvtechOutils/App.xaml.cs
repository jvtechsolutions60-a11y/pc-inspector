using System.Windows;

namespace JvtechOutils;

public partial class App : Application
{
    public App() => PcInspector.CrashGuard.Install(this, "JVTECH Outils");

}
