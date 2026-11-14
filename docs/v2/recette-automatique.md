# Recette automatique sur machine Windows jetable (GitHub Actions)

Workflow : `.github/workflows/recette-windows.yml`, outil : `tools/JvtechRecette` (non livré). À chaque envoi sur la branche
v2, une machine Windows neuve de GitHub lance les **vraies lectures** d'Audit et d'Atelier, ouvre `JvtechAudit.exe`
60 secondes, et compare le registre, les services, les tâches planifiées et `%TEMP%` avant et après.

**Ce que cela prouve** : le code de lecture tourne sur un vrai Windows sans planter ni bloquer, produit un rapport,
et ne modifie aucun réglage. **Ce que cela ne prouve pas** : la machine est une machine virtuelle Windows Server
2025 (Azure, compte administrateur, langue anglaise), sans batterie, sans TPM visible, avec des disques virtuels
sans SMART. Rien ne remplace la recette sur PC réels (`recette-audit.md`, `recette-atelier.md`). Aucune action
d'Atelier n'est lancée (mises à jour, installations, Sysprep).

## Premier passage : 14/11/2026, run 43581045991

| Point | Résultat |
|---|---|
| Lectures d'Audit (18 parties) | Faites en 1,7 s, aucune bloquée, code de sortie 0 |
| Lectures d'Atelier (inventaire, mode, disques, mémoire, batterie, pilotes) | Faites sans erreur |
| `JvtechAudit.exe` | Démarré, toujours ouvert après 60 s, fermé proprement, sans demande d'élévation |
| Réglages modifiés (registre surveillé, modes de démarrage des services, tâches) | **Aucun** |
| Écarts observés | Traces que Windows écrit lui-même quand un programme est lancé : UserAssist et MuiCache (liste des programmes ouverts), BAM (activité des programmes), saisie (CTF/TypingInsights), synchronisation de l'heure. Service « smphost » (fournisseur de stockage) démarré à la demande par Windows pour la lecture des disques, mode de démarrage inchangé. Aucune écriture dans `%TEMP%`. |
| Valeurs lues | Machine virtuelle Microsoft, AMD EPYC 7763, 16 Go, 2 disques 161 Go, Windows activé, pare-feu actif, UAC actif, Defender absent (« Aucun antivirus détecté »), BitLocker désactivé, 1 périphérique en erreur (code 28) |

Anomalies trouvées et corrigées :
1. Windows Server 2025 (build 26100) annoncé « Windows 11 24H2 (Famille/Pro), hors support depuis le 13/10/2026 ».
   Corrigé : Windows Server reconnu, nom « Windows Server Datacenter », date de fin « à vérifier », jamais devinée.
2. Bureau à distance ouvert sur un poste administré compté « Conforme ». Corrigé : « À vérifier » (à voir avec
   l'administrateur), comme le prévoit T05.
3. Points de restauration non lus écrits deux fois dans « Limites de cet audit ». Corrigé.

À surveiller sur PC réel : l'inscription MDM « Deploy Authority » vue sur cette machine Azure a fait passer le
poste en « Poste administré ». Vérifier sur des PC de particuliers qu'aucune inscription de ce type n'apparaît (T05).
