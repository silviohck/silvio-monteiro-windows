using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>"Importar dados (.json)": juntar com os dados atuais ou substituir todos (como no Mac).</summary>
public sealed class ImportarDadosJanela : Dialogo
{
    private Armazenamento.ModoDeImportacao? _modo;

    public ImportarDadosJanela(string arquivo) : base("Importar dados", 560, 330)
    {
        var p = Ui.Coluna(12);
        var titulo = Ui.Texto($"Importar “{arquivo}”?", Estilo.Secao);
        titulo.TextWrapping = TextWrapping.Wrap;
        p.Add(titulo);
        p.Add(Ui.Texto("Juntar: entram os pacientes, locais e documentos que ainda não estão aqui. Substituir: os dados atuais são trocados pelos do arquivo (restaurar um backup ou trazer do Mac). Antes, uma cópia dos dados atuais é guardada em Backups."));
        Corpo = Ui.Pagina(p, 520);
        AdicionarBotao("Cancelar", Close);
        var substituir = AdicionarBotao("Substituir todos os dados", () => { _modo = Armazenamento.ModoDeImportacao.Substituir; Close(); });
        substituir.SetResourceReference(ForegroundProperty, Tema.Aviso);
        AdicionarBotao("Juntar com os dados atuais", () => { _modo = Armazenamento.ModoDeImportacao.Juntar; Close(); }, principal: true);
    }

    public Armazenamento.ModoDeImportacao? Perguntar()
    {
        ShowDialog();
        return _modo;
    }
}

/// <summary>
/// Conectar ao Google Drive (ConectarGoogleView do Mac): o navegador abre a página do Google para entrar na conta e
/// permitir. A credencial do app vem embutida (Secrets do GitHub) ou do arquivo client_secret….json.
/// </summary>
public sealed class ConectarGoogleJanela : Dialogo
{
    private readonly Armazenamento _arm;
    private readonly TextBox _id = Ui.CaixaDeTexto(dica: "ID do cliente");
    private readonly PasswordBox _segredo = new() { Padding = new Thickness(8, 5, 8, 5), MinHeight = 32 };
    private readonly ContentControl _situacao = new();
    private readonly TextBox _endereco = Ui.CaixaDeTexto(dica: "http://127.0.0.1:…/?…code=…");
    private readonly Button _conectar;
    private bool _aguardando;

    /// <summary>Credencial colocada no app pela compilação do GitHub (Secrets GOOGLE_CLIENT_ID e GOOGLE_CLIENT_SECRET).</summary>
    public static (string ClientID, string ClientSecret)? CredencialEmbutida
    {
        get
        {
            var meta = typeof(ConectarGoogleJanela).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
            var id = meta.FirstOrDefault(m => m.Key == "GoogleClientID")?.Value ?? "";
            var segredo = meta.FirstOrDefault(m => m.Key == "GoogleClientSecret")?.Value ?? "";
            return id.Length > 0 && segredo.Length > 0 ? (id, segredo) : null;
        }
    }

