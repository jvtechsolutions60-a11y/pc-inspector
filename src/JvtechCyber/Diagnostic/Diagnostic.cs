using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PcInspector;

namespace JvtechCyber.Diagnostic;

/// <summary>Où en est une menace : exposée, en partie couverte ou couverte.</summary>
public enum Exposure { Exposed, Partial, Covered }

/// <summary>
/// Un diagnostic d'entreprise à une date : niveau atteint par thème, remarques, postes importés depuis PC Sécurité.
/// Enregistré en texte simple dans Rapports\Diagnostic cyber\Entreprise\diagnostic-AAAA-MM-JJ.txt.
/// </summary>
public sealed class CyberDiagnostic
{
    public string Company { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Activity { get; set; } = "";
    public int Workstations { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string Notes { get; set; } = "";
    public Dictionary<string, int> Levels { get; } = new();
    public Dictionary<string, string> Remarks { get; } = new();
    public List<PosteResult> Postes { get; } = new();

    public static string Root => Path.Combine(AppPaths.ReportsRoot, "Diagnostic cyber");

    public string Folder => Path.Combine(Root, Safe(Company.Length > 0 ? Company : "Sans nom"));
    public string FilePath => Path.Combine(Folder, $"diagnostic-{Date:yyyy-MM-dd}.txt");

    public int Level(string themeId) => Levels.TryGetValue(themeId, out var level) ? level : 0;

    /// <summary>Note sur 100, les thèmes essentiels (sauvegardes, mises à jour, comptes, arnaques) comptant double.</summary>
    public int Score
    {
        get
        {
            var max = Referentiel.Themes.Sum(t => 3 * t.Weight);
            return (int)Math.Round(100.0 * Referentiel.Themes.Sum(t => Level(t.Id) * t.Weight) / max);
        }
    }

    public static string Grade(int score) => score switch
    {
        >= 80 => "Bon niveau",
        >= 55 => "Correct, à renforcer",
        >= 30 => "Fragile",
        _ => "Très exposé",
    };

    /// <summary>Une menace est couverte quand tous ses thèmes sont au moins au niveau 2, exposée si l'un d'eux est à 0.</summary>
    public Exposure ExposureOf(Threat threat)
    {
        var levels = threat.Themes.Select(Level).ToList();
        if (levels.Any(l => l == 0)) return Exposure.Exposed;
        return levels.All(l => l >= 2) ? Exposure.Covered : Exposure.Partial;
    }

    /// <summary>Prochaine étape de chaque thème, des plus urgents (niveau bas, thème essentiel) aux moins urgents.</summary>
    public IEnumerable<(Theme Theme, Level Next)> Plan() =>
        Referentiel.Themes
            .Where(t => Level(t.Id) < 3)
            .OrderBy(t => Level(t.Id)).ThenByDescending(t => t.Weight)
            .Select(t => (t, t.Levels[Level(t.Id) + 1]));

    // ---------- Enregistrement ----------

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var lines = new List<string>
        {
            "# Diagnostic cyber JVTECH",
            "Entreprise=" + OneLine(Company),
            "Interlocuteur=" + OneLine(ContactName),
            "Activite=" + OneLine(Activity),
            "Postes=" + Workstations,
            "Date=" + Date.ToString("yyyy-MM-dd"),
            "Notes=" + OneLine(Notes),
        };
        foreach (var theme in Referentiel.Themes)
        {
            lines.Add($"Niveau.{theme.Id}={Level(theme.Id)}");
            if (Remarks.TryGetValue(theme.Id, out var remark) && remark.Trim().Length > 0) lines.Add($"Remarque.{theme.Id}={OneLine(remark)}");
        }
        foreach (var poste in Postes) lines.AddRange(poste.Serialize());
        File.WriteAllLines(FilePath, lines, new UTF8Encoding(true));
    }

    public static CyberDiagnostic Load(string path)
    {
        var diagnostic = new CyberDiagnostic();
        PosteResult? poste = null;
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2 || line.StartsWith("#")) continue;
            var (key, value) = (parts[0].Trim(), parts[1]);
            switch (key)
            {
                case "Entreprise": diagnostic.Company = value; break;
                case "Interlocuteur": diagnostic.ContactName = value; break;
                case "Activite": diagnostic.Activity = value; break;
                case "Postes": diagnostic.Workstations = int.TryParse(value, out var n) ? n : 0; break;
                case "Date": diagnostic.Date = DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.Today; break;
                case "Notes": diagnostic.Notes = value.Replace(" ¶ ", "\n"); break;
                case "Poste":
                    poste = new PosteResult { Name = value };
                    diagnostic.Postes.Add(poste);
                    break;
                default:
                    if (key.StartsWith("Niveau.") && int.TryParse(value, out var level)) diagnostic.Levels[key.Substring(7)] = Math.Max(0, Math.Min(3, level));
                    else if (key.StartsWith("Remarque.")) diagnostic.Remarks[key.Substring(9)] = value.Replace(" ¶ ", "\n");
                    else poste?.Read(key, value);
                    break;
            }
        }
        return diagnostic;
    }

    /// <summary>Diagnostics déjà faits pour une entreprise, du plus récent au plus ancien.</summary>
    public static List<string> History(string company)
    {
        var folder = Path.Combine(Root, Safe(company));
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "diagnostic-*.txt").OrderByDescending(f => f, StringComparer.Ordinal).ToList()
            : new List<string>();
    }

    public static List<string> Companies() =>
        Directory.Exists(Root)
            ? Directory.GetDirectories(Root).Where(d => History(Path.GetFileName(d)).Count > 0)
                .Select(d => Load(History(Path.GetFileName(d))[0]).Company).Where(c => c.Length > 0)
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToList()
            : new List<string>();

    /// <summary>Le diagnostic précédent de la même entreprise, pour montrer la progression.</summary>
    public CyberDiagnostic? Previous()
    {
        var previous = History(Company).FirstOrDefault(f => string.CompareOrdinal(Path.GetFileName(f), Path.GetFileName(FilePath)) < 0);
        try { return previous is null ? null : Load(previous); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name.Trim();
    }

    internal static string OneLine(string text) => text.Replace("\r", "").Replace("\n", " ¶ ").Trim();
}
