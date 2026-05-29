using System.Windows;

namespace CustomDialogBox
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Open { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                MessageBox.Show(
                    "Fichier selectionne:\n" + dialog.SelectedPath,
                    "Resultat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
