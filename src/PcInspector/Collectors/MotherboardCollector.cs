using System.Linq;
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
                .Add("Mode", Firmware.IsUefi() ? "UEFI" : "Legacy (BIOS)")
                .Add("Secure Boot", Firmware.SecureBootText()));
        }

        var system = Wmi.Query("SELECT * FROM Win32_ComputerSystem").FirstOrDefault();
        if (system is not null)
        {
            var product = Wmi.Query("SELECT IdentifyingNumber FROM Win32_ComputerSystemProduct").FirstOrDefault();
            var serial = product?.Str("IdentifyingNumber") ?? bios?.Str("SerialNumber");
            var group = new InfoGroup("Ordinateur")
                .Add("Fabricant", system.Str("Manufacturer"))
                .Add("Modèle", system.Str("Model"))
                .Add("Numéro de série", serial)
                .Add("Nom du PC", system.Str("Name"))
                .Add("Type", system.Str("SystemType"));
            if (Warranty.Url(system.Str("Manufacturer"), serial) is { } url)
                group.AddLink("Garantie", "Vérifier sur le site du fabricant", url);
            section.Groups.Add(group);
        }

        return section;
    }
}
