# Analyse sectorielle — Custom Dialog Box WPF
> Analyse multi-métiers des concurrents, points forts/faibles et concepts techniques applicables.
> Généré le 2026-05-30.

---

## Contexte du projet

**Produit analysé :** Boîte de dialogue d'ouverture de fichiers en WPF, C#/.NET Framework 4.7.2.
**Fonctionnalités actuelles :** double panneau (arborescence gauche + liste droite), lazy-loading par dummy-node, filtrage des fichiers système/cachés, navigation par événements d'expansion et de sélection.

**Position de marché :** prototype/V1 fonctionnel, sans theming, sans virtualisation UI, sans breadcrumb, sans icônes shell, sans multi-sélection.

---

## Concurrents identifiés

| Concurrent | Type | Prix | Pertinence |
|---|---|---|---|
| **Telerik RadFileDialogs** | Suite commerciale | ~979–1 649 $/dev/an | Référence absolue du secteur |
| **DevExpress DXOpenFileDialog** | Suite commerciale | ~1 666 $/dev/an | Theming + MVVM |
| **Syncfusion SfTreeView** | Commercial + tier gratuit (≤5 dev) | Gratuit sous seuil | Meilleure architecture MVVM |
| **Ookii.Dialogs** | BSD-3 (gratuit) | 0 | Dialog shell natif, non personnalisable |
| **CommonOpenFileDialog** (WindowsAPICodePack) | Forks communautaires | 0 | COM interop Shell, non embarquable |
| **MahApps.Metro** | MIT (gratuit) | 0 | Theming uniquement, aucun file browser |
| **Actipro Shell for WPF** | Commercial royalty-free | Non publié | Meilleur produit embeddable |
| **LogicNP FileView.WPF / Shell MegaPack.WPF** | Commercial royalty-free | Non publié | Thumbnails + colonnes custom |

---

---

# MÉTIER 1 — Développeur WPF / Desktop

## Ce que ce métier attend

Intégration NuGet propre, API ergonomique proche de `Microsoft.Win32.OpenFileDialog`, support MVVM, compatibilité .NET moderne, facilité de customisation sans surcharge cognitive.

## Points forts des concurrents → à copier

### Telerik — API de filtrage déclarative + lieux personnalisés
```xml
<!-- Telerik: filter déclaratif en XAML -->
<telerik:RadOpenFileDialog Filter="Images|*.png;*.jpg|All files|*.*"
                            FilterIndex="1"
                            InitialDirectory="C:\Users"/>
```
**→ À implémenter :** Propriétés de dépendance `Filter`, `FilterIndex`, `InitialDirectory`, `SelectedFile(s)` sur le contrôle. Copier exactement la syntaxe pipe `libellé|*.ext` déjà connue des développeurs.

### Telerik — Chargement des drives en background
```csharp
// Telerik fait ça en interne :
LoadDrivesInBackground = true; // le thread UI n'est pas bloqué au démarrage
```
**→ À implémenter :** `Task.Run(() => DriveInfo.GetDrives())` au chargement, `Dispatcher.InvokeAsync` pour peupler l'arbre.

### Syncfusion — Support MVVM natif via `LoadOnDemandCommand`
```csharp
// ViewModel possède la logique de chargement :
public ICommand LoadOnDemandCommand => new DelegateCommand<TreeViewNode>(async node => {
    node.IsExpanded = true;
    var children = await _fileSystem.GetChildrenAsync(node.Path);
    foreach (var child in children) node.ChildNodes.Add(child);
});
```
**→ À implémenter :** Exposer un `ICommand` pour le chargement à la demande + un `IFileSystemProvider` injectable.

### DevExpress — `OpenFileDialogMode` unifié (fichiers ET dossiers)
```csharp
dialog.Mode = OpenFileDialogMode.Files;       // ou .Folders / .FilesAndFolders
```
**→ À implémenter :** Enum `DialogMode { Files, Folders, FilesAndFolders }` sur le contrôle.

## Points faibles des concurrents → solutions

