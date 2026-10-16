using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Onglet pas encore implémenté (remplacé par un vrai collecteur à l'étape 2).</summary>
public sealed class PlaceholderCollector : ICollector
{
    public PlaceholderCollector(string title, string icon)
    {
        Title = title;
        Icon = icon;
    }

    public string Title { get; }
    public string Icon { get; }

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        section.Groups.Add(new InfoGroup("À venir").Add("Statut", "Cet onglet sera rempli à l'étape 2."));
        return section;
    }
}
