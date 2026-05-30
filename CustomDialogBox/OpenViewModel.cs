using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CustomDialogBox
{
    /// <summary>
    /// ViewModel de la fenetre Open.
    /// Expose les lecteurs disponibles, le repertoire courant et la selection de l'utilisateur.
    /// </summary>
    public class OpenViewModel : INotifyPropertyChanged
    {
        // -----------------------------------------------------------------------
        // Construction
        // -----------------------------------------------------------------------

        public OpenViewModel()
        {
            Drives          = new ObservableCollection<FileSystemNodeViewModel>();
            CurrentChildren = new ObservableCollection<FileSystemNodeViewModel>();
            QuickAccess     = new ObservableCollection<NavItem>();
            Breadcrumbs     = new ObservableCollection<NavItem>();

            NavigateCommand = new RelayCommand<string>(
                path => _ = SetCurrentDirectoryAsync(path),
                path => !string.IsNullOrEmpty(path));

            GoUpCommand = new RelayCommand(
                () => {
                    var parent = GetParentPath(CurrentDirectory);
                    if (parent != null) SetCurrentDirectory(parent);
                },
                () => GetParentPath(CurrentDirectory) != null);

            RefreshCommand = new RelayCommand(
                () => { if (!string.IsNullOrEmpty(CurrentDirectory)) SetCurrentDirectory(CurrentDirectory); },
                () => !string.IsNullOrEmpty(CurrentDirectory));

            LoadQuickAccess();
            _ = LoadDrivesAsync();
        }

        // -----------------------------------------------------------------------
        // Lecteurs
        // -----------------------------------------------------------------------

        /// <summary>
        /// Collection des lecteurs locaux prets (reseau et non-prets exclus).
        /// </summary>
        public ObservableCollection<FileSystemNodeViewModel> Drives { get; private set; }

        private async Task LoadDrivesAsync()
        {
            DriveInfo[] drives;
            try
            {
                // DriveInfo.GetDrives() peut bloquer sur des lecteurs USB lents —
                // on le deplace hors du thread UI.
                drives = await Task.Run(() => DriveInfo.GetDrives());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenViewModel] LoadDrivesAsync — echec enumeration lecteurs : {ex}");
                return;
            }

            foreach (var drive in drives)
            {
                if (drive.DriveType == DriveType.Network) continue;
                if (!drive.IsReady)                       continue;

                try
                {
                    Drives.Add(new FileSystemNodeViewModel(drive));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OpenViewModel] LoadDrivesAsync — lecteur {drive.Name} inaccessible : {ex}");
                }
            }
        }

        // -----------------------------------------------------------------------
        // CurrentChildren  (contenu du repertoire courant, panneau liste)
        // -----------------------------------------------------------------------

        public ObservableCollection<FileSystemNodeViewModel> CurrentChildren { get; private set; }

        // -----------------------------------------------------------------------
        // CancellationTokenSource — protege contre les race conditions de navigation
        // -----------------------------------------------------------------------

        private CancellationTokenSource _navigationCts;

        // -----------------------------------------------------------------------
        // NavigateCommand — ICommand expose pour le XAML et les tests unitaires
        // -----------------------------------------------------------------------

        public ICommand NavigateCommand { get; }
        public ICommand GoUpCommand     { get; }
        public ICommand RefreshCommand  { get; }

        // -----------------------------------------------------------------------
        // Quick Access (raccourcis vers emplacements Windows connus)
        // -----------------------------------------------------------------------

        public ObservableCollection<NavItem> QuickAccess { get; }

        private void LoadQuickAccess()
        {
            var specials = new[]
            {
                Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolder.MyMusic,
                Environment.SpecialFolder.MyVideos,
                Environment.SpecialFolder.UserProfile,
            };
            var labels = new[] { "Bureau", "Documents", "Images", "Musique", "Vidéos", "Profil" };

            for (int i = 0; i < specials.Length; i++)
            {
                try
                {
                    var path = Environment.GetFolderPath(specials[i]);
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        QuickAccess.Add(new NavItem(labels[i], path));
                }
                catch { }
            }
        }

        // -----------------------------------------------------------------------
        // Breadcrumbs (segments du chemin courant)
        // -----------------------------------------------------------------------

        public ObservableCollection<NavItem> Breadcrumbs { get; }

        private void UpdateBreadcrumbs(string path)
        {
            Breadcrumbs.Clear();
            if (string.IsNullOrEmpty(path)) return;

            var segments = new List<NavItem>();
            var current  = path.TrimEnd(Path.DirectorySeparatorChar);

            while (!string.IsNullOrEmpty(current))
            {
                var root = Path.GetPathRoot(current);
                if (current == root || string.IsNullOrEmpty(Path.GetDirectoryName(current)))
                {
                    segments.Insert(0, new NavItem(root.TrimEnd(Path.DirectorySeparatorChar), root));
                    break;
                }
                segments.Insert(0, new NavItem(Path.GetFileName(current), current));
                current = Path.GetDirectoryName(current);
            }

            foreach (var seg in segments)
                Breadcrumbs.Add(seg);
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try { return Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)); }
            catch { return null; }
        }

        // -----------------------------------------------------------------------
        // SetCurrentDirectory — point d'entree interne (et depuis le code-behind si besoin)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Charge le contenu du repertoire designe dans CurrentChildren.
        /// Annule toute operation de chargement precedente (race condition evitee).
        /// CurrentDirectory n'est mis a jour qu'apres que le chargement a reussi,
        /// garantissant la coherence entre CurrentDirectory et CurrentChildren.
        /// </summary>
        public async void SetCurrentDirectory(string path)
        {
            await SetCurrentDirectoryAsync(path);
        }

        private async Task SetCurrentDirectoryAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // Annuler la navigation precedente, puis creer un nouveau token.
            CancellationTokenSource previousCts = Interlocked.Exchange(
                ref _navigationCts,
                new CancellationTokenSource());

            try
            {
                previousCts?.Cancel();
                previousCts?.Dispose();
            }
            catch (ObjectDisposedException) { /* deja dispose — inoffensif */ }

            CancellationToken ct = _navigationCts.Token;

            FileSystemNodeViewModel[] items;
            try
            {
                items = await Task.Run(() => FileSystemService.EnumerateChildren(path, ct), ct);
            }
            catch (OperationCanceledException)
            {
                // Navigation annulee par une selection plus recente — etat intact.
                return;
            }
            catch (Exception ex)
            {
                // Repertoire inaccessible apres selection (droits retires, media ejecte…).
                Debug.WriteLine($"[OpenViewModel] SetCurrentDirectoryAsync('{path}') — echec chargement : {ex}");
                CurrentChildren.Clear();
                return;
            }

            if (ct.IsCancellationRequested) return;

            // CurrentDirectory + breadcrumbs mis a jour APRES chargement reussi.
            CurrentDirectory = path;
            UpdateBreadcrumbs(path);

            CurrentChildren.Clear();
            foreach (var item in items)
                CurrentChildren.Add(item);
        }

        // -----------------------------------------------------------------------
        // CurrentDirectory
        // -----------------------------------------------------------------------

        private string _currentDirectory;

        public string CurrentDirectory
        {
            get { return _currentDirectory; }
            private set
            {
                if (_currentDirectory == value) return;
                _currentDirectory = value;
                OnPropertyChanged();
            }
        }

        // -----------------------------------------------------------------------
        // SelectedPath / SelectedFileName / HasSelection
        // -----------------------------------------------------------------------

        private string _selectedPath;

        /// <summary>
        /// Chemin complet du fichier selectionne par l'utilisateur.
        /// </summary>
        public string SelectedPath
        {
            get { return _selectedPath; }
            set
            {
                if (_selectedPath == value) return;
                _selectedPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedFileName));
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        /// <summary>
        /// Nom seul du fichier selectionne (derive de SelectedPath, lecture seule).
        /// </summary>
        public string SelectedFileName
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedPath)) return string.Empty;
                try { return Path.GetFileName(_selectedPath); }
                catch { return _selectedPath; }
            }
        }

        /// <summary>
        /// Vrai si un fichier est selectionne et que son chemin n'est pas vide.
        /// </summary>
        public bool HasSelection
        {
            get { return !string.IsNullOrWhiteSpace(_selectedPath); }
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
