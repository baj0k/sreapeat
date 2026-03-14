using System.Windows;
using System.Windows.Threading;

namespace Sreapeat;

public partial class App : Application
{
    private void Application_OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.ToString(),
            "sreapeat startup error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(-1);
    }
}
