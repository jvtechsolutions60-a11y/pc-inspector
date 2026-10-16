using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>Logiciels installés, lus comme le fait « Applications et fonctionnalités » (clés Uninstall).</summary>
public sealed class SoftwareCollector : ICollector
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public string Title => "Logiciels";
    public string Icon => "";

    public InfoSection Collect()
    {
        var section = new InfoSection(Title);
        var programs = new Dictionary<string, (string Name, string? Version, string? Publisher, string? Date, ulong? SizeKb)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, view) in new[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(UninstallPath);
            if (uninstall is null) continue;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var app = uninstall.OpenSubKey(name);
                if (app is null || IsHidden(app)) continue;
                var displayName = (app.GetValue("DisplayName") as string)?.Trim();
                if (string.IsNullOrEmpty(displayName)) continue;

                var version = app.GetValue("DisplayVersion") as string;
                var key = displayName + "|" + version;
                if (programs.ContainsKey(key)) continue;

                programs[key] = (displayName!, version, (app.GetValue("Publisher") as string)?.Trim(),
                    InstallDate(app.GetValue("InstallDate") as string), app.GetValue("EstimatedSize") is int kb && kb > 0 ? (ulong)kb : null);
            }
        }

        var group = new InfoGroup($"{programs.Count} logiciels installés");
        foreach (var p in programs.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var details = new[]
            {
                p.Version,
                p.Publisher,
                p.Date is null ? null : "installé le " + p.Date,
                p.SizeKb is null ? null : Wmi.FormatBytes(p.SizeKb * 1024),
            }.Where(d => !string.IsNullOrWhiteSpace(d));
            group.Add(p.Name, string.Join(" · ", details));
        }
        section.Groups.Add(group);
        return section;
    }

    /// <summary>Composants système, mises à jour et sous-éléments que Windows ne montre pas à l'utilisateur.</summary>
    private static bool IsHidden(RegistryKey app)
    {
        if (app.GetValue("SystemComponent") is int sc && sc == 1) return true;
        if (app.GetValue("ParentKeyName") is not null) return true;
        var release = app.GetValue("ReleaseType") as string;
        return release is "Update" or "Hotfix" or "Security Update";
    }

    private static string? InstallDate(string? raw) =>
        raw is { Length: 8 } && DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("dd/MM/yyyy")
            : null;
}
