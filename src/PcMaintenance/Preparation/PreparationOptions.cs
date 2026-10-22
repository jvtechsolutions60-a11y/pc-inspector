using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PcInspector;

namespace PcMaintenance.Preparation;

/// <summary>
/// Réglages de la préparation, lus dans options.ini à côté du .exe (une ligne « Nom = 1 ou 0 »).
/// Sans fichier, tout est fait.
/// </summary>
internal sealed class PreparationOptions
{
    /// <summary>Retirer les applications préinstallées inutiles (liste dans debloat.ini).</summary>
    public bool Debloat { get; private set; } = true;
    /// <summary>Pilotes et logiciels du constructeur (Dell, Lenovo, HP).</summary>
    public bool Manufacturer { get; private set; } = true;
    /// <summary>Mettre aussi à jour le BIOS par l'outil du constructeur, seulement sur secteur.</summary>
    public bool Bios { get; private set; } = true;
    /// <summary>À la fin, remettre le PC au premier démarrage (choix de la langue, compte de l'acheteur) et l'éteindre.</summary>
    public bool Reseal { get; private set; } = true;
    /// <summary>Nombre maximal de passages de Windows Update (chacun suivi d'un redémarrage).</summary>
    public int MaxRounds { get; private set; } = 8;

    public static string FilePath => Path.Combine(AppPaths.ExeFolder, "options.ini");

    public static PreparationOptions Load()
    {
        var options = new PreparationOptions();
        try
        {
            if (!File.Exists(FilePath)) return options;
            var values = File.ReadAllLines(FilePath)
                .Where(l => !l.TrimStart().StartsWith("#"))
                .Select(l => l.Split(new[] { '=' }, 2))
                .Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
            options.Debloat = Flag(values, "Debloat", options.Debloat);
            options.Manufacturer = Flag(values, "Constructeur", options.Manufacturer);
            options.Bios = Flag(values, "Bios", options.Bios);
            options.Reseal = Flag(values, "PremierDemarrage", options.Reseal);
            if (values.TryGetValue("PassagesMax", out var max) && int.TryParse(max, out var rounds) && rounds > 0) options.MaxRounds = rounds;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return options;
    }

    private static bool Flag(Dictionary<string, string> values, string name, bool fallback) =>
        values.TryGetValue(name, out var value) ? value != "0" : fallback;
}