    public ConectarGoogleJanela(Armazenamento arm) : base("Conectar ao Google Drive", 600, 560)
    {
        _arm = arm;
        var p = Ui.Coluna(12);
        p.Add(Ui.Texto("Ao clicar em Conectar, o navegador abre a página do Google: entre com o e-mail e a senha da sua conta e clique em Permitir. O app nunca vê a sua senha e só enxerga o arquivo que ele mesmo cria no seu Drive."));
        p.Add(Ui.Texto("Depois de conectado, os dados (pacientes, locais, histórico e favoritos) vão para o arquivo “Orthck - dados.json” no seu Drive e o que mudar em outro computador conectado à mesma conta é juntado aqui. A fila de impressão é de cada computador; apagar um paciente não se propaga.", Estilo.Apoio));

        _id.Text = arm.Drive.Conta.ClientID;
        _segredo.Password = arm.Drive.Conta.ClientSecret;
        var credencial = Ui.Coluna(8);
        credencial.Add(Ui.Texto("Arquivo client_secret….json baixado no Google Cloud Console, ou o ID do cliente e a chave secreta.", Estilo.Apoio));
        credencial.Add(Ui.Botao("Escolher arquivo da credencial (.json)…", EscolherArquivo, glifo: Ui.GAbrir));
        credencial.Add(Ui.Rotulado("ID do cliente", _id, 170));
        credencial.Add(Ui.Rotulado("Chave secreta", _segredo, 170));
        var expansor = new Expander { Header = "Credencial do Google", Content = credencial, IsExpanded = _id.Text.Length == 0 || _segredo.Password.Length == 0 };
        p.Add(expansor);
        p.Add(_situacao);
        Corpo = Ui.Pagina(p, 560);

        AdicionarBotao("Cancelar", () => { _arm.Drive.CancelarLogin(); Close(); });
        _conectar = AdicionarBotao("Conectar", () => Ui.ExecutarAsync(Conectar), principal: true);
        _id.TextChanged += (_, _) => AtualizarBotao();
        _segredo.PasswordChanged += (_, _) => AtualizarBotao();
        Closed += (_, _) => { if (_aguardando) _arm.Drive.CancelarLogin(); };
        AtualizarBotao();
    }

    private void AtualizarBotao() => _conectar.IsEnabled = !_aguardando && _id.Text.Trim().Length > 0 && _segredo.Password.Trim().Length > 0;

    private void EscolherArquivo()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o arquivo da credencial baixado no Google Cloud Console (client_secret….json)",
            Filter = "Credencial do Google (*.json)|*.json",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        if (dialogo.ShowDialog(this) != true) return;
        try
        {
            var (id, segredo) = GoogleDrive.LerCredencial(dialogo.FileName);
            _id.Text = id;
            _segredo.Password = segredo;
            Situacao(null);
        }
        catch (Exception ex) { Situacao(ex.Message, erro: true); }
    }

    private void Situacao(string? texto, bool erro = false, bool aguardando = false)
    {
        if (texto == null && !aguardando) { _situacao.Content = null; return; }
        var p = Ui.Coluna(8);
        if (aguardando)
        {
            p.Add(Ui.Linha(8, new ProgressBar { IsIndeterminate = true, Width = 60, Height = 4, VerticalAlignment = VerticalAlignment.Center },
                Ui.Texto("Aguardando a autorização no navegador…", Estilo.Normal, Tema.TextoSecundario)));
            p.Add(Ui.Texto("Se, depois de clicar em Permitir, o navegador mostrar um erro ou não voltar para o app, copie o endereço da barra do navegador (começa com http://127.0.0.1) e cole aqui:", Estilo.Apoio));
            var usar = Ui.Botao("Usar endereço", () =>
            {
                if (!_arm.Drive.EntregarEndereco(_endereco.Text))
                    Ui.Erro("Esse endereço não é a volta do login do Google. Copie o endereço inteiro da página que abriu depois de clicar em Permitir.");
            });
            usar.Margin = new Thickness(8, 0, 0, 0);
            var linha = new DockPanel();
            DockPanel.SetDock(usar, Dock.Right);
            linha.Children.Add(usar);
            if (_endereco.Parent is Panel antigo) antigo.Children.Remove(_endereco);
            linha.Children.Add(_endereco);
            p.Add(linha);
        }
        if (texto != null)
        {
            var t = Ui.Texto(texto, Estilo.Normal, erro ? Tema.Aviso : Tema.Sucesso);
            p.Add(Ui.Linha(8, Ui.Icone(erro ? Ui.GAviso : Ui.GOkCirculo, 14, erro ? Tema.Aviso : Tema.Sucesso), t));
        }
        _situacao.Content = p;
    }

    private async Task Conectar()
    {
        _aguardando = true;
        AtualizarBotao();
        Situacao(null, aguardando: true);
        try
        {
            await _arm.ConectarGoogleAsync(_id.Text, _segredo.Password, Ui.AbrirEndereco);
            _aguardando = false;
            Close();
        }
        catch (Exception ex) when (ex is ErroGoogle or HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            _aguardando = false;
            AtualizarBotao();
            Situacao(ex is HttpRequestException or TaskCanceledException ? "Sem conexão com o Google. Confira a internet e tente de novo." : ex.Message, erro: true);
        }
    }
}

