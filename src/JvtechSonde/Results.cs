using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace JvtechSonde;

/// <summary>
/// Résultats gardés sur le PC, dans C:\ProgramData\JVTECH\Sonde : la dernière mesure (JSON), l'historique
/// (une ligne JSON par passage, pour le futur rapport mensuel) et un court journal.
/// </summary>
internal static class Results
{
    private const int MaxHistory = 400;
    private const long MaxLog = 512 * 1024;

    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JVTECH", "Sonde");

    public static string ToJson(Settings settings, DateTime when, IReadOnlyCollection<Point> points)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        Field(sb, "sonde", PcInspector.AppInfo.Version).Append(',');
        Field(sb, "client", settings.Client).Append(',');
        Field(sb, "poste", settings.Machine).Append(',');
        Field(sb, "date", when.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)).Append(',');
        Field(sb, "etat", Point.LevelCode(Rules.Overall(points))).Append(',');
        sb.Append("\"points\":[");
        sb.Append(string.Join(",", points.Select(p =>
        {
            var item = new StringBuilder("{");
            Field(item, "domaine", p.Area).Append(',');
            Field(item, "nom", p.Name).Append(',');
            Field(item, "niveau", Point.LevelCode(p.Level)).Append(',');
            Field(item, "valeur", p.Value);
            return item.Append('}').ToString();
        })));
        sb.Append("]}");
        return sb.ToString();
    }

    private static StringBuilder Field(StringBuilder sb, string name, string value) =>
        sb.Append('"').Append(name).Append("\":\"").Append(Escape(value)).Append('"');

    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Écrit la dernière mesure et l'ajoute à l'historique. Renvoie le dossier, ou null si l'écriture a échoué.</summary>
    public static string? Save(string json)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(Path.Combine(DataFolder, "derniere-mesure.json"), json, new UTF8Encoding(false));
            var history = Path.Combine(DataFolder, "historique.jsonl");
            var lines = File.Exists(history) ? File.ReadAllLines(history, Encoding.UTF8).ToList() : new List<string>();
            lines.Add(json);
            if (lines.Count > MaxHistory) lines = lines.Skip(lines.Count - MaxHistory).ToList();
            File.WriteAllLines(history, lines, new UTF8Encoding(false));
            return DataFolder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var log = Path.Combine(DataFolder, "sonde.log");
            // Un seul ancien journal est gardé (sonde.log.old) : le dossier ne grossit jamais.
            if (File.Exists(log) && new FileInfo(log).Length > MaxLog)
            {
                File.Copy(log, log + ".old", overwrite: true);
                File.Delete(log);
            }
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
