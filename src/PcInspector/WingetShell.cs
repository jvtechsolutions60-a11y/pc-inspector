using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PcInspector;

/// <summary>Un logiciel pour lequel winget connaît une version plus récente.</summary>
public sealed record WingetUpdate(string Name, string Id, string Installed, string Available);

/// <summary>
/// Ce que PC Inspector, PC Maintenance et PC Migration font tous avec winget : le trouver, le lancer, nettoyer ce
/// qu'il affiche et lire son tableau de mises à jour. Chaque logiciel garde ses propres commandes.
/// </summary>
public static class WingetShell
{
    /// <summary>« Aucun package installé ne correspond » : winget répond ainsi quand tout est à jour.</summary>
    public const int NoApplicationsFound = unchecked((int)0x8A150014);

    public const string MissingMessage = "winget n'est pas installé sur ce PC (« Programme d'installation d'application » sur le Microsoft Store).";

    /// <summary>winget est une application du Store : il est dans le PATH du compte, sinon dans WindowsApps.</summary>
    public static IEnumerable<string> Candidates()
    {
        yield return "winget.exe";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) yield return Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
    }

    /// <summary>Lance winget sans fenêtre (sortie lue en UTF-8). Null : winget introuvable.</summary>
    public static Process? Start(string arguments)
    {
        foreach (var exe in Candidates())
        {
            try
            {
                return Process.Start(new ProcessStartInfo(exe, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8,
                });
            }
            catch (Win32Exception)
            {
                // winget introuvable à cet emplacement : essayer le suivant.
            }
        }
        return null;
    }

    /// <summary>Ce qui reste affiché d'une ligne que winget redessine avec \r (barres de progression).</summary>
    public static string Visible(string line) => line.Substring(line.LastIndexOf('\r') + 1).TrimEnd();

    /// <summary>Toutes les lignes affichées, sans les animations de progression.</summary>
    public static List<string> Lines(string output) =>
        output.Replace("\r\n", "\n").Split('\n').Select(Visible).ToList();

    /// <summary>Barres de progression, tailles téléchargées et petit moulinet « - \ | / » : inutiles dans un journal.</summary>
    public static bool IsProgress(string text)
    {
        text = text.Trim();
        return text.Length <= 2 || text.IndexOfAny(new[] { '█', '▒' }) >= 0
               || Regex.IsMatch(text, @"^[\d.,]+\s*(KB|MB|GB|Ko|Mo|Go)\s*/");
    }

    /// <summary>
    /// Lit le tableau de « winget upgrade » : une ligne d'en-têtes (traduits selon la langue de Windows), une ligne de
    /// tirets, puis une ligne par logiciel. Les colonnes sont repérées par la position des en-têtes.
    /// Renvoie null s'il n'y a pas de tableau.
    /// </summary>
    public static List<WingetUpdate>? ParseUpgradeTable(IReadOnlyList<string> lines)
    {
        var dashes = -1;
        for (var i = 1; i < lines.Count; i++)
        {
            if (lines[i].Length >= 10 && lines[i].All(c => c == '-')) { dashes = i; break; }
        }
        if (dashes < 0) return null;

        var header = lines[dashes - 1];
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        }
        // Nom, Id, Version, Disponible (et Source quand plusieurs sources sont interrogées).
        if (starts.Count < 4) return null;

        string Column(string line, int index)
        {
            var from = starts[index];
            if (from >= line.Length) return "";
            var to = index + 1 < starts.Count ? Math.Min(starts[index + 1], line.Length) : line.Length;
            return line.Substring(from, to - from).Trim();
        }

        var updates = new List<WingetUpdate>();
        for (var i = dashes + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            // La ligne « N mises à jour disponibles » qui suit le tableau n'atteint pas la colonne Disponible.
            if (line.Length == 0 || Column(line, 3).Length == 0) break;
            updates.Add(new WingetUpdate(Column(line, 0), Column(line, 1), Column(line, 2), Column(line, 3)));
        }
        return updates;
    }
}
