namespace PcInspector.Models;

/// <summary>Une ligne "libellé : valeur" affichée dans un onglet et dans le rapport.</summary>
public sealed record InfoItem(string Label, string Value);

/// <summary>Un groupe de lignes (par ex. une barrette de RAM, un disque).</summary>
public sealed class InfoGroup
{
    public InfoGroup(string title) => Title = title;

    public string Title { get; }
    public List<InfoItem> Items { get; } = new();

    public InfoGroup Add(string label, string? value)
    {
        Items.Add(new InfoItem(label, string.IsNullOrWhiteSpace(value) ? "—" : value.Trim()));
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
