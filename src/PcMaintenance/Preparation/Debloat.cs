using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcInspector;

namespace PcMaintenance.Preparation;

/// <summary>
/// Retire les applications préinstallées inutiles, pour tous les comptes et pour les futurs comptes
/// (sinon l'acheteur les retrouverait, et Sysprep refuse de remettre le PC au premier démarrage).
/// La liste se modifie dans debloat.ini à côté du .exe : un nom de paquet par ligne, * accepté.
/// </summary>
internal static class Debloat
{
    private static readonly string[] Defaults =
    {
        "Clipchamp.Clipchamp",
        "Microsoft.BingNews",
        "Microsoft.BingSearch",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.MixedReality.Portal",
        "Microsoft.People",
        "Microsoft.PowerAutomateDesktop",
        "Microsoft.SkypeApp",
        "Microsoft.Todos",
        "Microsoft.Windows.DevHome",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.549981C3F5F10",
        "MicrosoftCorporationII.MicrosoftFamily",
        "MicrosoftTeams",
        "MSTeams",
    };

    public static string FilePath => Path.Combine(AppPaths.ExeFolder, "debloat.ini");

    public static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return Defaults;
            var names = File.ReadAllLines(FilePath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .ToList();
            return names.Count > 0 ? names : Defaults;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Defaults;
        }
    }

    /// <summary>Renvoie les applications retirées et les erreurs, une ligne chacune.</summary>
    public static (List<string> Removed, List<string> Errors) Run(IProgress<string> log)
    {
        var names = Load();
        log.Report($"{names.Count} application(s) à retirer si elles sont présentes.");
        var script = "$names = @(" + string.Join(",", names.Select(PowerShell.Quote)) + ")\n" + @"
foreach ($n in $names) {
  $found = $false
  Get-AppxPackage -AllUsers -Name $n -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction Stop; $found = $true }
    catch { Write-Output ('ERREUR|' + $n + '|' + $_.Exception.Message) }
  }
  Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $n } | ForEach-Object {
    try { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop | Out-Null; $found = $true }
    catch { Write-Output ('ERREUR|' + $n + '|' + $_.Exception.Message) }
  }
  if ($found) { Write-Output ('RETIRE|' + $n) }
}";
        var removed = new List<string>();
        var errors = new List<string>();
        foreach (var line in PowerShell.Run(script, TimeSpan.FromMinutes(15)))
        {
            var parts = line.Split(new[] { '|' }, 3);
            if (parts[0] == "RETIRE" && parts.Length >= 2) { removed.Add(parts[1]); log.Report("Retiré : " + parts[1]); }
            else if (parts[0] == "ERREUR" && parts.Length == 3) { errors.Add($"{parts[1]} : {parts[2]}"); log.Report($"Échec pour {parts[1]} : {parts[2]}"); }
        }
        return (removed, errors);
    }
}
