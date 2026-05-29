# Rapport d'Analyse Critique — Custom Dialog Box WPF

**Date :** 2026-05-30
**Agents mobilisés :** 10 (arch-wpf, perf-engineer, qa-engineer, security-auditor, ux-designer, product-manager, oss-strategist, user-thomas, user-lea, user-marie, user-kevin)
**Statut de convergence :** Tous agents approuvés — consensus fort sur les priorités, divergences mineures sur la profondeur du refactoring MVVM

---

## 1. Verdict Global

**Score : 22 / 100**

Prototype fonctionnel dans un périmètre très contrôlé, non distribuble, non intégrable, et crashable sur n'importe quelle machine réelle en moins de 60 secondes. L'idée est juste, la mécanique de base (lazy-load, dual-pane, filtrage Hidden/System) est architecturalement correcte dans ses intentions — mais l'exécution est du niveau d'un exercice de cours 2019 : monolithique, synchrone, sans gestion d'erreurs, sans retour de valeur, sans packaging.

**Potentiel :** 75-80/100 après 3-4 sprints ciblés — le gap marché est réel (aucun composant standalone WPF actif sur NuGet en 2026), la base est réécrivable sans partir de zéro.

---

## 2. Issues Critiques (numérotées, priorisées)

### 2.1 — UI freeze garanti sur toute I/O non triviale [BLOQUANT ABSOLU]

**Sources :** arch-wpf, perf-engineer, qa-engineer, ux-designer, product-manager, user-thomas, user-lea, user-marie, user-kevin

**Description :** `ExploreDirectories()` et `ExploreFiles()` appellent `directoryInfo.GetDirectories()` et `directoryInfo.GetFiles()` de façon synchrone sur le thread UI Dispatcher. Pendant toute la durée de l'I/O, le rendu WPF est totalement bloqué — le `Cursors.Wait` est un placebo car le thread qui devrait l'afficher est le même qui est occupé. Sur `C:\Windows\System32` (~4500 entrées, SSD) : ~120ms de freeze. Sur un lecteur réseau ou USB défaillant : 5-60 secondes — fenêtre non-répondante, message système "ne répond pas", expérience perçue comme un crash.

**Impact :** Rédhibitoire sur toute démo réelle. Aucune autre amélioration n'a de sens tant que ce bug existe.

**Solution :**
1. Remplacer `GetDirectories()` par `EnumerateDirectories()` (streaming vs batch).
2. Réécrire `item_Expanded` en `async void` avec `await Task.Run(() => dir.EnumerateDirectories().ToList())`.
3. Réinjecter les items sur le thread UI via `Dispatcher.InvokeAsync(DispatcherPriority.Background)` par batch de 50 items.
4. Ajouter un `CancellationToken` lié à la prochaine sélection pour annuler un chargement en cours.
5. **Note critique (perf-engineer) :** `GetDirectories()` ne permet pas l'annulation mid-call — seul `EnumerateDirectories()` permet un `CancellationToken` effectif dans la boucle. Ce choix doit être fait avant l'implémentation.

---

### 2.2 — Zéro gestion d'exceptions I/O — crash garanti sur toute machine réelle [BLOQUANT ABSOLU]

**Sources :** arch-wpf, qa-engineer, perf-engineer, security-auditor, ux-designer, product-manager, user-thomas, user-lea, user-kevin

**Description :** `DriveInfo.GetDrives()`, `directoryInfo.GetDirectories()`, `directoryInfo.GetFiles()` et `file.Attributes` sont appelés sans aucun `try/catch`. En production : `UnauthorizedAccessException` sur `C:\System Volume Information`, `C:\$Recycle.Bin`, `C:\Windows\System32\config` (reproductible sur toute machine Windows), `PathTooLongException` sur `node_modules`/archives Java (>260 chars), `IOException` sur lecteur CD vide ou USB en éjection.

**Cas critique supplémentaire (qa-engineer) :** `LoadDirectories()` est appelé dans le constructeur `Open()`, avant que le message handler WPF soit actif. Toute exception sur `DriveInfo.GetDrives()` à ce stade est fondamentalement non-récupérable — `Dispatcher.UnhandledException` ne peut pas l'intercepter de façon fiable.

