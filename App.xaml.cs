using System.Windows;
using System.Threading.Tasks;
using System.Windows.Threading;
using Sreapeat.Services;

namespace Sreapeat;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;
    }

    private void Application_OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("Unhandled dispatcher exception.", e.Exception);
        MessageBox.Show(
            "An unexpected error occurred. Details were written to the local error log.",
            "sreapeat error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(-1);
    }

    private static void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLogger.Error("Unhandled AppDomain exception.", exception);
            return;
        }

        AppLogger.Error("Unhandled AppDomain exception with non-Exception payload.");
    }

    private static void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved task exception.", e.Exception);
        e.SetObserved();
    }
}
