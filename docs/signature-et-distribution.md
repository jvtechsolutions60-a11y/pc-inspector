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

## Mise en place de la signature (Azure Artifact Signing)

Décision du 13/11/2026 : Azure Artifact Signing, offre Basic (environ 10 €/mois, 5 000 signatures par mois). JVTECH
est une SARL : elle entre dans la catégorie « organisation de l'Union européenne » de la confiance publique.
La compilation GitHub est **déjà prête** : l'étape « Signer les .exe » s'active seule dès que les réglages ci-dessous
existent, puis l'étape « Vérifier les signatures » arrête la compilation si un fichier n'est pas signé correctement.

1. **Compte Azure** : sur https://portal.azure.com, créer un abonnement (paiement à l'usage) au nom de la SARL.
2. **Compte de signature** : rechercher « Artifact Signing », créer un compte (offre Basic, région Europe
   de l'Ouest ou Europe du Nord). Noter son nom et son **point de terminaison** (`https://weu.codesigning.azure.net/`
   pour l'Europe de l'Ouest, `https://neu.codesigning.azure.net/` pour l'Europe du Nord : il est affiché sur la page du compte).
3. **Validation d'identité** : dans le compte, « Identity validation » > « Public » > organisation, avec le nom
   légal exact de la SARL (celui du Kbis, qui sera affiché dans la signature), le SIRET et le Kbis. Compter quelques
   jours à quelques semaines.
4. **Profil de certificat** : une fois l'identité validée, « Certificate profiles » > « Public Trust ». Noter son nom.
5. **Application pour GitHub** : dans Microsoft Entra ID > « Inscriptions d'applications », créer « GitHub JVTECH
   signature », puis un **secret client**. Noter l'ID d'annuaire (tenant), l'ID d'application (client) et le secret.
6. **Droits** : sur le compte de signature, « Contrôle d'accès (IAM) » > ajouter le rôle
   **Artifact Signing Certificate Profile Signer** à cette application.
7. **Dans GitHub** (dépôt > Settings > Secrets and variables > Actions) :
   - onglet *Secrets* : `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` ;
   - onglet *Variables* : `AZURE_SIGNING_ENDPOINT` (point de terminaison de l'étape 2), `AZURE_SIGNING_ACCOUNT`
     (nom du compte), `AZURE_SIGNING_PROFILE` (nom du profil).
8. Relancer une compilation : les .exe JVTECH, JvtechSonde.exe et JvtechSonde.Capteurs.dll sortent signés
   (horodatage Microsoft, la signature reste valide après l'expiration du certificat). Les DLL de
   LibreHardwareMonitor ne sont pas signées par JVTECH : ce n'est pas notre code.

Le secret client expire (24 mois au plus) : noter sa date et le renouveler avant, sinon la compilation s'arrête.
SmartScreen peut encore avertir sur les toutes premières versions signées, le temps que la réputation s'établisse.
