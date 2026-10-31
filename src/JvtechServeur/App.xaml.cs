using System.Windows;

namespace JvtechServeur;

public partial class App : Application
{
    public App() => PcInspector.CrashGuard.Install(this, "JVTECH Serveur");

}
