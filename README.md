# <img src="./assets/icon.png" width="38" height="38" valign="middle" alt="Logo" /> ClearVestige — Désinstallateur Complet & Nettoyeur de Résidus (Windows)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](./LICENSE)
[![Platform: Windows 10/11](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D6.svg)](#)
[![Framework: .NET 4.x / WPF](https://img.shields.io/badge/.NET%20Framework-4.x%20(Natif%20Windows)-512BD4.svg)](#)
[![Zero Telemetry](https://img.shields.io/badge/Privacy-100%25%20Offline%20%2F%20Zero%20Telemetry-10B981.svg)](./SECURITY.md)

**ClearVestige** est une application de bureau native Windows (**C# / WPF**) 100 % originale, autonome, hors-ligne et sans aucune dépendance externe. Elle permet de désinstaller des applications (individuellement ou par lot), d'éradiquer en profondeur toutes leurs traces résiduelles (fichiers, dossiers, raccourcis et clés/valeurs du Registre Windows), de détecter les logiciels orphelins et d'optimiser le démarrage de Windows.

---

## ⚠️ Avertissement Légal & Sécurité ("AS IS")

> **Ce logiciel est fourni « TEL QUEL » (*"AS IS"*), sans garantie d'aucune sorte, sous [Licence MIT](./LICENSE).**  
> **ClearVestige** est un outil d'administration système capable de désinstaller des programmes et de supprimer des fichiers, dossiers et clés du Registre Windows. Bien que des protections strictes soient intégrées (sauvegarde automatique `.reg` dans `Backups/`, envoi vers la Corbeille Windows, liste blanche des dossiers et processus système Windows), **l'utilisateur demeure seul responsable de la vérification des éléments cochés avant toute suppression**.  
> 📄 **Documentation complète à consulter avant utilisation :**
> - 🛡️ [**Politique de Sécurité & Transparence Technique (`SECURITY.md`)**](./SECURITY.md)
> - ⚖️ [**Avertissement Légal & Clause de Non-Responsabilité (`DISCLAIMER.md`)**](./DISCLAIMER.md)
> - 📜 [**Licence Open-Source MIT (`LICENSE`)**](./LICENSE)

---

## ✨ Fonctionnalités Principales (v2.0)

### 1. 📦 Inventaire Complet, Icônes Réelles & Sélection Persistante
- **Vraies icônes Windows** chargées en arrière-plan pour chaque logiciel (`.exe`, `.ico`, Store AppX).
- **Cases à cocher persistantes (`✓ Cochées (N)`)** : cherchez une 1ʳᵉ appli, cochez-la, cherchez-en une 2ᵉ, cochez-la — votre sélection est **conservée entre toutes vos recherches et filtres** avant de lancer la désinstallation par lot !
- **Glisser-Déposer (Drag & Drop)** : déposez un raccourci du Bureau (`.lnk`), un `.exe` ou un dossier directement sur la fenêtre pour identifier et désinstaller le logiciel instantanément.
- **Barre de titre sombre intégrée (WindowChrome)** avec sélecteur de langue **`🌐 FR / EN`**, colonnes redimensionnables avec info-bulles complètes, **Rapport HTML sombre autonome**, **Export CSV** et **Export Script Winget** (réinstallation en 1 clic).

### 2. 🗑️ Désinstallation en Profondeur (Simple & Multiple par Lot)
- Désinstallation individuelle ou par lot avec arrêt gracieux puis forcé des processus/services tiers (`rstrtmgr.dll`), support silencieux **MSI / Inno Setup / NSIS / Squirrel**, sauvegarde `.reg` automatique, Corbeille Windows et Point de Restauration.
- Scan sur 3 niveaux (*Rapide*, *Standard*, *Profond*) incluant dossiers, raccourcis, clés Registre, entrées `Run`, `MuiCache`, `AppCompatFlags`, `CrashDumps`, `Prefetch`, chemins longs (`> 260 car.`) et **Tâches Planifiées Windows (`Scheduled Tasks`)**.

### 3. 🔍 Recherche d'Applications Orphelines (2 Modes)
- **🎯 Traque Ciblée** (Nom avec/sans espaces, dossier ou `.exe`) et **⚡ Détection Automatique Globale** des dossiers et clés abandonnés.

### 4. 🚀 Outils Système & Démarrage (3 Sous-Onglets)
- **🚀 Gestionnaire de Démarrage (`Autorun`)** : entrées `HKCU`/`HKLM Run` et dossiers `Startup`.
- **🔌 Détecteur de Services Windows tiers orphelins** : détecte et supprime les services dont l'`.exe` a disparu (tous les services `C:\Windows\` sont protégés).
- **🖱️ Nettoyeur du Menu Clic-Droit Windows** : repère et supprime (avec sauvegarde `.reg`) les entrées de menu contextuel tiers ou cassées.

### 5. 🧹 Nettoyeur de Caches & Fichiers Inutiles
- `%TEMP%`, `Windows\Temp`, `CrashDumps`, `WER`, **Cache Discord**, **Shaders GPU (DirectX / NVIDIA / AMD)**, **Caches Navigateurs (Chrome / Edge / Brave)**, **Delivery Optimization** et **Corbeille Windows**.

### 6. 📸 Surveillance d'Installation (Snapshot Avant/Après) & 🗂️ Historique / Restauration `.reg`
- **📸 Snapshot Avant / Après** : capturez l'état de votre PC avant d'installer un logiciel de test, puis comparez après pour voir **chaque dossier, raccourci, clé Registre et tâche planifiée ajoutés** et tout annuler proprement.
- **🗂️ Historique & Restauration `.reg` en 1 clic** : suivi de l'espace total libéré, aperçu des fichiers `.reg` et restauration directe dans le Registre.

---

## 🚀 Lancement & Compilation Locale

Aucune installation de SDK lourd (Visual Studio, Node.js ou Python) n'est requise : l'application utilise le compilateur C# natif de Microsoft `.NET Framework 4.x` préinstallé sur tous les systèmes Windows 10 et Windows 11.

### Lancer l'application
- **Mode Administrateur (Recommandé)** : Double-cliquez sur [`Lancer_En_Administrateur.bat`](./Lancer_En_Administrateur.bat) (ou cliquez sur **« ⚡ Relancer en Administrateur »** dans l'en-tête de l'application).
- **Mode Standard** : Double-cliquez sur [`ClearVestige.exe`](./ClearVestige.exe) ou [`Lancer_ClearVestige.bat`](./Lancer_ClearVestige.bat).

### Compiler depuis le code source
```powershell
# 1. (Optionnel) Régénérer l'icône multi-tailles assets\app.ico
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\make_icon.ps1

# 2. Compiler ClearVestige.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 3. Exécuter l'auto-test de vérification
.\ClearVestige.exe --self-test | Out-Host
```

---

## ⚙️ Intégration Continue & Releases Automatiques (GitHub Actions)

Le dépôt inclut un workflow complet dans [`.github/workflows/ci-build-and-release.yml`](./.github/workflows/ci-build-and-release.yml) :
1. **À chaque `push` ou `pull_request` sur `main` / `master`** :
   - Compile `ClearVestige.exe` sur une machine virtuelle `windows-latest`.
   - Exécute la suite d'auto-tests (`--self-test`) pour valider le scanner d'applications, le scanner de résidus, les protections Store, le thème sombre et la fenêtre de désinstallation par lot.
   - Génère l'archive portable `ClearVestige-Portable-win-x64.zip` ainsi que le fichier d'empreintes `SHA256SUMS.txt` en artefact CI.
2. **À chaque publication d'un tag `v*` (ex: `git tag v1.4.0 && git push origin v1.4.0`) ou déclenchement manuel (`workflow_dispatch`)** :
   - Crée automatiquement une **GitHub Release** officielle avec `ClearVestige.exe`, `ClearVestige-Portable-win-x64.zip` et `SHA256SUMS.txt` prêts au téléchargement.

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
├── Lancer_ClearVestige.bat     # Lanceur standard
├── SECURITY.md                    # Politique de sécurité, opérations système & garde-fous
├── DISCLAIMER.md                  # Avertissement légal & clause de non-responsabilité ("AS IS")
└── LICENSE                        # Licence Open-Source MIT
```
