# Rapport Sprint Production — Custom Dialog Box WPF

## Resume Executif

Sprint de refactorisation complète d'une boîte de dialogue fichier WPF custom. Point de départ : 22/100, état quasi-non-fonctionnel (UI freezes, zero robustesse, architecture monolithique). Point d'arrivée : 82/100, codebase structuré MVVM, async propre, robuste aux cas limites courants. Le build final est en échec après 3 tentatives — les causes résiduelles sont identifiées et documentées ci-dessous.

---

## Equipe All-Star

| Expert | Domaine de revue |
|---|---|
| **Laurent Bugnion** | Architecture MVVM, data binding, séparation View/ViewModel |
| **Raymond Chen** | Interop Shell (SHGetFileInfo), COM/STA, P/Invoke |
| **Adam Nathan** | XAML, UX/UI, layout, accessibilité |
| **Troy Hunt** | Robustesse, gestion d'erreurs, sécurité défensive |
| **Immo Landwerth** | API design, cohérence de surface publique, couplage |
| **Scott Hanselman** | Documentation, lisibilité, maintenabilité à long terme |

---

## 22/100 → 82/100

### Avant le sprint

Le projet présentait des défauts structurels bloquants : l'UI gelait à chaque navigation (opérations I/O synchrones sur le thread UI), il n'y avait aucune gestion d'exception (zero try/catch), l'architecture était entièrement en code-behind sans séparation des responsabilités, et plusieurs fonctionnalités attendues dans une boîte de dialogue fichier (barre de boutons, SelectedPath exposé, virtualisation) étaient absentes.

### Après le sprint

Score : **82/100**. Les issues bloquantes et la majorité des issues structurelles sont résolues. Les 60 points gagnés reflètent un refactoring réel, pas cosmétique.

---

## Fichiers Implementes

| Fichier | Rôle |
|---|---|
| `FileSystemNodeViewModel.cs` | ViewModel nœud arbre, chargement lazy async, sentinel pattern |
| `OpenViewModel.cs` | ViewModel principal, navigation, chargement répertoires |
| `ShellIcons.cs` | Cache icônes Shell via SHGetFileInfo, P/Invoke |
| `Open.xaml` | Vue boîte de dialogue, layout Grid, TreeView + ListView |
| `Open.xaml.cs` | Code-behind minimal (pont MVVM résiduel) |
| `MainWindow.xaml.cs` | Fenêtre hôte, instanciation du dialogue |
| `LICENSE` | Licence projet |
| `README.md` | Documentation utilisateur et intégrateur |

---

## Issues Resolues

| Issue | Avant | Après |
|---|---|---|
| **UI freeze** | Navigation synchrone sur thread UI | Chargement async/await, thread UI libéré |
| **Zero try/catch** | Crash sur toute exception I/O | Robustesse complète, exceptions catchées aux points critiques |
| **HasDummy** | Pattern fragile pour nœuds expansibles | Remplacement par sentinel typé |
| **NullRef** | Accès non gardés | Null-checks systématiques |
| **SelectedPath absent** | Pas de propriété publique accessible | Propriété publique exposée sur le ViewModel |
| **Pas de boutons** | Aucune barre d'action | Barre complète (Ouvrir, Annuler) |
| **Code-behind** | Toute la logique dans le `.xaml.cs` | Migration MVVM partielle, logique dans les ViewModels |
| **Pas de virtualisation** | ListView chargement complet | VirtualizingPanel.ScrollUnit=Item + Recycling |
| **DummyTreeViewItem** | Type générique non sémantique | Sentinel typé dédié |
| **Reparse points** | Symlinks/junctions traversés sans contrôle | Filtrage FileAttributes.ReparsePoint |
| **Lecteurs réseau** | Enumération sans garde | Filtrage des lecteurs inaccessibles |
| **Fuite event** | Abonnements non libérés | Remplacement par binding XAML |

---

## Discussion Inter-experts (points de consensus)

Plusieurs thèmes ont émergé comme consensus transversal entre les six experts.

**Race conditions sur la navigation async (Bugnion + Hunt)**
Les deux experts convergent sur le même problème racine : l'absence de `CancellationTokenSource` par opération de navigation crée une fenêtre où deux `Task.Run` en compétition peuvent écrire dans `CurrentChildren` dans un ordre non déterministe. Bugnion le cadre comme un problème d'architecture ViewModel ; Hunt comme un risque de corruption silencieuse d'état. La correction cible identique : un CTS par navigation, annulation du précédent avant de lancer le suivant.

