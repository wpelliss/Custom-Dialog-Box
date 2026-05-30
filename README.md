# CustomDialogBox — WPF File Open Dialog

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-blueviolet)](https://dotnet.microsoft.com/download/dotnet-framework)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d7)](https://www.microsoft.com/windows)
[![Build](https://img.shields.io/badge/build-MSBuild%20VS2022-green)](https://visualstudio.microsoft.com/)

A drop-in replacement for `OpenFileDialog` in WPF/.NET Framework apps. Explorer-style dual-pane browser — tree on the left, file list on the right — with full MVVM architecture, real-time search, multi-selection, and Shell thumbnails for images.

---

## Features

### Navigation
- **Breadcrumb bar** — click any segment to jump up, or click the bar to type a path directly (Alt+D)
- **Quick Access panel** — Bureau, Documents, Images, Musique, Vidéos, Profil from known Windows folders
- **Lazy-loaded TreeView** — async enumeration, expand-on-first-open with sentinel pattern
- **Keyboard shortcuts** — F5 refresh, Backspace up, Alt+D address bar, Ctrl+F search, Enter confirm, Escape cancel

### File Listing
- **Real-time search** — Ctrl+F to focus the search box, 200 ms debounce, Escape to clear
- **Multi-selection** — Ctrl+click, Shift+click; file bar shows count when multiple files are selected
- **4-column ListView** — Name (with Shell icon), Type, Size (KB), Date Modified
- **Column sorting** — click any header, click again to reverse
- **UI virtualization** — `VirtualizingStackPanel` in Recycling mode; handles 10 000+ files without a freeze

### Shell Integration
- **Shell icons** — `SHGetFileInfo` with extension-keyed cache; same icons as Windows Explorer
- **Thumbnails for images** — `IShellItemImageFactory` via P/Invoke for `.jpg/.jpeg/.png/.gif/.bmp/.webp/.tiff`; uses Windows thumbnail cache, async load, falls back to Shell icon
- **Reparse point filtering** — symlinks and junctions excluded from the tree and list
- **Network drives hidden by default** — avoids accidental hangs on unreachable UNC paths

### Architecture
- **Full MVVM** — `OpenViewModel`, `FileSystemNodeViewModel`, `IFileSystemProvider`
- **IFileSystemProvider abstraction** — inject a test double for unit tests, or a VFS/cloud backend
- **Async everywhere** — `Task.Run` + `CancellationToken` for all disk I/O; UI never blocks
- **Robust error handling** — `UnauthorizedAccess`, `PathTooLong`, USB hot-unplug, all handled gracefully

---

## Quick Start

### Single file selection (drop-in replacement)

```csharp
using CustomDialogBox;

var dialog = new Open { Owner = this };
if (dialog.ShowDialog() == true)
{
    string path = dialog.SelectedPath;  // full path of the selected file
}
```

### Multi-file selection

```csharp
var dialog = new Open { Owner = this };
if (dialog.ShowDialog() == true)
{
    IReadOnlyList<string> paths = dialog.SelectedPaths;  // all selected files
    string first = dialog.SelectedPath;                   // first (or only) selection
}
```

---

## Build

> **Note:** This project targets .NET Framework 4.7.2. Use **MSBuild from Visual Studio 2022**, not `dotnet build` (the .NET SDK refuses to build old-style `.csproj` WPF projects).

```bat
# From a Developer Command Prompt for VS 2022
MSBuild CustomDialogBox\CustomDialogBox.csproj /t:Build /p:Configuration=Release
```

Output lands in `CustomDialogBox\bin\Release\CustomDialogBox.exe`.

---

## Requirements

| | |
|---|---|
| OS | Windows 10 / 11 |
| .NET Framework | 4.7.2 or later |
| UI framework | WPF |
| Build tool | MSBuild (Visual Studio 2022) |

---

## Architecture

```
CustomDialogBox/
│
├── IFileSystemProvider.cs          # Abstraction — inject test doubles or VFS backends
├── WindowsFileSystemProvider.cs    # Default: wraps FileSystemService + DriveInfo
│
├── FileSystemNodeViewModel.cs      # Tree node — lazy load, async icons/thumbnails, INotifyPropertyChanged
├── FileSystemService.cs            # Static enumeration — EnumerateDirectories/Files, all exception handling
├── ShellIcons.cs                   # SHGetFileInfo (16×16 icons) + IShellItemImageFactory (thumbnails)
│
├── OpenViewModel.cs                # Dialog state — drives, current list, search, selection, breadcrumbs
├── NavItem.cs                      # Immutable data item for breadcrumb segments and Quick Access entries
├── RelayCommand.cs                 # ICommand wrappers (generic + non-generic)
│
├── Open.xaml                       # Dialog layout — breadcrumb, Quick Access, TreeView, ListView
└── Open.xaml.cs                    # Thin code-behind — routes events to VM, handles keyboard and address bar
```

### Key design decisions

| Decision | Rationale |
|---|---|
| Sentinel child node | TreeView shows expand arrow before enumeration; replace on first expand |
| `async void LoadChildrenAsync()` | Fire-and-forget is intentional — UI should not await tree expansion |
| `CancellationToken` per node | Rapid expand/collapse does not accumulate background tasks |
| Filter via `CollectionViewSource` | Sort and filter share one `ICollectionView`; no data duplication |
| `IFileSystemProvider` | Makes `OpenViewModel` unit-testable without touching the real file system |
| Thumbnails lazy-loaded | 32×32 thumbnails via Windows thumbnail cache; falls back to Shell icon without a performance penalty |

---

## Extending

### Custom file system backend (VFS, cloud, ZIP)

```csharp
public class MyCloudProvider : IFileSystemProvider
{
    public FileSystemNodeViewModel[] GetChildren(string path, CancellationToken ct = default)
    {
        // query your API, return nodes
    }

    public DriveInfo[] GetDrives() => DriveInfo.GetDrives();
}

// Inject at construction
var dialog = new Open(new MyCloudProvider()) { Owner = this };
```

> The `Open` window's constructor passes the provider through to `OpenViewModel`.
> You will need to expose a `public Open(IFileSystemProvider provider)` overload — currently the constructor is parameterless but the underlying ViewModel already accepts the provider.

### File validation before Ouvrir fires

Override `BtnOpen_Click` in a subclass or subscribe to `Closing` to add your own guards (MIME check, size limit, signature verification, etc.). `SelectedPaths` gives you the full list before the dialog closes.

---

## Keyboard Reference

| Key | Action |
|---|---|
| **Enter** | Confirm selection (Ouvrir) |
| **Escape** | Cancel / close address bar edit |
| **F5** | Refresh current directory |
| **Backspace** | Navigate to parent directory |
| **Alt+D** | Focus address bar (type a path directly) |
| **Ctrl+F** | Focus search box |
| **Ctrl+click** | Add to selection (multi-select) |
| **Shift+click** | Range selection |
| **Double-click file** | Confirm selection |
| **Double-click folder** | Navigate into folder |

---

## Roadmap

- [ ] `.NET 6 / 8` project target (SDK-style `.csproj`, WPF on modern .NET)
- [ ] `IFileValidator` composable — max size, magic bytes, date range
- [ ] File content preview pane (text, image, PDF first page)
- [ ] Pinned favorites in Quick Access (persist to `%APPDATA%`)
- [ ] NuGet package (pending API surface review)

---

## Contributing

Issues and PRs are welcome.

- Shell interop belongs in `ShellIcons.cs`, not in ViewModels
- Any disk I/O must be off the UI thread (`Task.Run` + `CancellationToken`)
- Code style: no `var` for non-obvious types, no regions, no XML doc on obvious members

---

## License

MIT — see [LICENSE](LICENSE). Ship it, fork it, teach with it.
