# Références de PC Sécurité : tenue à jour

Toutes les données qui vieillissent sont dans un seul fichier : `src/PcSecurite/Checks/References.cs`.

| Liste | Contenu | Source à consulter |
|---|---|---|
| `WindowsSupport` | Build de Windows → nom de version → date de fin du support (Famille et Pro) | learn.microsoft.com, « Windows 11 release information » et « Lifecycle FAQ » |
| `KnownDns` | DNS des box françaises et DNS publics connus | pages d'assistance des opérateurs et des services DNS |
| `VulnerableSoftware` | Logiciel → première version corrigée d'une faille exploitée | catalogue CISA KEV, CERT-FR, notes de version de l'éditeur |
| `ObsoleteSoftware` | Logiciels sans aucun correctif | annonces de fin de vie des éditeurs |

La liste des processeurs compatibles Windows 11 n'est pas dans ce fichier : elle est dans PC Inspector
(`src/PcInspector/Collectors/Windows11Collector.cs`, expression `IsCpuSupported`). Elle se vérifie à chaque nouvelle
génération de processeurs Intel ou AMD.

## Procédure (tous les 3 mois, et à chaque nouvelle version de Windows)

1. Vérifier chaque liste avec les sources ci-dessus et la corriger dans `References.cs`.
2. Mettre la date du jour dans `UpdatedOn`.
3. Compiler (GitHub Actions) et diffuser la nouvelle version.

## Ce qui se passe si on oublie

- Plus d'un an après `UpdatedOn`, les contrôles « Version de Windows », « Logiciels avec failles connues » et
  « Logiciels abandonnés » n'affichent plus « EN ORDRE » mais « NON VÉRIFIÉ » (ils ne comptent alors plus dans la
  note). Un problème trouvé reste signalé : une date de fin de support passée reste passée.
- Une version de Windows plus récente que la dernière connue est affichée « NON VÉRIFIÉ » au lieu d'être confondue
  avec la précédente.
- Un DNS absent de `KnownDns` donne « À AMÉLIORER » (à vérifier avec le client) et non plus « URGENT » : un DNS
  d'entreprise, de VPN ou d'un petit opérateur n'est pas une attaque.

## Statuts d'un contrôle

| Statut | Affichage | Compte dans la note |
|---|---|---|
| Ok | EN ORDRE | oui (1 point) |
| Warning | À AMÉLIORER | oui (0,5 point) |
| Danger | URGENT | oui (0 point) |
| Info | INFO | non |
| NotApplicable | NON CONCERNÉ (pas de Wi-Fi, édition sans BitLocker…) | non |
| Unknown | NON VÉRIFIÉ (accès refusé, service muet, références trop anciennes) | non |

Un contrôle qui n'a pas pu conclure n'est jamais compté comme un échec, ni comme une réussite.