| Faiblesse | Concurrent | Solution proposée |
|---|---|---|
| **Prix excessif** ($1 000+/dev/an pour 150 contrôles) | Telerik, DevExpress | Composant standalone à prix ciblé, ou open-source MIT |
| **Pas d'abstraction IFileSystem** (couplé au disque physique) | Tous sauf Syncfusion | Implémenter `IFileSystemProvider` injectable via `System.IO.Abstractions` |
| **`FileSystemWatcher` souscrit sur tout le tree** (storm d'events) | Telerik | Watcher uniquement sur le dossier courant, pas sur l'arbre entier |
| **Pas d'annulation async** sur expansion rapide | Syncfusion | `CancellationToken` dans chaque appel d'expansion, annulé si le nœud se referme |
| **STA thread requis, pas documenté** | Tous | Détecter `Thread.CurrentThread.GetApartmentState()` et lancer automatiquement un thread STA si besoin |
| **Absence de packaging NuGet standalone** | Tous (bundled) | Publier sur NuGet.org comme package unique |

---

# MÉTIER 2 — UX/UI Designer

## Ce que ce métier attend

Cohérence visuelle avec l'application hôte, navigation multi-modale (arbre + breadcrumb + saisie directe), feedback immédiat, accessibilité WCAG, densité d'information configurable.

## Points forts des concurrents → à copier

### Telerik — Breadcrumb auto-complete + dropdown des voisins
Le contrôle d'adresse de Telerik permet :
1. Clic sur un segment → navigation directe
2. Clic sur la zone vide → bascule en zone de texte éditable
3. Flèche à droite de chaque segment → dropdown des dossiers frères

**→ À implémenter :** `BreadcrumbControl` composé d'un `ItemsControl` de `Button` + un `ContextMenu` dynamique par segment, avec bascule `TextBox` sur double-clic.
Librairie open-source : `Dirkster99/bm` (MIT, WPF, MVVM).

### macOS NSOpenPanel — Vue en colonnes (Miller Columns)
Chaque niveau de la hiérarchie s'affiche dans une colonne : sélectionner un dossier dans la colonne N ouvre la colonne N+1. Plusieurs niveaux simultanément visibles.

**→ À implémenter :** Option `ViewMode.Columns` : `HorizontalScrollViewer` contenant des `ListView` générées dynamiquement à chaque sélection.

### macOS — Dimming des fichiers filtrés (vs. masquage)
Les fichiers non correspondant au filtre sont **grisés** et non cliquables, mais **visibles**. L'utilisateur comprend que des fichiers existent mais ne sont pas sélectionnables, évitant la confusion "dossier vide".

**→ À implémenter :** Trigger de style sur `IsEnabled` lié à une propriété `MatchesFilter`. Opacité à 0.4 pour les items filtrés, non `Visibility.Collapsed`.

### Windows 11 File Picker — Breadcrumb responsive avec ellipsis overflow
Sur petite fenêtre, les segments les plus à gauche se collapsent en `…` avec flyout.

**→ À implémenter :** `MeasureOverride` custom sur le panel breadcrumb : afficher autant de segments que possible de droite à gauche, remplacer le reste par un bouton `…` ouvrant un `Popup`.

### Windows Explorer — Curseur de taille d'icônes (slider continu)
Un seul contrôle `Slider` passe de liste compacte à grandes vignettes.

**→ À implémenter :** `Slider` lié à `IconSize` (16/32/48/96/128px), modifiant la `Width`/`Height` du template d'item.

## Points faibles des concurrents → solutions

| Faiblesse UX | Concurrent | Solution proposée |
|---|---|---|
| **Pas de volet de prévisualisation** | Telerik, DevExpress WPF, WinUI 3 | Panneau droit optionnel : miniature Shell (`IShellItemImageFactory`) ou texte pour `.txt`/`.log` |
| **Navigation mono-modale** (arbre uniquement) | Projet actuel | 3 modes : arbre + breadcrumb + saisie chemin direct |
| **Accessibilité non documentée** | Telerik, DevExpress | `AutomationPeer` custom sur chaque contrôle + `LiveSetting.Polite` sur les changements de dossier |
| **Densité non configurable** | Tous | Modes : List / Details / SmallIcons / LargeIcons / Thumbnails |
| **Pas de Quick Access / Favoris** | Tous sauf Telerik (partiel) | Panneau latéral gauche : lecteurs + emplacements connus + favoris persistés |

---

# MÉTIER 3 — Architecte Logiciel

## Ce que ce métier attend

Séparation des responsabilités (MVVM propre), extensibilité via interfaces, abstraction du système de fichiers, support async/await, testabilité, faible couplage.

## Points forts des concurrents → à copier

### Syncfusion — Architecture ViewModel-first (meilleure du secteur)

```csharp
// Le ViewModel possède tout — le contrôle ne fait que lier
public class FileBrowserViewModel
{
    public ObservableCollection<FileNodeViewModel> RootNodes { get; } = new();
    public ICommand ExpandCommand { get; }
    public ICommand SelectCommand { get; }
    public string? SelectedPath { get; set; }
    
    public FileBrowserViewModel(IFileSystemProvider fs) // injectable
    {
        ExpandCommand = new AsyncRelayCommand<FileNodeViewModel>(ExpandAsync);
    }
    
    private async Task ExpandAsync(FileNodeViewModel node, CancellationToken ct)
    {
        var children = await _fs.GetChildrenAsync(node.Path, ct);
        foreach (var c in children) node.Children.Add(c);
    }
}
```

**→ Pattern à adopter intégralement :** le contrôle WPF ne contient aucune logique métier. Tout passe par des `ICommand` bindables et un `IFileSystemProvider` injecté.

### Telerik — Points d'extension via events (DirectoryRequesting, ExceptionRaised)

```csharp
dialog.DirectoryRequesting += (s, e) => {
    if (e.Directory.FullName.Contains("System32"))
        e.Cancel = true; // blocage navigation
};
dialog.ExceptionRaised += (s, e) => {
    Logger.Error(e.Exception, "File dialog error: {Path}", e.Path);
    e.Handled = true;
};
```

**→ À implémenter :** Events `DirectoryRequesting` (annulable), `ExceptionRaised` (centralisé), `SelectionConfirming` (validation custom avant fermeture).

### DevExpress — Service MVVM via interface `IDialogService`

```csharp
// ViewModel ne connaît pas la View :
public class MainViewModel
{
    private readonly IFileDialogService _dialog;
    
    public async Task OpenAsync()
    {
        var result = await _dialog.ShowOpenFileDialogAsync(new OpenOptions {
            Filter = "Excel|*.xlsx", InitialDir = _lastDir
        });
        if (result.Confirmed) LoadFile(result.SelectedPath);
    }
}
```

**→ À exposer :** Interface `IFileDialogService` avec implémentation réelle et mock injectable pour tests unitaires.

## Points faibles des concurrents → solutions

| Faiblesse architecturale | Concurrent | Solution proposée |
|---|---|---|
| **Pas de IFileSystem abstrait** | Telerik, DevExpress, Ookii | `IFileSystemProvider` : `GetDrives()`, `GetChildren(path)`, `GetFiles(path, filter)`, `Exists(path)` |
| **Pas de CancellationToken** sur expansion | Syncfusion | Token par nœud, annulé si collapse avant fin de chargement |
| **FileSystemWatcher sur tout l'arbre** (tempête d'events) | Telerik | Watcher uniquement sur le `CurrentDirectory`, abonné/désabonné à chaque navigation |
| **Thread STA non géré** | Tous | Factory méthode statique `FileBrowserDialog.ShowAsync()` qui garantit STA |
| **Couplage fort à `System.IO`** | Tous | `IFileSystemProvider` permet : disque réel, mock test, ZIP, FTP, cloud |
| **Pas d'injection de dépendances** | Tous | Constructeur acceptant `IFileSystemProvider` + factory statique avec implémentation par défaut |

