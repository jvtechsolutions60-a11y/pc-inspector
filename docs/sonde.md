# JVTECH Sonde

Petit programme sans fenêtre, installé sur les PC sous contrat de maintenance. Une fois par jour, il contrôle l'état
du PC et l'envoie à Uptime Kuma. **Lecture seule** : il n'installe, ne corrige et ne supprime rien sur le PC
(à part ses propres fichiers).

## Ce qui est contrôlé

| Point | Source | Seuils par défaut (sonde.ini) |
|---|---|---|
| Processeur : utilisation moyenne sur 30 s | compteurs Windows | orange ≥ 90 % |
| Processeur : température | LibreHardwareMonitor si PawnIO est installé, sinon zone ACPI (approchée) | orange ≥ 85 °C, rouge ≥ 95 °C |
| Mémoire utilisée | WMI | orange ≥ 90 %, rouge ≥ 97 % |
| Carte graphique : température | comme le Gestionnaire des tâches (PC Inspector) | orange ≥ 85 °C, rouge ≥ 95 °C |
| Remplissage de chaque disque | Windows | orange ≥ 85 %, rouge ≥ 95 % |
| Santé de chaque disque (SMART, NVMe) | PC Inspector | règles de PC Inspector |
| Usure de la batterie (portables) | PC Inspector | orange ≥ 40 %, rouge ≥ 60 % |
| Version de Windows et fin de support | PC Sécurité | règles de PC Sécurité |
| Antivirus, Microsoft Defender, pare-feu | PC Sécurité | règles de PC Sécurité |
| Plantages (écrans bleus, arrêts brutaux) sur 30 jours | journal Système | orange ≥ 1, rouge ≥ 3 |
| Redémarrage en attente | registre | orange |
| Windows Update : dernière recherche réussie | agent Windows Update | orange > 7 jours, rouge > 21 jours |
| Windows Update : dernière installation | PC Sécurité | règles de PC Sécurité |
| Windows Update : mises à jour en attente (rien n'est téléchargé) | PC Sécurité | orange s'il y en a |

Un point « non mesurable » (pas de sonde de température, accès refusé) n'est jamais une alerte.

## Mise en place sur un PC

1. **Dans Uptime Kuma**, ajouter un moniteur :
   - type **Push** ;
   - nom : client et poste (ex. « Mairie – ACCUEIL-01 ») ;
   - **intervalle de pulsation : 90000 secondes (25 h)**, pour qu'un passage par jour suffise ;
   - nouvelles tentatives : 1 ; les notifications habituelles (e-mail, téléphone).
   - Copier l'**adresse Push** affichée (`https://…/api/push/XXXX?status=up&msg=OK&ping=`).
2. **Sur le PC**, décompresser `JVTECH-Sonde-vX.zip` dans un dossier temporaire et remplir `sonde.ini` :
   `client`, `poste` si besoin, et `kuma_push` = l'adresse copiée (les paramètres `?status=…` peuvent rester).
3. Ouvrir une invite de commandes **en administrateur** dans ce dossier, puis :
   ```
   JvtechSonde.exe --sans-envoi
   ```
   pour voir les mesures, puis :
   ```
   JvtechSonde.exe --installer
   ```
   La sonde est copiée dans `C:\Program Files\JVTECH\Sonde`, la tâche planifiée `\JVTECH\Sonde` est créée
   (compte SYSTEM, chaque jour vers 12 h et 5 minutes après chaque démarrage, arrêtée au bout de 5 minutes)
   et un premier passage est lancé. Le moniteur doit passer au vert dans Kuma dans les minutes qui suivent.
4. Le dossier temporaire peut être supprimé.

Changer un réglage plus tard : modifier `C:\Program Files\JVTECH\Sonde\sonde.ini` (en administrateur).

## Lire les résultats

- **Uptime Kuma** : « up » (vert) si aucun point rouge, « down » (rouge) sinon. Le message liste les points
  rouges puis orange. Avec `alerte_orange = oui`, un point orange met aussi le moniteur en panne.
  Si le PC ne donne plus de nouvelles pendant 25 h (éteint, hors ligne, sonde supprimée), Kuma le signale.
- **Sur le PC**, dans `C:\ProgramData\JVTECH\Sonde` : `derniere-mesure.json`, `historique.jsonl` (les 400
  derniers passages, pour le rapport mensuel) et `sonde.log`.

## Température précise du processeur (facultatif)

Sans pilote, Windows ne donne qu'une valeur approchée (zone ACPI), et souvent aucune. Pour la valeur exacte, la
sonde utilise LibreHardwareMonitor (dossier `capteurs`), **seulement si le pilote PawnIO est déjà installé** :
installateur officiel signé du projet https://github.com/namazso/PawnIO, aussi disponible par
`winget install namazso.PawnIO`. La sonde n'installe jamais de pilote elle-même ; sans PawnIO,
LibreHardwareMonitor n'est même pas chargé. Installer un pilote noyau chez un client reste une décision au cas par cas.

## Désinstaller

En administrateur : `"C:\Program Files\JVTECH\Sonde\JvtechSonde.exe" --desinstaller`. La tâche planifiée, le
programme et `C:\ProgramData\JVTECH\Sonde` sont supprimés. Penser à supprimer le moniteur dans Kuma.

## Recette (à faire sur 3 PC avant le pilote)

- [ ] Un passage dure moins de 3 minutes (dont 30 s de mesure du processeur et jusqu'à 90 s de recherche Windows Update).
- [ ] Rien n'est modifié en dehors de `Program Files\JVTECH\Sonde`, `ProgramData\JVTECH\Sonde` et la tâche planifiée.
- [ ] `disque_orange = 1` dans sonde.ini, puis `schtasks /Run /TN "\JVTECH\Sonde"` : le disque apparaît en orange
      dans le message Kuma (et le moniteur passe en panne avec `alerte_orange = oui`). Remettre 85 ensuite.
- [ ] PC éteint plus de 25 h : Kuma signale le moniteur en panne.
- [ ] Les mises à jour Windows en attente sont listées et aucune n'est téléchargée (Paramètres > Windows Update inchangé).
- [ ] Après la mise en place de la signature : `Get-AuthenticodeSignature JvtechSonde.exe` est valide et Defender ne détecte rien.
- [ ] `--desinstaller` retire la tâche et les deux dossiers.
- [ ] Sur un PC avec PawnIO : la température du processeur indique « LibreHardwareMonitor + PawnIO ».
