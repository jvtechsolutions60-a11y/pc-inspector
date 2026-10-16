using System.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

public sealed class MotherboardCollector : ICollector
{
    public string Title => "Carte mère";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);

        var board = Wmi.Query("SELECT * FROM Win32_BaseBoard").FirstOrDefault();
        if (board is not null)
        {
            section.Groups.Add(new InfoGroup("Carte mère")
                .Add("Fabricant", board.Str("Manufacturer"))
                .Add("Modèle", board.Str("Product"))
                .Add("Version", board.Str("Version"))
                .Add("Numéro de série", board.Str("SerialNumber")));
        }

        var bios = Wmi.Query("SELECT * FROM Win32_BIOS").FirstOrDefault();
        if (bios is not null)
        {
            section.Groups.Add(new InfoGroup("BIOS")
                .Add("Fabricant", bios.Str("Manufacturer"))
                .Add("Version", bios.Str("SMBIOSBIOSVersion"))
                .Add("Date", bios.Date("ReleaseDate"))
                .Add("Mode", FirmwareMode())
                .Add("Secure Boot", SecureBoot()));
        }

        var system = Wmi.Query("SELECT * FROM Win32_ComputerSystem").FirstOrDefault();
        if (system is not null)
        {
            var product = Wmi.Query("SELECT IdentifyingNumber FROM Win32_ComputerSystemProduct").FirstOrDefault();
            section.Groups.Add(new InfoGroup("Ordinateur")
                .Add("Fabricant", system.Str("Manufacturer"))
                .Add("Modèle", system.Str("Model"))
                .Add("Numéro de série", product?.Str("IdentifyingNumber") ?? bios?.Str("SerialNumber"))
                .Add("Nom du PC", system.Str("Name"))
                .Add("Type", system.Str("SystemType")));
        }

        return section;
    }

    private static string FirmwareMode()
    {
        // Cette clé n'existe que sur les PC démarrés en UEFI.
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return key is null ? "Legacy (BIOS)" : "UEFI";
    }

    private static string SecureBoot()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return key?.GetValue("UEFISecureBootEnabled") switch
        {
            1 => "Activé",
            0 => "Désactivé",
            _ => "Non pris en charge",
        };
    }
}
