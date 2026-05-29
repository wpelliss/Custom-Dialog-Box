using System;
using System.Windows.Input;

namespace CustomDialogBox
{
    /// <summary>
    /// Implementation generique de ICommand basee sur des delegates.
    /// Permet d'exposer des commandes dans un ViewModel sans reference a la vue,
    /// ce qui facilite les tests unitaires et respecte le pattern MVVM.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T>    _execute;
        private readonly Func<T, bool> _canExecute;

        /// <param name="execute">Logique executee par la commande.</param>
        /// <param name="canExecute">Predicat de disponibilite (null = toujours disponible).</param>
        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <inheritdoc/>
        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;
            return parameter is T t ? _canExecute(t) : false;
        }

        /// <inheritdoc/>
        public void Execute(object parameter)
        {
            if (parameter is T t)
                _execute(t);
        }

        /// <summary>
        /// Declencher manuellement si le contexte de CanExecute a change.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        public event EventHandler CanExecuteChanged;
    }
}
