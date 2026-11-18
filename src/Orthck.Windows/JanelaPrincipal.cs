using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Orthck.Core;

namespace Orthck.Windows;

public enum Secao { Gerar, Prontuario, Fila, Pacientes, Locais, Modelos, Historico, Cid, Sigtap }

/// <summary>Janela principal: barra lateral com as seções (como o NavigationSplitView do Mac) e a tela escolhida.</summary>
public sealed class JanelaPrincipal : Window
{
    public Armazenamento Arm { get; }
    public Word Word { get; }
    private readonly ListBox _navegacao = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, ItemContainerStyle = Ui.ItemDeLista() };
    private readonly ContentControl _tela = new();
    private readonly StackPanel _rodapeLateral = Ui.Coluna(14);
    private TextBlock? _contagemFila;
    private DateTime _ultimaSincronizacao = DateTime.MinValue;
    public Secao SecaoAtual { get; private set; } = Secao.Gerar;
    /// <summary>A tela de Gerar é mantida entre as trocas de seção, para não perder o que foi digitado.</summary>
    private GerarView? _gerar;
    private ProntuarioView? _prontuario;
    /// <summary>Paciente que está sendo atendido agora, compartilhado entre Gerar documento e Prontuário (para não digitar
    /// o nome de novo ao trocar de aba). Só na memória.</summary>
    public string NomeEmAtendimento { get; set; } = "";
    public Guid? PacienteEmAtendimento { get; set; }
    private bool _fechandoDepoisDoDrive;

    public static readonly (Secao Secao, string Titulo, string Glifo)[] Secoes =
    [
        (Secao.Gerar, "Gerar documento", Ui.GDocumentoNovo),
        (Secao.Prontuario, "Prontuário", Ui.GProntuario),
        (Secao.Fila, "Fila de impressão", Ui.GImpressora),
        (Secao.Pacientes, "Pacientes", Ui.GPessoas),
        (Secao.Locais, "Locais de atendimento", Ui.GPredio),
        (Secao.Modelos, "Modelos", Ui.GDocumento),
        (Secao.Historico, "Histórico", Ui.GRelogio),
        (Secao.Cid, "CID-10", Ui.GEstetoscopio),
        (Secao.Sigtap, "SIGTAP (SUS)", Ui.GCruz),
    ];

    public JanelaPrincipal(Armazenamento armazenamento, Word word)
    {
        Arm = armazenamento;
        Word = word;
        Title = "Orthck";
        Width = 1240; Height = 820; MinWidth = 980; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        SetResourceReference(FontSizeProperty, Tema.TamanhoBase);
        SetResourceReference(BackgroundProperty, Tema.Fundo);
        try { Icon = new BitmapImage(new Uri("pack://application:,,,/Recursos/Orthck.png")); } catch { }

        var grade = new Grid();
        var colunaLateral = new ColumnDefinition();
        colunaLateral.SetResourceReference(ColumnDefinition.WidthProperty, Tema.LarguraLateral);
        grade.ColumnDefinitions.Add(colunaLateral);
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lateral = MontarLateral();
        grade.Children.Add(lateral);
        Grid.SetColumn(_tela, 1);
        grade.Children.Add(_tela);
        Content = grade;

        Arm.Mudou += () => Dispatcher.InvokeAsync(AtualizarLateral);
        Arm.DriveMudou += () => Dispatcher.InvokeAsync(AtualizarLateral);
        // Ao abrir o app e sempre que ele volta para a frente: antes junta os dados da cópia no Google Drive (se mudou em
        // outro computador) e depois acompanha a pasta de modelos (como no Mac).
        Activated += (_, _) => Ui.ExecutarAsync(async () =>
        {
            await Arm.SincronizarDriveAoVoltarAsync();
            await Sincronizar();
        });
        Closing += (_, e) =>
        {
            if (_fechandoDepoisDoDrive) return;
            if (Arm.ProcessandoFila && !Ui.Confirmar("A fila ainda está enviando documentos ao Word.", "Fechar agora pode deixar a impressão incompleta. Fechar mesmo assim?", true))
            {
                e.Cancel = true;
                return;
            }
            // Antes de fechar, envia ao Google Drive as mudanças que ainda esperavam o envio (no máximo 15 segundos;
            // sem internet, fecha assim mesmo).
            if (Arm.EnvioPendente && Arm.GoogleConectado)
            {
                e.Cancel = true;
                _fechandoDepoisDoDrive = true;
                Title = "Orthck — enviando ao Google Drive…";
                Ui.ExecutarAsync(async () =>
                {
                    try { await Task.WhenAny(Arm.EnviarPendenteAoDriveAsync(), Task.Delay(TimeSpan.FromSeconds(15))); }
                    finally { Close(); }
                });
            }
        };
        PreviewKeyDown += AoTeclar;
        // Documentos impressos ficam 2 minutos na fila como "Enviado" e depois saem sozinhos (como no Mac).
        var relogio = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        relogio.Tick += (_, _) =>
        {
            try { if (Arm.LimparEnviadosAntigos() && SecaoAtual == Secao.Fila) Mostrar(Secao.Fila); }
            catch (Exception ex) { Registro.Erro("limpar enviados da fila", ex); }
        };
        relogio.Start();
        // Com o Google Drive conectado, confere a cópia a cada 30 segundos (traz o que mudou no outro computador e envia
        // o que mudou aqui).
        var nuvem = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        nuvem.Tick += (_, _) => { if (Arm.GoogleConectado && !Capturas.Ativo) Ui.ExecutarAsync(Arm.SincronizarDriveAsync); };
        nuvem.Start();
        Mostrar(Secao.Gerar);
        AtualizarLateral();
    }

    private Border MontarLateral()
    {
        var doca = new DockPanel();
        // Cabeçalho com a logo.
        var logo = new Image { Width = 40, Height = 40, Margin = new Thickness(0, 0, 11, 0) };
        try { logo.Source = new BitmapImage(new Uri("pack://application:,,,/Recursos/Orthck.png")); } catch { }
        var nomes = Ui.Coluna(0);
        nomes.Children.Add(Ui.Texto("Orthck", Estilo.Titulo));
        nomes.Children.Add(Ui.Texto("Documentos clínicos", Estilo.Legenda));
        nomes.VerticalAlignment = VerticalAlignment.Center;
        var cabecalho = new Border { Padding = new Thickness(16, 14, 16, 14), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Linha(0, logo, nomes) };
        cabecalho.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        DockPanel.SetDock(cabecalho, Dock.Top);
        doca.Children.Add(cabecalho);

        // O rodapé (backup, impressora, ajustes) fica embaixo; numa janela baixa, ele rola em vez de esconder as seções.
        var rodape = new Border { Padding = new Thickness(14), BorderThickness = new Thickness(0, 1, 0, 0), Child = _rodapeLateral, VerticalAlignment = VerticalAlignment.Bottom };
        rodape.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        var rolagemDoRodape = new ScrollViewer { Content = rodape, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        foreach (var (secao, titulo, glifo) in Secoes)
        {
            var linha = new DockPanel { Margin = new Thickness(2, 3, 2, 3) };
            var icone = Ui.Icone(glifo, 16);
            icone.Width = 22; icone.Margin = new Thickness(0, 0, 10, 0);
            linha.Children.Add(icone);
            if (secao == Secao.Fila)
            {
                _contagemFila = Ui.Texto("", Estilo.Legenda, Tema.TextoNoDestaque);
                _contagemFila.FontWeight = FontWeights.SemiBold;
                var bolha = new Border { Child = _contagemFila, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                bolha.SetResourceReference(Border.BackgroundProperty, Tema.Destaque);
                bolha.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Text") { Source = _contagemFila, Converter = new VisivelSeTexto() });
                DockPanel.SetDock(bolha, Dock.Right);
                linha.Children.Add(bolha);
            }
            linha.Children.Add(Ui.Texto(titulo));
            _navegacao.Items.Add(new ListBoxItem { Content = linha, Tag = secao, Padding = new Thickness(10, 7, 10, 7), HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        _navegacao.Margin = new Thickness(8, 10, 8, 10);
        _navegacao.SelectionChanged += (_, _) => { if (_navegacao.SelectedItem is ListBoxItem { Tag: Secao s } && s != SecaoAtual) Mostrar(s); };
        ScrollViewer.SetVerticalScrollBarVisibility(_navegacao, ScrollBarVisibility.Disabled);
        var corpo = new Grid();
        corpo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        corpo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        corpo.Children.Add(_navegacao);
        Grid.SetRow(rolagemDoRodape, 1);
        corpo.Children.Add(rolagemDoRodape);
        doca.Children.Add(corpo);

        var borda = new Border { Child = doca, BorderThickness = new Thickness(0, 0, 1, 0) };
        borda.SetResourceReference(Border.BackgroundProperty, Tema.Lateral);
        borda.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        return borda;
    }

    private sealed class VisivelSeTexto : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type t, object p, System.Globalization.CultureInfo c) => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    public void Mostrar(Secao secao)
    {
        SecaoAtual = secao;
        var item = _navegacao.Items.OfType<ListBoxItem>().First(i => (Secao)i.Tag == secao);
        if (!item.IsSelected) item.IsSelected = true;
        _tela.Content = secao switch
        {
            Secao.Gerar => _gerar ??= new GerarView(this),
            Secao.Prontuario => _prontuario ??= new ProntuarioView(this),
            Secao.Fila => new FilaView(this),
            Secao.Pacientes => new PacientesView(this),
            Secao.Locais => new LocaisView(this),
            Secao.Modelos => new ModelosView(this),
            Secao.Historico => new HistoricoView(this),
            Secao.Cid => new TabelaCodigosView(TabelaDeCodigos.Cid10, "cid"),
            _ => new TabelaCodigosView(TabelaDeCodigos.Sigtap, "sigtap"),
        };
        if (secao == Secao.Gerar) _gerar!.AoAparecer();
        if (secao == Secao.Prontuario) _prontuario!.AoAparecer();
    }

    /// <summary>Recria a tela atual (depois de importar dados ou mudar o tamanho do texto).</summary>
    public void Recarregar()
    {
        _gerar = null;
        _prontuario = null;
        Mostrar(SecaoAtual);
        AtualizarLateral();
    }

    /// <summary>Contagem da fila, pasta de modelos e impressora no rodapé da barra lateral.</summary>
    public void AtualizarLateral()
    {
        if (_contagemFila != null) _contagemFila.Text = Arm.PendentesNaFila > 0 ? Arm.PendentesNaFila.ToString() : "";
        // Só refaz o rodapé quando algo dele mudou (não a cada tecla digitada num cadastro).
        var chave = $"{Arm.Dados.PastaSincronizada.Length > 0}|{_compactando}|{Ajustes.Atual.Impressora}|{_impressoras?.Count}|{Arm.GoogleConectado}|{Arm.SincronizandoDrive}|{Arm.EnvioPendente}|{Arm.SituacaoNuvem}|{Arm.EnviandoPastaAoDrive}";
        if (chave == _chaveDoRodape) return;
        _chaveDoRodape = chave;
        _rodapeLateral.Children.Clear();
        _rodapeLateral.Add(SecaoBackup());
        var impressora = Ui.Coluna(6);
        impressora.Add(Ui.TituloSecao("Impressora", Ui.GImpressora));
        _rodapeLateral.Add(impressora);
        impressora.Add(SeletorDeImpressora());
        var procurar = Ui.Botao("Impressoras do Windows…", Impressoras.AbrirConfiguracoes, glifo: Ui.GBusca, dica: "Abre as configurações de impressoras do Windows para adicionar uma impressora ou escolher a padrão");
        procurar.HorizontalAlignment = HorizontalAlignment.Stretch;
        impressora.Add(procurar);
        var parar = Ui.Botao("Parar todas as impressões", PararImpressoes, glifo: Ui.GParar,
            dica: "A fila não envia mais nada e os documentos que já estão na impressora são cancelados");
        parar.HorizontalAlignment = HorizontalAlignment.Stretch;
        impressora.Add(parar);
        var ajustes = Ui.Botao("Ajustes…", AbrirAjustes, glifo: Ui.GEngrenagem, dica: "Aparência, dados e backup (Ctrl+,)");
        ajustes.HorizontalAlignment = HorizontalAlignment.Stretch;
        _rodapeLateral.Add(ajustes);
    }

    /// <summary>Backup (como no Mac): compactar a pasta (com os dados), importar um arquivo de dados .json, enviar a pasta
    /// ao Google Drive e a cópia automática dos dados no Google Drive.</summary>
    private FrameworkElement SecaoBackup()
    {
        var backup = Ui.Coluna(6);
        backup.Add(Ui.TituloSecao("Backup", Ui.GZip));
        Button Largo(Button b) { b.HorizontalAlignment = HorizontalAlignment.Stretch; return b; }
        var temPasta = Arm.Dados.PastaSincronizada.Length > 0;
        if (temPasta)
        {
            var compactar = Largo(Ui.Botao(_compactando ? "Compactando…" : "Compactar (.zip)…", () => Ui.ExecutarAsync(() => Compactar()), glifo: Ui.GZip,
                dica: "Cria um .zip com todos os arquivos da pasta sincronizada e os dados do app"));
            compactar.IsEnabled = !_compactando;
            backup.Add(compactar);
        }
        backup.Add(Largo(Ui.Botao("Importar dados (.json)…", () => ImportarDados(), glifo: Ui.GImportar,
            dica: "Traz pacientes, locais, histórico e fila de um backup (dados.json ou “Orthck - dados (backup).json”)")));
        if (temPasta)
        {
            var enviar = Largo(Ui.Botao(Arm.EnviandoPastaAoDrive ? "Enviando…" : "Enviar pasta ao Drive", () => Ui.ExecutarAsync(EnviarPastaAoDrive), glifo: Ui.GEnviar,
                dica: "Manda ao Google Drive um .zip com a pasta sincronizada inteira e os dados do app (substitui o anterior)"));
            enviar.IsEnabled = !Arm.EnviandoPastaAoDrive;
            backup.Add(enviar);
            if (Ajustes.Atual.UltimoEnvioPastaDrive is { } quando)
                backup.Add(Ui.Texto($"Pasta enviada em {quando.LocalDateTime:dd/MM/yyyy HH:mm}", Estilo.Legenda, Tema.TextoSecundario));
        }
        backup.Add(CopiaNaNuvem(), 4);
        return backup;
    }

    /// <summary>Cópia automática dos dados no Google Drive: conectar, ou a situação e o menu (sincronizar, abrir, desconectar).</summary>
    private FrameworkElement CopiaNaNuvem()
    {
        var p = Ui.Coluna(2);
        if (!Arm.GoogleConectado)
        {
            var conectar = Ui.Botao("Conectar ao Google Drive…", () => ConectarGoogle(), glifo: Ui.GNuvem,
                dica: "Guarda os dados no seu Google Drive e, ao abrir o app, traz o que mudou em outro computador");
            conectar.HorizontalAlignment = HorizontalAlignment.Stretch;
            p.Add(conectar);
        }
        else
        {
            var menu = new ContextMenu();
            void Opcao(string titulo, Action acao)
            {
                var item = new MenuItem { Header = titulo };
                item.Click += (_, _) => Ui.Executar(acao);
                menu.Items.Add(item);
            }
            Opcao("Sincronizar agora", () => Ui.ExecutarAsync(Arm.SincronizarDriveAsync));
            if (Arm.EnderecoNoDrive is { } endereco) Opcao("Abrir no navegador", () => Ui.AbrirEndereco(endereco));
            menu.Items.Add(new Separator());
            Opcao("Desconectar", () =>
            {
                if (Ui.Confirmar("Desconectar do Google Drive?", "Os dados deixam de ir para o Drive. Nada é apagado aqui nem no Drive.")) Arm.DesconectarGoogle();
            });
            var mais = Ui.BotaoIcone(Ui.GMaisOpcoes, "Opções do Google Drive", () => { });
            mais.Click += (_, _) => { menu.PlacementTarget = mais; menu.IsOpen = true; };
            var titulo = Ui.Linha(6, Ui.Icone(Arm.SincronizandoDrive ? Ui.GSincronizar : Ui.GNuvem, 14), Ui.Texto("Google Drive"));
            p.Add(Ui.Extremos(titulo, mais));
            if (Arm.SituacaoNuvem.Length > 0) p.Add(Quebra(Ui.Texto(Arm.SituacaoNuvem, Estilo.Legenda, Tema.TextoSecundario)));
            if (Arm.EnvioPendente) p.Add(Quebra(Ui.Texto("Mudanças vão para o Drive em até 30 segundos (ou ao fechar o app).", Estilo.Legenda, Tema.TextoSecundario)));
        }
        if (!Arm.GoogleConectado && Arm.SituacaoNuvem.Length > 0) p.Add(Quebra(Ui.Texto(Arm.SituacaoNuvem, Estilo.Legenda, Tema.TextoSecundario)));
        return p;
    }

    private static TextBlock Quebra(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; return t; }

    /// <summary>Abre a janela de conexão; devolve true se ficou conectado.</summary>
    public bool ConectarGoogle()
    {
        new ConectarGoogleJanela(Arm) { Owner = this }.ShowDialog();
        AtualizarLateral();
        return Arm.GoogleConectado;
    }

    private async Task EnviarPastaAoDrive()
    {
        if (!Arm.GoogleConectado && !ConectarGoogle()) return;
        try
        {
            var bytes = await Arm.EnviarPastaAoGoogleDriveAsync();
            Ajustes.Atual.UltimoEnvioPastaDrive = DateTimeOffset.Now;
            Ajustes.Salvar();
            _chaveDoRodape = null;
            AtualizarLateral();
            Ui.Informar($"Pasta enviada ao seu Google Drive como “{Armazenamento.NomeDoZipNoDrive}” ({bytes / 1024.0 / 1024.0:0.0} MB), com o backup dos dados. O envio anterior foi substituído.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Ui.Erro(ex.Message); }
    }

    private List<string>? _impressoras;
    private string? _chaveDoRodape;
    private FrameworkElement SeletorDeImpressora()
    {
        _impressoras ??= Impressoras.Instaladas();
        var escolhida = Ajustes.Atual.Impressora;
        // Mantém na lista a impressora escolhida mesmo se ela estiver desligada ou removida.
        var opcoes = new List<(string, string)> { ("Padrão do Windows", "") };
        opcoes.AddRange(_impressoras.Select(n => (n, n)));
        if (escolhida.Length > 0 && !_impressoras.Contains(escolhida)) opcoes.Add((escolhida + " (indisponível)", escolhida));
        var combo = Ui.Escolha(opcoes, escolhida, valor =>
        {
            Ajustes.Atual.Impressora = valor;
            Ajustes.Salvar();
            if (SecaoAtual == Secao.Fila) Mostrar(Secao.Fila);
        }, 120);
        var atualizar = Ui.BotaoIcone(Ui.GAtualizar, "Atualizar a lista de impressoras", () => { _impressoras = Impressoras.Instaladas(); _chaveDoRodape = null; AtualizarLateral(); });
        var linha = new DockPanel();
        DockPanel.SetDock(atualizar, Dock.Right);
        linha.Children.Add(atualizar);
        combo.Margin = new Thickness(0, 0, 4, 0);
        linha.Children.Add(combo);
        return linha;
    }

    /// <summary>Para a fila (o documento que já está no Word termina) e cancela o que está no spooler do Windows.</summary>
    public void PararImpressoes()
    {
        if (!Ui.Confirmar("Parar todas as impressões?", $"A fila não envia o próximo documento e os trabalhos que estão em {NomeDaImpressora} são cancelados. Os documentos continuam salvos e podem ser enviados de novo pela fila.", true))
            return;
        Arm.PararFila();
        var (cancelados, falharam) = Impressoras.CancelarTudo(ImpressoraEscolhida);
        var texto = cancelados == 0 ? "Nenhum trabalho estava aguardando na impressora." : $"{Texto.Plural(cancelados, "trabalho cancelado", "trabalhos cancelados")} na impressora.";
        if (falharam > 0) texto += $"\n\n{falharam} não puderam ser cancelados pelo app (pode exigir administrador). Abra a impressora nas configurações do Windows e cancele por lá.";
        if (Arm.ProcessandoFila) texto += "\n\nA fila para depois do documento que está sendo enviado agora.";
        Ui.Informar(texto);
    }

    public static string NomeDaImpressora => Ajustes.Atual.Impressora.Length == 0 ? "impressora padrão do Windows" : Ajustes.Atual.Impressora;
    public static string? ImpressoraEscolhida => Ajustes.Atual.Impressora.Length == 0 ? null : Ajustes.Atual.Impressora;

    private bool _compactando;
    private async Task Compactar()
    {
        var pasta = Arm.Dados.PastaSincronizada;
        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Onde salvar o .zip com todos os arquivos da pasta sincronizada?",
            FileName = $"{Path.GetFileName(pasta.TrimEnd('\\'))} {DateTime.Now:dd-MM-yyyy}.zip",
            Filter = "Arquivo compactado (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialogo.ShowDialog(this) != true) return;
        _compactando = true; AtualizarLateral();
        try
        {
            await Arm.CompactarPastaSincronizadaAsync(dialogo.FileName);
            Ui.MostrarNaPasta(dialogo.FileName);
        }
        catch (Exception ex) { Ui.Erro($"Não foi possível criar o .zip. {ex.Message}"); }
        finally { _compactando = false; AtualizarLateral(); }
    }

    /// <summary>Sincroniza a pasta de modelos; no máximo uma vez a cada poucos segundos ao alternar janelas.</summary>
    public async Task Sincronizar(bool forcar = false)
    {
        if (Arm.Dados.PastaSincronizada.Length == 0 || Arm.Sincronizacao.EmAndamento) return;
        if (!forcar && DateTime.Now - _ultimaSincronizacao < TimeSpan.FromSeconds(5)) return;
        _ultimaSincronizacao = DateTime.Now;
        try { await Arm.SincronizarAsync(() => SincronizacaoMudou?.Invoke()); }
        catch (Exception ex)
        {
            Registro.Erro("sincronizar", ex);
            // Automática (ao voltar para o app): sem janela de erro, que reativaria o app e repetiria a sincronização.
            if (forcar) Ui.Erro($"Erro ao sincronizar a pasta de modelos: {ex.Message}");
        }
        SincronizacaoMudou?.Invoke();
        // Só refaz a tela Gerar se algum modelo mudou: assim o cursor não sai da caixa em que se está digitando.
        var estado = Arm.Sincronizacao;
        if (SecaoAtual == Secao.Gerar && (estado.Novos + estado.Atualizados + estado.Removidos) > 0) _gerar?.AoAparecer();
    }

    /// <summary>A tela Modelos acompanha o andamento da sincronização.</summary>
    public event Action? SincronizacaoMudou;

    /// <summary>
    /// Importa um dados.json (do Mac, do .zip "Orthck - dados (backup).json" ou de um backup). Os dados atuais vão antes
    /// para Backups. Se houver a pasta Modelos ao lado do arquivo, as cópias dos modelos vêm junto.
    /// </summary>
    public bool ImportarDados()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o arquivo de dados do Orthck (dados.json, um backup da pasta Backups ou o “Orthck - dados (backup).json” do .zip)",
            Filter = "Dados do Orthck (*.json)|*.json|Todos os arquivos|*.*",
            InitialDirectory = Arm.PastaBackups
        };
        if (dialogo.ShowDialog(this) != true) return false;
        var modo = new ImportarDadosJanela(Path.GetFileName(dialogo.FileName)) { Owner = this }.Perguntar();
        if (modo == null) return false;
        try
        {
            var resumo = Arm.ImportarDados(dialogo.FileName, modo.Value);
            Recarregar();
            Ui.Informar(resumo);
            return true;
        }
        catch (Exception ex) { Ui.Erro(ex.Message); return false; }
    }

    public void AbrirAjustes()
    {
        new AjustesJanela(this) { Owner = this }.ShowDialog();
    }

    private void AoTeclar(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) return;
        if (!shift && e.Key == Key.B && Keyboard.FocusedElement is TextBox { IsReadOnly: false } campo)
        {
            // Negrito (⌘B no Mac): o trecho fica entre ** e sai em negrito no Word.
            AlternarNegrito(campo);
            e.Handled = true;
        }
        else if (shift && e.Key == Key.N)
        {
            // Próximo paciente (⇧⌘N no Mac): no Prontuário, o do Prontuário; nas outras telas, o do Gerar documento.
            if (SecaoAtual == Secao.Prontuario) _prontuario?.ProximoPaciente();
            else
            {
                Mostrar(Secao.Gerar);
                _gerar?.ProximoPaciente();
            }
            e.Handled = true;
        }
        else if (e.Key is Key.OemPlus or Key.Add) { MudarTamanho(+1); e.Handled = true; }
        else if (e.Key is Key.OemMinus or Key.Subtract) { MudarTamanho(-1); e.Handled = true; }
        else if (e.Key is Key.D0 or Key.NumPad0) { MudarTamanho(0, absoluto: true); e.Handled = true; }
        else if (e.Key == Key.OemComma) { AbrirAjustes(); e.Handled = true; }
    }

    /// <summary>Põe ou tira "**" em volta do trecho selecionado (sem seleção, abre "****" com o cursor no meio).</summary>
    public static void AlternarNegrito(TextBox campo)
    {
        var (texto, inicio, tamanho) = Negrito.Alternar(campo.Text, campo.SelectionStart, campo.SelectionLength);
        campo.Text = texto;
        campo.Select(inicio, tamanho);
        campo.Focus();
    }

    public void MudarTamanho(int passo, bool absoluto = false)
    {
        var novo = absoluto ? passo : Math.Clamp((int)Ajustes.Atual.TamanhoTexto + passo, 0, 2);
        Ajustes.Atual.TamanhoTexto = (TamanhoTexto)novo;
        Ajustes.Salvar();
        Tema.Aplicar(Application.Current, Ajustes.Atual.TamanhoTexto, Ajustes.Atual.Aparencia);
    }
}