### Pattern complet recommandé

```csharp
public interface IFileSystemProvider
{
    IEnumerable<DriveNode> GetDrives();
    IAsyncEnumerable<FileSystemNode> GetChildrenAsync(string path, CancellationToken ct);
    bool CanRead(string path);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
}

// Implémentation réelle :
public class PhysicalFileSystemProvider : IFileSystemProvider { ... }

// Mock pour tests :
public class MockFileSystemProvider : IFileSystemProvider { ... }
```

---

# MÉTIER 4 — QA Engineer

## Ce que ce métier attend

Contrôles testables via UI Automation, gestion robuste des cas limites (chemins UNC, dossiers inaccessibles, lecteurs débranchés à chaud), comportement déterministe.

## Cas limites mal gérés par les concurrents → à traiter

### 1. Chemin racine de lecteur (`C:\`, `L:\`) → crash Ookii
**Bug Ookii #9 :** `NullReferenceException` quand `InitialDirectory = "C:\\"` avec `DefaultFileName = ""`.
```csharp
// Défense dans le code :
if (string.IsNullOrEmpty(path.GetPathRoot(initialDir)))
    throw new ArgumentException("InitialDirectory must be a valid rooted path");
// OU normalisation automatique :
initialDir = Path.IsPathRooted(initialDir) ? initialDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
```

### 2. Chemin UNC / lecteur réseau → reset au root (Telerik) ou hang (DevExpress)
```csharp
// Wrap toute navigation réseau avec timeout et cancellation :
private async Task<IEnumerable<FileSystemNode>> GetChildrenWithTimeoutAsync(string path, CancellationToken ct)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(5)); // timeout réseau
    try { return await _provider.GetChildrenAsync(path, cts.Token); }
    catch (OperationCanceledException) { return Enumerable.Empty<FileSystemNode>(); }
    catch (UnauthorizedAccessException) { return Enumerable.Empty<FileSystemNode>(); }
    catch (IOException) { return Enumerable.Empty<FileSystemNode>(); }
}
```

