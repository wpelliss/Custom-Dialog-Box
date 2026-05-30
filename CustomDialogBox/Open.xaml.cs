using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CustomDialogBox
{
    /// <summary>
    /// Thin relay — toute la logique métier est dans OpenViewModel.
    /// Ce code-behind route les événements UI vers le VM et gère les
    /// comportements purement visuels (toggle barre adresse, tri, raccourcis).
    /// </summary>
    public partial class Open : Window
    {
        private readonly OpenViewModel _vm;

        private string         _lastSortProperty;
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;
        private bool           _isEditingPath;

        public string SelectedPath => _vm.SelectedPath;

        public Open()
        {
            InitializeComponent();
            _vm = new OpenViewModel();
            DataContext = _vm;
        }

        // ------------------------------------------------------------------ //
        //  Raccourci Alt+D — bascule en mode saisie directe du chemin
        // ------------------------------------------------------------------ //

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                SetAddressEditMode(true);
                e.Handled = true;
            }
        }

        private void SetAddressEditMode(bool editing)
        {
            _isEditingPath = editing;

            if (editing)
            {
                addressBox.Text = _vm.CurrentDirectory ?? string.Empty;
                breadcrumbBorder.Visibility = Visibility.Collapsed;
                addressBox.Visibility       = Visibility.Visible;
                addressBox.SelectAll();
                addressBox.Focus();
            }
            else
            {
                addressBox.Visibility       = Visibility.Collapsed;
                breadcrumbBorder.Visibility = Visibility.Visible;
            }
        }

        // ------------------------------------------------------------------ //
        //  Barre d'adresse (TextBox en mode édition)
        // ------------------------------------------------------------------ //

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;  // consume BEFORE hiding box — prevents IsDefault button firing
                var path = addressBox.Text?.Trim();
                SetAddressEditMode(false);

                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    _vm.SetCurrentDirectory(path);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SetAddressEditMode(false);
            }
        }

        private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isEditingPath)
                SetAddressEditMode(false);
        }

        private void BreadcrumbBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1 && !_isEditingPath)
            {
                SetAddressEditMode(true);
                e.Handled = true;
            }
        }

        // ------------------------------------------------------------------ //
        //  Accès rapide
        // ------------------------------------------------------------------ //

        private void QuickAccess_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var lb   = sender as ListBox;
            var item = lb?.SelectedItem as NavItem;
            if (item == null) return;

            _vm.SetCurrentDirectory(item.FullPath);
            lb.SelectedItem = null; // permet de re-cliquer le même item
        }

        // ------------------------------------------------------------------ //
        //  TreeView (navigation arborescente)
        // ------------------------------------------------------------------ //

        private void TreeView_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            var node = e.NewValue as FileSystemNodeViewModel;
            if (node == null) return;

            if (!node.IsFile)
                _vm.SetCurrentDirectory(node.FullPath);
        }

        // ------------------------------------------------------------------ //
        //  ListView (contenu du répertoire courant)
        // ------------------------------------------------------------------ //

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0) return;
            var node = e.AddedItems[0] as FileSystemNodeViewModel;
            if (node == null) return;

            if (node.IsFile)
                _vm.SelectedPath = node.FullPath;
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
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

            if (string.IsNullOrWhiteSpace(_vm.SelectedPath) || !File.Exists(_vm.SelectedPath))
            {
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
        //  Tri par clic sur l'en-tête de colonne
        // ------------------------------------------------------------------ //

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = sender as GridViewColumnHeader;
            if (header == null) return;

            var property = header.Tag as string;
            if (string.IsNullOrEmpty(property)) return;

            var direction = ListSortDirection.Ascending;
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
