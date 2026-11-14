using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace JvtechCyber.Diagnostic;

/// <summary>Un contrôle de PC Sécurité sur un poste : nom, feu (Ok, Warning, Danger, Info) et ce qui a été vu.</summary>
public sealed record PosteCheck(string Name, string Status, string Detail)
{
    public bool IsProblem => Status is "Danger" or "Warning";
}

/// <summary>
/// Résultat de PC Sécurité pour un poste, exporté par son bouton « Pour le diagnostic cyber » (fichier .jvsec).
/// Format texte : Poste=, DatePoste=, Windows=, puis une ligne Controle=Nom|Feu|Détail par contrôle.
/// </summary>
public sealed class PosteResult
{
    public string Name { get; set; } = "";
    public string Windows { get; set; } = "";
    public DateTime? Date { get; set; }
    public List<PosteCheck> Checks { get; } = new();

    public int Problems => Checks.Count(c => c.IsProblem);

    public static PosteResult Import(string path)
    {
        var poste = new PosteResult { Name = Path.GetFileNameWithoutExtension(path) };
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2) continue;
            if (parts[0].Trim() == "Poste") poste.Name = parts[1].Trim();
            else poste.Read(parts[0].Trim(), parts[1]);
        }
        if (poste.Checks.Count == 0) throw new InvalidDataException("Ce fichier ne vient pas de PC Sécurité.");
        return poste;
    }

    internal void Read(string key, string value)
    {
        switch (key)
        {
            case "Windows": Windows = value.Trim(); break;
            case "DatePoste":
                Date = DateTime.TryParse(value, out var d) ? d : null;
                break;
            case "Controle":
                var p = value.Split(new[] { '|' }, 3);
                if (p.Length == 3) Checks.Add(new PosteCheck(p[0].Trim(), p[1].Trim(), p[2].Trim()));
                break;
        }
    }

    internal IEnumerable<string> Serialize()
    {
        yield return "Poste=" + CyberDiagnostic.OneLine(Name);
        yield return "Windows=" + CyberDiagnostic.OneLine(Windows);
        if (Date is { } date) yield return "DatePoste=" + date.ToString("yyyy-MM-dd HH:mm");
        foreach (var c in Checks) yield return $"Controle={c.Name}|{c.Status}|{CyberDiagnostic.OneLine(c.Detail)}";
    }

    /// <summary>
    /// Constat sur l'ensemble des postes pour un thème : « 3 postes sur 8 : Antivirus (Désactivé)… », ou null si
    /// aucun contrôle importé ne concerne ce thème.
    /// </summary>
    public static string? Finding(Theme theme, IReadOnlyList<PosteResult> postes)
    {
        if (theme.Checks.Length == 0 || postes.Count == 0) return null;
        var concerned = postes.Where(p => p.Checks.Any(c => theme.Checks.Contains(c.Name))).ToList();
        if (concerned.Count == 0) return null;
        var withProblems = concerned
            .Select(p => (p.Name, Problems: p.Checks.Where(c => theme.Checks.Contains(c.Name) && c.IsProblem).ToList()))
            .Where(p => p.Problems.Count > 0)
            .ToList();
        if (withProblems.Count == 0) return $"Postes vérifiés : {concerned.Count}/{concerned.Count} en ordre.";
        var details = withProblems.Take(4).Select(p => $"{p.Name} ({string.Join(", ", p.Problems.Select(c => c.Name))})");
        return $"Postes vérifiés : {withProblems.Count}/{concerned.Count} à corriger · " + string.Join(" · ", details)
               + (withProblems.Count > 4 ? " · …" : "");
    }

    /// <summary>Niveau maximal compatible avec ce que montrent les postes (null : les postes ne disent rien).</summary>
    public static int? Ceiling(Theme theme, IReadOnlyList<PosteResult> postes)
    {
        var concerned = postes.SelectMany(p => p.Checks).Where(c => theme.Checks.Contains(c.Name)).ToList();
        if (concerned.Count == 0) return null;
        if (concerned.Any(c => c.Status == "Danger")) return 0;
        return concerned.Any(c => c.Status == "Warning") ? 1 : null;
    }
}
