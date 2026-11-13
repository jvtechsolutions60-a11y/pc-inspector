using System;
using System.Diagnostics;
using System.Threading;

namespace Jvtech.Atelier.Actions;

public enum ResultatAction
{
    NonLance = 0,
    Reussi,
    Echec,
    Annule,
}

/// <summary>Ce que l'action a réellement produit : code retour du programme, détail lisible, réussite.</summary>
public sealed record SortieAction(bool Reussi, int? CodeRetour, string Detail);

/// <summary>
/// Une action qui modifie Windows. Chaque action sait lire l'état qu'elle touche, pour le noter avant et après.
/// </summary>
public interface IActionAtelier
{
    string Id { get; }
    string Libelle { get; }

    /// <summary>L'action ne peut pas se faire sans droits administrateur (demandés à cette étape seulement).</summary>
    bool DemandeAdministrateur { get; }

    /// <summary>Relevé court de l'état concerné (« 3 mises à jour en attente », « VLC absent »…). Lecture seule.</summary>
    string LireEtat();

    SortieAction Executer(CancellationToken annulation);
}

/// <summary>Trace d'une action : état avant, état après, code retour et durée. Écrite dans le journal du PC.</summary>
public sealed record ActionRecord(
    string Id,
    string Libelle,
    DateTime Debut,
    TimeSpan Duree,
    string EtatAvant,
    string EtatApres,
    int? CodeRetour,
    ResultatAction Resultat,
    string Detail)
{
    public string Ligne() =>
        $"{Debut:yyyy-MM-dd HH:mm:ss} | {Id} | {Resultat} | code {(CodeRetour?.ToString() ?? "-")} | {Duree.TotalSeconds:0} s | avant : {EtatAvant} | après : {EtatApres} | {Detail}";
}

/// <summary>Lance une action en relevant tout ce qu'il faut pour la tracer. Ne relance jamais une action seule.</summary>
public static class ActionJournal
{
    public static ActionRecord Lancer(IActionAtelier action, CancellationToken annulation, Func<DateTime>? horloge = null)
    {
        var maintenant = horloge ?? (() => DateTime.Now);
        var debut = maintenant();
        var avant = Relever(action);

        if (annulation.IsCancellationRequested)
            return new ActionRecord(action.Id, action.Libelle, debut, TimeSpan.Zero, avant, avant, null, ResultatAction.NonLance,
                "Arrêt demandé avant le lancement");

        var chrono = Stopwatch.StartNew();
        SortieAction sortie;
        ResultatAction resultat;
        try
        {
            sortie = action.Executer(annulation);
            resultat = sortie.Reussi ? ResultatAction.Reussi : ResultatAction.Echec;
        }
        catch (OperationCanceledException)
        {
            sortie = new SortieAction(false, null, "Arrêtée à la demande du technicien");
            resultat = ResultatAction.Annule;
        }
        catch (Exception ex)
        {
            // Une action qui plante est un échec tracé, jamais une réussite silencieuse.
            sortie = new SortieAction(false, null, $"Erreur : {ex.GetType().Name} : {ex.Message}");
            resultat = ResultatAction.Echec;
        }
        chrono.Stop();

        return new ActionRecord(action.Id, action.Libelle, debut, chrono.Elapsed, avant, Relever(action), sortie.CodeRetour,
            resultat, sortie.Detail);
    }

    private static string Relever(IActionAtelier action)
    {
        try
        {
            var etat = action.LireEtat();
            return string.IsNullOrWhiteSpace(etat) ? "non relevé" : etat.Trim();
        }
        catch (Exception ex)
        {
            return $"illisible ({ex.GetType().Name} : {ex.Message})";
        }
    }
}
