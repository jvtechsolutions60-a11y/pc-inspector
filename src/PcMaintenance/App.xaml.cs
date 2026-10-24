using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PcMaintenance;

public partial class App : Application
{
    // Une seule préparation à la fois : la clé Run et un lancement manuel ne doivent pas se croiser.
    private Mutex? _preparationLock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Any(a => a.Equals("/preparation", StringComparison.OrdinalIgnoreCase)))
        {
            _preparationLock = new Mutex(true, @"Global\JVTECH-Preparation", out var first);
            if (!first)
            {
                Shutdown();
                return;
            }
            new PreparationWindow().Show();
            return;
        }
        new MainWindow().Show();
    }
}
