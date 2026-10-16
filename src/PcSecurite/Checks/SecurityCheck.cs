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

/// <summary>Le résultat d'un contrôle : un feu, ce qui a été vu, et quoi faire.</summary>
public sealed class SecurityCheck
{
    public SecurityCheck(string name, CheckStatus status, string detail, string? advice = null)
    {
        Name = name;
        Status = status;
        Detail = detail;
        Advice = advice;
    }

    public string Name { get; }
    public CheckStatus Status { get; }
    public string Detail { get; }
    public string? Advice { get; }

    public string Symbol => Status switch
    {
        CheckStatus.Ok => "🟢",
        CheckStatus.Warning => "🟠",
        CheckStatus.Danger => "🔴",
        _ => "⚪",
    };

    public static SecurityCheck Ok(string name, string detail) => new(name, CheckStatus.Ok, detail);
    public static SecurityCheck Warning(string name, string detail, string advice) => new(name, CheckStatus.Warning, detail, advice);
    public static SecurityCheck Danger(string name, string detail, string advice) => new(name, CheckStatus.Danger, detail, advice);
    public static SecurityCheck Info(string name, string detail) => new(name, CheckStatus.Info, detail);
}
