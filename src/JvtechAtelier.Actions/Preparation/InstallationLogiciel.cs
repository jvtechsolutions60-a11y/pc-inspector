using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PcInspector;
using PcMaintenance.Maintenance;

namespace Jvtech.Atelier.Actions.Preparation;

/// <summary>Un logiciel proposé par Atelier, installé avec winget par son identifiant exact.</summary>
public sealed record Logiciel(string Id, string Nom, bool DeBase);

/// <summary>Pack de base (7-Zip, VLC : toujours proposés) et logiciels optionnels (décochés par défaut).</summary>
public static class PackAtelier
{
    public static IReadOnlyList<Logiciel> Logiciels { get; } = new[]
    {
        new Logiciel("7zip.7zip", "7-Zip", true),
        new Logiciel("VideoLAN.VLC", "VLC", true),
        new Logiciel("Google.Chrome", "Google Chrome", false),
        new Logiciel("Mozilla.Firefox", "Mozilla Firefox", false),
        new Logiciel("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", false),
        new Logiciel("TheDocumentFoundation.LibreOffice", "LibreOffice", false),
    };

    public static IEnumerable<Logiciel> DeBase => Logiciels.Where(l => l.DeBase);
}

/// <summary>Ce que veut dire le code de sortie de winget pour une installation.</summary>
public static class CodesWinget
{
    /// <summary>Aucune version plus récente : le logiciel est déjà là.</summary>
    public const int DejaAJour = unchecked((int)0x8A15002B);
    /// <summary>Paquet déjà installé.</summary>
    public const int DejaInstalle = unchecked((int)0x8A150061);

    public static (bool Reussi, string Texte) Interpreter(int? code) => code switch
    {
        null => (false, WingetShell.MissingMessage),
        0 => (true, "installé"),
        DejaAJour or DejaInstalle => (true, "déjà installé"),
        WingetShell.NoApplicationsFound => (false, "introuvable dans winget (identifiant ?)"),
        _ => (false, $"échec, code winget 0x{code.Value:X8}"),
    };
}

/// <summary>Installation silencieuse d'un logiciel avec winget (source « winget » seulement, comme PC Maintenance).</summary>
public sealed class InstallationLogiciel : IActionAtelier
{
    private static readonly TimeSpan DelaiLecture = TimeSpan.FromSeconds(60);
    private readonly Logiciel _logiciel;
    private readonly Action<string>? _progression;

    public InstallationLogiciel(Logiciel logiciel, Action<string>? progression = null)
    {
        _logiciel = logiciel;
        _progression = progression;
    }

    public string Id => "logiciel:" + _logiciel.Id;
    public string Libelle => _logiciel.Nom;
    public bool DemandeAdministrateur => true;

    /// <summary>Présence du logiciel selon winget (« winget list »), en lecture seule.</summary>
    public string LireEtat()
    {
        using var p = WingetShell.Start($"list --exact --id {_logiciel.Id} --source winget --accept-source-agreements --disable-interactivity");
        if (p is null) return "winget introuvable";
        p.StandardInput.Close();
        var sortie = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit((int)DelaiLecture.TotalMilliseconds))
        {
            try { p.Kill(); } catch (InvalidOperationException) { }
            return "winget ne répond pas";
        }
        _ = sortie.Result;
        return p.ExitCode switch
        {
            0 => $"{_logiciel.Nom} présent",
            WingetShell.NoApplicationsFound => $"{_logiciel.Nom} absent",
            var c => $"présence inconnue (code 0x{c:X8})",
        };
    }

    public SortieAction Executer(CancellationToken annulation)
    {
        annulation.ThrowIfCancellationRequested();
        EnregistrerWingetSiAbsent(_progression);
        var dernieres = new List<string>();
        var journal = new Progress<string>(l =>
        {
            if (string.IsNullOrWhiteSpace(l)) return;
            dernieres.Add(l);
            _progression?.Invoke(l);
        });
        var code = Winget.InstallAsync(_logiciel.Id, journal).GetAwaiter().GetResult();
        var (reussi, texte) = CodesWinget.Interpreter(code);
        return new SortieAction(reussi, code, texte);
    }

    private static bool _enregistrementTente;

    /// <summary>
    /// En mode audit, « Programme d'installation d'application » (winget) n'est pas encore enregistré pour le compte :
    /// on l'enregistre une fois (comme PC Maintenance), sans rien télécharger.
    /// </summary>
    private static void EnregistrerWingetSiAbsent(Action<string>? progression)
    {
        if (_enregistrementTente) return;
        using (var test = WingetShell.Start("--version"))
        {
            if (test is not null)
            {
                test.StandardInput.Close();
                test.WaitForExit(30000);
                return;
            }
        }
        _enregistrementTente = true;
        progression?.Invoke("winget absent pour ce compte : enregistrement de « Programme d'installation d'application »…");
        var powershell = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(powershell,
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction SilentlyContinue\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is not null && !p.WaitForExit(180000))
            {
                try { p.Kill(); } catch (InvalidOperationException) { }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            progression?.Invoke("Enregistrement de winget impossible : " + ex.Message);
        }
    }
}
