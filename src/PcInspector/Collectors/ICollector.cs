using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Chaque composant (CPU, RAM, GPU…) a son collecteur. L'étape 2 en ajoute un par onglet.
/// </summary>
public interface ICollector
{
    string Title { get; }
    string Icon { get; }
    InfoSection Collect();
}
