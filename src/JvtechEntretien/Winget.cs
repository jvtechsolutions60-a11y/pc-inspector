using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PcInspector;

namespace JvtechEntretien;

/// <summary>
/// winget lancé par l'entretien. En compte SYSTEM (tâche planifiée), winget n'est pas dans le PATH : il est cherché
/// dans C:\Program Files\WindowsApps (paquet « Programme d'installation d'application »). Seuls les logiciels
/// installés pour tout le PC sont alors visibles : ceux installés pour un seul compte ne sont pas touchés.
/// </summary>
internal static class Winget
{
    public sealed record Liste(IReadOnlyList<WingetUpdate>? MisesAJour, string? Erreur);

    /// <summary>Déjà à jour / aucune mise à jour applicable : pas un échec.</summary>
    private const int DejaAJour = unchecked((int)0x8A15002B);

    public static string? Chemin()
    {
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            var candidats = Directory.GetDirectories(apps, "Microsoft.DesktopAppInstaller_*__8wekyb3d8bbwe")
                .Select(d => (Dossier: d, Version: VersionDe(Path.GetFileName(d))))
                .Where(c => File.Exists(Path.Combine(c.Dossier, "winget.exe")))
                .OrderByDescending(c => c.Version)
                .ToList();
            if (candidats.Count > 0) return Path.Combine(candidats[0].Dossier, "winget.exe");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // Lancé à la main par le technicien : winget du compte.
        return WingetShell.Candidates().FirstOrDefault(c => c == "winget.exe" || File.Exists(c));
    }

    /// <summary>« Microsoft.DesktopAppInstaller_1.24.25200.0_x64__8wekyb3d8bbwe » donne 1.24.25200.0.</summary>
    internal static Version VersionDe(string dossier)
    {
        var parties = dossier.Split('_');
        return parties.Length > 1 && Version.TryParse(parties[1], out var v) ? v : new Version(0, 0);
    }

    /// <summary>Mises à jour proposées par winget (tous logiciels), sans rien installer.</summary>
    public static Liste MisesAJour(Action<string> journal)
    {
        var exe = Chemin();
        if (exe is null) return new Liste(null, WingetShell.MissingMessage);
        var lignes = new List<string>();
        var resultat = Lancer(exe, "upgrade --source winget --accept-source-agreements --disable-interactivity", l => lignes.Add(l), TimeSpan.FromMinutes(3));
        if (resultat is null) return new Liste(null, WingetShell.MissingMessage);
        if (resultat == -1) return new Liste(null, "winget n'a pas répondu en 3 minutes (connexion Internet ?).");
        if (WingetShell.ParseUpgradeTable(lignes) is { } table) return new Liste(table, null);
        if (resultat is 0 or WingetShell.NoApplicationsFound) return new Liste(Array.Empty<WingetUpdate>(), null);
        journal("winget : " + string.Join(" / ", lignes.Where(l => l.Trim().Length > 0).Take(5)));
        return new Liste(null, $"winget a échoué (code 0x{resultat:X8}).");
    }

    /// <summary>Met à jour un logiciel par son identifiant exact, en silencieux. Renvoie null si réussi, sinon la raison.</summary>
    public static string? MettreAJour(string id, Action<string> journal)
    {
        var exe = Chemin();
        if (exe is null) return WingetShell.MissingMessage;
        var resultat = Lancer(exe,
            $"upgrade --exact --id {id} --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
            l => { var t = l.Trim(); if (t.Length > 0 && !WingetShell.IsProgress(t)) journal("  " + t); }, TimeSpan.FromMinutes(20));
        return resultat switch
        {
            0 or DejaAJour => null,
            null => "winget introuvable",
            -1 => "pas terminé en 20 minutes",
            var code => $"code 0x{code:X8}",
        };
    }

    /// <summary>Code de sortie ; null si winget n'a pas démarré ; -1 s'il ne répondait plus (arrêté).</summary>
    private static int? Lancer(string exe, string arguments, Action<string> ligne, TimeSpan delai)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
            });
            if (p is null) return null;
            p.StandardInput.Close();
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) ligne(WingetShell.Visible(e.Data)); };
            p.BeginOutputReadLine();
            if (!p.WaitForExit((int)delai.TotalMilliseconds))
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
                return -1;
            }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
