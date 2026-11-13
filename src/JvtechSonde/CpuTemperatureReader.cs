using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Win32;
using PcInspector;
using PcInspector.Collectors;

namespace JvtechSonde;

/// <summary>
/// Température du processeur, de la plus précise à la plus approchée :
/// 1. le module « capteurs » (LibreHardwareMonitor), seulement si le pilote PawnIO est déjà installé sur le PC ;
/// 2. sinon les zones thermiques ACPI de Windows, sans pilote (valeur approchée, absente sur beaucoup de PC).
/// La sonde n'installe jamais de pilote : sans PawnIO, LibreHardwareMonitor n'est même pas chargé.
/// </summary>
internal static class CpuTemperatureReader
{
    /// <summary>Dossier du module optionnel, à côté du .exe (livré dans le .zip de la sonde).</summary>
    public static string ModuleFolder => Path.Combine(AppPaths.ExeFolder, "capteurs");

    private const string ModuleFile = "JvtechSonde.Capteurs.dll";

    public static (double? Celsius, string Source) Read()
    {
        if (IsPawnIoInstalled() && File.Exists(Path.Combine(ModuleFolder, ModuleFile)))
        {
            var precise = FromModule();
            if (precise is { } p) return (p, "sonde du processeur, LibreHardwareMonitor + PawnIO");
        }
        var acpi = FromAcpi();
        return acpi is { } a ? (a, "zone thermique ACPI, valeur approchée") : (null, "");
    }

    /// <summary>
    /// Le pilote PawnIO est installé (à part, par son installateur officiel signé). Même clé que celle que
    /// LibreHardwareMonitor 0.9.6 consulte : l'entrée « PawnIO » des programmes installés.
    /// </summary>
    public static bool IsPawnIoInstalled()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return key?.GetValue("DisplayVersion") is string version && version.Length > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    private static double? FromAcpi()
    {
        double? max = null;
        foreach (var zone in Wmi.QueryOptional("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", Wmi.WmiScope))
        {
            // Dixièmes de kelvin ; certaines cartes mères renvoient une valeur fixe absurde, écartée ici.
            if (zone.ULong("CurrentTemperature") is not { } tenths || tenths == 0) continue;
            var celsius = tenths / 10.0 - 273.15;
            if (celsius is < 5 or > 125) continue;
            if (max is null || celsius > max) max = celsius;
        }
        return max;
    }

    /// <summary>
    /// Charge le module à part (et ses dépendances, dans le même dossier) et lui demande la température.
    /// Tout échec (module absent ou incompatible, pilote refusé) donne null : on repasse sur l'ACPI.
    /// </summary>
    private static double? FromModule()
    {
        ResolveEventHandler resolve = (_, args) =>
        {
            var file = Path.Combine(ModuleFolder, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try
        {
            var read = Task.Run(() =>
            {
                var assembly = Assembly.LoadFrom(Path.Combine(ModuleFolder, ModuleFile));
                var method = assembly.GetType("JvtechSonde.Capteurs.CpuSensors")?.GetMethod("CpuTemperature", BindingFlags.Public | BindingFlags.Static);
                return method?.Invoke(null, null) is double value ? value : (double?)null;
            });
            if (!read.Wait(TimeSpan.FromSeconds(20))) return null;
            return read.Result is { } c && c is > 5 and < 125 ? c : null;
        }
        catch (Exception ex) when (ex is AggregateException or IOException or BadImageFormatException or TargetInvocationException)
        {
            return null;
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }
}
