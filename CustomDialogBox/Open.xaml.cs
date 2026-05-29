using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CustomDialogBox
{
    /// <summary>
    /// Thin relay — toute la logique metier est dans OpenViewModel.
    /// Ce code-behind se contente de router les evenements UI vers le VM.
    /// </summary>
    public partial class Open : Window
    {
        private readonly OpenViewModel _vm;

        // Memorise la colonne et la direction du dernier tri pour le tri cyclique.
        private string         _lastSortProperty;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;

        /// <summary>
        /// Chemin du fichier selectionne, expose vers l'appelant.
        /// _vm est garanti non-null (affecte dans le constructeur avant tout appel externe).
        /// </summary>
        public string SelectedPath => _vm.SelectedPath;

        public Open()
        {
            InitializeComponent();
            _vm = new OpenViewModel();
            DataContext = _vm;
        }

        // Note: le handler Window_Loaded n'est plus branche dans le XAML car
        // LoadDrives est deja appele dans le constructeur d'OpenViewModel.

        // ------------------------------------------------------------------ //
        //  TreeView (navigation arborescente)
        // ------------------------------------------------------------------ //

        private void TreeView_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as FileSystemNodeViewModel;
            if (node == null)
                return;

            // Si c'est un repertoire (ou lecteur), on navigue dedans.
            if (!node.IsFile)
                _vm.SetCurrentDirectory(node.FullPath);
        }

        // ------------------------------------------------------------------ //
        //  ListView (contenu du repertoire courant)
        // ------------------------------------------------------------------ //

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Utiliser e.AddedItems pour eviter le couplage implicite au nom du controle XAML.
            if (e.AddedItems.Count == 0) return;

            var node = e.AddedItems[0] as FileSystemNodeViewModel;
            if (node == null) return;

            // On selectionne uniquement les fichiers, pas les repertoires.
            if (node.IsFile)
                _vm.SelectedPath = node.FullPath;
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // e.AddedItems n'est pas disponible ici — on lit le SelectedItem du sender.
            var lv   = sender as ListView;
            var node = lv?.SelectedItem as FileSystemNodeViewModel;
            if (node == null) return;

            if (node.IsFile)
                DialogResult = true;
            else
                _vm.SetCurrentDirectory(node.FullPath);
        }

        // ------------------------------------------------------------------ //
        //  Boutons
        // ------------------------------------------------------------------ //

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.HasSelection) return;

            // Validation defensive : le fichier peut avoir ete supprime entre
            // la navigation et le clic Ouvrir.
            if (string.IsNullOrWhiteSpace(_vm.SelectedPath) ||
                !File.Exists(_vm.SelectedPath))
            {
                // Invalider la selection ; l'utilisateur doit re-selectionner.
                _vm.SelectedPath = null;
                return;
            }

            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        // ------------------------------------------------------------------ //
        //  Tri par clic sur l'en-tete de colonne
        // ------------------------------------------------------------------ //

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = sender as GridViewColumnHeader;
            if (header == null) return;

            string property = header.Tag as string;
            if (string.IsNullOrEmpty(property)) return;

            // Alterner la direction si on reclique sur la meme colonne.
            ListSortDirection direction = ListSortDirection.Ascending;
            if (property == _lastSortProperty)
                direction = _lastSortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

            _lastSortProperty  = property;
            _lastSortDirection = direction;

            var view = CollectionViewSource.GetDefaultView(listViewFiles.ItemsSource);
            if (view == null) return;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(property, direction));
        }
    }
}
