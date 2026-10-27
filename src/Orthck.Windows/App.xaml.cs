using System.Windows;
using Orthck.Core;

namespace Orthck.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Um erro inesperado numa tela não pode fechar o app no meio de um atendimento.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Ocorreu um erro inesperado. Os dados já salvos foram preservados.\n\n{args.Exception.Message}",
                "Orthck", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        try
        {
            var storage = new Storage();
            var queue = new PrintQueue(storage, new OfficeWordPrinter());
            new MainWindow(storage, queue).Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível carregar os dados do Orthck. Nenhum cadastro foi sobrescrito.\n\n{ex.Message}\n\n" +
                "A versão anterior fica em dados.backup.json (e cópias antes-importacao-*.json) na mesma pasta; " +
                "para restaurá-la, feche o Orthck, guarde o dados.json atual e renomeie o backup para dados.json.",
                "Orthck — Dados protegidos", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
