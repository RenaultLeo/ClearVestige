# Politique de Sécurité & Transparence Technique (`SECURITY.md`)

Ce document détaille le fonctionnement interne de **DeepUninstaller Pro**, les opérations système qu'il effectue, les garde-fous de sécurité intégrés au code source, ainsi que la procédure de signalement d'une vulnérabilité.

---

## 1. Philosophie & Confidentialité (100 % Hors-Ligne)

- **Aucune connexion réseau / Zéro télémétrie** : `DeepUninstaller Pro` ne contient aucun appel réseau (`HttpClient`, `WebRequest`, `Socket`). Il n'envoie aucune donnée, statistique ou liste d'applications vers Internet.
- **Zéro dépendance externe** : L'application utilise exclusivement les bibliothèques natives de Microsoft présentes par défaut dans Windows (`.NET Framework 4.x`, `PresentationFramework` WPF et les API Win32 standard de Windows). Aucun paquet NuGet tiers ni bibliothèque opaque n'est embarqué.
- **Code source 100 % ouvert et auditable** : L'intégralité du programme tient dans le dossier [`src/`](./src) et peut être recompilée en moins de 3 secondes sur n'importe quel PC Windows via le script [`build.ps1`](./build.ps1) utilisant le compilateur officiel `csc.exe` intégré à Windows.

---

## 2. Opérations Système Effectuées par l'Application

De par sa nature de **désinstallateur complet et nettoyeur de résidus système**, l'application effectue les opérations suivantes, **uniquement à la demande explicite de l'utilisateur** :

1. **Lecture de l'inventaire logiciel (`src/AppScanner.cs`)** :
   - Lecture en mode non destructif des ruches de désinstallation du Registre (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`, `WOW6432Node` et `HKCU`) ainsi que de la liste des packages Microsoft Store (`Get-AppxPackage`).
2. **Exécution des désinstallateurs officiels (`src/UninstallerEngine.cs`)** :
   - Lancement de la commande officielle de désinstallation inscrite par l'éditeur du logiciel (`UninstallString` / `QuietUninstallString` / `msiexec.exe` / `Remove-AppxPackage`).
3. **Arrêt ciblé des processus et services de l'application désinstallée (`src/UninstallerEngine.cs`)** :
   - Fermeture des processus exécutés depuis le dossier d'installation de l'application cible et utilisation de l'API officielle **Windows Restart Manager (`rstrtmgr.dll`)** pour identifier les processus tiers verrouillant un fichier à supprimer.
4. **Suppression de fichiers, dossiers et entrées du Registre Windows (`src/UninstallerEngine.cs`)** :
   - Envoi vers la **Corbeille Windows** via l'API officielle `shell32.dll!SHFileOperation` (`FOF_ALLOWUNDO`), ou suppression directe (`System.IO.File.Delete` / `Directory.Delete`).
   - Pour les bibliothèques `.dll` ou exécutables encore chargés en mémoire au moment de la désinstallation (ex: extensions du menu contextuel de l'Explorateur), utilisation de l'API officielle Microsoft [`kernel32.dll!MoveFileEx`](https://learn.microsoft.com/fr-fr/windows/win32/api/winbase/nf-winbase-movefileexw) avec le drapeau standard `MOVEFILE_DELAY_UNTIL_REBOOT` afin que Windows finalise leur nettoyage au redémarrage suivant sans interrompre la session.

---

## 3. Garde-Fous & Protections Intégrées

Pour prévenir les suppressions accidentelles de composants critiques de Windows, plusieurs protections strictes sont codées en dur :

- **Protection des répertoires système (`AppScanner.IsProtectedSystemDirectory`)** :
  - Il est techniquement impossible pour le scanner ou le moteur de suppression de supprimer les répertoires racines ou conteneurs système partagés : `C:\Windows`, `C:\Program Files`, `C:\Program Files (x86)`, `C:\Program Files\WindowsApps`, `%LocalAppData%\Packages`, `%LocalAppData%\Programs`, `%ProgramData%`, `%AppData%`, etc.
- **Protection des processus système Windows (`UninstallerEngine.IsSystemOrProtectedProcess`)** :
  - L'application s'interdit strictement d'arrêter tout processus système de Windows (`explorer.exe`, `svchost.exe`, `csrss.exe`, `lsass.exe`, `dwm.exe`, `SearchHost.exe`, `RuntimeBroker.exe`, `dllhost.exe`, `msiexec.exe`, etc.) ainsi que tout exécutable situé dans `C:\Windows\`.
- **Protection des clés racines du Registre (`UninstallerEngine.DeleteRegistryKeyTree`)** :
  - Toute tentative de suppression portant sur une clé racine (`Software`, `Software\Microsoft`, `CurrentVersion\Uninstall`) est automatiquement bloquée.
- **Sauvegardes automatiques avant nettoyage** :
  - **Sauvegarde Registre (`.reg`)** : Activée par défaut. Avant la suppression d'une clé ou valeur de registre, un fichier `.reg` horodaté est exporté dans le dossier local `Backups/` (restaurable par simple double-clic).
  - **Corbeille Windows** : Activée par défaut pour tous les fichiers et dossiers supprimés.
  - **Point de Restauration Système Windows** : Option disponible en un clic (via `Checkpoint-Computer`) avant toute désinstallation en mode Administrateur.

---

## 4. Avertissement Windows Defender SmartScreen & Exécutables Non Signés

Les exécutables fournis dans les *Releases GitHub* sont compilés automatiquement par **GitHub Actions** à partir du code source public, mais **ne sont pas signés avec un certificat payant d'autorité de certification commerciale (EV Code Signing)**.

Par conséquent :
- Lors du premier lancement d'un fichier `.exe` téléchargé depuis Internet, **Windows SmartScreen** peut afficher un bandeau d'avertissement standard (*« Windows a protégé votre ordinateur — Éditeur inconnu »*).
- Certains moteurs antivirus heuristiques peuvent analyser plus attentivement un utilitaire capable de lire le Registre, de gérer les entrées de démarrage (`Run`) et de lancer des désinstallations en mode Administrateur.

**Comment vérifier ou compiler vous-même l'application :**
1. Vérifiez l'empreinte **SHA-256** de `DeepUninstaller.exe` avec le fichier `SHA256SUMS.txt` généré par le workflow GitHub Actions.
2. Ou clonez simplement le dépôt et compilez vous-même votre propre exécutable en ouvrant PowerShell dans le dossier du projet :
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
   ```

---

## 5. Versions Supportées & Signalement d'une Vulnérabilité

| Version | Support de sécurité |
| :--- | :--- |
| `v1.4.x` (Dernière version) | ✅ Supportée |
| `< v1.4` | ❌ Non supportée (mettez à jour vers la dernière release) |

Si vous identifiez un bug de sécurité (par exemple un faux positif du scanner de résidus susceptible de cibler un dossier partagé ou système) :
1. **Ouvrez une Issue sur GitHub** (ou utilisez l'onglet *Security Advisories* du dépôt) en précisant :
   - Le nom de l'application testée.
   - Le niveau de scan sélectionné (*Niveau 1*, *2* ou *3* / *Détection d'orphelins*).
   - Le chemin du fichier ou de la clé de registre concerné.
2. N'exécutez pas la suppression si vous avez un doute sur un élément listé avec une fiabilité *« Moyenne (Vérifier) »*.