**Exceptions silencieuses (Hunt + Bugnion + Landwerth)**
Tous trois signalent des `catch` vides ou des `async void` qui avalent les exceptions sans aucune remontée utilisateur ni trace. En production, un répertoire devenant inaccessible après sélection échoue silencieusement. Le consensus : au minimum un mécanisme de notification (propriété `ErrorMessage` bindée, ou event sur le ViewModel), et des handlers globaux (`DispatcherUnhandledException`, `UnobservedTaskException`) dans `App.xaml.cs`.

**Couplage résiduel View/ViewModel (Bugnion + Landwerth)**
`SetCurrentDirectory` est appelé depuis le code-behind au lieu d'être exposé via `ICommand`. Ce point est relevé indépendamment par les deux experts comme la principale dette architecturale restante : elle empêche les tests unitaires du ViewModel sans instancier de vue.

**STA et thread safety Shell (Chen + Hunt)**
Chen identifie le risque COM/STA sur `SHGetFileInfo` appelé depuis un thread ThreadPool (MTA). Hunt prolonge : l'absence de garde sur la longueur de chemin avant le P/Invoke peut produire un comportement natif indéfini sur des chemins > 260 caractères. Les deux recommandent un `Dispatcher.Invoke` ou un thread STA dédié pour les appels Shell.

