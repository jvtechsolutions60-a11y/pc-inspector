namespace PcSecurite.Checks;

public enum CheckStatus
{
    /// <summary>Rien à faire.</summary>
    Ok,
    /// <summary>À améliorer.</summary>
    Warning,
    /// <summary>Risque réel, à corriger.</summary>
    Danger,
    /// <summary>Information seulement, ne compte pas dans la note.</summary>
    Info,
}

/// <summary>Ce que demande un point rouge ou orange.</summary>
public enum CheckKind
{
    /// <summary>Un réglage ou une mise à jour, faisable pendant l'intervention.</summary>
    Fix,
    /// <summary>Un logiciel, une version de Windows ou du matériel à remplacer.</summary>
    Change,
}

/// <summary>Le résultat d'un contrôle : un feu, ce qui a été vu, et quoi faire.</summary>
public sealed class SecurityCheck
{
    public SecurityCheck(string name, CheckStatus status, string detail, string? advice = null, CheckKind kind = CheckKind.Fix)
    {
        Kind = kind;
        Name = name;
        Status = status;
        Detail = detail;
        Advice = advice;
    }

    public string Name { get; }
    public CheckStatus Status { get; }
    public string Detail { get; }
    public string? Advice { get; }
    public CheckKind Kind { get; }

    /// <summary>Position dans la liste des contrôles, pour garder un ordre stable à l'intérieur d'un groupe.</summary>
    public int Order { get; set; }

    /// <summary>Le pas à pas pour corriger ce point, seulement s'il est rouge ou orange.</summary>
    public Tutorial? Tutorial { get; set; }

    public bool NeedsAction => Status is CheckStatus.Danger or CheckStatus.Warning;

    /// <summary>Corrections d'abord, puis ce qu'il faut changer, puis le reste.</summary>
    public int GroupOrder => !NeedsAction ? 2 : Kind == CheckKind.Fix ? 0 : 1;

    public string Group => GroupOrder switch
    {
        0 => CorrectionsTitle,
        1 => ChangesTitle,
        _ => "Points vérifiés",
    };

    public int Severity => Status switch { CheckStatus.Danger => 0, CheckStatus.Warning => 1, CheckStatus.Ok => 2, _ => 3 };

    public const string CorrectionsTitle = "Corrections à faire";
    public const string ChangesTitle = "À changer ou remplacer";

    public string Symbol => Status switch
    {
        CheckStatus.Ok => "🟢",
        CheckStatus.Warning => "🟠",
        CheckStatus.Danger => "🔴",
        _ => "⚪",
    };

    public static SecurityCheck Ok(string name, string detail) => new(name, CheckStatus.Ok, detail);
    public static SecurityCheck Warning(string name, string detail, string advice, CheckKind kind = CheckKind.Fix) => new(name, CheckStatus.Warning, detail, advice, kind);
    public static SecurityCheck Danger(string name, string detail, string advice, CheckKind kind = CheckKind.Fix) => new(name, CheckStatus.Danger, detail, advice, kind);
    public static SecurityCheck Info(string name, string detail) => new(name, CheckStatus.Info, detail);
}
