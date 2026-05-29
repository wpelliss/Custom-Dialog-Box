# CustomDialogBox — WPF File Open Dialog

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2%2B-blueviolet)](https://dotnet.microsoft.com/download/dotnet-framework)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d7)](https://www.microsoft.com/windows)

> Custom file browser dialog for WPF applications (.NET Framework 4.7.2+).
> Replaces the native `OpenFileDialog` with a full-featured Explorer-style dual-pane browser — because sometimes the box Windows ships you just is not the box you need.

---

## Why Does This Exist?

The built-in `OpenFileDialog` is fine. It is also a black box. You cannot style it, you cannot filter it your way, you cannot add a favorites panel, and you definitely cannot add a preview pane without doing some pretty dark P/Invoke magic against a COM interface that was designed in 1995.

This project replaces it with a WPF-native dialog you actually own. Full MVVM-partial architecture, lazy-loaded tree, Shell icons from the OS itself, and a clean seam to extend it. The code is yours — read it, fork it, ship it.

---

## Features

- **Lazy-loaded TreeView** — async file system enumeration keeps the UI responsive even on slow network paths or large directory trees
- **Shell icons via `SHGetFileInfo`** — the exact same icons Windows Explorer shows, keyed by file extension with an in-memory cache
- **ListView with full metadata columns** — Name, Type, Size, Date Modified
- **UI virtualization** — `VirtualizingStackPanel` in Recycling mode; scales comfortably to 10 000+ files without a hiccup
- **Robust error handling** — `UnauthorizedAccess`, `PathTooLong`, hot-unplug of USB drives, and NTFS reparse points are all handled gracefully, not silently swallowed
- **MVVM-partial architecture** — `FileSystemNodeViewModel` + `OpenViewModel` carry the logic; code-behind is deliberately thin
- **Keyboard support** — Enter to confirm, Escape to cancel; works the way users expect
- **Resizable panels** — `GridSplitter` between the tree and the file list
- **Network drives hidden by default** — reduces noise and avoids accidental hangs on unreachable UNC paths (opt-in to show them)

---

## Quick Start

Add the project reference, then drop in a three-liner:

```csharp
using CustomDialogBox;

var dialog = new Open { Owner = this };
if (dialog.ShowDialog() == true)
{
    string selectedPath = dialog.SelectedPath;
    // selectedPath is the full path the user picked
}
```

That is it. No NuGet dependency graph, no runtime surprises. Just a `Window` you can instantiate.

---

## Requirements

| Requirement | Version |
|---|---|
| OS | Windows (WPF is Windows-only) |
| .NET Framework | 4.7.2 or later |
| UI framework | WPF |

> .NET 5 / 6 / 8 compatibility is on the roadmap (see Phase 3 below). The P/Invoke surface and WPF APIs used here are all available on modern .NET — it is mostly a project file migration.

---

## Architecture

Three focused layers, each with a single responsibility:

```
CustomDialogBox/
├── FileSystemNodeViewModel.cs   # Tree node: lazy loading, async enumeration, Shell icons
├── OpenViewModel.cs             # Dialog state: drives, current file list, selected path
├── ShellIcons.cs                # P/Invoke wrapper for SHGetFileInfo + extension-keyed cache
├── Open.xaml                    # Dialog layout: TreeView | GridSplitter | ListView
└── Open.xaml.cs                 # Thin code-behind: wires ViewModel, handles Enter/Escape
```

### FileSystemNodeViewModel

Owns a single node in the tree. Exposes a dummy child on first load so the expand arrow appears, then replaces it with real children on first expand — the classic lazy-load pattern. Enumeration runs on a background thread; results marshal back to the UI thread via `Dispatcher`.

### OpenViewModel

Holds the root drive collection and the flat file list for the current directory. When the tree selection changes, `OpenViewModel` repopulates the `ListView`. Keeps `SelectedPath` as a simple bindable string.

### ShellIcons

One static class, one public method: `GetIcon(string path)`. Calls `SHGetFileInfo` with `SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES`, converts the `HICON` to a `BitmapSource`, caches by extension. Cold call is a Win32 round-trip; warm call is a dictionary lookup.

---

## Roadmap

### Phase 2 — Navigation and Selection

- [ ] Breadcrumb navigation bar (click a segment to jump, or type a path directly)
- [ ] Multi-selection with Ctrl+click and Shift+click
- [ ] Quick Access / Favorites panel (pinned folders, recent locations)
- [ ] Real-time search and filter within the current directory

### Phase 3 — Polish and Distribution

- [ ] Shell thumbnails via `IShellItemImageFactory` for image and document previews
- [ ] File content validators (max size, allowed MIME types, date range filter)
- [ ] .NET 5 / 6 / 8 / 9 project targets
- [ ] NuGet package (pending decision on API surface stability)

---

## Contributing

Issues and PRs are welcome. If you are adding a feature:

1. Keep the three-layer separation intact — Shell interop belongs in `ShellIcons`, not scattered through ViewModels
2. Do not block the UI thread — any enumeration that touches the file system goes async
3. Match the existing code style (no regions, explicit types over `var` for non-obvious returns)

---

## References and Prior Art

The design draws on two solid writeups that are worth reading if you want to understand the TreeView lazy-load pattern and the Shell icon integration:

- [Designing a WPF TreeView File Explorer](https://medium.com/@mikependon/designing-a-wpf-treeview-file-explorer-565a3f13f6f2) — Mike Pendon
- [WPF Windows Explorer-like TreeView](https://docs.telerik.com/devtools/wpf/controls/radtreeview/how-to/wpf-windowsexplorer-like-treeview) — Telerik Docs

---

## License

MIT. See [LICENSE](LICENSE) for the full text.

Do what you want with it. Ship it in a product, fork it, teach with it. Attribution appreciated but not required.
