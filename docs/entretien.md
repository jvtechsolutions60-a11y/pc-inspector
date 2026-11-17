# JVTECH Entretien : entretien automatique à chaque ouverture de session

Petit programme sans fenêtre, à installer sur les PC sous contrat. Il se lance 5 minutes après **chaque ouverture de
session** et fait à chaque fois l'entretien complet : état avant (version de Windows, mises à jour Windows en
attente, logiciels à mettre à jour, espace disque, antivirus, redémarrage nécessaire), mises à jour Windows, mises à
jour des logiciels de la **liste sûre** (winget), état après, puis rapport dans le dossier du PC sur le NAS et une
ligne dans `suivi-entretien.csv`.

Sur le NAS, dans le dossier réglé (`dossier_partage`) :

```
suivi-entretien.csv                une ligne par PC et par ouverture de session : avant / après (s'ouvre dans Excel)
POSTE\POSTE_2026-11-17_0905.html   rapport de l'entretien (un dossier par PC)
POSTE\journal.log                  journal du PC : chaque étape, chaque mise à jour et son résultat, les erreurs
```

Il ne redémarre **jamais** le PC et n'affiche **rien** à l'utilisateur : il tourne en tâche de fond (compte SYSTEM).
Seule chose visible possible : la notification habituelle de Windows « redémarrage nécessaire » après des mises à jour.

## Installer sur un PC

1. Décompresser `JVTECH-Entretien-vX.zip` dans un dossier du PC (ou sur la clé).
2. Remplir `entretien.ini` : code client, `dossier_partage` (ex. `\\NAS\JVTECH\Entretien`), compte du NAS autorisé à
   écrire (`partage_utilisateur`, `partage_mot_de_passe`).
3. Invite de commandes **en administrateur**, dans ce dossier : `JvtechEntretien.exe --installer`.
   L'installation vérifie d'abord qu'elle peut écrire sur le NAS, puis copie le programme dans
   `C:\Program Files\JVTECH\Entretien` (réglages lisibles seulement par les administrateurs) et crée la tâche
   planifiée « JVTECH\Entretien » (5 minutes après chaque ouverture de session).
4. Pour essayer tout de suite : `JvtechEntretien.exe --etat` (montre l'état sans rien installer), puis
   `JvtechEntretien.exe --maintenant` (fait l'entretien tout de suite).

Désinstaller : `JvtechEntretien.exe --desinstaller` (en administrateur). Les rapports déjà sur le NAS restent.

## Le NAS

- Créer sur le NAS un dossier partagé (ex. `JVTECH`) et un compte qui peut y écrire, utilisé seulement pour ça.
- Le PC doit être sur le même réseau que le NAS. S'il ne le voit pas (portable en déplacement, NAS éteint), rien n'est
  perdu : rapport, ligne du tableau et journal restent sur le PC et partent au NAS à la prochaine ouverture de session où il
  répond.
- Plusieurs PC peuvent écrire dans le même tableau : chaque ligne est ajoutée à la suite.

## Le rapport et le tableau

Aucune donnée personnelle : seulement le code client, le nom du poste, les mises à jour et l'état du PC (ni nom
d'utilisateur, ni e-mail, ni fichier). Colonnes de `suivi-entretien.csv` : date, client, poste, mises à jour Windows (avant,
installées, en échec, après), logiciels (avant, mis à jour, en échec, après, autres non touchés), disque libre avant
et après, antivirus, redémarrage nécessaire, points à voir (« OK » si rien).

Points à voir signalés : Windows Update ou winget en échec ou absent, disque système sous le seuil
(`disque_libre_alerte`, 15 % par défaut), antivirus absent, désactivé ou pas à jour, PC qui attendait déjà un
redémarrage avant l'entretien.

## Liste sûre des logiciels

Par défaut : Chrome, Firefox (et ESR), Thunderbird, Adobe Reader, Foxit Reader, 7-Zip, WinRAR, VLC, Notepad++,
LibreOffice, Zoom, Java, IrfanView, SumatraPDF, Audacity, GIMP. Les autres logiciels (métier, Office, antivirus,
pilotes, prise en main à distance) ne sont jamais mis à jour : leur nombre est seulement indiqué dans le rapport.
Pour changer la liste d'un PC : `logiciels = Google.Chrome; 7zip.7zip; …` dans `entretien.ini`.

Limites :
- en compte SYSTEM, winget ne voit que les logiciels installés pour tout le PC ; ceux installés pour un seul compte
  ne sont pas mis à jour ;
- un PC sans winget (vieux Windows 10) : seulement Windows Update, et « Logiciels non mis à jour : winget n'est pas
  installé » dans le rapport ;
- pas de réseau à l'ouverture de session : rien n'est installé, « Windows Update ne répond pas » dans le rapport ;
- une ouverture de session sans mise à jour à faire prend une ou deux minutes ; avec des mises à jour Windows, plus
  longtemps (en tâche de fond, plafonné à 4 heures).

## En cas de problème

Journal : `POSTE\journal.log` sur le NAS, et `C:\ProgramData\JVTECH\Entretien\entretien.log` sur le PC (une ligne
datée par étape, chaque mise à jour et son résultat, les erreurs). Rapports des 60 derniers jours aussi gardés sur le PC dans ce dossier.
