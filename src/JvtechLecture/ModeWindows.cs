using System;
using Microsoft.Win32;

namespace Jvtech.Lecture;

/// <summary>État de l'installation de Windows sur le PC à préparer.</summary>
public enum ModeWindows
{
    /// <summary>Indicateurs illisibles ou contradictoires : traité comme une session normale, en plus prudent.</summary>
    Inconnu = 0,
    /// <summary>Mode audit (Ctrl+Maj+F3 à l'écran d'accueil de l'installation) : aucun compte utilisateur créé.</summary>
    Audit,
    /// <summary>Session Windows normale.</summary>
    Normal,
}

/// <summary>
/// Lecture du mode d'installation (registre, lecture seule) et règle de finalisation.
/// Mêmes critères que la préparation v1 (audit C02) : mode audit seulement si AuditInProgress = 1 ET ImageState
/// dans la liste blanche ; tout autre cas douteux = Inconnu.
/// </summary>
public static class Installation
{
    private const string SetupKey = @"SYSTEM\Setup";
    private const string StateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\State";

    public static ModeWindows Lire()
    {
        try
        {
            int? audit;
            using (var setup = Registry.LocalMachine.OpenSubKey(SetupKey))
                audit = setup?.GetValue("AuditInProgress") is int value ? value : null;
            string? imageState;
            using (var state = Registry.LocalMachine.OpenSubKey(StateKey))
                imageState = state?.GetValue("ImageState") as string;
            return Classer(audit, imageState);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return ModeWindows.Inconnu;
        }
    }

    public static ModeWindows Classer(int? auditInProgress, string? imageState)
    {
        var state = (imageState ?? "").Trim().ToUpperInvariant();
        var etatAudit = state is "IMAGE_STATE_UNDEPLOYABLE" or "IMAGE_STATE_SPECIALIZE_RESEAL_TO_AUDIT" or "IMAGE_STATE_GENERALIZE_RESEAL_TO_AUDIT";
        var etatNormal = state == "IMAGE_STATE_COMPLETE";

        if (auditInProgress == 1) return etatAudit ? ModeWindows.Audit : ModeWindows.Inconnu;
        if (etatAudit) return ModeWindows.Inconnu;
        if (etatNormal || auditInProgress == 0) return ModeWindows.Normal;
        return ModeWindows.Inconnu;
    }

    /// <summary>
    /// Ajustement 1 du 13/11/2026 : la finalisation (Sysprep) n'est proposée qu'en mode audit. La finalisation depuis
    /// une session normale fera l'objet d'une étude à part ; d'ici là, elle reste refusée.
    /// </summary>
    public static bool FinalisationPermise(ModeWindows mode) => mode == ModeWindows.Audit;

    public static string Libelle(ModeWindows mode) => mode switch
    {
        ModeWindows.Audit => "Windows en mode audit",
        ModeWindows.Normal => "Session Windows normale",
        _ => "Mode de Windows non reconnu",
    };
}
