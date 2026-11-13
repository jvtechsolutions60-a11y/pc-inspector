# Vérifier un téléchargement JVTECH Outils

Réponse au point I10 de l'audit du 13/11/2026. Les .exe ne sont pas encore signés (décision en attente, voir
`signature-et-distribution.md`). Deux preuves existent en attendant, **à vérifier avant d'installer chez un client**.

## 1. Empreintes SHA-256 (intégrité)

Chaque version publie `SHA256SUMS.txt`. Dans PowerShell, dans le dossier du téléchargement :

```powershell
Get-FileHash .\JVTECH-Outils-v1.0.N.zip -Algorithm SHA256
```

Le résultat doit être identique (en minuscules) à la ligne du fichier dans `SHA256SUMS.txt`. Cela prouve que le
fichier est complet et identique à celui publié, **pas** qui l'a publié : un attaquant qui remplacerait la page de
version pourrait aussi remplacer `SHA256SUMS.txt`. D'où la preuve suivante.

## 2. Attestation de provenance (origine, signée)

Depuis la v1.0.N publiée sur `main` après le 13/11/2026, chaque .zip et chaque .exe publié a une **attestation de
provenance signée** (GitHub + Sigstore) : elle prouve que le fichier a été produit par la compilation GitHub
Actions de ce dépôt, à partir d'un commit précis. Vérification, avec l'outil `gh` de GitHub :

```powershell
gh attestation verify .\PcMaintenance.exe -R jvtechsolutions60-a11y/pc-inspector
```

Résultat attendu : « Verification succeeded! » avec le nom du workflow `build.yml`. Tout autre résultat :
**ne pas utiliser le fichier**. Les attestations sont aussi visibles sur la page « Attestations » de l'onglet
Actions du dépôt.

## 3. Ce que ces preuves ne remplacent pas

- La signature Authenticode : sans elle, SmartScreen affiche « éditeur inconnu ». **Ne jamais désactiver
  SmartScreen ni l'antivirus** pour contourner l'avertissement : cliquer « Informations complémentaires » puis
  « Exécuter quand même » seulement après les vérifications 1 et 2.
- La reproductibilité au bit près : non garantie (horodatages de compilation). L'attestation relie le fichier au
  commit et au journal de compilation, consultables publiquement.
