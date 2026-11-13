using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Jvtech.Lecture;
using Jvtech.Lecture.Regles;

namespace JvtechAtelier;

public abstract class Notifiant : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changer([CallerMemberName] string? nom = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
}

/// <summary>Une ligne de l'inventaire : la valeur détectée (figée) et la valeur retenue (modifiable).</summary>
public sealed class LigneChamp : Notifiant
{
    public LigneChamp(Champ champ)
    {
        Champ = champ;
        _retenu = champ.Valeur ?? "";
    }

    public Champ Champ { get; }
    public string Nom => Champ.Nom;
    public string Detecte => Champ.Detecte ?? Jvtech.Lecture.Champ.NonDetecte;
    public string Source => Champ.Source ?? "";

    private string _retenu;
    public string Retenu
    {
        get => _retenu;
        set { _retenu = value ?? ""; Changer(); }
    }
}

/// <summary>Un contrôle affiché avec sa pastille (Conforme, Anomalie, À vérifier, Non applicable).</summary>
public sealed class LigneControle
{
    public LigneControle(Controle c) => Controle = c;

    public Controle Controle { get; }
    public string Titre => Controle.Titre;
    public string Constat => Controle.Constat;

    public string PillText => Controle.Statut == Statut.Anomalie
        ? (Controle.Portee == Portee.ReserveCommerciale ? "Réserve" : "Anomalie")
        : Libelles.De(Controle.Statut);

    // Couleurs de la charte (fond clair, texte foncé, contraste AA).
    public Brush PillBrush => Couleur(Controle.Statut switch
    {
        Statut.Conforme => "#DCFCE7",
        Statut.Anomalie => Controle.Portee == Portee.ReserveCommerciale ? "#FEF3C7" : "#FEE2E2",
        Statut.NonApplicable => "#E2E8F0",
        _ => "#FEF3C7",
    });

    public Brush PillForeground => Couleur(Controle.Statut switch
    {
        Statut.Conforme => "#14532D",
        Statut.Anomalie => Controle.Portee == Portee.ReserveCommerciale ? "#78350F" : "#7F1D1D",
        Statut.NonApplicable => "#334155",
        _ => "#78350F",
    });

    private static Brush Couleur(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>Un test manuel en cours de saisie.</summary>
public sealed class LigneTest : Notifiant
{
    public LigneTest(TestManuel test, ResultatTestManuel? resultat)
    {
        Test = test;
        _resultatIndex = (int)(resultat?.Resultat ?? ResultatTest.NonTeste);
        _porteeIndex = resultat?.PorteeEchec == Portee.ReserveCommerciale ? 1 : 0;
        _remarque = resultat?.Remarque ?? "";
        Enregistre = resultat;
    }

    public TestManuel Test { get; }
    public string Libelle => Test.Libelle;

    /// <summary>Dernier résultat enregistré (pour garder l'heure et le technicien d'origine s'il n'a pas changé).</summary>
    public ResultatTestManuel? Enregistre { get; set; }

    private int _resultatIndex;
    public int ResultatIndex
    {
        get => _resultatIndex;
        set { _resultatIndex = value < 0 ? 0 : value; Changer(); Changer(nameof(EstEchec)); }
    }

    public bool EstEchec => (ResultatTest)ResultatIndex == ResultatTest.Echec;

    private int _porteeIndex;
    public int PorteeIndex
    {
        get => _porteeIndex;
        set { _porteeIndex = value < 0 ? 0 : value; Changer(); }
    }

    private string _remarque;
    public string Remarque
    {
        get => _remarque;
        set { _remarque = value ?? ""; Changer(); }
    }

    public ResultatTest Resultat => (ResultatTest)ResultatIndex;
    public Portee PorteeEchec => PorteeIndex == 1 ? Portee.ReserveCommerciale : Portee.Bloquante;
}

/// <summary>Une action de préparation déjà lancée, avec son résultat réel (code retour, durée).</summary>
public sealed class LigneAction
{
    public LigneAction(Jvtech.Atelier.Actions.ActionRecord r) => Record = r;

    public Jvtech.Atelier.Actions.ActionRecord Record { get; }
    public string Titre => Record.Libelle;
    public string Constat => $"{Record.Detail} · {Record.Debut:HH:mm} · {Duree}";
    private string Duree => Record.Duree.TotalMinutes >= 1 ? $"{Record.Duree.TotalMinutes:0} min" : $"{Record.Duree.TotalSeconds:0} s";

    public string PillText => Record.Resultat switch
    {
        Jvtech.Atelier.Actions.ResultatAction.Reussi => "Réussi",
        Jvtech.Atelier.Actions.ResultatAction.Echec => "Échec",
        Jvtech.Atelier.Actions.ResultatAction.Annule => "Arrêté",
        _ => "Non lancé",
    };

    public Brush PillBrush => Couleur(Record.Resultat switch
    {
        Jvtech.Atelier.Actions.ResultatAction.Reussi => "#DCFCE7",
        Jvtech.Atelier.Actions.ResultatAction.Echec => "#FEE2E2",
        _ => "#E2E8F0",
    });

    public Brush PillForeground => Couleur(Record.Resultat switch
    {
        Jvtech.Atelier.Actions.ResultatAction.Reussi => "#14532D",
        Jvtech.Atelier.Actions.ResultatAction.Echec => "#7F1D1D",
        _ => "#334155",
    });

    private static Brush Couleur(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
}

/// <summary>Un logiciel à cocher (pack de base coché, options décochées).</summary>
public sealed class LigneLogiciel : Notifiant
{
    public LigneLogiciel(Jvtech.Atelier.Actions.Preparation.Logiciel l)
    {
        Logiciel = l;
        _coche = l.DeBase;
    }

    public Jvtech.Atelier.Actions.Preparation.Logiciel Logiciel { get; }
    public string Nom => Logiciel.DeBase ? $"{Logiciel.Nom} (pack de base)" : $"{Logiciel.Nom} (option)";

    private bool _coche;
    public bool Coche
    {
        get => _coche;
        set { _coche = value; Changer(); }
    }
}
