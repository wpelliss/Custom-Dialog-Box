using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace CustomDialogBox
{
    public class FileSystemNodeViewModel : INotifyPropertyChanged
    {
        private enum Kind { Drive, Directory, File }

        private static readonly FileSystemNodeViewModel _sentinel = new FileSystemNodeViewModel();

        private readonly Kind _kind;
        private bool _isExpanded;
        private bool _isSelected;
        private bool _isLoaded;
        private CancellationTokenSource _cts;
        private ImageSource _icon;
        private ObservableCollection<FileSystemNodeViewModel> _children;

        // Private constructor for sentinel placeholder
        private FileSystemNodeViewModel()
        {
            _kind = Kind.File;
            Name = string.Empty;
            FullPath = string.Empty;
            _children = new ObservableCollection<FileSystemNodeViewModel>();
        }

        public FileSystemNodeViewModel(DriveInfo drive)
        {
            if (drive == null) throw new ArgumentNullException("drive");
            _kind    = Kind.Drive;
            Name     = drive.Name;
            FullPath = drive.RootDirectory.FullName;
            _children = new ObservableCollection<FileSystemNodeViewModel> { _sentinel };
            LoadIconAsync();
        }

        public FileSystemNodeViewModel(DirectoryInfo directory)
        {
            if (directory == null) throw new ArgumentNullException("directory");
            _kind         = Kind.Directory;
            Name          = directory.Name;
            FullPath      = directory.FullName;
            DateModified  = directory.LastWriteTime;
            _children     = new ObservableCollection<FileSystemNodeViewModel> { _sentinel };
            LoadIconAsync();
        }

        public FileSystemNodeViewModel(FileInfo file)
        {
            if (file == null) throw new ArgumentNullException("file");
            _kind         = Kind.File;
            Name          = file.Name;
            FullPath      = file.FullName;
            Size          = file.Length;
            DateModified  = file.LastWriteTime;
            _children     = new ObservableCollection<FileSystemNodeViewModel>();
            LoadIconAsync();
        }

        // -----------------------------------------------------------------------
        // Identity properties
        // -----------------------------------------------------------------------

        public string   Name         { get; private set; }
        public string   FullPath     { get; private set; }
        public bool     IsFile       { get { return _kind == Kind.File; } }
        public long     Size         { get; private set; }
        public DateTime DateModified { get; private set; }

        /// <summary>Human-readable type string used in the ListView "Type" column.</summary>
        public string NodeType
        {
            get
            {
                switch (_kind)
                {
                    case Kind.Drive:     return "Lecteur";
                    case Kind.Directory: return "Dossier";
                    default:             return "Fichier";
                }
            }
        }

        // -----------------------------------------------------------------------
        // Icon (loaded asynchronously via ShellIcons)
        // -----------------------------------------------------------------------

        public ImageSource Icon
        {
            get { return _icon; }
            private set { _icon = value; OnPropertyChanged(); }
        }

        // -----------------------------------------------------------------------
        // Children
        // -----------------------------------------------------------------------

        public ObservableCollection<FileSystemNodeViewModel> Children
        {
            get { return _children; }
            private set { _children = value; OnPropertyChanged(); }
        }

        // -----------------------------------------------------------------------
        // IsExpanded  (triggers lazy load on first expansion)
        // -----------------------------------------------------------------------

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();

                if (_isExpanded && !_isLoaded)
                    LoadChildrenAsync();
            }
        }

        // -----------------------------------------------------------------------
        // IsSelected
        // -----------------------------------------------------------------------

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        // -----------------------------------------------------------------------
        // Async children loading
        // -----------------------------------------------------------------------

        private async void LoadChildrenAsync()
        {
            CancellationTokenSource prev = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
            try   { prev?.Cancel();  }
            finally { prev?.Dispose(); }

            CancellationToken ct = _cts.Token;
            FileSystemNodeViewModel[] results;

            try
            {
                results = await Task.Run(() => FileSystemService.EnumerateChildren(FullPath, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.WriteLine("[FileSystemNodeViewModel] LoadChildrenAsync('" + FullPath + "') — " + ex.Message);
                _isLoaded = true;
                return;
            }

            if (ct.IsCancellationRequested) return;

            _children.Clear();
            foreach (var child in results)
                _children.Add(child);

            _isLoaded = true;
        }

        // -----------------------------------------------------------------------
        // Async icon loading (yielded to avoid blocking the UI thread)
        // -----------------------------------------------------------------------

        private async void LoadIconAsync()
        {
            await Task.Yield(); // let the UI initialize before loading icons

            try
            {
                Icon = _kind == Kind.File
                    ? ShellIcons.GetFileIcon(Path.GetExtension(Name))
                    : ShellIcons.GetDirectoryIcon();
            }
            catch { /* icon loading is non-critical */ }
        }

        // -----------------------------------------------------------------------
        // INotifyPropertyChanged
        // -----------------------------------------------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