/// <summary>
/// Restaurar uma cópia (RestaurarBackupView do Mac): a lista das cópias guardadas em Backups, com data, pacientes e
/// documentos; restaurar pede confirmação e os dados de agora viram uma cópia antes (dá para desfazer).
/// </summary>
public sealed class RestaurarCopiaJanela : Dialogo
{
    private readonly JanelaPrincipal _janela;

    public RestaurarCopiaJanela(JanelaPrincipal janela) : base("Restaurar uma cópia dos dados", 620, 600)
    {
        _janela = janela;
        Montar();
        var pasta = Ui.Botao("Abrir a pasta das cópias", () => Ui.AbrirPasta(_janela.Arm.PastaBackups), glifo: Ui.GPastaAberta);
        pasta.Margin = new Thickness(0, 0, 8, 0);
        var outro = Ui.Botao("Usar outro arquivo…", () => { if (_janela.ImportarDados()) Close(); }, glifo: Ui.GImportar,
            dica: "Um dados.json de outro computador ou do Mac");
        var esquerda = Ui.Linha(0, pasta, outro);
        Rodape.Children.Add(esquerda);
        AdicionarBotao("Fechar", Close);
    }

    private void Montar()
    {
        var p = Ui.Coluna(12);
        var explicacao = Ui.Texto("O app guarda sozinho uma cópia dos pacientes, locais e histórico por dia (dos últimos 30 dias). Restaurar volta os dados para como estavam na cópia escolhida; os dados de agora viram uma cópia antes, para poder desfazer. A fila de impressão e os arquivos Word não mudam.",
            Estilo.Normal, Tema.TextoSecundario);
        explicacao.TextWrapping = TextWrapping.Wrap;
        p.Add(explicacao);
        var copias = _janela.Arm.CopiasDosDados();
        if (copias.Count == 0) p.Add(Ui.Texto("Ainda não há cópias guardadas.", Estilo.Apoio));
        foreach (var copia in copias)
        {
            var info = Ui.Coluna(2);
            var data = Ui.Texto(copia.Data.ToString("dd 'de' MMMM 'de' yyyy, HH:mm", new System.Globalization.CultureInfo("pt-BR")));
            data.FontWeight = FontWeights.SemiBold;
            info.Add(data);
            info.Add(Ui.Texto($"{Texto.Plural(copia.Pacientes, "paciente", "pacientes")} · {Texto.Plural(copia.Documentos, "documento", "documentos")}"
                + (copia.AntesDeImportar ? " · feita antes de importar/restaurar" : ""), Estilo.Apoio));
            var restaurar = Ui.Botao("Restaurar…", () => Restaurar(copia));
            p.Add(Ui.Cartao(Ui.Extremos(info, restaurar), espaco: 12));
        }
        Corpo = Ui.Pagina(p, 580);
    }

    private void Restaurar(Armazenamento.CopiaDosDados copia)
    {
        var quando = copia.Data.ToString("dd/MM/yyyy HH:mm");
        if (!Ui.Confirmar($"Voltar os dados para a cópia de {quando}?",
            $"Ficam {Texto.Plural(copia.Pacientes, "paciente", "pacientes")} e {Texto.Plural(copia.Documentos, "documento", "documentos")} no histórico. " +
            "O que foi feito depois dessa cópia sai do app (os dados de agora são guardados antes, como uma nova cópia).", true)) return;
        try
        {
            var resumo = _janela.Arm.ImportarDados(copia.Arquivo, Armazenamento.ModoDeImportacao.Substituir);
            _janela.Recarregar();
            Ui.Informar(resumo);
            Montar();
        }
        catch (Exception ex) { Ui.Erro(ex.Message); }
    }
}
