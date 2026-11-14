using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace JvtechSonde;

/// <summary>
/// Réglages d'un PC, lus dans sonde.ini à côté du .exe : client, adresse Uptime Kuma et seuils d'alerte.
/// Une ligne « clé = valeur » par réglage ; « ; » ou « # » en début de ligne = commentaire. Une valeur absente ou
/// illisible garde la valeur par défaut, pour qu'une faute de frappe ne coupe jamais la surveillance.
/// </summary>
public sealed class Settings
{
    public string Client { get; private set; } = "";
    public string Machine { get; private set; } = Environment.MachineName;

    /// <summary>Adresse du moniteur « Push » d'Uptime Kuma (https://…/api/push/XXXX), vide = pas d'envoi.</summary>
    public string KumaPush { get; private set; } = "";

    /// <summary>Si oui, un point orange met aussi le moniteur Kuma en panne (sinon : seulement les points rouges).</summary>
    public bool AlertOnWarning { get; private set; }

    public double DiskWarning { get; private set; } = 85;
    public double DiskCritical { get; private set; } = 95;
    public double RamWarning { get; private set; } = 90;
    public double RamCritical { get; private set; } = 97;
    public double CpuUsageWarning { get; private set; } = 90;
    public double CpuTempWarning { get; private set; } = 85;
    public double CpuTempCritical { get; private set; } = 95;
    public double GpuTempWarning { get; private set; } = 85;
    public double GpuTempCritical { get; private set; } = 95;
    public double BatteryWearWarning { get; private set; } = 40;
    public double BatteryWearCritical { get; private set; } = 60;
    public int UpdateSearchDays { get; private set; } = 7;
    public int CrashWarning { get; private set; } = 1;
    public int CrashCritical { get; private set; } = 3;

    /// <summary>Durée de mesure de l'utilisation du processeur (moyenne), en secondes.</summary>
    public int CpuSampleSeconds { get; private set; } = 30;

    /// <summary>
    /// Exclusions de Microsoft Defender voulues sur ce PC (logiciel métier, gestionnaire de mods…), séparées par « ; ».
    /// Elles ne sont plus signalées ; une exclusion qui n'est pas dans la liste reste orange.
    /// </summary>
    public List<string> KnownExclusions { get; } = new();

    /// <summary>Si non, seuls les écrans bleus comptent pour l'alerte ; les arrêts brutaux (coupure, bouton) sont seulement affichés.</summary>
    public bool CountAbruptShutdowns { get; private set; } = true;

    /// <summary>Réglages lus, et les lignes ignorées (pour le signaler dans le journal).</summary>
    public List<string> Ignored { get; } = new();

    public static Settings Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new Settings();

    public static Settings Parse(string text)
    {
        var s = new Settings();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#' or '[') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) { s.Ignored.Add(line); continue; }
            var key = line.Substring(0, eq).Trim().ToLowerInvariant();
            var value = line.Substring(eq + 1).Trim();
            if (!s.Apply(key, value)) s.Ignored.Add(line);
        }
        return s;
    }

    private bool Apply(string key, string value)
    {
        switch (key)
        {
            case "client": Client = value; return true;
            case "poste": if (value.Length > 0) Machine = value; return true;
            case "kuma_push": KumaPush = value; return true;
            case "alerte_orange": return Bool(value, v => AlertOnWarning = v);
            case "disque_orange": return Number(value, v => DiskWarning = v);
            case "disque_rouge": return Number(value, v => DiskCritical = v);
            case "ram_orange": return Number(value, v => RamWarning = v);
            case "ram_rouge": return Number(value, v => RamCritical = v);
            case "cpu_orange": return Number(value, v => CpuUsageWarning = v);
            case "cpu_temp_orange": return Number(value, v => CpuTempWarning = v);
            case "cpu_temp_rouge": return Number(value, v => CpuTempCritical = v);
            case "gpu_temp_orange": return Number(value, v => GpuTempWarning = v);
            case "gpu_temp_rouge": return Number(value, v => GpuTempCritical = v);
            case "batterie_usure_orange": return Number(value, v => BatteryWearWarning = v);
            case "batterie_usure_rouge": return Number(value, v => BatteryWearCritical = v);
            case "recherche_maj_jours": return Number(value, v => UpdateSearchDays = (int)v);
            case "plantages_orange": return Number(value, v => CrashWarning = (int)v);
            case "plantages_rouge": return Number(value, v => CrashCritical = (int)v);
            case "exclusions_connues":
                KnownExclusions.Clear();
                KnownExclusions.AddRange(value.Split(';').Select(e => e.Trim()).Where(e => e.Length > 0));
                return true;
            case "plantages_arrets_brutaux": return Bool(value, v => CountAbruptShutdowns = v);
            case "cpu_mesure_secondes": return Number(value, v => CpuSampleSeconds = (int)Math.Max(1, Math.Min(120, v)));
            default: return false;
        }
    }

    private static bool Number(string value, Action<double> set)
    {
        var text = value.Replace(',', '.').TrimEnd('%', ' ');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return false;
        set(v);
        return true;
    }

    private static bool Bool(string value, Action<bool> set)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "oui": case "1": case "true": case "vrai": set(true); return true;
            case "non": case "0": case "false": case "faux": case "": set(false); return true;
            default: return false;
        }
    }
}
