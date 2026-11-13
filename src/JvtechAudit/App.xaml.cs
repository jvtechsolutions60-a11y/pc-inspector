using System.Windows;
using System.Windows.Threading;

namespace JvtechAudit;

public partial class App : Application
{
    public App()
    {
        // Lecture seule stricte : une erreur imprévue est affichée, jamais écrite sur le PC du client
        // (pas de journal : la seule écriture d'Audit est le rapport exporté, voir ExportRapport.cs).
        DispatcherUnhandledException += OnError;
    }

    private static void OnError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show("Erreur imprévue, le logiciel reste ouvert :\n\n" + e.Exception.Message, "JVTECH Audit",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
