using System.Windows;

namespace JvtechAtelier;

public partial class App : Application
{
    // Atelier tourne sur les PC de l'atelier (pas chez un client) : journaux et rapports d'erreur comme les v1.
    public App() => PcInspector.CrashGuard.Install(this, "JVTECH Atelier");
}