### 3. Lecteur débranché à chaud → exception non gérée (tous les concurrents)
Abonner `DriveInfo.GetDrives()` à un `FileSystemWatcher` sur les volumes :
```csharp
// Win32 : WM_DEVICECHANGE → DBT_DEVICEARRIVAL / DBT_DEVICEREMOVECOMPLETE
// WPF : via HwndSource interop
protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
    source.AddHook(WndProc);
}
private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
{
    if (msg == WM_DEVICECHANGE) RefreshDrives();
    return IntPtr.Zero;
}
```

### 4. Chemin trop long (>260 caractères) → `PathTooLongException`
```csharp
// Opt-in long paths dans le manifest :
// <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
// + catch dans toute énumération :
catch (PathTooLongException ex) { 
    OnExceptionRaised(new FileDialogExceptionEventArgs(ex, path)); 
}
```

### 5. Dossier inaccessible → exception non remontée (Ookii, CommonOpenFileDialog)
```csharp
// Pattern centralisé pour TOUTES les exceptions FileSystem :
private IEnumerable<DirectoryInfo> SafeGetDirectories(DirectoryInfo dir)
{
    try { return dir.EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System)); }
    catch (UnauthorizedAccessException) { yield break; }
    catch (IOException) { yield break; }
}
```

## Support UI Automation (pour tests automatisés)

```csharp
// AutomationPeer pour le contrôle principal :
public class FileBrowserAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    public IRawElementProviderSimple[] GetSelection() =>
        Owner is FileBrowser fb ? new[] { ProviderFromPeer(CreatePeerForElement(fb.SelectedItem)) } : Array.Empty<IRawElementProviderSimple>();
    
    public bool CanSelectMultiple => true;
    public bool IsSelectionRequired => false;
    
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
    protected override string GetClassNameCore() => "FileBrowser";
}
```

**Point faible DevExpress :** AutomationPeer désactivé par défaut (impact perf). **Solution :** activer par défaut, option `AutomationMode = Disabled` pour opt-out.

---

# MÉTIER 5 — Chef de Produit / Product Owner

## Positionnement marché

**Gap principal identifié :** Aucun composant WPF file dialog standalone à prix raisonnable n'existe. Telerik et DevExpress facturent $1 000+/dev/an pour une suite de 150+ contrôles dont seul le file dialog est utilisé. Les alternatives gratuites (Ookii, CommonOpenFileDialog) ne sont pas personnalisables et sont en maintenance minimale depuis 2021.

## Features prioritaires par impact/effort

| Priorité | Feature | Valeur | Effort | Concurrent qui le fait |
|---|---|---|---|---|
| 🔴 1 | **Virtualisation UI** (VirtualizingStackPanel Recycling) | Critique — perf sur gros FS | Faible | Telerik, Syncfusion, DevExpress |
| 🔴 2 | **Icônes Shell** (SHGetFileInfo) | Différenciateur visuel immédiat | Moyen | Explorer, Actipro, LogicNP |
| 🔴 3 | **Breadcrumb** (navigation + saisie directe) | Standard utilisateur attendu | Moyen | Telerik, Explorer |
| 🟠 4 | **Enumération async** + CancellationToken | UX fluide, pas de freeze | Moyen | Telerik (partiel) |
| 🟠 5 | **Theming** (couleurs, dark/light) | Cohérence app hôte | Moyen | Telerik, DevExpress |
| 🟠 6 | **Multi-sélection** (Ctrl/Shift+clic) | Standard absolu | Moyen | Tous les commerciaux |
| 🟡 7 | **Quick Access / Favoris** | Confort utilisateur | Moyen | Telerik (réseau uniquement) |
| 🟡 8 | **Volet de prévisualisation** | Différenciateur fort | Élevé | Aucun concurrent WPF |
| 🟡 9 | **Recherche temps réel** | Confort power user | Faible | Explorer, Telerik |
| 🟢 10 | **IFileSystemProvider** abstrait | Testabilité + VFS | Faible | Syncfusion (partiel) |

## Analyse des gaps de marché exploitables

### Gap 1 — Aucun produit standalone mid-market
**Opportunity :** composant NuGet standalone, MIT ou commercial à <$99 à vie, avec RadFileDialog feature parity sans la suite à $1 000.

### Gap 2 — Aucune prévisualisation dans les dialogs commerciaux WPF
Telerik documente explicitement "thumbnails not supported". DevExpress WPF idem. Seul LogicNP FileView.WPF le fait mais est peu connu.
**Opportunity :** panneau preview avec `IShellItemImageFactory` = différenciateur unique vs. les 3 leaders.

