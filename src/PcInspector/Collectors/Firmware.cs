using Microsoft.Win32;

namespace PcInspector.Collectors;

/// <summary>Mode de démarrage et Secure Boot, partagés par les onglets Carte mère et Windows 11.</summary>
internal static class Firmware
{
    private const string SecureBootKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";

    /// <summary>La clé Secure Boot n'existe que sur les PC démarrés en UEFI.</summary>
    public static bool IsUefi()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SecureBootKey);
        return key is not null;
    }

    /// <summary>true/false si le PC gère Secure Boot, null sinon.</summary>
    public static bool? SecureBootEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SecureBootKey);
        return key?.GetValue("UEFISecureBootEnabled") switch
        {
            1 => true,
            0 => false,
            _ => null,
        };
    }

    public static string SecureBootText() => SecureBootEnabled() switch
    {
        true => "Activé",
        false => "Désactivé",
        null => "Non pris en charge",
    };
}
