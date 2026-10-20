using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PcMaintenance.Maintenance;

/// <summary>Point de restauration Windows, créé avant toute action pour pouvoir revenir en arrière.</summary>
internal static class RestorePoint
{
    private const string Scope = @"\\.\root\default";

    // RestorePointType 12 = MODIFY_SETTINGS ; EventType 100 = BEGIN_SYSTEM_CHANGE.
    private const int ModifySettings = 12;
    private const int BeginSystemChange = 100;

    /// <summary>Crée le point et renvoie une phrase pour le journal et le rapport.</summary>
    public static string Create(IProgress<string> log)
    {
        try
        {
            var before = Latest();
            var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
            using (var restore = new ManagementClass(Scope, "SystemRestore", null))
            {
                // Sans effet si la protection est déjà active ; l'active sinon (désactivée d'origine sur certains PC).
                using (var enable = restore.GetMethodParameters("Enable"))
                {
                    enable["Drive"] = drive;
                    var enabled = Convert.ToUInt32(restore.InvokeMethod("Enable", enable, null)?["ReturnValue"] ?? 1u);
                    log.Report(enabled == 0 ? $"Protection du système active sur {drive}" : $"Activation de la protection sur {drive} : code {enabled}");
                }

                log.Report("Création du point de restauration (jusqu'à une minute)…");
                using var create = restore.GetMethodParameters("CreateRestorePoint");
                create["Description"] = $"PC Maintenance {DateTime.Now:dd/MM/yyyy HH:mm}";
                create["RestorePointType"] = ModifySettings;
                create["EventType"] = BeginSystemChange;
                var code = Convert.ToUInt32(restore.InvokeMethod("CreateRestorePoint", create, null)?["ReturnValue"] ?? 1u);
                if (code != 0) return $"Échec de la création (code {code}).";
            }

            var after = Latest();
            if (after is { } created && created.Sequence != before?.Sequence)
                return $"Point de restauration créé le {created.When:dd/MM/yyyy à HH:mm}.";
            // Windows refuse d'en créer un deuxième moins de 24 h après le précédent.
            return before is { } last
                ? $"Windows n'en crée pas de nouveau : le dernier date du {last.When:dd/MM/yyyy à HH:mm} (moins de 24 h)."
                : "Windows n'a pas créé de point de restauration.";
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return "Impossible de créer le point de restauration : " + ex.Message;
        }
    }

    private static (uint Sequence, DateTime When)? Latest()
    {
        using var searcher = new ManagementObjectSearcher(Scope, "SELECT SequenceNumber, CreationTime FROM SystemRestore");
        return searcher.Get().Cast<ManagementObject>()
            .Select(p => ((uint)Convert.ToUInt32(p["SequenceNumber"]), ManagementDateTimeConverter.ToDateTime(p["CreationTime"]?.ToString() ?? "")))
            .OrderByDescending(p => p.Item1)
            .Select(p => ((uint, DateTime)?)p)
            .FirstOrDefault();
    }
}
