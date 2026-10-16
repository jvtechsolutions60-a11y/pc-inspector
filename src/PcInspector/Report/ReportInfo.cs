using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PcInspector.Report;

/// <summary>En-tête du rapport : qui intervient, pour quel client.</summary>
public sealed class ReportInfo
{
    public string Company { get; set; } = "";
    public string Technician { get; set; } = "";
    public string LogoPath { get; set; } = "";
    public string Client { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcInspector", "settings.ini");

    /// <summary>Société, technicien et logo sont mémorisés d'un rapport à l'autre.</summary>
    public static ReportInfo LoadDefaults()
    {
        var info = new ReportInfo();
        try
        {
            if (!File.Exists(SettingsPath)) return info;
            var values = File.ReadAllLines(SettingsPath)
                .Select(l => l.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim())
                .ToDictionary(g => g.Key, g => g.First()[1].Trim());
            info.Company = values.TryGetValue("Company", out var c) ? c : "";
            info.Technician = values.TryGetValue("Technician", out var t) ? t : "";
            info.LogoPath = values.TryGetValue("LogoPath", out var l) ? l : "";
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return info;
    }

    public void SaveDefaults()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllLines(SettingsPath, new List<string>
            {
                "Company=" + OneLine(Company),
                "Technician=" + OneLine(Technician),
                "LogoPath=" + OneLine(LogoPath),
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string OneLine(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();
}