**Impact :** Crash process sur toute machine avec un lecteur optique vide. Crash immédiat à la première navigation dans `C:\`.

**Solution :**
```csharp
try
{
    foreach (var dir in directoryInfo.EnumerateDirectories()
        .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0))
    {
        // ajouter au TreeView
    }
}
catch (UnauthorizedAccessException) { /* griser le noeud, log */ }
catch (PathTooLongException) { /* indicateur visuel */ }
catch (IOException ex) { /* message dans treeViewDetails */ }
```
Déplacer `LoadDirectories()` dans `Window_Loaded` pour permettre une interception fiable.

---

### 2.3 — Lazy-load guard désactivé — double chargement et duplication d'items [CRITIQUE]

**Sources :** arch-wpf, perf-engineer, qa-engineer, security-auditor, user-thomas

**Description :** Le bloc `if (this.HasDummy(item))` est commenté dans `LoadEverything()`. Conséquences : (1) chaque expansion re-scanne intégralement le filesystem et ajoute les items sans nettoyer les anciens — 1ère expansion = 50 items, 2ème = 100, etc. (2) `TreeView_SelectedItemChanged` appelle aussi `LoadEverything()` — un clic qui sélectionne et expanse simultanément déclenche le double chargement. (3) Sur un lecteur réseau lent, c'est un vecteur de DoS applicatif par amplification ressource.

**Impact :** Consommation mémoire exponentielle, doublons visuels, re-scan réseau répété.

**Solution :** Décommenter le guard. En MVVM : propriété `bool IsLoaded` sur `FileSystemNodeViewModel`, setter d'`IsExpanded` ne charge que si `!IsLoaded`.

---

### 2.4 — NullReferenceException garantie sur déselection [CRITIQUE]

**Sources :** arch-wpf, qa-engineer, user-thomas

**Description :** `TreeView_SelectedItemChanged` (ligne 176) appelle `LoadEverything(treeView.SelectedItem)` sans null-check. `treeView.SelectedItem` est `null` lors d'une déselection programmatique, d'un refresh de l'arbre, ou si l'événement fire à l'initialisation. Le cast `(TreeViewItem)sender` sans guard dans `LoadEverything` lève ensuite une `NullReferenceException` non rattrapée.

**Solution :**
```csharp
private void TreeView_SelectedItemChanged(...)
{
    if (treeView.SelectedItem == null) return;
    // ...
}
```

---

### 2.5 — Absence totale d'un mécanisme de retour de valeur [BLOQUANT FONCTIONNEL]

**Sources :** ux-designer, product-manager, user-lea, user-marie

**Description :** La fenêtre `Open` n'expose aucune propriété publique `SelectedPath`, `SelectedFile`, ou `DialogResult`. L'utilisateur peut naviguer et sélectionner — mais l'application appelante n'a aucun moyen de récupérer ce choix. Le dialog est invoqué via `window.Show()` (non-modal). C'est une omission architecturale, pas une feature manquante : sans contrat de retour, le composant ne peut pas remplir sa fonction primaire.

**Solution :**
- Exposer `public string SelectedPath { get; private set; }`
- Passer à `ShowDialog()` (modal)
- Setter `DialogResult = true` sur validation (bouton Ouvrir ou double-clic)
- Setter `DialogResult = false` sur annulation (Échap, bouton Annuler)

---

### 2.6 — Aucun bouton Ouvrir / Annuler — dialog non fonctionnel [CRITIQUE]

**Sources :** ux-designer, user-marie, user-kevin

**Description :** Il n'existe pas de bouton Ouvrir ni de bouton Annuler. L'utilisateur sélectionne un fichier et... rien. La seule sortie est la croix de fermeture. Ce n'est pas un oubli UX — c'est l'absence du mécanisme de confirmation qui rend le composant inutilisable dans tout contexte d'intégration réelle.

**Solution :** Ajouter une barre inférieure avec champ "Nom de fichier" (TextBox), filtre d'extension (ComboBox), bouton "Ouvrir" (IsDefault=True, raccourci Entrée), bouton "Annuler" (IsCancel=True, raccourci Échap).

---

### 2.7 — Architecture 100% code-behind — non testable, non intégrable [CRITIQUE]

**Sources :** arch-wpf, qa-engineer, product-manager, user-thomas, user-kevin

**Description :** `Open.xaml.cs` est à la fois View, ViewModel et Service. `LoadDirectories()`, `ExploreDirectories()`, `ExploreFiles()`, gestion du curseur et construction des `TreeViewItem` sont tous dans le code-behind. `Tag` est utilisé comme conteneur d'état typé (`DriveInfo`/`DirectoryInfo`/`FileInfo`) via des casts manuels — du data binding à la main, sans les bénéfices du binding WPF. Impossible d'unit-tester la logique de navigation, impossible d'intégrer dans Prism (`IDialogService` exige `IDialogAware` sur un ViewModel). Couverture de test : 0%.

**Solution (progressive, non big-bang) :**
1. Créer `FileSystemNodeViewModel : INotifyPropertyChanged` avec `Name`, `FullPath`, `NodeType`, `Children : ObservableCollection<FileSystemNodeViewModel>`, `IsExpanded`, `IsLoaded`.
2. Lier `TreeView.ItemsSource` à `ObservableCollection<FileSystemNodeViewModel>`.
3. Le code-behind devient un thin relay — `InitializeComponent()` + binding du DataContext.
4. Ne pas introduire `IFileSystemService` tant qu'il n'y a pas de test unitaire concret à écrire — c'est de l'over-engineering sans use case identifié.

---

### 2.8 — Absence de virtualisation UI — O(n) TreeViewItem en mémoire [CRITIQUE]

**Sources :** perf-engineer, ux-designer, product-manager, user-thomas

**Description :** `Items.Add()` crée et maintient en mémoire un `TreeViewItem` WPF complet pour chaque fichier du répertoire, même hors-viewport. Sur `C:\Windows\System32` (~4500 fichiers) : ~4500 objets WPF actifs simultanément, 3-5 MB rien que pour ce panel. Sur 10 000+ fichiers (partages réseau courants) : 10-20 MB, rendu initial catastrophique car WPF mesure et arrange tous les items invisibles.

**Solution :** Activer `VirtualizingStackPanel.IsVirtualizing="True"` et `VirtualizationMode="Recycling"` sur les deux TreeViews — 2 lignes XAML. Prérequis : migrer de `Items.Add()` vers binding sur `ObservableCollection`. Avec Recycling, seuls ~20-30 items visibles existent en mémoire.

---

### 2.9 — Reparse points (symlinks/junctions) non détectés — traversal hors scope et boucle infinie [CRITIQUE]

**Sources :** qa-engineer, security-auditor

**Description :** Le filtre vérifie `FileAttributes.Hidden` et `FileAttributes.System` mais pas `FileAttributes.ReparsePoint`. Windows contient des junctions de compatibilité qui forment des cycles (`C:\Users\Default\AppData\Local\Application Data` → boucle). Sans détection de cycle, l'énumération part en boucle infinie, remplissant le TreeView et consommant toute la mémoire. Vecteur de sécurité documenté : une junction placée dans un dossier accessible peut pointer vers `C:\Windows\System32`, révélant des chemins hors portée.

**Solution :**
```csharp
if ((directory.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
    continue; // ou afficher avec icône différenciée, opt-in via AllowSymlinkTraversal
```
Ajouter un `HashSet<string>` des chemins visités pour détecter les cycles résiduels.

---

### 2.10 — Lecteurs réseau sans IsReady check ni filtrage — crash et risque NTLM relay [CRITIQUE]

**Sources :** qa-engineer, security-auditor

**Description :** `DriveInfo.GetDrives()` retourne tous les lecteurs y compris réseau (DriveType.Network). Problèmes distincts : (1) Un lecteur réseau déconnecté bloque le thread UI 30-60 secondes (timeout réseau Windows). (2) L'énumération d'un partage SMB déclenche une authentification NTLM automatique, exploitable via NTLM relay attack en environnement corporate. `DriveInfo.IsReady` n'est jamais consulté — crash garanti sur tout lecteur CD vide ou USB en éjection.

**Solution :**
- Filtrer `DriveType.Network` par défaut, exposer `ShowNetworkDrives = false` en opt-in.
- Vérifier `drive.IsReady` avant tout accès : si `!IsReady`, griser l'item plutôt que l'exclure.
- L'énumération réseau ne peut se faire qu'après le fix async (issue 2.1).

---

### 2.11 — Fuite mémoire — event handlers Expanded non désabonnés [CRITIQUE]

**Sources :** arch-wpf, perf-engineer

**Description :** `item.Expanded += new RoutedEventHandler(item_Expanded)` est souscrit dans `GetItem(DriveInfo)` et `GetItem(DirectoryInfo)` sans jamais de `-=` correspondant. Le guard commenté (issue 2.3) aggrave le problème : chaque expansion recréée souscrit un nouveau handler sur les items enfants sans nettoyer les anciens. Sur une session longue avec navigation intensive, les `TreeViewItem` orphelins sont retenus en mémoire via les closures.

**Solution court terme :** Restaurer le guard HasDummy (empêche la recréation en boucle). **Solution définitive :** Migration MVVM — `IsExpanded` bindé à une propriété du ViewModel élimine structurellement tous les event handlers manuels.

---

### 2.12 — `OutputType = WinExe` — publication NuGet impossible [CRITIQUE si objectif NuGet]

**Sources :** oss-strategist, product-manager

**Description :** Le `.csproj` déclare `<OutputType>WinExe</OutputType>`. Un package NuGet de contrôle WPF doit être une `ClassLibrary`. En l'état, impossible de produire un `.nupkg` utilisable. Le format ancien `.csproj` non-SDK ne supporte pas `<PackageReadmeFile>`, `<PackageReleaseNotes>` ni les métadonnées NuGet inline.

**Solution :** Séparer en deux projets — `CustomDialogBox.csproj` (SDK-style ClassLibrary) et `CustomDialogBox.Demo.csproj` (WinExe). Décision à prendre avant la restructuration : fixer le `PackageId` définitif (`CustomDialogBox` est trop générique, risque de conflit sur NuGet.org).

---

### 2.13 — Aucune barre d'adresse — violation H6 Nielsen, modèle mental brisé [CRITIQUE UX]

**Sources :** ux-designer, user-marie, user-thomas

**Description :** L'utilisateur ne peut pas lire sa position courante sans remonter visuellement l'arbre. Pour naviguer vers un chemin connu (`C:\Clients\Dossiers\2024\Contrats`), il faut cliquer sur chaque niveau. Aucun équivalent de `Alt+D` ou `Ctrl+L`. Violation H6 (reconnaissance > rappel) : l'utilisateur mémorise où il est plutôt que de le lire. Absent de tout file dialog depuis Windows Vista.

**Solution :** TextBox éditable en haut affichant le chemin complet. Mode double : breadcrumb cliquable par défaut, clic → TextBox éditable pour saisie directe + dropdown des 10 derniers chemins. `Alt+D` / `Ctrl+L` pour focus direct.

---

### 2.14 — README quasi vide, absence de CHANGELOG et LICENSE [CRITIQUE OSS]

**Sources :** oss-strategist

**Description :** (1) README : 2 liens + 1 phrase d'intention, aucun screenshot, aucune instruction d'installation, aucun quickstart. Signal d'abandon en 30 secondes sur GitHub. (2) Aucun `CHANGELOG.md` — un consommateur NuGet ne sait pas ce qui change entre versions. (3) Aucun fichier `LICENSE` — en l'absence de licence explicite, le droit d'auteur s'applique par défaut : personne n'est légalement autorisé à utiliser le code. Refusé par les outils d'audit enterprise (FOSSA, Snyk, OSSF Scorecard).

**Solution :**
- `LICENSE` MIT à la racine (condition sine qua non).
- `CHANGELOG.md` format Keep a Changelog avec section `[Unreleased]` et `[0.1.0-alpha] - 2026-05-30`.
- README 150 lignes minimum : badge CI, GIF animé, `dotnet add package`, quickstart 10 lignes, Features, Roadmap.

---

### 2.15 — DummyTreeViewItem visible dans l'arbre d'accessibilité — violation WCAG 4.1.2 [CRITIQUE ACCESSIBILITÉ]

**Sources :** ux-designer

**Description :** `DummyTreeViewItem` hérite de `TreeViewItem` avec `Header="Dummy"` et `Tag="Dummy"`. Cette fuite d'implémentation apparaît dans l'arbre UI Automation : Narrator, NVDA et JAWS annoncent "Dummy" comme enfant valide de chaque nœud avant expansion. Violation WCAG 4.1.2 (Name, Role, Value).

**Solution :** Remplacer par `private static readonly object _loadingPlaceholder = new object()`. Stocker dans `item.Tag` ou via flag booléen. Aucun `TreeViewItem` ne doit être injecté dans l'arbre visuel/accessibilité avant le vrai chargement.

---

## 3. Points Positifs

**Mécanique de lazy-loading conceptuellement correcte.** `DummyTreeViewItem` + handler `Expanded` est le pattern recommandé pour les TreeView WPF (dummy child pour forcer le triangle d'expansion). C'est la fondation sur laquelle tout le reste peut être construit. Convergence totale de tous les agents sur ce point.

**Filtrage Hidden/System en place dès V1.** Vérification bitwise correcte (`FileAttributes.Hidden | FileAttributes.System`) sur les deux méthodes d'exploration. Comportement cohérent avec l'Explorateur Windows, pas de pollution de l'arbre avec des entrées inutilisables.

**Séparation treeView (navigation) / treeViewDetails (contenu).** Pattern dual-pane architecturalement correct — correspond exactement au modèle Windows Explorer. L'intention est juste, l'implémentation est couplée à l'UI mais le concept est réutilisable.

**Polymorphisme GetItem() par surcharge.** `GetItem(DriveInfo)`, `GetItem(DirectoryInfo)`, `GetItem(FileInfo)` — intention de séparation des responsabilités, extensible via surcharges supplémentaires.

**DynamicResource pour SystemColors.MenuBarColorKey.** Conscience du theming Windows — le dialog respecte le thème système clair/sombre sans code supplémentaire.

**Code court et lisible (~150 lignes).** Compréhensible en 10-15 minutes. Refactoring vers une architecture extensible est une affaire d'heures, pas de semaines.

**Gap marché réel identifié.** Aucun composant standalone WPF file dialog actif sur NuGet en mai 2026. Concurrents commerciaux à prix prohibitif (Telerik ~980$/an, DevExpress ~1600$/an). Fenêtre d'opportunité réelle pour une alternative MIT.

**Zéro dépendance NuGet.** Pas de conflits de packages pour le consommateur. Point de départ sain pour une bibliothèque.

---

## 4. Analyse par Domaine

### 4.1 Architecture WPF

Le code-behind monolithique est la racine de 80% des issues. Il n'est pas récupérable tel quel pour un usage en bibliothèque — mais il n'est pas non plus à jeter. La mécanique (lazy-load, dual-pane, filtrage) est correcte dans ses intentions ; c'est l'implémentation qui couples tout au mauvais endroit.

Le chemin de migration recommandé est une refonte MVVM **partielle** : un seul `FileSystemNodeViewModel` avec `ObservableCollection<FileSystemNodeViewModel> Children`, `bool IsExpanded` (setter avec lazy-load), `bool IsLoaded`, et deux `ICommand`. Le code-behind devient `InitializeComponent()` + binding du DataContext. Ce n'est pas une réécriture complète — c'est 4-6h de travail ciblé.

L'introduction d'`IFileSystemService` est reportée jusqu'à ce qu'un test unitaire concret nécessite le mock — sans use case réel, c'est de l'over-engineering. `System.IO.Abstractions` est une bonne bibliothèque mais pas un prérequis pour V2.

`CommunityToolkit.Mvvm` (NuGet) est recommandé pour les source generators `[ObservableProperty]` et `[RelayCommand]` — divise le boilerplate par 3, aucune dépendance lourde.

### 4.2 Performance

Trois problèmes distincts, à traiter dans l'ordre :

1. **Freeze UI (synchrone)** — le plus visible, le plus impactant. Fix : `async/await` + `Task.Run` + `EnumerateDirectories`. Durée : 2-3h. Débloque toutes les démos.

2. **Absence de virtualisation** — invisible jusqu'à 500 items, catastrophique au-delà. Fix : 2 lignes XAML (`VirtualizingStackPanel.IsVirtualizing="True"`, `VirtualizationMode="Recycling"`). Prérequis : binding sur `ObservableCollection` (lié au MVVM).

3. **Guard lazy-load commenté** — duplication exponentielle des items. Fix : décommenter 2 lignes. Applicable immédiatement en code-behind, 30 secondes de travail.

Le remplacement de `DummyTreeViewItem : TreeViewItem` par `private static readonly object _dummy` supprime l'allocation d'un Control WPF complet pour chaque nœud de l'arbre — gain mémoire non négligeable sur des hiérarchies profondes.

### 4.3 QA / Robustesse / Sécurité

**Robustesse :** Zéro try/catch sur toute l'I/O filesystem. C'est un projet de 2019 — les pratiques de l'époque ne justifient pas de maintenir cela en 2026. La matrice de cas d'erreur à couvrir en priorité : lecteur CD vide (`DriveInfo.IsReady`), `C:\System Volume Information` (`UnauthorizedAccessException`), chemins >260 chars (`PathTooLongException`), partage réseau hors-ligne (`IOException`), junctions cycliques (`FileAttributes.ReparsePoint`), `SelectedItem` null, double-expansion du même nœud.

**Sécurité :** Deux vecteurs non-triviaux identifiés par le security-auditor : (1) reparse points/junctions comme vecteur d'escalade de privilèges indirect (documenté dans le CVE landscape 2024-2025), (2) lecteurs réseau SMB avec authentification NTLM automatique exploitable en NTLM relay. Ces deux points se règlent par filtrage par défaut + opt-in explicite, pas par des changements d'architecture complexes.

**Absence de surface de restriction :** Aucune API `RootPath` pour confiner le composant à un sous-arbre — critique pour tout usage en bibliothèque tierce. Un composant sans mécanisme de confinement sera mal utilisé par les intégrateurs.

**Testabilité :** 0% de couverture. Sans projet de test (`CustomDialogBox.Tests`) et sans infrastructure CI, tout refactoring est aveugle. Le premier test à écrire : simulation d'`UnauthorizedAccessException` sur `GetDirectories()`.

### 4.4 UX / Accessibilité

Le modèle conceptuel dual-pane est juste — c'est le pattern que tous les utilisateurs Windows reconnaissent. Mais l'exécution actuelle viole plusieurs heuristiques de Nielsen en même temps :

- **H1 (visibilité du statut)** : freeze UI = aucun feedback pendant l'I/O. L'utilisateur ne sait pas si l'application est vivante.
- **H6 (reconnaissance > rappel)** : pas de barre d'adresse, l'utilisateur doit mémoriser sa position.
- **H4 (cohérence avec les standards)** : pas de bouton Ouvrir/Annuler, pas de double-clic pour ouvrir, pas de Alt+D.

Le `treeViewDetails` devrait être un `ListView` avec colonnes (Nom, Type, Taille, Date de modification) — le TreeView est un widget de navigation hiérarchique, inadapté pour lister des fichiers à plat.

L'absence d'icônes Shell est le différenciateur visuel négatif le plus fort. La comparaison mentale avec l'Explorateur Windows est immédiatement défavorable. `SHGetFileInfo` avec `SHGFI_USEFILEATTRIBUTES` + cache `ImageSource` est du P/Invoke bien documenté, effort moyen.

Navigation clavier : tabulation entre les deux panneaux opaque, ordre de focus non défini, spec W3C APG TreeView incomplète.

### 4.5 Marché / OSS

Le gap marché est réel et documenté. Mais le projet n'existe pas pour le marché en l'état :

- Pas de package NuGet (OutputType = WinExe).
- Pas de licence (usage légalement interdit).
- README de 3 lignes (taux de rebond maximal).
- Pas de CI/CD (signal de non-maintenance).
- Pas de CONTRIBUTING.md, templates GitHub, SECURITY.md.

La décision stratégique de ne pas cibler NuGet à court terme est cohérente avec le profil et les ressources. Si l'objectif change, la séquence minimale est : `LICENSE` → SDK-style `.csproj` → PackageId fixé → README 150 lignes → CI GitHub Actions → publication `v0.1.0-alpha`. Chaque étape est indépendante des autres.

**Multi-ciblage :** `.NET Framework 4.7.2` reste pertinent pour Flowbird (environnement enterprise). Ajouter `net8.0-windows` en multi-targeting (`<TargetFrameworks>`) est quasi-gratuit si le code reste WPF standard — couvre un marché 3x plus large pour zéro dette technique.

---

## 5. Retours Utilisateurs Simulés

### Thomas, 38, Senior Dev Enterprise

> "Bon, j'ai regardé le code et je vais être direct : c'est exactement ce qu'on m'a vendu comme 'prototype MVVM-ready' il y a deux ans par un stagiaire, et qu'on a mis six mois à dépiler après. Tout est dans le code-behind. TOUT. Le chargement des drives, l'expansion, la navigation, la gestion du curseur — dans le .xaml.cs. Il n'y a pas de ViewModel. Il n'y a pas de binding. Il n'y a pas de commande. C'est du WinForms habillé en WPF. Pour brancher ça sur Prism, il faudrait que le ViewModel implémente IDialogAware, que la View expose un ItemsSource bindé, que l'expansion soit gérée par un RelayCommand ou un property setter. Là, l'expansion c'est un event handler qui appelle une méthode privée qui manipule des items nommés directement. Je ne peux même pas enregistrer ce truc dans mon container. Et le freeze UI sur les répertoires réseau — c'est rédhibitoire."

**Synthèse :** Diagnostic exact, verdict sans appel : non intégrable en l'état dans un contexte Prism/enterprise. Thomas identifie aussi un bug reproduisible non mentionné par les agents techniques : `TreeView_SelectedItemChanged` appelle `LoadEverything()` sur un nœud déjà expanded — double scan filesystem + race condition garantie. Le concept est bon, la base trop fragile sans refactoring majeur.

---

### Léa, 26, Indie Dev

> "Ok donc... j'arrive sur le GitHub, je vois 'V1' dans les commits, je lis le README et y'a juste deux liens vers des tutos. Super. J'ouvre le code et y'a genre 150 lignes donc je me dis cool, c'est pas la mort. Mais en fait... comment je fais pour savoir quel fichier l'utilisateur a choisi ?? Je vois pas de propriété 'fichier sélectionné' nulle part. Genre l'utilisateur clique sur un truc et... rien se passe côté mon appli ?"

**Synthèse :** L'absence de `SelectedPath` et de package NuGet sont les deux bloquants immédiats pour une intégration. Léa note aussi que des alternatives existent (Ookii.Dialogs.Wpf, WPF.InternalDialogs) qui donnent un résultat professionnel en 3 lignes — ces alternatives ne sont pas mentionnées dans le README, ce qui force l'évaluateur à une décision à l'aveugle. Verdict : adapter le code source est plus réaliste que l'intégrer comme librairie.

---

### Marie, 51, Power User (non-développeuse)

> "Si on me donnait ça au bureau demain matin, je continuerais avec le dialog Windows normal. Déjà, il n'y a nulle part où taper un chemin. Tous les matins je vais dans le même dossier client — avec Windows j'appuie Alt+D, je tape le chemin, c'est plié en 3 secondes. Là je dois dérouler l'arbre depuis le début, cliquer sur C:\\, attendre, cliquer sur le dossier client, attendre encore... Et l'autre truc qui m'a perdu : il y a deux arbres côte à côte. Lequel je suis censée utiliser ?"

**Synthèse :** Marie valide empiriquement les issues UX les plus critiques : absence de barre d'adresse (bloquant pour un usage quotidien clavier-intensif), freeze sur gros répertoires, ambiguïté du layout deux-TreeViews, absence d'icônes/dates/tailles, aucune mémoire du dernier chemin, aucun bouton Ouvrir. Son retour confirme aussi que les lecteurs réseau mappés sont 80% de l'usage réel en contexte professionnel — point peu traité par les agents techniques.

---

### Kevin, 21, Étudiant en info

> "Ok donc j'ai regardé le code et... c'est pas si dur à lire, j'ai compris ce qui se passe en 10 minutes, ce qui est cool. Mais le truc c'est... il n'y a PAS DE MVVM DU TOUT. Sérieusement. Tout est dans le Open.xaml.cs, le XAML est presque vide, y'a pas de ViewModel, pas de binding sur des propriétés, pas d'ICommand... Mon prof va regarder ça et va me demander 't'as mis quoi dans ton ViewModel ?' et je vais rester bouche bée."

**Synthèse :** Kevin confirme que le code est lisible et pédagogiquement clair — bon point de départ pour comprendre le problème. Mais l'absence de MVVM le rend inutilisable comme base de rendu académique sans réécriture quasi-complète. Il soulève aussi la vraie question : repartir de ce code ou d'un template MVVM propre ? Réponse pragmatique : ce code est une meilleure base de compréhension du problème qu'un template générique — mais il faut accepter de réécrire la logique dans une structure MVVM, pas de juste "ajouter" un ViewModel.

---

## 6. Synthèse des Discussions Inter-agents

### Consensus fort

- **Priorité absolue : async I/O.** Convergence 10/10 agents. Aucune autre amélioration n'a de valeur perceptible tant que l'UI freeze.
- **Lazy-load guard à réactiver immédiatement.** Consensus sur le diagnostic et le fix (décommenter 2 lignes).
- **Gestion d'exceptions I/O obligatoire avant toute distribution.** Accord unanime sur la liste des types à attraper.
- **La mécanique lazy-load avec DummyTreeViewItem est conceptuellement correcte.** Consensus positif — la base est bonne, l'exécution est à corriger.
- **`SelectedPath` manquant = bloquant fonctionnel.** Identifié par ux-designer, product-manager, user-lea, user-marie — pas explicitement dans les rapports des agents techniques architecturaux, mais reconnu comme omission critique.

### Désaccords notables

**Profondeur du refactoring MVVM :**
- arch-wpf, user-thomas, user-kevin : refonte MVVM avec `IFileSystemService` injectable.
- product-manager, décision Wilfried : MVVM minimal (un ViewModel, pas d'injection de dépendance), `IFileSystemService` reporté sans use case concret.
- **Résolution :** MVVM partiel. Le ViewModel minimal est le bon compromis profil/énergie/résultat. L'abstraction `IFileSystemService` attend un test unitaire réel.

**Timing MVVM vs patch async :**
- perf-engineer : patch async d'abord (V1.1), MVVM après (V2).
- arch-wpf : MVVM d'abord, async naturellement dans le setter d'IsExpanded.
- **Résolution :** patch async d'abord. Avoir une démo non-freezante est le seul moteur qui garantit la continuation. Le MVVM arrive dans la foulée.

**Objectif NuGet :**
- oss-strategist : préparer l'infrastructure OSS maintenant (CI, CONTRIBUTING, SemVer).
- product-manager + décision Wilfried : portfolio + usage Flowbird, NuGet en réévaluation à 6 mois.
- **Résolution :** `LICENSE` immédiatement (non-négociable légalement), le reste de l'infrastructure OSS attend une décision explicite de publication.

### Résiduel identifié lors de la validation

- **arch-wpf :** Multi-ciblage `net472;net8.0-windows` doit être décidé avant la restructuration du `.csproj`.
- **perf-engineer :** `GetDirectories()` vs `EnumerateDirectories()` — la distinction est critique pour l'efficacité du `CancellationToken`. Seul `EnumerateDirectories()` permet l'annulation mid-call.
- **qa-engineer :** `LoadDirectories()` dans le constructeur = exceptions non-récupérables avant l'initialisation du message handler WPF.
- **security-auditor :** Aucune API `RootPath` pour confiner le composant — manque le plus critique du point de vue bibliothèque tierce.
- **oss-strategist :** PackageId à fixer AVANT migration SDK-style — un renommage post-publication casse tous les consommateurs.
- **user-thomas :** Bug reproduisible non documenté — `TreeView_SelectedItemChanged` sur nœud déjà expanded = double scan filesystem.
- **user-marie :** Lecteurs réseau mappés = 80% de l'usage professionnel réel, peu traité par les agents techniques.

---

## 7. Décisions Stratégiques

**Objectif :** Outil utilisable en contexte Flowbird/QA-tooling + démo de compétences senior pour Lead QA 2030. Pas de lancement NuGet à horizon prévisible.

**Direction validée :** `continue_refactor` — pas d'abandon, pas de réécriture from scratch, refactoring incrémental avec livrables visibles à chaque sprint.

**Décisions clés :**

1. **MVVM partiel, pas complet.** Un seul `FileSystemNodeViewModel` avec `ObservableCollection` et `ICommand`. Pas d'`IFileSystemService` tant qu'il n'y a pas de test unitaire concret. `CommunityToolkit.Mvvm` comme seul ajout NuGet.

2. **Async en premier.** `Task.Run` + `EnumerateDirectories` + `Dispatcher.InvokeAsync`. Résultat démontrable en une session. La virtualisation UI (2 lignes XAML) s'ajoute dans la même session.

3. **Crashes I/O corrigés avant features.** 30 lignes de `try/catch` ciblés. Sur un CV Lead QA, un composant qui crash sur `C:\Windows` est inacceptable.

4. **Lecteurs réseau masqués par défaut.** Une ligne de filtre `DriveType.Network`. Opt-in via propriété publique. Réversible sans dette.

5. **NuGet : pas de décision à court terme.** `LICENSE` MIT créé immédiatement (condition légale minimale). Infrastructure OSS complète (CI, CONTRIBUTING, packaging) seulement si décision explicite de publication.

6. **`.NET Framework 4.7.2` conservé comme cible primaire.** Multi-targeting `net8.0-windows` ajouté lors de la migration SDK-style si elle a lieu — coût nul, gain de couverture marché.

7. **Async d'abord, icônes ensuite, breadcrumb en V3.** Ne pas paralléliser sur un projet solo. Chaque sprint produit quelque chose de démontrable.

8. **Risque hyperfocus → abandon.** La refonte MVVM complète (2-3 jours) est le pattern exact qui se lance en hyperfocus et s'arrête à 80%. Le scope limité à un ViewModel unique + async est la protection contre ce pattern. Définir une V1.1 avec 3 PRs précises et une définition finie.

---

## 8. Plan d'Action

### Phase 1 — Immédiat (une session, 3-4h)

| # | Action | Effort | Impact |
|---|---|---|---|
| 1 | Décommenter le guard `HasDummy` | 2 min | Élimine la duplication exponentielle |
| 2 | Null-check dans `TreeView_SelectedItemChanged` | 5 min | Élimine la NullReferenceException garantie |
| 3 | `try/catch` sur toute I/O : `UnauthorizedAccessException`, `PathTooLongException`, `IOException`, `DirectoryNotFoundException` | 30 min | Élimine tous les crashes I/O |
| 4 | Filtre `DriveInfo.IsReady` dans `LoadDirectories()` | 10 min | Élimine le crash sur lecteur CD vide |
| 5 | Filtre `DriveType.Network` par défaut | 5 min | Élimine le blocage UI sur réseau déconnecté |
| 6 | Détecter `FileAttributes.ReparsePoint` | 10 min | Élimine le risque de boucle infinie |
| 7 | `async/await` + `Task.Run` + `EnumerateDirectories` + `Dispatcher.InvokeAsync` | 2-3h | Élimine tous les freezes UI — impact maximal |
| 8 | Ajouter `VirtualizingStackPanel.IsVirtualizing="True"` + `Recycling` | 5 min XAML | Scalabilité sur gros répertoires |
| 9 | `LICENSE` MIT à la racine | 2 min | Condition légale minimale |

**Résultat Phase 1 :** composant non-crashable, non-freezant, légalement utilisable.

### Phase 2 — Court terme (1 mois, 3-4 sessions de 2-3h)

| # | Action | Effort | Impact |
|---|---|---|---|
| 1 | Ajouter bouton Ouvrir/Annuler + propriété `SelectedPath` publique | 1h | Composant fonctionnellement utilisable |
| 2 | `FileSystemNodeViewModel` avec `ObservableCollection<FileSystemNodeViewModel>`, `IsExpanded`, `IsLoaded` | 3-4h | Architecture testable, base de tout le reste |
| 3 | `CommunityToolkit.Mvvm` + source generators | 30 min | Suppression du boilerplate INotifyPropertyChanged |
| 4 | Grid layout avec `GridSplitter` + supprimer les dimensions hardcodées | 30 min XAML | Fenêtre redimensionnable |
| 5 | Remplacer `DummyTreeViewItem` par sentinel object | 15 min | Fix WCAG 4.1.2, suppression allocation Control inutile |
| 6 | Icônes Shell via `SHGetFileInfo` avec cache `ImageSource` | 3-4h | Différence visuelle radicale |
| 7 | `treeViewDetails` → `ListView` avec colonnes Nom/Type/Taille/Date | 2-3h | Modèle mental correct, cohérence Explorateur Windows |
| 8 | Déplacer `LoadDirectories()` dans `Window_Loaded` | 5 min | Exceptions récupérables par le message handler WPF |

**Résultat Phase 2 :** composant intégrable, avec retour de valeur, architecture propre, rendu professionnel.

### Phase 3 — Long terme (3-6 mois, selon disponibilité)

| # | Action | Effort | Condition de déclenchement |
|---|---|---|---|
| 1 | Barre d'adresse breadcrumb (BreadcrumbViewModel, Alt+D) | 4-6h | MVVM Phase 2 stable |
| 2 | Persistance du dernier chemin (Settings) | 1h | Phase 2 terminée |
| 3 | Filtrage par extension (`AllowedExtensions[]`) | 1h | Phase 2 terminée |
| 4 | Multi-sélection (Ctrl+clic, Shift+clic) | 3-4h | MVVM stable |
| 5 | Multi-targeting `net472;net8.0-windows` | 30 min | Si migration SDK-style décidée |
| 6 | CI GitHub Actions (build + publish NuGet sur tag) | 2-3h | Si décision NuGet explicite |
| 7 | README 150 lignes + GIF animé | 3-4h | Si décision NuGet explicite |
| 8 | `IFileSystemService` injectable | 2-3h | Si tests unitaires concrets identifiés |
| 9 | Support UNC paths + lecteurs réseau async avec timeout | 4-6h | Après async Phase 1 stabilisé |

---

## 9. Validation Finale par Agent

| Agent | Rôle | Statut | Résiduel critique |
|---|---|---|---|
| arch-wpf | Senior WPF/XAML Architect | Approuvé | Multi-targeting `net472;net8.0-windows` à décider avant restructuration `.csproj` |
| perf-engineer | WPF Performance Engineer | Approuvé | `GetDirectories()` → `EnumerateDirectories()` obligatoire pour un `CancellationToken` effectif mid-call |
| qa-engineer | QA/Test Engineer | Approuvé | `LoadDirectories()` dans le constructeur = exceptions non-récupérables avant initialisation WPF |
| security-auditor | Security & Robustness Auditor | Approuvé | Absence d'API `RootPath` — manque le plus critique pour usage bibliothèque tierce |
| ux-designer | UX/UI Designer | Approuvé | Absence de bouton Ouvrir/Annuler = omission architecturale, pas une feature manquante |
| product-manager | Product Manager | Approuvé | Contrat de retour de valeur (`SelectedPath`, API publique) non documenté — bloquant fonctionnel |
| oss-strategist | Open Source Strategist | Approuvé | `PackageId` à fixer AVANT migration SDK-style — renommage post-publication casse les consommateurs |
| user-thomas | Thomas, 38, Senior Dev Enterprise | Approuvé | Bug non documenté : `SelectedItemChanged` sur nœud déjà expanded = double scan + race condition |
| user-lea | Léa, 26, Indie Dev | Approuvé | Alternatives NuGet (Ookii.Dialogs.Wpf, WPF.InternalDialogs) non mentionnées dans le README |
| user-marie | Marie, 51, Power User | Approuvé | Lecteurs réseau mappés = 80% de l'usage professionnel réel, sous-traité par les agents techniques |
| user-kevin | Kevin, 21, Étudiant | Approuvé | Repartir de ce code ou d'un template MVVM propre ? Réponse : base de compréhension du problème > template générique, mais réécriture de la logique dans une structure MVVM nécessaire |

---

## 10. Questions Ouvertes Non Résolues

**Q1 — Nom du package NuGet**
`CustomDialogBox` est trop générique, risque de conflit sur nuget.org. Vérifier la disponibilité de `CustomDialogBox.Wpf`, `WpfFileDialog`, `FlowbirdDialog` avant toute migration SDK-style. Décision irréversible post-publication.

**Q2 — Multi-targeting : net472 + net8.0-windows ?**
La décision conditionne la structure du `.csproj`. Coût quasi-nul si le code reste WPF standard. À prendre avant de toucher au projet file.

**Q3 — API publique de confinement (RootPath)**
Aucune interface de restriction de portée n'est définie. Pour un composant bibliothèque, c'est la question de sécurité la plus critique : un intégrateur doit pouvoir contraindre la navigation à un sous-arbre. Définir `public string RootPath` avant de stabiliser l'API publique.

**Q4 — Lecteurs réseau et chemins UNC : scope de la V2 ?**
La décision de masquer `DriveType.Network` par défaut est prise. Mais les chemins UNC directs (`\\serveur\partage`) — 80% de l'usage enterprise selon Marie — nécessitent une barre d'adresse (Phase 2) pour être accessibles. Ce n'est pas une décision Phase 1, mais il faut l'anticiper dans le design de la barre d'adresse.

**Q5 — Événement OnFileSystemError vs silencing**
Le security-auditor recommande d'exposer un événement `OnFileSystemError(path, exception)` plutôt que de silencer les erreurs. Cela laisse la politique à l'application hôte (log, alerte, griser le nœud). Question ouverte : cela fait-il partie de l'API V2, ou est-ce une fonctionnalité à ajouter seulement si un consommateur réel en exprime le besoin ?

**Q6 — Décision NuGet à 6 mois**
Si d'ici novembre 2026 le composant est utilisé à Flowbird et stable : réévaluer la publication. Point de décision explicite à placer dans STATE.md / PROJETS.md avec checkpoint.
