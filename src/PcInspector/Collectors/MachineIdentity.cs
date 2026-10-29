using System.Linq;

namespace PcInspector.Collectors;

/// <summary>
/// Marque, modèle et numéro de série du PC. Sur un PC monté, le BIOS laisse des textes génériques
/// (« System manufacturer », « System Serial Number », « To Be Filled By O.E.M. ») : on prend alors la carte mère.
/// </summary>
internal static class MachineIdentity
{
    public static string Model()
    {
        var system = Wmi.QueryOptional("SELECT Manufacturer, Model FROM Win32_ComputerSystem").FirstOrDefault();
        var manufacturer = Clean(system?.Str("Manufacturer"));
        var model = Clean(system?.Str("Model"));
        if (manufacturer is not null && model is not null) return $"{manufacturer} {model}";
        var board = Wmi.QueryOptional("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        var boardName = $"{Clean(board?.Str("Manufacturer"))} {Clean(board?.Str("Product"))}".Trim();
        if (boardName.Length > 0) return "PC monté · carte mère " + boardName;
        return $"{manufacturer} {model}".Trim();
    }

    /// <summary>Numéro de série du PC, sinon de la carte mère, sinon vide (à noter à la main).</summary>
    public static string Serial()
    {
        if (Clean(Wmi.QueryOptional("SELECT SerialNumber FROM Win32_BIOS").FirstOrDefault()?.Str("SerialNumber")) is { } serial) return serial;
        return Clean(Wmi.QueryOptional("SELECT SerialNumber FROM Win32_BaseBoard").FirstOrDefault()?.Str("SerialNumber")) is { } board
            ? board + " (carte mère)"
            : "";
    }

    /// <summary>Null si le texte est vide ou un texte générique laissé par le fabricant.</summary>
    public static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || IsPlaceholder(text!) ? null : text;
    }

    public static bool IsPlaceholder(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        return s.Contains("default") || s.Contains("o.e.m") || s.Contains("to be filled") || s.Contains("not applicable")
               || s.StartsWith("system ") || s is "0" or "none" or "n/a" or "base board" || s.Trim('0', ' ', '.', '-').Length == 0;
    }
}
