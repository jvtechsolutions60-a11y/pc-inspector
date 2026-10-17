using System;
using System.Collections.Generic;
using System.IO;

namespace PcSecurite.Checks;

/// <summary>Un mode d'emploi pas à pas, et le réglage de Windows à ouvrir pour le faire.</summary>
public sealed class Tutorial
{
    public Tutorial(string[] steps, string? openLabel = null, string? openFile = null, string? openArgs = null, bool clientCanDo = true)
    {
        ClientCanDo = clientCanDo;
        Steps = steps;
        OpenLabel = openLabel;
        OpenFile = openFile;
        OpenArgs = openArgs;
    }

    public string[] Steps { get; }
    /// <summary>Texte du bouton, par exemple « Ouvrir Windows Update ».</summary>
    public string? OpenLabel { get; }
    /// <summary>Adresse ms-settings:, windowsdefender: ou programme à lancer.</summary>
    public string? OpenFile { get; }
    public string? OpenArgs { get; }
    /// <summary>Faux quand l'opération est réservée au technicien (BIOS, registre…) : la fiche client renvoie alors vers lui.</summary>
    public bool ClientCanDo { get; }

    /// <summary>Les étapes numérotées, une par ligne.</summary>
    public string Text => string.Join(Environment.NewLine, Numbered());

    public IEnumerable<string> Numbered()
    {
        for (int i = 0; i < Steps.Length; i++) yield return $"{i + 1}. {Steps[i]}";
    }
}

