using System;

namespace PcInspector.Collectors;

/// <summary>Page de vérification de garantie du fabricant, pré-remplie avec le numéro de série quand le site le permet.</summary>
internal static class Warranty
{
    public static string? Url(string? manufacturer, string? serial)
    {
        if (string.IsNullOrWhiteSpace(manufacturer)) return null;
        var m = manufacturer!.ToLowerInvariant();
        var sn = Uri.EscapeDataString((serial ?? "").Trim());
        var hasSerial = sn.Length > 0 && !IsPlaceholder(serial!);

        if (m.Contains("dell"))
            return hasSerial ? $"https://www.dell.com/support/home/fr-fr/product-support/servicetag/{sn}/overview" : "https://www.dell.com/support/home/fr-fr";
        if (m.Contains("lenovo"))
            return hasSerial ? $"https://pcsupport.lenovo.com/fr/fr/products/{sn}/warranty" : "https://pcsupport.lenovo.com/fr/fr/warrantylookup";
        if (m.Contains("hp") || m.Contains("hewlett"))
            return "https://support.hp.com/fr-fr/check-warranty";
        if (m.Contains("asus"))
            return "https://www.asus.com/fr/support/warranty-status-inquiry/";
        if (m.Contains("acer"))
            return "https://www.acer.com/fr-fr/support/warranty";
        if (m.Contains("micro-star") || m.Contains("msi"))
            return "https://www.msi.com/support/warranty-check";
        if (m.Contains("microsoft"))
            return "https://mybusinessservice.surface.com/fr-FR/CheckWarranty/CheckWarranty";
        if (m.Contains("toshiba") || m.Contains("dynabook"))
            return "https://support.dynabook.com/warrantycheck";
        if (m.Contains("samsung"))
            return "https://www.samsung.com/fr/support/";
        if (m.Contains("apple"))
            return "https://checkcoverage.apple.com/fr/fr/";
        return null;
    }

    /// <summary>Les PC montés et certains OEM laissent un texte générique au lieu d'un vrai numéro.</summary>
    private static bool IsPlaceholder(string serial)
    {
        var s = serial.Trim().ToLowerInvariant();
        return s.Contains("default") || s.Contains("o.e.m") || s.Contains("to be filled") || s == "0" || s.Trim('0').Length == 0 || s == "system serial number";
    }
}
