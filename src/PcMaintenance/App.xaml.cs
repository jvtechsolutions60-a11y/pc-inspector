using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PcMaintenance;

public partial class App : Application
{
    // Une seule préparation à la fois : la clé Run et un lancement manuel ne doivent pas se croiser.
    private Mutex? _preparationLock;

    public App() => PcInspector.CrashGuard.Install(this, "PC Maintenance");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (CommandLine.Wanted(e.Args))
        {
            // Sans fenêtre : le logiciel se ferme avec le code de sortie quand tout est fini.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.InvokeAsync(async () => Shutdown(await CommandLine.RunAsync(e.Args)));
            return;
        }
        if (e.Args.Any(a => a.Equals("/preparation", StringComparison.OrdinalIgnoreCase)))
        {
            _preparationLock = new Mutex(true, @"Global\JVTECH-Preparation", out var first);
            if (!first)
            {
                Shutdown();
                return;
            }
            // Garde-fou : jamais de préparation revente sur un PC en service (PC d'un client) sans confirmation explicite.
            var options = Preparation.PreparationOptions.Load();
            var state = Preparation.PreparationState.Load();
            var mode = Preparation.PreparationGuard.ReadMode();
            var machine = Environment.MachineName;
            var permission = Preparation.PreparationGuard.Decide(mode, state.ConfirmedMachine, machine, options.Reseal);
            PcInspector.CrashGuard.Trace($"Préparation : mode Windows {mode}, autorisée {permission.Allowed}, Sysprep {permission.ResealAllowed}. {permission.Reason}");
            if (!permission.Allowed)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (!PreparationRefusalWindow.Ask(permission.Reason, machine))
                {
                    // Lancement par erreur ou PC sorti du mode audit : on retire aussi la reprise automatique.
                    Preparation.PreparationRunner.RemoveAutoStart();
                    PcInspector.CrashGuard.Trace("Préparation refusée, rien n'a été fait.");
                    Shutdown();
                    return;
                }
                state.ConfirmedMachine = machine;
                state.Save();
                permission = Preparation.PreparationGuard.Decide(mode, state.ConfirmedMachine, machine, options.Reseal);
                PcInspector.CrashGuard.Trace("Préparation hors mode audit confirmée par le technicien (nom du PC tapé).");
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            var window = new PreparationWindow(permission);
            MainWindow = window;
            window.Show();
            return;
        }
        new MainWindow().Show();
    }
}