### Gap 3 — Syncfusion n'a pas de WPF file dialog (feature request ouverte, 20 votes, depuis 2020)
**Opportunity :** cibler les utilisateurs Syncfusion qui cherchent un complémentaire compatible.

### Gap 4 — Aucun validateur de contenu à la sélection
Tous s'arrêtent aux filtres par extension. Taille max, type MIME, en-têtes magiques = non couverts.
**Opportunity :** `IFileValidator` interface avec implémentations prêtes à l'emploi.

### Impact .NET 8 sur le marché
`.NET 8 (2023)` a comblé plusieurs gaps (OpenFolderDialog, ClientGuid, RootDirectory). Le marché se recentre sur : **cohérence visuelle (theming) + mode embarqué non-modal + features avancées (thumbnails, validateurs, favoris)**.

---

# MÉTIER 6 — Développeur d'applications métier (Enterprise / LOB)

## Cas d'usage spécifiques et besoins

### Cas A — Système de gestion documentaire (DMS)
**Besoins :** navigation restreinte à un repository, colonnes custom (version, auteur, statut), thumbnails documents, IFileSystemProvider pointant vers une API REST.
**Gap concurrent :** Actipro Shell propose un "custom shell object framework" mais requiert un gros travail d'intégration. Aucun autre concurrent ne supporte un backend non-filesystem.
**Solution :** `IFileSystemProvider` permettant une implémentation REST/HTTP :
```csharp
public class RestDocumentProvider : IFileSystemProvider
{
    public async IAsyncEnumerable<FileSystemNode> GetChildrenAsync(string path, CancellationToken ct)
    {
        var items = await _apiClient.GetDocumentsAsync(path, ct);
        foreach (var item in items) yield return MapToNode(item);
    }
}
```

### Cas B — Application CAO / Engineering
**Besoins :** colonnes custom (dimensions, révision, matière), filtrage par date de modification (pas seulement extension), prévisualisation 3D (miniature shell via handler COM enregistré).
**Gap :** Aucun concurrent ne supporte "filtre par plage de dates". Filter par attributs custom = zéro support commercial.
**Solution :** `IFileFilter` composable :
```csharp
public interface IFileFilter
{
    bool Matches(FileSystemNode node);
}

// Filtres composables :
new CompositeFilter(
    new ExtensionFilter(".dwg", ".dxf"),
    new DateRangeFilter(DateTime.Now.AddDays(-30), DateTime.Now),
    new FileSizeFilter(maxBytes: 50_000_000)
)
```

### Cas C — Application ERP / Finance
**Besoins :** theming cohérent, restriction stricte aux extensions autorisées, validation contenu avant confirmation (taille max, format), support UNC, MVVM propre.
**Gap DevExpress :** bug documenté — `DXOpenFileDialog` modifie le `CurrentDirectory` de l'application en navigation. Gap Telerik : thumbnails absents.
**Solution — validation à la confirmation :**
```csharp
dialog.FileConfirming += async (s, e) => {
    var fileInfo = new FileInfo(e.SelectedPath);
    if (fileInfo.Length > 50_000_000) {
        e.Cancel = true;
        await ShowErrorAsync("Fichier trop volumineux (max 50 Mo)");
    }
    // Vérification magic bytes :
    var header = new byte[4];
    using var fs = File.OpenRead(e.SelectedPath);
    await fs.ReadAsync(header, 0, 4, e.CancellationToken);
    if (!header.SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 })) // ZIP/XLSX
        e.Cancel = true;
};
```

### Cas D — IDE / Outil de développement
**Besoins :** `ClientGuid` par contexte de dialog, récents par projet (pas global Windows), accès rapide au workspace.
**Note .NET 8 :** `ClientGuid` et `RootDirectory` sont maintenant natifs. Pour .NET 4.x, implémenter via un dictionnaire `Dictionary<Guid, string> _lastPaths` dans les settings utilisateur.

---

---

# CONCEPTS TECHNIQUES SECTORIELS — APPLICABLE AU PROJET

## 1. Virtualisation UI — Priorité CRITIQUE

**Problème actuel :** le `TreeView` crée un `TreeViewItem` pour chaque entrée visible, sans recycling. Sur un dossier avec 5 000 fichiers : freeze de plusieurs secondes.

**Solution :**
```xml
<TreeView VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling"
          VirtualizingStackPanel.ScrollUnit="Item"
          ScrollViewer.IsDeferredScrollingEnabled="True">
    <TreeView.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel/>
        </ItemsPanelTemplate>
    </TreeView.ItemsPanel>
</TreeView>
```
Chaque niveau imbriqué doit avoir son propre `VirtualizingStackPanel` dans le `HierarchicalDataTemplate`.

