using System;
using System.Collections.Generic;
using System.Linq;
using PcMaintenance.Maintenance;

namespace JvtechEntretien;

/// <summary>Ce qui s'est passé pendant un entretien : états avant/après et ce qui a été installé ou non.</summary>
public sealed class Resultat
{
    public Etat Avant { get; init; } = new();
    public Etat Apres { get; init; } = new();
    public List<string> WindowsInstallees { get; } = new();
    public List<string> WindowsEchecs { get; } = new();
    public List<string> LogicielsMisAJour { get; } = new();
    public List<string> LogicielsEchecs { get; } = new();
    public TimeSpan Duree { get; set; }

    /// <summary>Ni Windows Update ni winget n'ont répondu : sans doute pas de réseau, l'entretien sera refait.</summary>
    public bool RienNaRepondu => Avant.MajWindows is null && Avant.MajLogiciels is null;
}

internal static class Entretien
{
    public static Resultat Lancer(Reglages reglages, Action<string> journal)
    {
        var debut = DateTime.Now;
        journal("État avant…");
        var avant = Etat.Relever(reglages, journal);
        journal($"Avant : {avant.MajWindows?.Count.ToString() ?? "?"} mise(s) à jour Windows, {avant.MajLogiciels?.Count.ToString() ?? "?"} logiciel(s) de la liste sûre à mettre à jour.");

        var installees = new List<string>();
        var echecsWindows = new List<string>();
        if (avant.MajWindows is { Count: > 0 })
        {
            try
            {
                var wu = WindowsUpdate.Create();
                foreach (var maj in wu.Search())
                {
                    journal("Windows Update : " + maj.Title);
                    try
                    {
                        if (wu.Install(maj, out _)) installees.Add(maj.Title);
                        else { echecsWindows.Add(maj.Title); journal("  échec de l'installation"); }
                    }
                    catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
                    {
                        echecsWindows.Add($"{maj.Title} ({WindowsUpdate.Describe(ex)})");
                        journal("  " + WindowsUpdate.Describe(ex));
                    }
                }
            }
            catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
            {
                echecsWindows.Add("Windows Update : " + WindowsUpdate.Describe(ex));
                journal("Windows Update : " + WindowsUpdate.Describe(ex));
            }
        }

        var misAJour = new List<string>();
        var echecsLogiciels = new List<string>();
        foreach (var logiciel in avant.MajLogiciels ?? new List<PcInspector.WingetUpdate>())
        {
            journal($"Logiciel : {logiciel.Name} {logiciel.Installed} → {logiciel.Available}");
            var erreur = Winget.MettreAJour(logiciel.Id, journal);
            if (erreur is null) misAJour.Add($"{logiciel.Name} {logiciel.Available}");
            else { echecsLogiciels.Add($"{logiciel.Name} ({erreur})"); journal("  échec : " + erreur); }
        }

        journal("État après…");
        var apres = Etat.Relever(reglages, journal);
        var resultat = new Resultat { Avant = avant, Apres = apres, Duree = DateTime.Now - debut };
        resultat.WindowsInstallees.AddRange(installees);
        resultat.WindowsEchecs.AddRange(echecsWindows);
        resultat.LogicielsMisAJour.AddRange(misAJour);
        resultat.LogicielsEchecs.AddRange(echecsLogiciels);
        return resultat;
    }
}
