using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using PcInspector.Collectors;

namespace PcSecurite.Checks;

/// <summary>Les données du client sont-elles sauvegardées quelque part ? C'est la vraie catastrophe en cas de panne ou de rançongiciel.</summary>
public static partial class SecurityChecks
{
    private static readonly string[] BackupTools =
    {
        "Acronis", "Macrium", "Veeam Agent", "EaseUS Todo Backup", "AOMEI Backupper", "Cobian", "Duplicati",
        "Active Backup for Business", "Backblaze", "Carbonite", "iDrive", "Paragon Backup", "Google Drive", "Dropbox", "pCloud",
    };

    private static SecurityCheck Backup()
    {
        const string name = "Sauvegarde des données";
        var found = new List<string>();

        // OneDrive sauvegarde Documents quand le dossier a été déplacé dans OneDrive.
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (documents.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) found.Add("Documents synchronisés avec OneDrive");

        // L'Historique des fichiers garde sa configuration dans le profil de l'utilisateur.
        var fileHistory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\Windows\FileHistory\Configuration");
        if (Directory.Exists(fileHistory) && Directory.GetFiles(fileHistory, "Config*.xml").Length > 0) found.Add("Historique des fichiers configuré");

        foreach (var tool in BackupTools.Where(t => InstalledPrograms().Any(p => p.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)))
            found.Add(tool);

        var restore = LastRestorePoint();
        var restoreText = restore is null
            ? "aucun point de restauration"
            : $"dernier point de restauration le {restore:dd/MM/yyyy}";

        if (found.Count == 0)
            return SecurityCheck.Warning(name, $"Aucune sauvegarde des documents détectée · {restoreText}",
                "Mettre en place une sauvegarde : OneDrive pour Documents, Bureau et Images, ou un disque externe avec l'Historique des fichiers.");
        var detail = string.Join(", ", found) + " · " + restoreText;
        if (restore is null || (DateTime.Now - restore.Value).TotalDays > 30)
            return SecurityCheck.Warning(name, detail,
                "Activer la protection du système sur C: pour pouvoir revenir en arrière après une mauvaise mise à jour.");
        return SecurityCheck.Ok(name, detail + " (vérifier avec le client que la sauvegarde est récente)");
    }

    private static DateTime? LastRestorePoint()
    {
        DateTime? last = null;
        foreach (var point in Wmi.QueryOptional("SELECT CreationTime FROM SystemRestore", @"root\default"))
        {
            var raw = point.Str("CreationTime");
            if (string.IsNullOrEmpty(raw)) continue;
            try
            {
                var date = ManagementDateTimeConverter.ToDateTime(raw);
                if (last is null || date > last) last = date;
            }
            catch (ArgumentOutOfRangeException) { }
        }
        return last;
    }
}
