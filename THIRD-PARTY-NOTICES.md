# Composants tiers et licences (SBOM simplifié)

État au 13/11/2026. Ce document recense tout ce qui n'est pas écrit par JVTECH : ce qui est **embarqué** dans les
.exe, ce qui est **téléchargé** par la compilation ou par les logiciels, et ce que le technicien **dépose lui-même**.
Il ne remplace pas un avis juridique : les conditions d'usage professionnel marquées « à confirmer » doivent être
relues sur le site de l'éditeur avant une utilisation commerciale.

## 1. Embarqué dans les .exe JVTECH

| Composant | Version | Licence | Où | Empreinte SHA-256 | Source |
|---|---|---|---|---|---|
| 7-Zip, version autonome `7zr.exe` (Igor Pavlov) | 26.06 | GNU LGPL 2.1 (+ restriction unRAR, sans objet pour 7zr) | Ressource de `PcInspector.exe` (`src/PcInspector/Tools/`), extraite dans le dossier de travail pour le test processeur | `e80db2dad715a6c5adabe292d31ed54e11f4e9b99ca349827b50573b175891bc` | https://www.7-zip.org/ · https://github.com/ip7z/7zip/releases |

Obligation LGPL : 7zr.exe est redistribué **non modifié**, comme programme séparé (lancé, pas lié). Le code source
est disponible chez l'éditeur (liens ci-dessus) ; la licence est à joindre à la distribution (ce fichier et
https://www.7-zip.org/license.txt).

Aucun paquet NuGet n'est utilisé dans les logiciels (seulement .NET Framework 4.8, fourni par Windows).

## 2. Téléchargé par la compilation (GitHub Actions)

| Composant | Version | Licence | Utilisation | Contrôle |
|---|---|---|---|---|
| Win11Debloat (Raphire) | étiquette 2026.08.24 = commit `f431c56159bfd0b68822c583509126bab8cb94cc` | MIT | Copié dans le kit du serveur PXE, lancé par PC Maintenance pendant la préparation revente | Téléchargé **par commit** ; empreinte des scripts `3bacb5953738abe54cf764fb0e618431e485f09ebc5f775e4b957925e3366f2b` contrôlée par la CI **et** par PC Maintenance avant lancement (refus si différent) |
| MSTest.TestFramework / TestAdapter | 3.6.4 | MIT | Tests automatisés uniquement, jamais livré | NuGet |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT | Tests automatisés uniquement, jamais livré | NuGet |
| Actions GitHub (checkout, setup-dotnet, upload/download-artifact, attest-build-provenance, softprops/action-gh-release) | voir `build.yml` | MIT | Compilation | Épinglées par **SHA de commit** |

Licence MIT de Win11Debloat : la notice de copyright est conservée dans le dossier copié (`LICENSE`).

## 3. Téléchargé par les logiciels, à la demande du technicien

| Composant | Éditeur | Licence / usage pro | Logiciel | Contrôle avant lancement |
|---|---|---|---|---|
| AdwCleaner | Malwarebytes | Gratuit ; usage en entreprise autorisé selon l'éditeur (**à confirmer** sur malwarebytes.com, conditions d'AdwCleaner) | PC Maintenance | Fichier `.part`, puis signature Authenticode valide **et** signataire Malwarebytes exigés |
| Mises à jour cumulatives Windows | Microsoft | Licence Windows | JVTECH Serveur PXE | HTTPS, hôtes Microsoft uniquement, signature « Microsoft » exigée, refus d'une version plus ancienne |
| Outils constructeurs : Dell Command Update, Lenovo System Update, HP Image Assistant | Dell, Lenovo, HP | Gratuits, fournis par le constructeur pour ses PC | PC Maintenance (préparation revente) | winget, identifiants exacts (empreinte du manifeste winget) |
| Pack logiciels : Chrome, Firefox, 7-Zip, VLC, LibreOffice, Malwarebytes | leurs éditeurs | leurs licences (gratuites) | PC Maintenance, à la demande | winget, identifiants exacts (`SoftwarePack.cs`) |

## 4. Déposé à la main par le technicien (facultatif)

| Composant | Éditeur | Licence | Contrôle |
|---|---|---|---|
| Cinebench | Maxon | Gratuit, **non redistribuable** : chaque technicien le télécharge chez Maxon | Lancé seulement si sa signature Authenticode est valide (éditeur affiché dans les résultats) |
| Blender Benchmark (`benchmark-launcher-cli`) | Blender Foundation | GPL (outil gratuit) | Même contrôle de signature ; non signé = non lancé |

Ces outils ne sont **pas** inclus dans les .zip JVTECH.

## 5. Outils de Windows utilisés

winsat, bcdedit, dism, sfc, robocopy, netsh, powercfg, cleanmgr, MpCmdRun, schtasks, winget, diskpart (Windows PE),
nvidia-smi (si pilote NVIDIA). Ils font partie de Windows ou du pilote, et sont appelés par leur chemin complet
dans `System32`.

## Mise à jour de ce document

À chaque changement de version d'un composant : mettre à jour la version, l'empreinte et la date en haut. Pour
Win11Debloat : changer `WIN11DEBLOAT`, `WIN11DEBLOAT_COMMIT` et `WIN11DEBLOAT_SHA256` dans `build.yml` **après avoir
lu les changements** du script.
