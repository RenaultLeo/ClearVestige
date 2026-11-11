# <img src="./assets/icon.png" width="38" height="38" valign="middle" alt="Logo" /> DeepUninstaller Pro — Désinstallateur Complet & Nettoyeur de Résidus (Windows)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6.svg)](#)
[![Framework: .NET 4.x / WPF](https://img.shields.io/badge/.NET%20Framework-4.x%20(Natif%20Windows)-512BD4.svg)](#)
[![Zero Telemetry](https://img.shields.io/badge/Privacy-100%25%20Offline%20%2F%20Zero%20Telemetry-10B981.svg)](./SECURITY.md)

**DeepUninstaller Pro** est une application de bureau native Windows (**C# / WPF**) 100 % originale, autonome, hors-ligne et sans aucune dépendance externe. Elle permet de désinstaller des applications (individuellement ou par lot), d'éradiquer en profondeur toutes leurs traces résiduelles (fichiers, dossiers, raccourcis et clés/valeurs du Registre Windows), de détecter les logiciels orphelins et d'optimiser le démarrage de Windows.

---

## ⚠️ Avertissement Légal & Sécurité ("AS IS")

> **Ce logiciel est fourni « TEL QUEL » (*"AS IS"*), sans garantie d'aucune sorte, sous [Licence MIT](./LICENSE).**  
> **DeepUninstaller Pro** est un outil d'administration système capable de désinstaller des programmes et de supprimer des fichiers, dossiers et clés du Registre Windows. Bien que des protections strictes soient intégrées (sauvegarde automatique `.reg` dans `Backups/`, envoi vers la Corbeille Windows, liste blanche des dossiers et processus système Windows), **l'utilisateur demeure seul responsable de la vérification des éléments cochés avant toute suppression**.  
> 📄 **Documentation complète à consulter avant utilisation :**
> - 🛡️ [**Politique de Sécurité & Transparence Technique (`SECURITY.md`)**](./SECURITY.md)
> - ⚖️ [**Avertissement Légal & Clause de Non-Responsabilité (`DISCLAIMER.md`)**](./DISCLAIMER.md)
> - 📜 [**Licence Open-Source MIT (`LICENSE`)**](./LICENSE)

---

## ✨ Fonctionnalités Principales (v1.4)

### 1. 📦 Inventaire Complet des Applications Installées
- Analyse simultanée du Registre Système 64-bit (`HKLM\SOFTWARE\...`), Système 32-bit (`WOW6432Node`), Utilisateur (`HKCU`), ainsi que des applications **Microsoft Store (`AppX`)** et composants système en option.
- Calcul automatique de l'espace disque occupé et détection intelligente du dossier d'installation.
- Recherche instantanée, tri par colonne (taille, date, éditeur) et filtres rapides (`64-bit`, `32-bit`, `Utilisateur`, `Volumineuses > 100 Mo`, `Installées récemment`).
- Export complet de l'inventaire au format **CSV**.

### 2. 🗑️ Désinstallation en Profondeur (Simple & Multiple par Lot)
- **Désinstallation individuelle ou par lot (`Ctrl + clic` / `Maj + clic`)** : sélectionnez plusieurs applications pour les désinstaller à la suite et regrouper l'analyse de leurs résidus dans une seule interface.
- **Arrêt automatique des processus et services actifs de l'application** (avec l'API officielle *Windows Restart Manager* `rstrtmgr.dll`, tout en protégeant intégralement `explorer.exe` et les processus système Windows).
- **Sauvegardes de sécurité avant nettoyage** :
  - Export automatique des clés de registre supprimées au format `.reg` dans le dossier `Backups/`.
  - Envoi des fichiers et dossiers vers la **Corbeille Windows** par défaut (`shell32.dll`).
  - Création optionnelle d'un **Point de Restauration Système Windows**.
- **Scan approfondi des résidus sur 3 niveaux (*Rapide*, *Standard*, *Profond*)** :
  - Dossier d'installation principal, `%AppData%` (Roaming), `%LocalAppData%` (Local, LocalLow, Programs), `%ProgramData%`, `%ProgramFiles%`, `%ProgramFiles(x86)%` et sous-dossiers d'applications Store.
  - Raccourcis orphelins du Bureau et du Menu Démarrer (`.lnk`, `.url`).
  - Clés de registre `HKCU\Software`, `HKLM\SOFTWARE`, `HKLM\SOFTWARE\WOW6432Node`.
  - Traces d'exécution avancées : Démarrage `Run`, `MuiCache`, `AppCompatFlags`, `CrashDumps`, `Prefetch` et `%TEMP%`.

### 3. 🔍 Recherche d'Applications Orphelines (2 Modes)
- **🎯 Traque Ciblée (Nom / Dossier / Exécutable)** : recherchez toutes les traces d'une application déjà désinstallée ou dont le désinstallateur est cassé en indiquant simplement son nom (même partiel ou avec espaces, ex: `one drive`, `VLC`) ou en sélectionnant son dossier/fichier résiduel.
- **⚡ Détection Automatique Globale** : analyse `Program Files`, `ProgramData`, `AppData` et les ruches `Software` du Registre en les croisant avec la liste des applications installées pour identifier les dossiers et clés abandonnés.

### 4. 🚀 Gestionnaire de Démarrage (Autorun) & 🧹 Nettoyeur de Fichiers Inutiles
- Visualisation et suppression des programmes lancés automatiquement au démarrage de Windows (`HKCU\...\Run`, `HKLM\...\Run`, dossiers `Startup`), avec signalement des entrées orphelines.
- Analyse et nettoyage en un clic des dossiers temporaires (`%TEMP%`, `Windows\Temp`, `CrashDumps`, rapports d'erreurs `WER`).

---

## 🚀 Lancement & Compilation Locale

Aucune installation de SDK lourd (Visual Studio, Node.js ou Python) n'est requise : l'application utilise le compilateur C# natif de Microsoft `.NET Framework 4.x` préinstallé sur tous les systèmes Windows 10 et Windows 11.

### Lancer l'application
- **Mode Administrateur (Recommandé)** : Double-cliquez sur [`Lancer_En_Administrateur.bat`](./Lancer_En_Administrateur.bat) (ou cliquez sur **« ⚡ Relancer en Administrateur »** dans l'en-tête de l'application).
- **Mode Standard** : Double-cliquez sur [`DeepUninstaller.exe`](./DeepUninstaller.exe) ou [`Lancer_DeepUninstaller.bat`](./Lancer_DeepUninstaller.bat).

### Compiler depuis le code source
```powershell
# 1. (Optionnel) Régénérer l'icône multi-tailles assets\app.ico
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\make_icon.ps1

# 2. Compiler DeepUninstaller.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 3. Exécuter l'auto-test de vérification
.\DeepUninstaller.exe --self-test | Out-Host
```

---

## ⚙️ Intégration Continue & Releases Automatiques (GitHub Actions)

Le dépôt inclut un workflow complet dans [`.github/workflows/ci-build-and-release.yml`](./.github/workflows/ci-build-and-release.yml) :
1. **À chaque `push` ou `pull_request` sur `main` / `master`** :
   - Compile `DeepUninstaller.exe` sur une machine virtuelle `windows-latest`.
   - Exécute la suite d'auto-tests (`--self-test`) pour valider le scanner d'applications, le scanner de résidus, les protections Store, le thème sombre et la fenêtre de désinstallation par lot.
   - Génère l'archive portable `DeepUninstaller-Pro-Portable-win-x64.zip` ainsi que le fichier d'empreintes `SHA256SUMS.txt` en artefact CI.
2. **À chaque publication d'un tag `v*` (ex: `git tag v1.4.0 && git push origin v1.4.0`) ou déclenchement manuel (`workflow_dispatch`)** :
   - Crée automatiquement une **GitHub Release** officielle avec `DeepUninstaller.exe`, `DeepUninstaller-Pro-Portable-win-x64.zip` et `SHA256SUMS.txt` prêts au téléchargement.

---

## 📁 Structure du Projet

```text
├── .github/workflows/
│   └── ci-build-and-release.yml   # Pipeline CI/CD GitHub Actions (Build + Auto-Test + Release)
├── assets/
│   ├── app.ico                    # Icône multi-résolutions (16px -> 256px) embarquée dans l'exécutable
│   └── icon.png                   # Logo haute résolution de l'application
├── src/
│   ├── Program.cs                 # Point d'entrée, gestion d'erreurs globale & mode --self-test
│   ├── Models.cs                  # Modèles de données (InstalledApp, LeftoverItem, OrphanCandidate...)
│   ├── UiTheme.cs                 # Thème sombre WPF personnalisé & chargement de l'icône embarquée
│   ├── AppScanner.cs              # Détection des applications (Registre 64/32-bit, HKCU, Store AppX)
│   ├── LeftoverScanner.cs         # Moteur de recherche des résidus (3 niveaux) & détecteur d'orphelins
│   ├── UninstallerEngine.cs       # Désinstallation, arrêt des processus tiers, sauvegardes .reg & nettoyage
│   ├── StartupAndJunkManager.cs   # Gestionnaire de démarrage Autorun & nettoyeur de fichiers temporaires
│   ├── MainWindow.cs              # Interface principale à 4 onglets
│   ├── UninstallWizardWindow.cs   # Assistant de désinstallation complète d'une application
│   └── BatchUninstallWindow.cs    # Assistant de désinstallation multiple par lot
├── build.ps1                      # Script de compilation native via csc.exe (.NET Framework 4.x)
├── make_icon.ps1                  # Générateur d'icône .ico multi-tailles sans dépendance externe
├── Lancer_En_Administrateur.bat   # Lanceur avec élévation UAC Administrateur
├── Lancer_DeepUninstaller.bat     # Lanceur standard
├── SECURITY.md                    # Politique de sécurité, opérations système & garde-fous
├── DISCLAIMER.md                  # Avertissement légal & clause de non-responsabilité ("AS IS")
└── LICENSE                        # Licence Open-Source MIT
```
