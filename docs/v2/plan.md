# JVTECH v2 : plan validé

Plan complet : document « JVTECH v2 : analyse de reprise et plan ». Validé le 13/11/2026 (D1), Atelier d'abord,
puis Audit. Ce fichier garde dans le dépôt les règles qui s'imposent au code.

## Choix confirmés

- Deux exécutables autonomes (un seul .exe chacun) : **JVTECH Atelier** et **JVTECH Audit**, avec une bibliothèque
  commune `JvtechLecture` en lecture seule (son code est compilé dans chaque .exe, aucune DLL à côté).
- Seul `JvtechAtelier.Actions` modifie Windows, et seul Atelier l'inclut. Des tests automatiques le vérifient.
- Aucun PXE obligatoire : Atelier fonctionne après une installation de Windows 11 par clé USB.
- Rapports et dossiers atelier sur la clé USB par défaut ; partage réseau configurable.
- Pack de base : 7-Zip et VLC ; le reste en option.
- PC Inspector, PC Sécurité, PC Maintenance, PC Migration et le serveur PXE restent compilés pendant tout le
  développement.
- Développement par lots, tests automatisés à chaque lot, recette sur PC réels (VM ou PC de test, jamais un poste
  client).
- **Compilation et tests automatisés ne valent pas validation** pour les opérations sensibles. Les corrections de
  sécurité sont suivies dans [registre-securite.md](registre-securite.md).

## Ajustements demandés à la validation (13/11/2026)

1. **Sysprep** : le mode audit de Windows reste le seul parcours de finalisation au départ. Une finalisation depuis
   une session Windows normale fera l'objet d'une étude séparée (risques pour les données et la stabilité, essais
   en VM) avant toute décision. Tant que l'étude n'est pas validée, le bouton Finaliser est inactif hors mode audit.
2. **Mémoire RAM** : deux niveaux distincts.
   - Contrôle rapide sans redémarrage (lecture des barrettes, erreurs mémoire déjà signalées par Windows, résultats
     des diagnostics précédents). Libellé : « aucune erreur signalée », jamais « mémoire sans défaut ».
   - Diagnostic approfondi (diagnostic mémoire de Windows, avec redémarrage, lancé seulement par le technicien),
     résultat relu dans les journaux au retour.
3. **Statut avant vente** : « Prêt » est impossible si un contrôle obligatoire est en échec **ou n'a pas été fait**.
   Deux familles séparées à l'écran et dans les règles :
   - anomalie bloquante → « Bloqué » ;
   - réserve commerciale (batterie usée, rayure, touche lustrée…) → « Prêt avec réserve », réserve écrite dans la
     fiche et l'annonce.
   Contrôle obligatoire non fait → « Incomplet ».
4. **Windows 11** : base de compatibilité tirée des listes officielles Microsoft, avec sa date et une mise à jour
   possible sans recompiler. Trois conclusions distinctes : *matériel incompatible* (processeur absent de la liste,
   pas de TPM 2.0…), *configuration BIOS/UEFI à corriger* (Secure Boot ou TPM désactivé, démarrage hérité),
   *impossible de conclure* (information illisible, processeur inconnu de la base). Jamais « compatible » par défaut.
5. **Batterie** : capacité nominale, capacité actuelle, taux d'usure et nombre de cycles **quand Windows les
   fournit** (sinon « non disponible »). Seuils commerciaux réglables (par défaut : 80 % et plus sans mention,
   60 à 79 % réserve, moins de 60 % réserve et remplacement conseillé).
6. **Fiches et annonces** : chaque information détectée peut être corrigée à la main ; la valeur détectée reste
   gardée à côté (affichée dans la fiche interne, journalisée). Une caractéristique absente reste « non détectée » :
   rien n'est jamais déduit ou inventé (ni dans le titre, ni dans l'annonce).

## Lots

| Lot | Contenu | Validation |
|---|---|---|
| 0 | Bibliothèque commune, statuts, valeurs détectées/corrigées, journal des actions, squelettes des 2 .exe, tests de séparation | Compilation + tests automatisés (aucune opération sensible) |
| 1 | JVTECH Atelier P0 | Tests auto + recette T14 à T21, T26 en VM puis PC de test |
| 2 | JVTECH Audit P0 | Tests auto + recette T01 à T13, T22 sur 3 PC au moins |
| 3 | P1 et ergonomie | Retours d'atelier |
| 4 | Recette complète T01 à T26, documentation, signature si décidée, version 2.0 | Procès-verbal |

Étude à part (après le lot 1) : finalisation Sysprep hors mode audit (ajustement 1).
