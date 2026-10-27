using System.Windows;
using Orthck.Core;

namespace Orthck.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var storage = new Storage();
            var queue = new PrintQueue(storage, new OfficeWordPrinter());
            new MainWindow(storage, queue).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar os dados do Orthck. Nenhum cadastro foi sobrescrito.\n\n{ex.Message}",
                "Orthck — Dados protegidos", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
