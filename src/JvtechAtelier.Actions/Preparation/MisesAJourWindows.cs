using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jvtech.Lecture.Regles;
using PcMaintenance.Maintenance;

namespace Jvtech.Atelier.Actions.Preparation;

/// <summary>
/// Une passe de Windows Update : recherche des mises à jour (pilotes proposés par Windows compris), puis
/// installation une par une. Ne redémarre jamais seule : la suite (nouvelle passe, redémarrage, arrêt) est
/// décidée par <see cref="PlanPreparation.Decider"/> et le redémarrage est demandé au technicien.
/// Moteur repris de PC Maintenance (agent Windows Update par COM).
/// </summary>
public sealed class MisesAJourWindows : IActionAtelier
{
    private readonly int _passe;
    private readonly Action<string>? _progression;

    public MisesAJourWindows(int passe, Action<string>? progression = null)
    {
        _passe = passe;
        _progression = progression;
    }

    public string Id => "windows-update";
    public string Libelle => $"Mises à jour Windows, passe {_passe}";
    public bool DemandeAdministrateur => true;

    public int Trouvees { get; private set; }
    public int Installees { get; private set; }
    public List<string> Echecs { get; } = new();
    public bool RedemarrageNecessaire { get; private set; }
    public SuitePasse Suite { get; private set; } = SuitePasse.Bloquee;

    public string LireEtat() => WindowsUpdate.IsRebootPending() ? "redémarrage en attente" : "pas de redémarrage en attente";

    public SortieAction Executer(CancellationToken annulation)
    {
        WindowsUpdate agent;
        List<WindowsUpdate.Update> liste;
        try
        {
            _progression?.Invoke("Recherche des mises à jour…");
            agent = WindowsUpdate.Create();
            liste = agent.Search(withDrivers: true);
        }
        catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
        {
            // Pas de réseau, service arrêté… : la passe échoue, sans rien compter comme installé.
            Suite = SuitePasse.Bloquee;
            return new SortieAction(false, null, "Recherche impossible : " + WindowsUpdate.Describe(ex));
        }

        Trouvees = liste.Count;
        var i = 0;
        foreach (var maj in liste)
        {
            annulation.ThrowIfCancellationRequested();
            i++;
            _progression?.Invoke($"{i} sur {liste.Count} : {maj.Title}");
            try
            {
                if (agent.Install(maj, out var redemarrage)) Installees++;
                else Echecs.Add(maj.Title);
                RedemarrageNecessaire |= redemarrage;
            }
            catch (Exception ex) when (WindowsUpdate.IsExpected(ex))
            {
                Echecs.Add($"{maj.Title} ({WindowsUpdate.Describe(ex)})");
            }
        }
        RedemarrageNecessaire |= WindowsUpdate.IsRebootPending();
        Suite = PlanPreparation.Decider(_passe, Trouvees, Installees, RedemarrageNecessaire);

        var detail = Trouvees == 0
            ? "aucune mise à jour proposée"
            : $"{Installees} installée(s) sur {Trouvees}" + (Echecs.Count > 0 ? " ; échec : " + string.Join(" ; ", Echecs.Take(5)) : "");
        detail += $" ; suite : {PlanPreparation.Libelle(Suite)}";
        return new SortieAction(Echecs.Count == 0, null, detail);
    }
}
