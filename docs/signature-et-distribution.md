# Signature et distribution : état et étude

## Ce qui est en place (v1.0.87 et suivantes)

- **Une version = une compilation GitHub Actions** (`v1.0.N`, N = numéro de compilation), identique dans les .exe,
  les rapports et la page de version.
- **Tests avant livraison** : si un test automatisé échoue, aucun .exe n'est produit.
- **Empreintes SHA-256** : `SHA256SUMS.txt` est joint à chaque version et à l'artefact. Vérifier un téléchargement :
  `Get-FileHash .\PcMaintenance.exe -Algorithm SHA256` puis comparer avec la ligne du fichier.
- **Updater** : il ne télécharge et n'installe rien. Il compare le numéro de version publié sur GitHub (HTTPS) et
  ouvre la page de la version. Pas de risque de mise à jour incomplète ou détournée par le logiciel lui-même.
- **Fichiers téléchargés par les logiciels** : AdwCleaner (PC Maintenance) et les mises à jour intégrées à l'image
  PXE ne sont lancés ou intégrés que s'ils portent une signature Authenticode valide de l'éditeur (Malwarebytes,
  Microsoft). AdwCleaner est téléchargé dans un fichier `.part` puis renommé seulement s'il est complet et signé.
- **Win11Debloat** : téléchargé par son commit, empreinte des scripts contrôlée par la CI et par PC Maintenance avant
  lancement (refus si différent). Voir `THIRD-PARTY-NOTICES.md`.
- **Attestation de provenance** (depuis le 13/11/2026) : chaque fichier publié sur `main` porte une attestation signée
  (Sigstore) qui prouve qu'il vient de la compilation GitHub de ce dépôt. Vérification : `docs/verification-telechargement.md`.
- **Chaîne de compilation** : actions GitHub épinglées par SHA de commit, droits en lecture seule sauf pour l'étape
  de publication.

## Ce qui manque : la signature de code des .exe JVTECH

Aujourd'hui les .exe ne sont pas signés. Conséquences : avertissement SmartScreen « éditeur inconnu » au premier
lancement, et certains antivirus sont plus méfiants envers un .exe non signé qui demande les droits administrateur.
**Ne jamais désactiver SmartScreen ou l'antivirus pour contourner cela.**

| Option | Coût indicatif | Remarques |
|---|---|---|
| Azure Artifact Signing (ex-Trusted Signing) | environ 10 €/mois | Signature dans GitHub Actions, réputation SmartScreen rapide. Vérification d'identité de l'entreprise (SIRET) requise. **Recommandé.** |
| Certificat OV classique (Sectigo, Certum…) | 200 à 400 €/an | Clé sur jeton matériel (obligatoire depuis 2023), donc signature difficile dans la CI. |
| Certificat EV | 300 à 600 €/an | Plus utile pour SmartScreen autrefois ; l'avantage a été réduit par Microsoft. |
| Certificat auto-signé | gratuit | Inutile pour les clients : Windows ne lui fait pas confiance. |

Tarifs à revérifier au moment de la décision. La signature est une décision (coût, démarches) qui reste à prendre :
rien n'a été mis en place.
