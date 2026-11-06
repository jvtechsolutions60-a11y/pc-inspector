using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PcMigration.Migration;

/// <summary>winget : export de la liste des applications sur l'ancien PC, installation sur le nouveau.</summary>
internal static class Winget
{
    /// <summary>Codes winget « déjà installé » / « rien à mettre à jour ».</summary>
    private static readonly HashSet<int> AlreadyInstalled = new() { unchecked((int)0x8A15002B), unchecked((int)0x8A150061) };

    public static int? Run(string arguments, List<string> output, TimeSpan timeout)
    {
        foreach (var exe in Candidates())
        {
            var lines = new List<string>();
            var code = Tools.Run(exe, arguments, lines, timeout, Encoding.UTF8);
            if (code is null && lines.Any(l => l.Contains("introuvable"))) continue;
            output.AddRange(lines.Select(Visible).Where(l => l.Length > 0 && !IsProgress(l)));
            return code;
        }
        output.Add("winget est introuvable sur ce PC (installer « Programme d'installation d'application » depuis le Microsoft Store).");
        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        yield return "winget.exe";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
    }

    public static List<string> ReadIds(string file)
    {
        try
        {
            return Regex.Matches(File.ReadAllText(file), "\"PackageIdentifier\"\\s*:\\s*\"([^\"]+)\"")
                .Cast<Match>().Select(m => m.Groups[1].Value)
                // Un identifiant winget n'a que lettres, chiffres et . _ + - : le reste (fichier modifié) est ignoré.
                .Where(id => Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }

    /// <summary>Installe une application, sans fenêtre ni question. Renvoie le résultat lisible.</summary>
    public static string Install(string id, IProgress<string> log)
    {
        var output = new List<string>();
        var code = Run($"install --id {Tools.Quote(id)} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            output, TimeSpan.FromMinutes(30));
        foreach (var line in output.Skip(Math.Max(0, output.Count - 3))) log.Report("      " + line);
        return code switch
        {
            0 => "installée",
            null => "non installée (winget introuvable ou trop long)",
            _ when AlreadyInstalled.Contains(code.Value) => "déjà installée",
            _ => $"échec (code 0x{code.Value:X8})",
        };
    }

    private static string Visible(string line) => line.Substring(line.LastIndexOf('\r') + 1).Trim();

    /// <summary>Barres de progression et petit moulinet « - \ | / » : inutiles dans le journal.</summary>
    private static bool IsProgress(string text) =>
        text.Length <= 2 || text.Contains("█") || text.Contains("▒") || Regex.IsMatch(text, @"^[\d.,]+\s*(KB|MB|GB|Ko|Mo|Go)\s*/");
}
