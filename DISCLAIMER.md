# Avertissement Légal & Clause de Non-Responsabilité (`DISCLAIMER.md`)

> **IMPORTANT — VEUILLEZ LIRE ATTENTIVEMENT CE DOCUMENT AVANT D'UTILISER `DEEPUNINSTALLER PRO`.**  
> L'utilisation de ce logiciel implique l'acceptation pleine et entière des termes ci-dessous ainsi que de la [Licence MIT](./LICENSE).

---

## 1. Logiciel Fourni « TEL QUEL » ("AS IS") — Absence de Garantie

Conformément à la [Licence MIT](./LICENSE), **DeepUninstaller Pro** (code source, scripts et fichiers exécutables compilés) est mis à disposition gratuitement et **« TEL QUEL » (*"AS IS"*)**, sans aucune garantie d'aucune sorte, expresse ou implicite, y compris, mais sans s'y limiter, les garanties de qualité marchande, d'adéquation à un usage particulier, de fiabilité ou d'absence d'erreur.

L'ensemble du risque lié à l'utilisation, aux résultats et aux performances du logiciel est assumé **exclusivement par l'utilisateur**.

---

## 2. Nature de l'Application & Risques Inhérents aux Opérations Système

**DeepUninstaller Pro** est un utilitaire d'administration système avancé conçu pour :
- Exécuter la désinstallation complète ou silencieuse de logiciels installés sur Windows.
- Arrêter les processus et services Windows associés aux applications ciblées.
- Rechercher et supprimer des fichiers, des répertoires (`Program Files`, `AppData`, `ProgramData`, `%TEMP%`, etc.), des raccourcis, des entrées de démarrage automatique (`Autorun`) et des clés/valeurs de la base de **Registre Windows (`HKCU` et `HKLM`)**.
- Rechercher des dossiers et clés de registre dits « orphelins » ou effectuer des désinstallations forcées.

Bien que le logiciel intègre des listes de protection pour les dossiers et processus critiques de Windows, **toute modification du Registre Windows ou suppression de fichiers système/applicatifs comporte un risque inhérent** de perte de données, de suppression de fichiers partagés entre plusieurs logiciels d'un même éditeur, ou d'instabilité d'applications tierces.

---

## 3. Responsabilité Exclusive de l'Utilisateur

1. **Vérification obligatoire avant suppression** :
   - L'application présente toujours la liste détaillée des résidus, fichiers et clés de registre détectés **avant** de procéder à leur suppression.
   - **Il appartient exclusivement à l'utilisateur de vérifier chaque ligne cochée** avant de confirmer la suppression, en particulier lors de l'utilisation :
     - du **Niveau 3 (Profond)** de l'analyse des résidus ;
     - des éléments signalés avec une fiabilité **« Moyenne (Vérifier) »** ;
     - de l'outil de **Traque Ciblée (Désinstallation Forcée)** ;
     - de l'outil de **Détection Automatique Globale des Orphelins** ;
     - du **Gestionnaire de Démarrage** et du **Nettoyeur de Fichiers Temporaires**.
2. **Précautions recommandées** :
   - Conservez les protections activées par défaut : **Sauvegarde automatique des clés de registre (`.reg` dans `Backups/`)** et **Envoi des fichiers supprimés vers la Corbeille Windows**.
   - Cochez l'option **« Créer un Point de Restauration Système Windows »** avant de désinstaller des applications système importantes ou des lots multiples d'applications.
   - Fermez et sauvegardez vos travaux en cours dans vos autres applications avant de lancer un nettoyage.

---

## 4. Limitation de Responsabilité

En aucun cas les auteurs, créateurs, contributeurs ou détenteurs du droit d'auteur de **DeepUninstaller Pro** ne pourront être tenus responsables de tout dommage direct, indirect, accessoire, spécial, consécutif ou punitif (notamment, mais sans s'y limiter : perte de données ou de fichiers personnels, corruption du Registre Windows, dysfonctionnement du système d'exploitation ou de logiciels tiers, interruption d'activité) résultant de l'utilisation, d'une mauvaise utilisation ou de l'impossibilité d'utiliser ce logiciel, même s'ils ont été informés de la possibilité de tels dommages.

---

## 5. Indépendance & Marques Déposées

- **DeepUninstaller Pro** est un projet open-source **100 % indépendant et original**, développé à partir de zéro en C# / WPF.
- Ce projet n'est affilié à, financé par, ni approuvé par **Microsoft Corporation** ou tout autre éditeur de logiciels tiers.
- *Microsoft*, *Windows*, *Microsoft Store*, *OneDrive* et *.NET* sont des marques commerciales ou des marques déposées de Microsoft Corporation. Tous les autres noms de produits ou d'entreprises éventuellement affichés dans l'interface lors de l'inventaire des applications installées sur le PC de l'utilisateur appartiennent à leurs propriétaires respectifs.
