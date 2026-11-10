using System;
using System.Security.Principal;
using Microsoft.Win32;

namespace PcMaintenance.Preparation;

/// <summary>Où en est l'installation de Windows : seul le mode audit est un PC fraîchement installé par le kit JVTECH.</summary>
internal enum SetupMode
{
    /// <summary>Mode audit : installation propre du kit, aucun compte client.</summary>
    Audit,
    /// <summary>Windows installé et utilisé normalement : c'est le cas d'un PC de client.</summary>
    Normal,
    /// <summary>Indicateurs illisibles ou contradictoires.</summary>
    Unknown,
}

/// <summary>Ce que la préparation a le droit de faire sur ce PC.</summary>
internal sealed record PreparationPermission(bool Allowed, bool ResealAllowed, string Reason);

/// <summary>
/// Garde-fou du mode « /preparation » : la préparation revente retire des applications, change des réglages pour
/// l'acheteur et finit par Sysprep. Elle ne doit jamais tourner par erreur sur le PC d'un client.
///
/// Règles :
/// 1. Mode audit (installation PXE du kit) : tout est permis, Sysprep compris si PremierDemarrage=1.
/// 2. Hors mode audit : refus, sauf si le technicien a confirmé lui-même la préparation « hors mode audit »
///    sur ce PC précis (nom du PC tapé à la main). Même alors, Sysprep reste interdit.
/// 3. Indicateurs illisibles : traité comme hors mode audit (refus par défaut).
/// La décision (<see cref="Decide"/>) ne lit rien sur le PC : elle est testée par des tests automatisés.
/// </summary>
internal static class PreparationGuard
{
    private const string SetupKey = @"SYSTEM\Setup";
    private const string StateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\State";

    /// <summary>Lit les indicateurs de Windows (registre, lecture seule).</summary>
    public static SetupMode ReadMode()
    {
        try
        {
            int? audit;
            using (var setup = Registry.LocalMachine.OpenSubKey(SetupKey))
                audit = setup?.GetValue("AuditInProgress") is int value ? value : null;
            string? imageState;
            using (var state = Registry.LocalMachine.OpenSubKey(StateKey))
                imageState = state?.GetValue("ImageState") as string;
            return Classify(audit, imageState);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return SetupMode.Unknown;
        }
    }

    /// <summary>
    /// AuditInProgress = 1 (HKLM\SYSTEM\Setup) est posé par Windows pendant le mode audit. ImageState confirme :
    /// IMAGE_STATE_COMPLETE = Windows installé et en service ; les états « …_TO_AUDIT » et UNDEPLOYABLE = mode audit.
    /// Les deux doivent être d'accord, sinon « inconnu » (donc refus).
    /// </summary>
    internal static SetupMode Classify(int? auditInProgress, string? imageState)
    {
        var state = (imageState ?? "").Trim().ToUpperInvariant();
        var stateSaysAudit = state is "IMAGE_STATE_UNDEPLOYABLE" or "IMAGE_STATE_SPECIALIZE_RESEAL_TO_AUDIT";
        var stateSaysNormal = state == "IMAGE_STATE_COMPLETE";

        if (auditInProgress == 1) return stateSaysNormal ? SetupMode.Unknown : SetupMode.Audit;
        if (stateSaysAudit) return SetupMode.Unknown; // ImageState dit audit mais Windows ne l'indique pas : on ne devine pas.
        if (stateSaysNormal || auditInProgress == 0) return SetupMode.Normal;
        return SetupMode.Unknown;
    }

    /// <param name="mode">Mode lu par <see cref="ReadMode"/>.</param>
    /// <param name="confirmedMachine">Nom du PC enregistré quand le technicien a confirmé la préparation hors mode audit (etat.ini), sinon null.</param>
    /// <param name="machineName">Nom de ce PC.</param>
    /// <param name="resealOption">PremierDemarrage=1 dans options.ini.</param>
    internal static PreparationPermission Decide(SetupMode mode, string? confirmedMachine, string machineName, bool resealOption)
    {
        if (mode == SetupMode.Audit)
            return new PreparationPermission(true, resealOption, "Windows est en mode audit (installation propre du kit JVTECH).");

        var confirmed = !string.IsNullOrWhiteSpace(confirmedMachine)
                        && string.Equals(confirmedMachine!.Trim(), machineName.Trim(), StringComparison.OrdinalIgnoreCase);
        if (confirmed)
            return new PreparationPermission(true, false,
                "Préparation hors mode audit confirmée par le technicien pour ce PC : la remise au premier démarrage (Sysprep) est désactivée.");

        var why = mode == SetupMode.Normal
            ? "Windows n'est pas en mode audit : ce PC est déjà en service (peut-être le PC d'un client)."
            : "Impossible de confirmer que Windows est en mode audit.";
        return new PreparationPermission(false, false, why + " La préparation revente n'a rien fait.");
    }

    /// <summary>Le technicien confirme en tapant le nom exact du PC : un clic par réflexe ne suffit pas.</summary>
    internal static bool IsConfirmation(string? typed, string machineName) =>
        !string.IsNullOrWhiteSpace(typed) && string.Equals(typed!.Trim(), machineName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>En mode audit, Windows ouvre la session avec le compte Administrateur intégré (RID 500). Indication pour le journal.</summary>
    public static bool IsBuiltInAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value.EndsWith("-500", StringComparison.Ordinal) == true;
        }
        catch (System.Security.SecurityException) { return false; }
    }
}