/// <summary>Les tutoriels, un par contrôle. Les chemins suivent Windows 11 ; Windows 10 est indiqué quand il diffère.</summary>
public static class Tutorials
{
    private static string Hosts => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    public static Tutorial? For(SecurityCheck check) => check.Name switch
    {
        "Version de Windows" when check.Kind == CheckKind.Change => new(new[]
        {
            "Ouvrir PC Inspector, onglet Windows 11, pour voir si le PC est compatible.",
            "S'il est compatible : Paramètres > Windows Update > « Mettre à niveau vers Windows 11 », ou l'Assistant d'installation de Windows 11 sur le site de Microsoft.",
            "Sauvegarder les données du client avant la mise à niveau.",
            "S'il n'est pas compatible (processeur, TPM) : proposer le remplacement du PC.",
        }, "Ouvrir Windows Update", "ms-settings:windowsupdate", clientCanDo: false),

        "Version de Windows" or "Dernière mise à jour" or "Mises à jour en attente" or "Certificats Secure Boot 2023" => new(new[]
        {
            "Ouvrir Paramètres > Windows Update.",
            "Cliquer sur « Rechercher des mises à jour », puis « Tout télécharger et installer ».",
            "Dans « Options avancées > Mises à jour facultatives », installer aussi les mises à jour de pilotes et de micrologiciel.",
            "Redémarrer le PC, puis recommencer jusqu'à ce qu'il n'y ait plus rien à installer.",
            "Si Windows Update bloque : vérifier l'espace libre sur C: (au moins 20 Go) et lancer l'utilitaire de résolution des problèmes.",
        }, "Ouvrir Windows Update", "ms-settings:windowsupdate"),

        "Antivirus" => new(new[]
        {
            "Ouvrir Sécurité Windows > Protection contre les virus et menaces.",
            "Si un autre antivirus est indiqué comme expiré : le désinstaller (Paramètres > Applications), Defender se réactive tout seul.",
            "Sinon, dans « Paramètres de protection », activer la protection en temps réel.",
            "Cliquer sur « Mises à jour de la protection » puis « Rechercher des mises à jour ».",
            "Garder un seul antivirus actif.",
        }, "Ouvrir la protection antivirus", "windowsdefender://threat"),

        "Microsoft Defender" => new(new[]
        {
            "Ouvrir Sécurité Windows > Protection contre les virus et menaces > Gérer les paramètres.",
            "Activer « Protection en temps réel » et « Protection contre les falsifications ».",
            "Plus bas, dans « Exclusions », supprimer chaque exclusion que le client ne connaît pas.",
            "Dans Contrôle des applications et du navigateur > Protection fondée sur la réputation, activer « Blocage d'applications potentiellement indésirables ».",
            "Lancer une analyse complète si des exclusions suspectes ont été trouvées.",
        }, "Ouvrir les réglages de Defender", "windowsdefender://threatsettings"),

        "Pare-feu" => new(new[]
        {
            "Ouvrir Sécurité Windows > Pare-feu et protection du réseau.",
            "Cliquer sur chaque réseau marqué comme désactivé (domaine, privé, public).",
            "Activer « Pare-feu Microsoft Defender ».",
        }, "Ouvrir le pare-feu", "windowsdefender://network"),

        "Activation de Windows" => new(new[]
        {
            "Ouvrir Paramètres > Système > Activation.",
            "Cliquer sur « Résolution des problèmes » : Windows retrouve souvent la licence liée à la carte mère.",
            "Sinon, « Modifier la clé de produit » et saisir la clé du client (la clé OEM du BIOS est visible dans PC Inspector).",
        }, "Ouvrir l'activation", "ms-settings:activation"),

        "Contrôle de compte (UAC)" => new(new[]
        {
            "Ouvrir « Modifier les paramètres de contrôle de compte d'utilisateur ».",
            "Remettre le curseur sur le deuxième cran en partant du haut (réglage par défaut).",
            "Valider puis redémarrer le PC.",
        }, "Ouvrir le réglage UAC", "UserAccountControlSettings.exe"),

        "SmartScreen" => new(new[]
        {
            "Ouvrir Sécurité Windows > Contrôle des applications et du navigateur > Paramètres de protection fondée sur la réputation.",
            "Activer « Vérifier les applications et les fichiers » et « SmartScreen pour Microsoft Edge ».",
            "Si le réglage est grisé, une stratégie le bloque : la retirer dans gpedit.msc (Composants Windows > Explorateur de fichiers > Configurer Windows Defender SmartScreen).",
        }, "Ouvrir SmartScreen", "windowsdefender://appbrowser"),

        "Intégrité de la mémoire" => new(new[]
        {
            "Ouvrir Sécurité Windows > Sécurité de l'appareil > Détails de l'isolation du noyau.",
            "Activer « Intégrité de la mémoire » puis redémarrer.",
            "Si un pilote incompatible est signalé : le mettre à jour ou désinstaller le vieux logiciel qui l'installe, puis réessayer.",
            "Si le réglage n'existe pas : activer la virtualisation dans le BIOS (Intel VT-x ou AMD SVM).",
        }, "Ouvrir l'isolation du noyau", "windowsdefender://coreisolation"),

        "Protection LSA" => new(new[]
        {
            "Ouvrir Sécurité Windows > Sécurité de l'appareil > Détails de l'isolation du noyau.",
            "Activer « Protection de l'autorité de sécurité locale ».",
            "Redémarrer le PC.",
            "Sous Windows 10 : créer la valeur DWORD RunAsPPL = 1 dans HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa (regedit), puis redémarrer.",
        }, "Ouvrir l'isolation du noyau", "windowsdefender://coreisolation"),

        "Blocage des pilotes vulnérables" => new(new[]
        {
            "Ouvrir Sécurité Windows > Sécurité de l'appareil > Détails de l'isolation du noyau.",
            "Activer « Liste de blocage des pilotes vulnérables Microsoft ».",
            "Redémarrer le PC.",
        }, "Ouvrir l'isolation du noyau", "windowsdefender://coreisolation"),

        "Mots de passe en mémoire (WDigest)" => new(new[]
        {
            "Ouvrir l'Éditeur du Registre.",
            "Aller dans HKLM\\SYSTEM\\CurrentControlSet\\Control\\SecurityProviders\\WDigest.",
            "Mettre UseLogonCredential à 0 (ou supprimer la valeur).",
            "Redémarrer, lancer une analyse antivirus complète et faire changer les mots de passe du client : ce réglage est souvent posé par un pirate.",
        }, "Ouvrir le Registre", "regedit.exe", clientCanDo: false),

        "Sauvegarde des données" => new(new[]
        {
            "Le plus simple : se connecter à OneDrive, puis Paramètres > Comptes > Sauvegarde Windows > Sauvegarde des dossiers, et cocher Bureau, Documents et Images.",
            "Sans abonnement : brancher un disque externe, puis Panneau de configuration > Historique des fichiers > Activer.",
            "Pour les points de restauration : rechercher « Créer un point de restauration », choisir C: > Configurer > Activer la protection du système.",
            "Garder une copie hors de la maison (cloud ou disque rangé ailleurs) : un rançongiciel chiffre aussi le disque externe branché.",
        }, "Ouvrir la sauvegarde Windows", "ms-settings:backup"),

        "Chiffrement BitLocker" => new(new[]
        {
            "Vérifier que le compte du client est un compte Microsoft (la clé de récupération y sera sauvegardée).",
            "Windows 11 Famille : Paramètres > Confidentialité et sécurité > Chiffrement de l'appareil > Activer.",
            "Windows Pro : Panneau de configuration > Chiffrement de lecteur BitLocker > Activer BitLocker sur C:.",
            "Enregistrer la clé de récupération (compte Microsoft ou impression) et la remettre au client.",
        }, "Ouvrir BitLocker", "control.exe", "/name Microsoft.BitLockerDriveEncryption"),

        "Secure Boot" => new(new[]
        {
            "Vérifier dans PC Inspector que le disque est en GPT. Sinon, le convertir d'abord : mbr2gpt /convert /allowFullOS dans une invite de commande administrateur.",
            "Redémarrer dans le BIOS : Paramètres > Système > Récupération > Démarrage avancé > Paramètres du microprogramme UEFI.",
            "Passer le mode de démarrage en UEFI (désactiver CSM/Legacy).",
            "Activer Secure Boot (parfois après avoir chargé les clés par défaut, « Install default keys »), enregistrer et redémarrer.",
        }, "Ouvrir le démarrage avancé", "ms-settings:recovery", clientCanDo: false),

        "Puce TPM" when check.Kind == CheckKind.Change => new(new[]
        {
            "Chercher dans le BIOS une option TPM 2.0 : certaines cartes mères passent de 1.2 à 2.0 par une mise à jour du micrologiciel TPM du fabricant.",
            "Sinon, Windows 11 ne pourra pas être installé normalement : prévoir le remplacement du PC.",
        }, clientCanDo: false),

        "Puce TPM" => new(new[]
        {
            "Redémarrer dans le BIOS : Paramètres > Système > Récupération > Démarrage avancé > Paramètres du microprogramme UEFI.",
            "Chercher l'option TPM : « Intel PTT » sur Intel, « AMD fTPM » sur AMD, souvent dans Sécurité ou Avancé.",
            "L'activer, enregistrer et redémarrer.",
            "Vérifier ensuite avec tpm.msc.",
        }, "Ouvrir le démarrage avancé", "ms-settings:recovery", clientCanDo: false),

        "BIOS" => new(new[]
        {
            "Relever le modèle exact du PC et la version du BIOS (onglet Carte mère de PC Inspector).",
            "Aller sur la page support du fabricant et chercher un BIOS plus récent.",
            "Brancher le portable sur secteur, suspendre BitLocker s'il est actif (manage-bde -protectors -disable C:).",
            "Lancer la mise à jour et ne pas couper le PC pendant l'opération.",
            "S'il n'existe aucun BIOS récent, le PC n'est plus suivi par le fabricant : le noter dans le rapport.",
        }, clientCanDo: false),

        "Protocole SMB1" => new(new[]
        {
            "Ouvrir « Activer ou désactiver des fonctionnalités Windows ».",
            "Décocher « Support de partage de fichiers SMB 1.0/CIFS ».",
            "Valider et redémarrer.",
            "Vérifier ensuite que le NAS ou l'imprimante réseau du client fonctionne toujours (sinon, mettre à jour leur micrologiciel).",
        }, "Ouvrir les fonctionnalités Windows", "optionalfeatures.exe", clientCanDo: false),

        "PowerShell 2.0" => new(new[]
        {
            "Ouvrir « Activer ou désactiver des fonctionnalités Windows ».",
            "Décocher « Windows PowerShell 2.0 ».",
            "Valider et redémarrer.",
        }, "Ouvrir les fonctionnalités Windows", "optionalfeatures.exe"),

        "Bureau à distance" => new(new[]
        {
            "Ouvrir Paramètres > Système > Bureau à distance.",
            "Désactiver le Bureau à distance si le client ne s'en sert pas.",
            "S'il en a besoin : laisser cochée « Exiger que les appareils utilisent l'authentification au niveau du réseau ».",
        }, "Ouvrir le Bureau à distance", "ms-settings:remotedesktop"),

        "Logiciels de prise en main à distance" => new(new[]
        {
            "Demander au client s'il a installé ces logiciels lui-même et qui s'en sert.",
            "Ouvrir Paramètres > Applications > Applications installées et désinstaller ceux qui ne servent pas.",
            "Pour une version portable (en cours d'exécution), la fermer et supprimer le fichier, souvent dans Téléchargements.",
            "Si le client a eu un appel d'un faux support : changer ses mots de passe (banque, e-mail) depuis un autre appareil et prévenir sa banque.",
        }, "Ouvrir les applications installées", "ms-settings:appsfeatures"),

        "Redirections réseau (hosts, proxy)" => new(new[]
        {
            "Ouvrir le fichier hosts (le bouton l'ouvre dans le Bloc-notes avec les droits administrateur).",
            "Supprimer les lignes que le client ne connaît pas, puis enregistrer.",
            "Ouvrir Paramètres > Réseau et Internet > Proxy et désactiver le proxy et le script de configuration s'ils ne sont pas voulus.",
            "Lancer une analyse antivirus complète.",
        }, "Ouvrir le fichier hosts", "notepad.exe", "\"" + Hosts + "\"", clientCanDo: false),

        "Réseaux Wi-Fi enregistrés" => new(new[]
        {
            "Ouvrir Paramètres > Réseau et Internet > Wi-Fi > Gérer les réseaux connus.",
            "Cliquer sur chaque réseau signalé puis « Oublier ».",
            "Pour un réseau ouvert utile (hôtel, gare) : décocher « Se connecter automatiquement ».",
        }, "Ouvrir le Wi-Fi", "ms-settings:network-wifisettings"),

        "Comptes utilisateurs" => new(new[]
        {
            "Pour mettre un mot de passe : Paramètres > Comptes > Options de connexion > Mot de passe (ou un code PIN Windows Hello).",
            "Pour désactiver le compte Invité ou Administrateur intégré : invite de commande administrateur, net user Invité /active:no (ou net user Administrateur /active:no).",
            "Sous Windows Pro, on peut aussi passer par lusrmgr.msc.",
        }, "Ouvrir les options de connexion", "ms-settings:signinoptions"),

        "Connexion automatique" => new(new[]
        {
            "Ouvrir netplwiz (le bouton le lance).",
            "Cocher « Les utilisateurs doivent entrer un nom d'utilisateur et un mot de passe pour utiliser cet ordinateur ».",
            "Valider puis redémarrer pour vérifier.",
            "Si la case n'apparaît pas : Paramètres > Comptes > Options de connexion, désactiver « Pour plus de sécurité, autoriser uniquement la connexion Windows Hello ».",
        }, "Ouvrir netplwiz", "netplwiz.exe"),

        "Navigateurs internet" => new(new[]
        {
            "Chrome : menu ⋮ > Aide > À propos de Google Chrome.",
            "Edge : menu … > Aide et commentaires > À propos de Microsoft Edge.",
            "Firefox : menu ☰ > Aide > À propos de Firefox.",
            "Laisser la mise à jour se faire puis cliquer sur « Relancer ».",
            "Si la mise à jour échoue, réinstaller le navigateur depuis son site officiel.",
        }),

        "Logiciels avec failles connues" => new(new[]
        {
            "Télécharger la dernière version de chaque logiciel sur son site officiel et l'installer par-dessus.",
            "Plus rapide : invite de commande administrateur, winget upgrade --all.",
            "Relancer l'analyse pour vérifier.",
        }),

        "Logiciels abandonnés" => new(new[]
        {
            "Ouvrir Paramètres > Applications > Applications installées.",
            "Désinstaller chaque logiciel signalé.",
            "Proposer un remplaçant : Microsoft 365 ou Office 2024 (ou LibreOffice gratuit), Acrobat Reader à jour, Java récent si une application en a besoin.",
        }, "Ouvrir les applications installées", "ms-settings:appsfeatures"),

        _ => null,
    };
}
