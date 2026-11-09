using System;
using System.Collections.Generic;

namespace PcInspector.Models;

/// <summary>Une ligne "libellé : valeur" affichée dans un onglet et dans le rapport.</summary>
public sealed record InfoItem(string Label, string Value, string? Url = null)
{
    /// <summary>Barre de graphique sous la valeur (0 à 1), null : pas de barre.</summary>
    public double? Bar { get; init; }

    /// <summary>Couleur de la barre : "ok", "warning", "critical" ou "accent" (moyenne).</summary>
    public string? BarLevel { get; init; }
}

/// <summary>Un groupe de lignes (par ex. une barrette de RAM, un disque).</summary>
public sealed class InfoGroup
{
    public InfoGroup(string title) => Title = title;

    public string Title { get; }
    public List<InfoItem> Items { get; } = new();

    public InfoGroup Add(string label, string? value)
    {
        Items.Add(new InfoItem(label, string.IsNullOrWhiteSpace(value) ? "—" : value!.Trim()));
        return this;
    }

    /// <summary>Ligne avec une barre horizontale (graphique simple, lisible dans l'onglet).</summary>
    public InfoGroup AddBar(string label, string value, double ratio, string level)
    {
        Items.Add(new InfoItem(label, value) { Bar = Math.Max(0.02, Math.Min(1, ratio)), BarLevel = level });
        return this;
    }

    public InfoGroup AddLink(string label, string text, string url)
    {
        Items.Add(new InfoItem(label, text, url));
        return this;
    }
}

/// <summary>Le contenu d'un onglet : CPU, RAM, Stockage…</summary>
public sealed class InfoSection
{
    public InfoSection(string title) => Title = title;

    public string Title { get; }
    public List<InfoGroup> Groups { get; } = new();
    public string? Error { get; set; }
}
