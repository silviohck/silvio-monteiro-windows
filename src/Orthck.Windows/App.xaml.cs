using System.Windows;
using Orthck.Core;

namespace Orthck.Windows;

public partial class App : Application
{
    // Duas janelas do Orthck gravariam o mesmo dados.json e poderiam imprimir a mesma fila duas vezes.
    private static Mutex? _umaInstancia;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Um erro inesperado numa tela não pode fechar o app no meio de um atendimento.
        // Erros fora da tela (tarefas em segundo plano) vão para o registro em vez de sumir ou fechar o app.
        TaskScheduler.UnobservedTaskException += (_, args) => { Registro.Erro("tarefa", args.Exception); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) Registro.Erro("fatal", ex); };
        DispatcherUnhandledException += (_, args) =>
        {
            Registro.Erro("tela", args.Exception);
            if (Capturas.Ativo) return; // no CI, o modo de capturas registra o erro e segue
            MessageBox.Show($"Ocorreu um erro inesperado. Os dados já salvos foram preservados.\n\n{args.Exception.Message}",
                "Orthck", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Modo usado pelo CI: gera capturas de tela de todas as telas com dados de demonstração e fecha.
        var indice = Array.IndexOf(e.Args, "--capturar");
        if (indice >= 0 && indice + 1 < e.Args.Length)
        {
            Capturas.Executar(this, e.Args[indice + 1]);
            return;
        }

        _umaInstancia = new Mutex(true, @"Local\Orthck.Windows.Instancia", out var primeira);
        if (!primeira)
        {
            MessageBox.Show("O Orthck já está aberto. Use a janela que já existe (veja a barra de tarefas).", "Orthck", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        Ajustes.Carregar();
        Tema.Aplicar(this, Ajustes.Atual.TamanhoTexto, Ajustes.Atual.Aparencia);
        var armazenamento = AbrirDados();
        if (armazenamento == null) { Shutdown(1); return; }
        var janela = new JanelaPrincipal(armazenamento, new Word());
        MainWindow = janela;
        janela.Show();
    }

    /// <summary>Abre os dados; se o dados.json estiver ilegível, oferece restaurar o backup mais recente.</summary>
    private static Armazenamento? AbrirDados()
    {
        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orthck");
        while (true)
        {
            try
            {
                return new Armazenamento(pasta) { Conversor = new Word(), ResolverAtalho = Atalhos.Resolver };
            }
            catch (DadosIlegiveisException ex)
            {
                var backups = Directory.Exists(Path.Combine(pasta, "Backups"))
                    ? Directory.GetFiles(Path.Combine(pasta, "Backups"), "dados-*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToList() : [];
                if (backups.Count == 0)
                {
                    MessageBox.Show($"{ex.Message}\n\nNão há backup automático para restaurar. Nada foi apagado; a pasta de dados será aberta para você conferir.",
                        "Orthck — Dados protegidos", MessageBoxButton.OK, MessageBoxImage.Error);
                    Ui.AbrirPasta(pasta);
                    return null;
                }
                var resposta = MessageBox.Show(
                    $"{ex.Message}\n\nRestaurar o backup automático mais recente ({Path.GetFileNameWithoutExtension(backups[0]).Replace("dados-", "")})? " +
                    "O arquivo atual continua guardado na pasta de dados.",
                    "Orthck — Dados protegidos", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (resposta != MessageBoxResult.Yes) { Ui.AbrirPasta(pasta); return null; }
                try { Armazenamento.RestaurarBackup(pasta, backups[0]); }
                catch (Exception erro)
                {
                    MessageBox.Show($"Não foi possível restaurar o backup: {erro.Message}", "Orthck", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir os dados do Orthck. Nada foi apagado.\n\n{ex.Message}", "Orthck", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
    }
}