**Complexité :** Faible.

---

## 2. Enumération async + streaming

**Problème actuel :** `Directory.GetFiles()` bloque le thread UI pendant l'énumération.

**Solution :**
```csharp
public async IAsyncEnumerable<FileSystemInfo> EnumerateAsync(
    string path, [EnumeratorCancellation] CancellationToken ct = default)
{
    await Task.Yield(); // sort du thread UI
    DirectoryInfo dir;
    try { dir = new DirectoryInfo(path); }
    catch (Exception) { yield break; }
    
    foreach (var entry in dir.EnumerateFileSystemInfos())
    {
        ct.ThrowIfCancellationRequested();
        if (ShouldShow(entry)) yield return entry;
    }
}

// Dans le ViewModel :
await foreach (var item in EnumerateAsync(path, _expansionCts.Token))
{
    // Batch dispatch tous les 50 items pour ne pas flooder le UI thread
    _batch.Add(MapToViewModel(item));
    if (_batch.Count >= 50)
    {
        var batchCopy = _batch.ToList();
        _batch.Clear();
        await Dispatcher.InvokeAsync(() => { foreach (var vm in batchCopy) node.Children.Add(vm); });
    }
}
```

**Complexité :** Moyen.

---

## 3. Icônes Shell (SHGetFileInfo) — Priorité HAUTE

**Problème actuel :** aucune icône affichée — le contrôle est visuellement nu.

**Solution :**
```csharp
[DllImport("shell32.dll", CharSet = CharSet.Auto)]
private static extern IntPtr SHGetFileInfo(
    string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi,
    uint cbFileInfo, uint uFlags);

private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

public static ImageSource GetFileIcon(string extension, bool isFolder = false)
{
    var key = isFolder ? "__folder__" : extension.ToLowerInvariant();
    return _iconCache.GetOrAdd(key, k => {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
        uint attr = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        SHGetFileInfo(isFolder ? "folder" : k, attr, ref info, (uint)Marshal.SizeOf(info), flags);
        var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        DestroyIcon(info.hIcon);
        source.Freeze();
        return source;
    });
}
```
`SHGFI_USEFILEATTRIBUTES` : récupère l'icône par extension **sans lire le fichier** — rapide et sûr depuis un thread background STA.

**Complexité :** Moyen (boilerplate P/Invoke bien documenté).

---

## 4. Miniatures Shell (IShellItemImageFactory) — Différenciateur unique

```csharp
[ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemImageFactory
{
    void GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
}

public async Task<ImageSource?> GetThumbnailAsync(string path, int size = 96)
{
    return await Task.Factory.StartNew(() => {
        // DOIT tourner sur un thread STA
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory) != 0)
            return null;
        
        ((IShellItemImageFactory)factory).GetImage(new SIZE(size, size), SIIGBF.SIIGBF_RESIZETOFIT, out var hBitmap);
        var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        DeleteObject(hBitmap);
        source.Freeze();
        return (ImageSource)source;
    }, CancellationToken.None, TaskCreationOptions.None, StaTaskScheduler.Default);
}
```
Utilise le cache de miniatures Windows — les images déjà vues dans Explorer s'affichent instantanément.

