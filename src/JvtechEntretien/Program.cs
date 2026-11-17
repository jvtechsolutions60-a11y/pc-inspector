using System;
using System.IO;
using System.Linq;
using System.Text;
using PcInspector;

namespace JvtechEntretien;

/// <summary>
/// JVTECH Entretien.
///   --tache       (tâche planifiée) entretien complet à chaque ouverture de session
///   --maintenant  entretien tout de suite (même chose, avec l'affichage dans la console)
///   --etat        relève l'état du PC sans rien installer
///   --installer / --desinstaller
/// Ne redémarre jamais le PC et n'affiche rien à l'utilisateur (la tâche tourne en compte SYSTEM, sans fenêtre).
/// </summary>
internal static class Program
{
    private static string FichierReglages => Path.Combine(AppPaths.ExeFolder, "entretien.ini");

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (IOException) { }

        var options = args.Select(a => a.Trim().ToLowerInvariant()).ToList();
        try
        {
            if (options.Count == 0 || options.Contains("--aide") || options.Contains("/?") || options.Contains("--help")) { Aide(); return 0; }
            if (options.Contains("--installer")) return Installation.Installer(Console.WriteLine);
            if (options.Contains("--desinstaller")) return Installation.Desinstaller(Console.WriteLine);
            if (options.Contains("--etat")) return EtatSeul();
            if (options.Contains("--maintenant")) return Lancer();
            if (options.Contains("--tache")) return Lancer();
            Aide();
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erreur : " + ex.Message);
            Suivi.Journal("ERREUR inattendue : " + ex);
            return 10;
        }
    }

    /// <summary>Lignes de ce passage, recopiées dans le journal du PC sur le NAS.</summary>
    private static readonly StringBuilder Passage = new();

    private static void Dire(string message)
    {
        Console.WriteLine(message);
        Suivi.Journal(message);
        Passage.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\r\n");
    }

    /// <summary>
    /// À chaque ouverture de session : état avant, mises à jour Windows et logiciels de la liste sûre, état après,
    /// rapport dans le dossier du PC et ligne dans suivi-entretien.csv sur le NAS.
    /// </summary>
    private static int Lancer()
    {
        var maintenant = DateTime.Now;
        var reglages = Reglages.Charger(FichierReglages);
        Dire($"JVTECH Entretien {AppInfo.Version} · {reglages.Poste}" + (reglages.Client.Length > 0 ? $" · {reglages.Client}" : ""));
        foreach (var ligne in reglages.Ignorees) Dire($"entretien.ini : ligne ignorée « {ligne} »");

        var code = EntretienComplet(reglages, maintenant);

        Envois.JournalNas(Passage.ToString());
        var erreur = Envois.Envoyer(reglages);
        if (erreur is null) Suivi.Journal("Envoyé sur le NAS.");
        else Dire($"Envoi sur le NAS impossible ({erreur}) : gardé sur le PC, nouvel essai à la prochaine ouverture de session.");
        return code;
    }

    private static int EntretienComplet(Reglages reglages, DateTime maintenant)
    {
        var r = Entretien.Lancer(reglages, Dire);
        if (r.RienNaRepondu) Dire("Ni Windows Update ni winget n'ont répondu (pas de réseau ?) : signalé dans le rapport.");

        var problemes = Rapport.Problemes(r, reglages);
        Dire(Rapport.Resume(r));
        foreach (var p in problemes) Dire("À voir : " + p);

        var nom = NomRapport(reglages, maintenant);
        var html = Rapport.Html(r, reglages, problemes, AppInfo.Version);
        Suivi.Enregistrer(nom, html);
        Envois.Rapport(nom, html);
        Envois.Ligne(Envois.SuiviEntretien, Rapport.LigneCsv(r, reglages, problemes));
        return problemes.Count == 0 ? 0 : 1;
    }

    internal static string NomRapport(Reglages reglages, DateTime date) =>
        $"{Envois.NomDossier(reglages.Poste)}_{date:yyyy-MM-dd_HHmm}.html";

    private static int EtatSeul()
    {
        var reglages = Reglages.Charger(FichierReglages);
        Console.WriteLine("Relevé de l'état du PC (rien n'est installé)…");
        var e = Etat.Relever(reglages, Console.WriteLine);
        Console.WriteLine($"Mises à jour Windows en attente : {e.MajWindows?.Count.ToString() ?? "non lu (" + e.ErreurWindows + ")"}");
        foreach (var t in e.MajWindows ?? new()) Console.WriteLine("  " + t);
        Console.WriteLine($"Logiciels de la liste sûre à mettre à jour : {e.MajLogiciels?.Count.ToString() ?? "non lu (" + e.ErreurLogiciels + ")"}");
        foreach (var l in e.MajLogiciels ?? new()) Console.WriteLine($"  {l.Name} {l.Installed} → {l.Available}");
        if (e.AutresLogiciels is > 0) Console.WriteLine($"Autres logiciels avec une mise à jour (non touchés) : {e.AutresLogiciels}");
        Console.WriteLine($"Disque système : {Rapport.Go(e.DisqueLibre)} libres");
        Console.WriteLine($"Antivirus : {Rapport.Av(e.Antivirus)}");
        Console.WriteLine($"Redémarrage nécessaire : {(e.RedemarrageNecessaire ? "oui" : "non")}");
        return 0;
    }

    private static void Aide()
    {
        Console.WriteLine($"JVTECH Entretien {AppInfo.Version} : entretien automatique à chaque ouverture de session (mises à jour Windows et logiciels).");
        Console.WriteLine();
        Console.WriteLine("  JvtechEntretien.exe --installer     copie dans Program Files et crée la tâche (à chaque ouverture de session)");
        Console.WriteLine("  JvtechEntretien.exe --etat          montre l'état du PC sans rien installer");
        Console.WriteLine("  JvtechEntretien.exe --maintenant    fait l'entretien tout de suite");
        Console.WriteLine("  JvtechEntretien.exe --desinstaller  supprime la tâche, le programme et les rapports gardés sur le PC");
        Console.WriteLine();
        Console.WriteLine("Réglages (code client, NAS, logiciels) : entretien.ini à côté du .exe.");
        Console.WriteLine($"Journal et rapports sur le PC : {Suivi.Dossier}");
    }
}
