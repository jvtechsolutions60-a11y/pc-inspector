using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using Jvtech.Lecture;

namespace Jvtech.Atelier.Actions.Finaliser;

/// <summary>
/// Finalisation : « sysprep /oobe /shutdown /quiet ». Le PC s'éteint ; l'acheteur découvrira l'écran de premier
/// démarrage de Windows (langue, réseau, son propre compte). N'efface aucune donnée et ne formate rien.
/// Lancée seulement après <see cref="Jvtech.Lecture.Regles.Finalisation.Decider"/> ; le mode audit est relu ici,
/// juste avant le lancement (comme PC Maintenance), et l'action refuse de partir sinon.
/// </summary>
public sealed class SysprepOobe : IActionAtelier
{
    private static readonly TimeSpan Delai = TimeSpan.FromMinutes(20);
    private readonly Action<string>? _progression;

    public SysprepOobe(Action<string>? progression = null) => _progression = progression;

    public string Id => "sysprep";
    public string Libelle => "Finalisation (Sysprep, premier démarrage pour l'acheteur)";
    public bool DemandeAdministrateur => true;

    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string Officiel => Path.Combine(Windows, @"System32\Sysprep\sysprep.exe");

    /// <summary>Fichiers de réponse gardés par Windows : un « Reseal Audit » dedans ramènerait le PC en mode audit.</summary>
    public static IReadOnlyList<string> FichiersDeReponse { get; } = new[]
    {
        @"Panther\unattend.xml", @"Panther\Unattend\unattend.xml", @"System32\Sysprep\unattend.xml",
    };

    public string LireEtat() => $"Windows : {Installation.Libelle(Installation.Lire())}";

    public SortieAction Executer(CancellationToken annulation)
    {
        annulation.ThrowIfCancellationRequested();
        var mode = Installation.Lire();
        if (!Installation.FinalisationPermise(mode))
            return new SortieAction(false, null, $"Refusé : Windows n'est pas en mode audit ({Installation.Libelle(mode)}).");

        // Sysprep refuse de démarrer si sa fenêtre du mode audit est ouverte : seule celle-ci est fermée.
        var autres = FermerFenetreAudit();
        if (autres.Count > 0)
            return new SortieAction(false, null, "Refusé : un Sysprep est déjà lancé avec des options (" + string.Join(" ; ", autres) + ").");

        var mises = MettreDeCoteFichiersDeReponse();
        _progression?.Invoke("Sysprep en cours : le PC s'éteindra seul à la fin.");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Officiel, "/oobe /shutdown /quiet") { UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return new SortieAction(false, null, "sysprep.exe ne s'est pas lancé");
            if (!p.WaitForExit((int)Delai.TotalMilliseconds))
                return new SortieAction(false, null, $"Sysprep toujours en cours après {Delai.TotalMinutes:0} min : regarder l'écran du PC.");
            if (p.ExitCode == 0)
                return new SortieAction(true, 0, "Sysprep terminé : extinction du PC" + (mises.Count > 0 ? $" ({mises.Count} fichier(s) de réponse mis de côté)" : ""));
            return new SortieAction(false, p.ExitCode, "Sysprep a échoué. " + DernieresErreurs());
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new SortieAction(false, null, "sysprep.exe introuvable : " + ex.Message);
        }
    }

    /// <summary>Ferme la fenêtre du mode audit ; renvoie les Sysprep lancés avec des options (laissés tels quels).</summary>
    private static List<string> FermerFenetreAudit()
    {
        var autres = new List<string>();
        try
        {
            using var recherche = new ManagementObjectSearcher("SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'sysprep.exe'");
            foreach (ManagementObject p in recherche.Get())
            {
                var chemin = p["ExecutablePath"] as string ?? "";
                var ligne = p["CommandLine"] as string ?? "";
                if (!EstFenetreAudit(chemin, ligne, Officiel))
                {
                    autres.Add(ligne.Length > 0 ? ligne : "ligne de commande illisible");
                    continue;
                }
                try
                {
                    using var proc = Process.GetProcessById(Convert.ToInt32(p["ProcessId"]));
                    proc.Kill();
                    proc.WaitForExit(10000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or Win32Exception) { }
            }
        }
        catch (ManagementException) { }
        return autres;
    }

    /// <summary>Le sysprep.exe de Windows lancé sans option (fenêtre du mode audit), et rien d'autre. Repris de PC Maintenance.</summary>
    public static bool EstFenetreAudit(string chemin, string ligneDeCommande, string officiel)
    {
        if (string.IsNullOrWhiteSpace(chemin)) return false;
        if (!string.Equals(Path.GetFullPath(chemin), Path.GetFullPath(officiel), StringComparison.OrdinalIgnoreCase)) return false;
        var ligne = ligneDeCommande.Trim();
        if (ligne.StartsWith("\"", StringComparison.Ordinal))
        {
            var fin = ligne.IndexOf('"', 1);
            ligne = fin < 0 ? "" : ligne.Substring(fin + 1);
        }
        else
        {
            var espace = ligne.IndexOf(' ');
            ligne = espace < 0 ? "" : ligne.Substring(espace + 1);
        }
        return ligne.Trim().Length == 0;
    }

    /// <summary>Renomme les fichiers de réponse en « .jvtech » (rien n'est supprimé). Renvoie ceux mis de côté.</summary>
    private static List<string> MettreDeCoteFichiersDeReponse()
    {
        var mis = new List<string>();
        foreach (var relatif in FichiersDeReponse)
        {
            var fichier = Path.Combine(Windows, relatif);
            try
            {
                if (!File.Exists(fichier)) continue;
                var cible = fichier + ".jvtech";
                if (File.Exists(cible)) File.Delete(cible);
                File.Move(fichier, cible);
                mis.Add(relatif);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return mis;
    }

    /// <summary>Dernières lignes de setuperr.log de Sysprep (cause de l'échec, souvent une application du Store).</summary>
    private static string DernieresErreurs()
    {
        var log = Path.Combine(Windows, @"System32\Sysprep\Panther\setuperr.log");
        try
        {
            if (!File.Exists(log)) return "Pas de setuperr.log.";
            var lignes = File.ReadAllLines(log).Where(l => l.Trim().Length > 0).ToList();
            return lignes.Count == 0 ? "setuperr.log vide." : "setuperr.log : " + string.Join(" | ", lignes.Skip(Math.Max(0, lignes.Count - 3)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "setuperr.log illisible : " + ex.Message;
        }
    }
}
