using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Jvtech.Lecture.Collecte;

namespace Jvtech.Atelier.Actions.Preparation;

/// <summary>
/// Diagnostic mémoire approfondi : ouvre l'outil de Windows (mdsched.exe). Le technicien choisit dans la fenêtre de
/// Windows de redémarrer tout de suite ou au prochain démarrage ; Atelier ne redémarre pas et ne modifie pas le
/// démarrage lui-même. Le résultat est lu au retour dans le journal Système (contrôle « diagnostic approfondi »).
/// </summary>
public sealed class DiagnosticMemoireWindows : IActionAtelier
{
    public string Id => "memoire-approfondi";
    public string Libelle => "Diagnostic mémoire de Windows (avec redémarrage)";
    public bool DemandeAdministrateur => true;

    public string LireEtat() => LectureControles.DernierDiagnosticMemoire() is { } d
        ? $"dernier diagnostic le {d.Date:dd/MM/yyyy HH:mm}, {(d.ErreursTrouvees ? "erreurs trouvées" : "aucune erreur")}"
        : "aucun diagnostic dans le journal";

    public SortieAction Executer(CancellationToken annulation)
    {
        annulation.ThrowIfCancellationRequested();
        var chemin = Chemin();
        try
        {
            using var p = Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
            return new SortieAction(p is not null, null,
                p is null ? "mdsched.exe ne s'est pas ouvert" : "fenêtre de Windows ouverte : choisir le redémarrage dans cette fenêtre");
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return new SortieAction(false, null, "mdsched.exe introuvable : " + ex.Message);
        }
    }

    /// <summary>System32 réel, même si le programme tournait en 32 bits (Sysnative).</summary>
    private static string Chemin()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sysnative = Path.Combine(windows, "Sysnative", "mdsched.exe");
        return !Environment.Is64BitProcess && File.Exists(sysnative) ? sysnative : Path.Combine(windows, "System32", "mdsched.exe");
    }
}
