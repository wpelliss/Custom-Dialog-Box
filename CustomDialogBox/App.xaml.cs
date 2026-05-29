using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CustomDialogBox
{
    /// <summary>
    /// Point d'entree de l'application.
    /// Installe les handlers d'exceptions globales pour eviter les crashes silencieux
    /// en production (exceptions non observees sur tasks background, exceptions Dispatcher).
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Exceptions non gerees sur le thread UI (Dispatcher).
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Exceptions non observees sur les Tasks background (ThreadPool).
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Exceptions non gerees sur les threads non-UI non-Task.
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        }

        private static void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[App] Exception Dispatcher non geree : {e.Exception}");
            // En production on pourrait loguer vers un fichier.
            // On ne marque pas e.Handled = true : on laisse l'application se fermer
            // proprement pour ne pas masquer un etat corrompu.
        }

        private static void OnUnobservedTaskException(
            object sender,
            UnobservedTaskExceptionEventArgs e)
        {
            // Marquer comme observe pour empecher le process de se terminer
            // sur un crash silencieux issu d'un fire-and-forget.
            e.SetObserved();
            Debug.WriteLine($"[App] Task exception non observee : {e.Exception}");
        }

        private static void OnDomainUnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"[App] Exception domaine non geree (isTerminating={e.IsTerminating}) : {e.ExceptionObject}");
        }
    }
}