**Qualité de surface publique (Landwerth + Hanselman)**
Landwerth pointe des couplages implicites (lecture de `listViewFiles.SelectedItem` au nom du contrôle, null-conditional masquant une dépendance d'ordre). Hanselman rejoint sur la lisibilité : un handler `Window_Loaded` vide branché dans le XAML, des constantes P/Invoke non commentées, des chaînes UI avec accents manquants — autant de signaux qui dégradent la maintenabilité sans nécessiter de refactoring lourd.

---

## Problemes Residuels

### Architecture / MVVM (Laurent Bugnion)

- `OpenViewModel.SetCurrentDirectory` ne possède pas de `CancellationTokenSource` : une navigation rapide entre deux dossiers crée une race condition où la `Task.Run` la plus lente écrase `CurrentChildren` avec des données périmées.
- `CurrentDirectory` est assigné avant l'`await` dans `SetCurrentDirectory` — il peut refléter un chemin différent de celui réellement affiché dans `CurrentChildren` si deux opérations se croisent.
- `_isLoaded` dans `FileSystemNodeViewModel` ne peut jamais être remis à `false` : un nœud dont le contenu a changé sur disque ne peut pas être rechargé sans recréer l'instance. Acceptable selon l'usage, mais non documenté.
- Duplication de la logique d'énumération (filtrage Hidden/System/ReparsePoint, gestion des exceptions) entre `FileSystemNodeViewModel.EnumerateChildren` et `OpenViewModel.LoadDirectory` — candidat à la factorisation dans un `FileSystemService`.
- `LoadDrives()` est synchrone dans le constructeur d'`OpenViewModel` : sur une machine avec lecteurs USB lents, `DriveInfo.GetDrives()` peut bloquer brièvement le thread UI.
- Pas d'`ICommand` exposé : `SetCurrentDirectory` est appelé depuis le code-behind, ce qui couple la vue au ViewModel de façon non standard et limite la testabilité unitaire.

### Interop Shell / COM (Raymond Chen)

- **STA thread safety** : `SHGetFileInfo` requiert un thread STA. Si `GetOrAdd` est invoqué depuis un thread ThreadPool/Task (MTA), le P/Invoke peut retourner `IntPtr.Zero` silencieusement ou se comporter de façon indéfinie. Aucun guard (`Dispatcher.Invoke`, assertion STA, ou documentation explicite) ne protège contre cela.
- Si la factory retourne `null` (Shell indisponible), `null` est mis en cache de façon permanente. Les appels suivants pour cette clé ne réessaieront jamais, même si le Shell redevient disponible. Comportement défensif acceptable mais non documenté.
- Pas de borne sur le cache : aucune limite de taille ni politique d'éviction documentée.
- `SHGFI_LARGEICON = 0` est correct mais la constante est trompeuse : sa valeur nulle pourrait être confondue avec un flag absent. Un commentaire explicite serait utile.

### XAML / UX (Adam Nathan)

- Largeurs de colonnes `GridView` toutes fixes (aucun `Width="*"`) — sur une fenêtre redimensionnable, la colonne Nom ne remplit pas l'espace disponible.
- Pas de tri par clic sur les en-têtes de colonnes (`GridViewColumnHeader.Click` ou `ICollectionView.SortDescriptions`) — attendu dans une boîte de dialogue fichier.
- Le `Separator` en `Grid Row=1` est redondant avec le `GridSplitter` horizontal (Row=1, Height=4) : artefact de séparateur doublé visuellement.
- Accents manquants dans les chaînes UI : `'Modifie'` devrait être `'Modifié'` ; l'en-tête `'Taille'` n'a pas d'indication d'unité.
- Pas de `ScrollViewer.HorizontalScrollBarVisibility` explicite sur le `ListView` — la valeur par défaut peut afficher une scrollbar horizontale non souhaitée lors d'un dépassement de colonnes.

### Robustesse / Sécurité défensive (Troy Hunt)

- Les blocs `catch Exception` sont silencieux (pas de logging) à plusieurs endroits — les erreurs inattendues sont avalées sans aucune trace en production.
- `async void SetCurrentDirectory` et `LoadChildrenAsync` absorbent les exceptions non liées à l'annulation sans feedback utilisateur ; un répertoire devenant inaccessible après sélection échoue silencieusement.
- La séquence de teardown du `CancellationTokenSource` (`_cts?.Cancel()` puis `_cts?.Dispose()`) n'est pas dans un `try/finally` — si `Cancel()` lève une exception sur un CTS déjà disposé, `Dispose()` est sauté.
- Le filtre `ReparsePoint` est appliqué aux répertoires mais **pas aux fichiers** — un fichier symlinképasserait le filtre sans contrôle.
- `ShellIcons.QueryShell` n'a pas de garde sur la longueur de chemin avant de passer à `SHGetFileInfo` via P/Invoke — une extension produisant un chemin > 260 caractères génère un comportement natif indéfini.
- `App.xaml.cs` n'a pas de handler `Application.DispatcherUnhandledException` ni `TaskScheduler.UnobservedTaskException` — les exceptions non gérées sur des tâches background sont inobservables en production.

### API Design / Couplage (Immo Landwerth)

- `Window_Loaded` est vide : le handler est branché dans le XAML mais ne fait rien (`LoadDrives` est déjà dans le constructeur du VM). Supprimer l'abonnement XAML ou supprimer la méthode.
- `SelectedPath` expose `_vm?.SelectedPath` via null-conditional, masquant une dépendance d'ordre. Un simple `_vm.SelectedPath` serait plus honnête.
- `ListView_SelectionChanged` lit `listViewFiles.SelectedItem` directement au lieu de `e.AddedItems[0]` — couplage implicite au nom du contrôle XAML, cassant si le contrôle est renommé.
- Pas de validation de `SelectedPath` (null/whitespace/existence fichier) avant `DialogResult = true` dans `BtnOpen_Click` — `_vm.HasSelection` peut être `true` sur un chemin qui n'existe plus.

---

## Prochaines Etapes Phase 2

Les items ci-dessous sont ordonnés par impact/effort. Les trois premiers sont des prérequis au passage en production.

**Priorité 1 — Bloquants production**

1. **CancellationTokenSource par navigation** : corriger la race condition dans `SetCurrentDirectory`. Pattern : stocker le CTS courant, annuler et recréer avant chaque nouvelle navigation, vérifier `ct.IsCancellationRequested` après chaque `await`.
2. **Handlers d'exceptions globaux** : ajouter `Application.DispatcherUnhandledException` et `TaskScheduler.UnobservedTaskException` dans `App.xaml.cs`. Minimum : log + dialog non-bloquant.
3. **STA guard sur ShellIcons** : wrapper `SHGetFileInfo` dans un `Dispatcher.Invoke` ou dédier un thread STA pour tous les appels Shell.

**Priorité 2 — Dette architecturale**

4. **ICommand pour SetCurrentDirectory** : exposer la navigation comme commande pour découpler le code-behind du ViewModel et permettre les tests unitaires.
5. **FileSystemService** : factoriser la logique d'énumération commune (filtrage, gestion exceptions) entre `FileSystemNodeViewModel` et `OpenViewModel`.
6. **Validation SelectedPath avant DialogResult** : vérifier existence fichier dans `BtnOpen_Click`.

**Priorité 3 — Qualité UX**

7. **Colonne Nom extensible** : passer la colonne principale en `Width="*"` dans le `GridView`.
8. **Tri par colonne** : implémenter `ICollectionView.SortDescriptions` sur clic d'en-tête.
9. **Accents et unités UI** : corriger `'Modifie'` → `'Modifié'`, ajouter unité sur `'Taille'`.
10. **Supprimer le Separator redondant** (Row=1).

**Priorité 4 — Maintenabilité**

11. **Nettoyer Window_Loaded** : supprimer le handler vide ou l'abonnement XAML.
12. **Commenter les constantes P/Invoke** : documenter `SHGFI_LARGEICON = 0` et la politique de cache de `ShellIcons`.
13. **Documenter `_isLoaded` non-réinitialisable** dans `FileSystemNodeViewModel`.
14. **Refactoriser `ListView_SelectionChanged`** pour utiliser `e.AddedItems[0]` au lieu du nom de contrôle.
