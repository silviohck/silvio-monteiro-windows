using System.Windows;
using Orthck.Core;

namespace Orthck.Windows;

public partial class App : Application
{
    // Duas janelas do Orthck gravariam o mesmo dados.json (a última apagaria o que a outra salvou)
    // e poderiam imprimir a mesma fila duas vezes.
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\Orthck.Windows.Instancia", out var first);
        if (!first)
        {
            MessageBox.Show("O Orthck já está aberto. Use a janela existente (veja a barra de tarefas).", "Orthck",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }
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
