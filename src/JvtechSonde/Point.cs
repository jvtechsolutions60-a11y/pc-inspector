namespace JvtechSonde;

/// <summary>Niveau d'un point surveillé. L'ordre sert au tri : le plus grave d'abord.</summary>
public enum Level
{
    /// <summary>Rouge : intervention nécessaire.</summary>
    Critical,
    /// <summary>Orange : à surveiller.</summary>
    Warning,
    /// <summary>Vert.</summary>
    Ok,
    /// <summary>Mesure impossible sur ce PC (pas de sonde, accès refusé). Jamais une alerte.</summary>
    Unknown,
    /// <summary>Sans objet (pas de batterie sur un PC fixe…). Jamais une alerte.</summary>
    NotApplicable,
}

/// <summary>Un point surveillé : où, quoi, le niveau et la valeur lue.</summary>
public sealed record Point(string Area, string Name, Level Level, string Value)
{
    public string Label => string.IsNullOrEmpty(Name) ? Area : $"{Area} {Name}";

    public static string LevelCode(Level level) => level switch
    {
        Level.Critical => "rouge",
        Level.Warning => "orange",
        Level.Ok => "ok",
        Level.Unknown => "non mesurable",
        _ => "sans objet",
    };

    public string Mark => Level switch
    {
        Level.Critical => "[ROUGE] ",
        Level.Warning => "[ORANGE]",
        Level.Ok => "[OK]    ",
        Level.Unknown => "[?]     ",
        _ => "[-]     ",
    };
}
