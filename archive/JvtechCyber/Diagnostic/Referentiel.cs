using System.Collections.Generic;
using System.Linq;

namespace JvtechCyber.Diagnostic;

/// <summary>Un palier d'un thème : où en est l'entreprise, et ce qu'il faut faire pour l'atteindre.</summary>
public sealed record Level(int Value, string Situation, string Action = "", string Cost = "", string Duration = "", bool Jvtech = false);

/// <summary>
/// Un thème du diagnostic, noté de 0 (rien en place) à 3 (bonne pratique). <see cref="Checks"/> liste les contrôles
/// de PC Sécurité qui renseignent ce thème quand les résultats des postes sont importés.
/// </summary>
public sealed record Theme(string Id, string Name, string Question, IReadOnlyList<Level> Levels, string[] Checks, int Weight = 1);

/// <summary>Une menace courante pour une TPE, et les thèmes qui la font reculer.</summary>
public sealed record Threat(string Name, string Example, string[] Themes);

/// <summary>
/// Référentiel JVTECH pour les TPE, artisans et professions libérales, rédigé d'après les recommandations publiques
/// de l'ANSSI (guide d'hygiène informatique, « La cybersécurité pour les TPE/PME en 13 questions ») et de
/// cybermalveillance.gouv.fr. Coûts et durées indicatifs, pour une structure de 1 à 20 postes.
/// </summary>
public static class Referentiel
{
    public static IReadOnlyList<Theme> Themes { get; } = new[]
    {
        new Theme("sauvegardes", "Sauvegardes", "Les données de l'entreprise sont-elles sauvegardées, et la sauvegarde a-t-elle déjà servi ?", new[]
        {
            new Level(0, "Pas de sauvegarde, ou personne ne sait si elle fonctionne."),
            new Level(1, "Une sauvegarde régulière existe sur un disque externe ou un NAS.",
                "Mettre en place une sauvegarde automatique des données sur un NAS ou un disque externe.", "100 à 600 €", "½ journée", true),
            new Level(2, "Une copie est conservée hors des locaux ou hors ligne (cloud, disque débranché).",
                "Ajouter une copie hors ligne ou dans le cloud, à l'abri d'un rançongiciel, d'un vol ou d'un incendie.", "5 à 20 € / mois", "2 heures", true),
            new Level(3, "Une restauration est testée au moins une fois par an.",
                "Tester une restauration chaque année et noter le résultat.", "0 €", "1 heure / an", true),
        }, new[] { "Sauvegarde des données" }, 2),

        new Theme("misesajour", "Mises à jour", "Windows, les logiciels et le matériel réseau sont-ils mis à jour ?", new[]
        {
            new Level(0, "Les mises à jour sont repoussées, ou des postes ont un Windows plus suivi."),
            new Level(1, "Windows Update est actif sur tous les postes.",
                "Activer et vérifier Windows Update sur chaque poste, remplacer les Windows qui ne sont plus suivis.", "0 €", "30 min / poste", true),
            new Level(2, "Les logiciels (navigateurs, PDF, bureautique) sont aussi tenus à jour.",
                "Mettre à jour les logiciels et retirer ceux qui ne sont plus suivis par leur éditeur.", "0 €", "30 min / poste", true),
            new Level(3, "La box, le NAS et les autres appareils réseau sont mis à jour, et quelqu'un vérifie chaque mois.",
                "Mettre à jour box, NAS, imprimantes et caméras, et prévoir une vérification mensuelle (contrat de maintenance).", "Contrat", "1 heure / mois", true),
        }, new[] { "Dernière mise à jour", "Mises à jour en attente", "Version de Windows", "Logiciels avec failles connues", "Logiciels abandonnés", "Navigateurs internet" }, 2),

        new Theme("protection", "Antivirus et protections", "Chaque poste est-il protégé par un antivirus et un pare-feu actifs ?", new[]
        {
            new Level(0, "Pas d'antivirus sur certains postes, ou il est désactivé."),
            new Level(1, "Un antivirus et le pare-feu sont actifs sur tous les postes.",
                "Activer Microsoft Defender (ou un antivirus à jour) et le pare-feu sur chaque poste.", "0 €", "15 min / poste", true),
            new Level(2, "Les protections de Windows sont toutes actives (SmartScreen, contrôle de compte, protection contre les rançongiciels).",
                "Activer les protections avancées de Windows sur chaque poste.", "0 €", "15 min / poste", true),
            new Level(3, "Les alertes de l'antivirus sont suivies par quelqu'un (console centrale ou supervision).",
                "Centraliser les alertes antivirus ou mettre les postes sous supervision.", "2 à 5 € / poste / mois", "½ journée", true),
        }, new[] { "Antivirus", "Microsoft Defender", "SmartScreen", "Pare-feu", "Contrôle de compte (UAC)" }),

        new Theme("comptes", "Comptes et mots de passe", "Chacun a-t-il ses propres comptes, avec des mots de passe solides ?", new[]
        {
            new Level(0, "Comptes partagés, sans mot de passe, ou mots de passe notés en clair."),
            new Level(1, "Chaque personne a sa session Windows avec un mot de passe personnel.",
                "Créer une session par personne, avec un mot de passe ou un code PIN.", "0 €", "20 min / poste", true),
            new Level(2, "Mots de passe longs, différents pour chaque service, rangés dans un gestionnaire de mots de passe.",
                "Installer un gestionnaire de mots de passe (Bitwarden, KeePass) et changer les mots de passe réutilisés.", "0 à 4 € / pers. / mois", "1 heure / pers.", true),
            new Level(3, "Double authentification sur la messagerie, la banque et les accès à distance ; personne ne travaille en administrateur.",
                "Activer la double authentification et utiliser des comptes sans droits d'administrateur au quotidien.", "0 €", "30 min / pers.", true),
        }, new[] { "Comptes utilisateurs", "Connexion automatique", "Mots de passe en mémoire (WDigest)" }, 2),

        new Theme("sensibilisation", "Messagerie et arnaques", "L'équipe sait-elle reconnaître un e-mail piégé ou une fausse demande de virement ?", new[]
        {
            new Level(0, "Aucune information n'a été donnée à l'équipe."),
            new Level(1, "Les consignes de base ont été données (liens, pièces jointes, faux support).",
                "Remettre une fiche de bons réflexes à chaque personne (pièces jointes, liens, faux support technique).", "0 €", "30 min", true),
            new Level(2, "Une courte formation a eu lieu, avec des exemples réels.",
                "Organiser une sensibilisation d'une heure avec des exemples d'arnaques récentes.", "Sur devis", "1 heure", true),
            new Level(3, "Les virements et changements de RIB sont toujours confirmés par téléphone, et les consignes sont rappelées chaque année.",
                "Instaurer un contre-appel pour tout virement inhabituel ou changement de RIB, et un rappel annuel.", "0 €", "1 heure / an", true),
        }, System.Array.Empty<string>(), 2),

        new Theme("reseau", "Réseau et Wi-Fi", "Le réseau de l'entreprise est-il fermé aux visiteurs et aux appareils non maîtrisés ?", new[]
        {
            new Level(0, "Le Wi-Fi est partagé avec les visiteurs, ou le mot de passe de la box n'a jamais été changé."),
            new Level(1, "Le Wi-Fi est protégé (WPA2 ou WPA3) et le mot de passe d'administration de la box a été changé.",
                "Changer les mots de passe de la box et du Wi-Fi, et vérifier le chiffrement WPA2/WPA3.", "0 €", "30 min", true),
            new Level(2, "Les visiteurs ont un Wi-Fi invité séparé ; les vieux protocoles de partage sont désactivés.",
                "Créer un Wi-Fi invité séparé et désactiver les vieux protocoles (SMB1).", "0 à 150 €", "1 heure", true),
            new Level(3, "Les objets connectés (caméras, imprimantes) sont sur un réseau à part, et seuls les accès nécessaires sont ouverts.",
                "Isoler caméras et objets connectés sur un réseau séparé et fermer les ports inutiles de la box.", "100 à 400 €", "½ journée", true),
        }, new[] { "Protocole SMB1", "Réseaux Wi-Fi enregistrés", "Serveurs DNS", "Redirections réseau (hosts, proxy)" }),

        new Theme("mobilite", "Accès à distance et portables", "Les accès à distance et les ordinateurs portables sont-ils protégés ?", new[]
        {
            new Level(0, "Bureau à distance ouvert sur Internet, ou outils de prise en main installés sans contrôle."),
            new Level(1, "Seuls les outils d'accès à distance connus et utilisés restent installés.",
                "Retirer les logiciels de prise en main inutiles et fermer le Bureau à distance s'il est ouvert.", "0 €", "15 min / poste", true),
            new Level(2, "Les portables sont chiffrés (BitLocker) : un vol n'expose pas les données.",
                "Chiffrer les portables avec BitLocker et conserver les clés de récupération.", "0 €", "30 min / poste", true),
            new Level(3, "Le travail à distance passe par un VPN ou un accès protégé par double authentification.",
                "Mettre en place un VPN ou un accès distant avec double authentification.", "0 à 300 €", "½ journée", true),
        }, new[] { "Bureau à distance", "Logiciels de prise en main à distance", "Chiffrement BitLocker" }),

        new Theme("prestataires", "Inventaire et prestataires", "Sait-on quel matériel, quels logiciels et quels prestataires ont accès au système ?", new[]
        {
            new Level(0, "Aucune liste du matériel, des comptes ou des prestataires."),
            new Level(1, "Une liste à jour du matériel, des logiciels et des abonnements existe.",
                "Faire l'inventaire du matériel, des logiciels, des abonnements et des comptes d'administration.", "0 €", "½ journée", true),
            new Level(2, "On sait quels prestataires ont un accès, et les accès des anciens salariés et prestataires sont fermés.",
                "Lister les accès des prestataires et fermer ceux des personnes parties.", "0 €", "2 heures", true),
            new Level(3, "Les contrats précisent la sécurité et les sauvegardes, et l'inventaire est revu chaque année.",
                "Revoir les contrats (sécurité, sauvegarde, réversibilité) et l'inventaire chaque année.", "0 €", "2 heures / an", false),
        }, System.Array.Empty<string>()),

        new Theme("incident", "Incident et continuité", "L'entreprise sait-elle quoi faire si elle est piratée ou si un poste tombe en panne ?", new[]
        {
            new Level(0, "Rien de prévu : en cas d'attaque, personne ne sait qui appeler."),
            new Level(1, "Les numéros utiles sont affichés (prestataire, banque, 17Cyber / cybermalveillance.gouv.fr).",
                "Afficher les contacts d'urgence et les premiers gestes (débrancher le réseau, ne pas éteindre, appeler).", "0 €", "30 min", true),
            new Level(2, "Une procédure simple dit comment continuer à travailler sans informatique pendant quelques jours.",
                "Écrire une procédure de mode dégradé (factures, agenda, contacts clients disponibles hors ligne).", "0 €", "2 heures", true),
            new Level(3, "La procédure a été testée, et une assurance cyber a été étudiée.",
                "Tester la procédure une fois par an et étudier une assurance cyber.", "Selon assureur", "½ journée / an", false),
        }, System.Array.Empty<string>()),
    };

    public static IReadOnlyList<Threat> Threats { get; } = new[]
    {
        new Threat("Rançongiciel", "Les fichiers sont chiffrés et une rançon est demandée.",
            new[] { "sauvegardes", "misesajour", "protection", "sensibilisation", "incident" }),
        new Threat("Fraude au virement", "Faux RIB de fournisseur, faux dirigeant qui demande un virement urgent.",
            new[] { "sensibilisation", "comptes" }),
        new Threat("Piratage de la messagerie", "Le compte e-mail est volé et sert à arnaquer les clients.",
            new[] { "comptes", "sensibilisation" }),
        new Threat("Vol ou fuite de données", "Données clients exposées : obligation de déclaration à la CNIL.",
            new[] { "comptes", "mobilite", "reseau", "prestataires" }),
        new Threat("Perte de données", "Panne de disque, vol ou incendie sans copie de secours.",
            new[] { "sauvegardes", "incident" }),
        new Threat("Faux support technique", "Un faux technicien prend la main sur le poste.",
            new[] { "sensibilisation", "protection", "mobilite" }),
    };

    public static Theme Find(string id) => Themes.First(t => t.Id == id);
}