**Complexité :** Moyen-Élevé. Référence open-source : [ShellThumbs (rlv-dan/ShellThumbs)](https://github.com/rlv-dan/ShellThumbs).

---

## 5. Breadcrumb Navigation

**Pattern recommandé :**
```csharp
public class BreadcrumbViewModel : ObservableObject
{
    public ObservableCollection<BreadcrumbSegment> Segments { get; } = new();
    public bool IsEditing { get; set; }
    public string EditText { get; set; } = "";
    
    public void SetPath(string fullPath)
    {
        Segments.Clear();
        var parts = fullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var accumulated = "";
        foreach (var part in parts)
        {
            accumulated = Path.Combine(accumulated, part);
            Segments.Add(new BreadcrumbSegment(part, accumulated, GetSiblings(accumulated)));
        }
    }
    
    private IEnumerable<string> GetSiblings(string path)
    {
        try { return Directory.GetDirectories(Path.GetDirectoryName(path) ?? path).Select(Path.GetFileName)!; }
        catch { return Enumerable.Empty<string>(); }
    }
}
```
Librairie prête à l'emploi : [`Dirkster99/bm`](https://github.com/Dirkster99/bm) (MIT, WPF/MVVM).

**Complexité :** Moyen.

---

## 6. Multi-sélection

Le `TreeView` WPF natif ne supporte que la sélection simple. Trois options :

**Option A — Librairie prête à l'emploi (recommandé) :**
[`ygoe/MultiSelectTreeView`](https://github.com/ygoe/MultiSelectTreeView) — MIT, NuGet, Ctrl+Click, Shift+Click, `SelectedItems` bindable.

**Option B — Behavior MVVM propre :**
```csharp
public class MultiSelectBehavior : Behavior<TreeView>
{
    private Point _dragStart;
    
    protected override void OnAttached()
    {
        AssociatedObject.PreviewMouseDown += OnMouseDown;
        AssociatedObject.PreviewKeyDown += OnKeyDown;
    }
    
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
        if (item == null) return;
        
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            ToggleSelection(item);
        else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            ExtendSelection(item);
        else
            SetSingleSelection(item);
    }
}
```

**Complexité :** Faible (Option A) / Moyen (Option B).

---

## 7. Quick Access / Emplacements connus

```csharp
// Emplacements Windows connus :
public static IEnumerable<KnownLocation> GetKnownLocations()
{
    var folders = new[] {
        (Environment.SpecialFolder.Desktop, "Bureau"),
        (Environment.SpecialFolder.MyDocuments, "Documents"),
        (Environment.SpecialFolder.MyPictures, "Images"),
        (Environment.SpecialFolder.MyMusic, "Musique"),
        (Environment.SpecialFolder.MyVideos, "Vidéos"),
        (Environment.SpecialFolder.UserProfile, "Profil"),
    };
    return folders.Select(f => new KnownLocation(
        f.Item2, Environment.GetFolderPath(f.Item1), GetFileIcon("__folder__", isFolder: true)));
}

// Quick Access Shell (dossiers épinglés par l'utilisateur) :
private static IEnumerable<string> GetQuickAccessPins()
{
    var qa = new Shell32.Shell().NameSpace("shell:::{679F85CB-0220-4080-B29B-5540CC05AAB6}");
    return (from Shell32.FolderItem item in qa.Items() select item.Path).ToList();
}
```

**Complexité :** Faible-Moyen.

---

## 8. FileSystemWatcher — Mises à jour en temps réel

```csharp
private FileSystemWatcher? _watcher;

private void WatchDirectory(string path)
{
    _watcher?.Dispose();
    _watcher = new FileSystemWatcher(path) {
        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        IncludeSubdirectories = false,
        EnableRaisingEvents = true
    };
    
    // Debounce : évite la tempête d'events sur copy/paste massif
    var debouncer = new System.Timers.Timer(200) { AutoReset = false };
    debouncer.Elapsed += (_, _) => Dispatcher.InvokeAsync(RefreshCurrentDirectory);
    
    _watcher.Created += (_, _) => { debouncer.Stop(); debouncer.Start(); };
    _watcher.Deleted += (_, _) => { debouncer.Stop(); debouncer.Start(); };
    _watcher.Renamed += (_, _) => { debouncer.Stop(); debouncer.Start(); };
}
```
**Important :** surveiller **uniquement le répertoire courant**, pas l'arbre entier (bug de performance Telerik documenté).

**Complexité :** Faible.

---

## 9. Recherche / Filtre temps réel

```csharp
// CollectionView pour la liste de fichiers :
ICollectionView _fileView = CollectionViewSource.GetDefaultView(Files);
private DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

public string FilterText
{
    set {
        _filterText = value;
        _filterDebounce.Stop();
        _filterDebounce.Start(); // déclenche ApplyFilter après 200ms
    }
}

private void ApplyFilter()
{
    _fileView.Filter = o => {
        if (string.IsNullOrWhiteSpace(_filterText)) return true;
        var node = (FileSystemNode)o;
        return node.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
        // Optionnel : FuzzySharp.Fuzz.Ratio(node.Name, _filterText) > 60
    };
}
```
Pour la recherche récursive (type Windows Search), lancer un `Task.Run` qui parcourt les sous-dossiers et alimente une `ObservableCollection<SearchResult>`.

**Complexité :** Faible (filtre simple) / Moyen (fuzzy + récursif).

---

## 10. Gestion robuste des erreurs FileSystem

```csharp
// Pattern centralisé pour toutes les opérations FileSystem :
private async IAsyncEnumerable<FileSystemNode> SafeEnumerateAsync(
    string path, [EnumeratorCancellation] CancellationToken ct)
{
    IEnumerable<FileSystemInfo> entries;
    try
    {
        entries = new DirectoryInfo(path).EnumerateFileSystemInfos();
    }
    catch (UnauthorizedAccessException ex) { OnExceptionRaised(ex, path); yield break; }
    catch (DirectoryNotFoundException ex) { OnExceptionRaised(ex, path); yield break; }
    catch (IOException ex) { OnExceptionRaised(ex, path); yield break; }
    catch (PathTooLongException ex) { OnExceptionRaised(ex, path); yield break; }
    catch (SecurityException ex) { OnExceptionRaised(ex, path); yield break; }
    
    foreach (var entry in entries)
    {
        ct.ThrowIfCancellationRequested();
        if (ShouldShow(entry)) yield return MapToNode(entry);
    }
}

// Event public pour que l'hôte puisse logguer/afficher :
public event EventHandler<FileDialogExceptionEventArgs>? ExceptionRaised;
```

**Complexité :** Faible.

---

## 11. Drag & Drop

```csharp
// Initier un drag depuis l'arbre :
private void TreeView_PreviewMouseMove(object sender, MouseEventArgs e)
{
    if (e.LeftButton != MouseButtonState.Pressed || _selectedNodes.Count == 0) return;
    if ((e.GetPosition(null) - _dragStartPoint).Length < SystemParameters.MinimumHorizontalDragDistance) return;
    
    var paths = _selectedNodes.Select(n => n.FullPath).ToArray();
    var data = new DataObject(DataFormats.FileDrop, paths);
    DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
}

// Recevoir un drop externe (depuis Explorer) :
private void TreeView_Drop(object sender, DragEventArgs e)
{
    if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
    var droppedPaths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
    var targetNode = GetNodeUnderCursor(e.GetPosition((IInputElement)sender));
    // Copier/déplacer selon e.Effects
}
```

**Complexité :** Moyen.

---

## 12. Raccourcis clavier

```xml
<UserControl.InputBindings>
    <KeyBinding Key="F5"    Command="{Binding RefreshCommand}"/>
    <KeyBinding Key="F2"    Command="{Binding RenameCommand}"/>
    <KeyBinding Key="Delete" Command="{Binding DeleteCommand}"/>
    <KeyBinding Key="Back"  Command="{Binding GoUpCommand}"/>
    <KeyBinding Key="A" Modifiers="Ctrl" Command="{Binding SelectAllCommand}"/>
    <KeyBinding Key="C" Modifiers="Ctrl" Command="{Binding CopyPathCommand}"/>
</UserControl.InputBindings>
```

Renommage inline : remplacer le `TextBlock` du `TreeViewItem` par un `TextBox` via un `DataTrigger` sur `IsRenaming`, engager sur F2, valider sur Enter, annuler sur Escape.

**Complexité :** Faible.

---

---

# SYNTHÈSE — ROADMAP PRIORISÉE

## Phase 1 — Fondations (impact immédiat, effort faible)
1. **Virtualisation UI** (`VirtualizingStackPanel.Recycling`) — résout les freezes
2. **Enumération async** avec CancellationToken — UI non bloquée
3. **Gestion robuste des exceptions** (UnauthorizedAccess, PathTooLong, Drive removed)
4. **Icônes Shell** (SHGetFileInfo) — différence visuelle radicale
5. **Raccourcis clavier** de base (F5, Back, Ctrl+A)

## Phase 2 — Différenciateurs UX (effort moyen)
6. **Breadcrumb** (navigation + saisie directe + dropdown voisins)
7. **Multi-sélection** (ygoe/MultiSelectTreeView ou Behavior custom)
8. **Quick Access** (emplacements connus + favoris persistés)
9. **Recherche / filtre** temps réel
10. **FileSystemWatcher** sur le répertoire courant

## Phase 3 — Différenciateurs techniques (effort élevé, ROI élevé)
11. **IFileSystemProvider** abstrait (testabilité + VFS)
12. **Miniatures Shell** (IShellItemImageFactory) — unique vs. Telerik/DevExpress
13. **IFileValidator** composable (taille, type MIME, dates)
14. **Vue colonnes** (Miller Columns) — inspiration macOS

## Ce qu'aucun concurrent WPF ne fait bien
- Miniatures + volet de prévisualisation dans un dialog WPF natif
- IFileSystemProvider pour backends non-filesystem (REST, ZIP, cloud)
- Validateurs de contenu à la confirmation
- Prix standalone (sans suite de 150 contrôles)

---

*Sources principales : Telerik Docs, DevExpress Support Center, Syncfusion Docs, Ookii.Dialogs GitHub, dotnet/wpf GitHub issues, Microsoft WPF .NET 8 blog, Actipro Shell Docs, LogicNP VS Marketplace, Apple NSOpenPanel Docs, WCAG 2.1 Guidelines.*
