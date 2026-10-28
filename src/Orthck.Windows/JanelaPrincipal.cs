using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Orthck.Core;

namespace Orthck.Windows;

public enum Secao { Gerar, Fila, Pacientes, Locais, Modelos, Historico, Cid, Sigtap, Tuss }

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

    public static readonly (Secao Secao, string Titulo, string Glifo)[] Secoes =
    [
        (Secao.Gerar, "Gerar documento", Ui.GDocumentoNovo),
        (Secao.Fila, "Fila de impressão", Ui.GImpressora),
        (Secao.Pacientes, "Pacientes", Ui.GPessoas),
        (Secao.Locais, "Locais de atendimento", Ui.GPredio),
        (Secao.Modelos, "Modelos", Ui.GDocumento),
        (Secao.Historico, "Histórico", Ui.GRelogio),
        (Secao.Cid, "CID-10", Ui.GEstetoscopio),
        (Secao.Sigtap, "SIGTAP (SUS)", Ui.GCruz),
        (Secao.Tuss, "TUSS", Ui.GLista),
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
        // Ao abrir o app e sempre que ele volta para a frente, acompanha a pasta de modelos (como no Mac).
        Activated += (_, _) => _ = Sincronizar();
        Closing += (_, e) =>
        {
            if (Arm.ProcessandoFila && !Ui.Confirmar("A fila ainda está enviando documentos ao Word.", "Fechar agora pode deixar a impressão incompleta. Fechar mesmo assim?", true))
                e.Cancel = true;
        };
        PreviewKeyDown += AoTeclar;
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

        var rodape = new Border { Padding = new Thickness(14), BorderThickness = new Thickness(0, 1, 0, 0), Child = _rodapeLateral };
        rodape.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        DockPanel.SetDock(rodape, Dock.Bottom);
        doca.Children.Add(rodape);

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
        doca.Children.Add(_navegacao);

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
            Secao.Fila => new FilaView(this),
            Secao.Pacientes => new PacientesView(this),
            Secao.Locais => new LocaisView(this),
            Secao.Modelos => new ModelosView(this),
            Secao.Historico => new HistoricoView(this),
            Secao.Cid => new TabelaCodigosView(TabelaDeCodigos.Cid10, "cid"),
            Secao.Sigtap => new TabelaCodigosView(TabelaDeCodigos.Sigtap, "sigtap"),
            _ => new TabelaCodigosView(TabelaDeCodigos.Tuss, "tuss"),
        };
        if (secao == Secao.Gerar) _gerar!.AoAparecer();
    }

    /// <summary>Recria a tela atual (depois de importar dados ou mudar o tamanho do texto).</summary>
    public void Recarregar()
    {
        _gerar = null;
        Mostrar(SecaoAtual);
        AtualizarLateral();
    }

    /// <summary>Contagem da fila, pasta de modelos e impressora no rodapé da barra lateral.</summary>
    public void AtualizarLateral()
    {
        if (_contagemFila != null) _contagemFila.Text = Arm.PendentesNaFila > 0 ? Arm.PendentesNaFila.ToString() : "";
        // Só refaz o rodapé quando algo dele mudou (não a cada tecla digitada num cadastro).
        var chave = $"{Arm.Dados.PastaSincronizada.Length > 0}|{_compactando}|{Ajustes.Atual.Impressora}|{_impressoras?.Count}";
        if (chave == _chaveDoRodape) return;
        _chaveDoRodape = chave;
        _rodapeLateral.Children.Clear();
        if (Arm.Dados.PastaSincronizada.Length > 0)
        {
            var pasta = Ui.Coluna(6);
            pasta.Add(Ui.TituloSecao("Pasta de modelos", Ui.GZip));
            var compactar = Ui.Botao(_compactando ? "Compactando…" : "Compactar (.zip)…", () => _ = Compactar(), glifo: Ui.GZip,
                dica: "Cria um .zip com todos os arquivos da pasta sincronizada (e uma cópia dos dados)");
            compactar.IsEnabled = !_compactando;
            compactar.HorizontalAlignment = HorizontalAlignment.Stretch;
            pasta.Add(compactar);
            _rodapeLateral.Add(pasta);
        }
        var impressora = Ui.Coluna(6);
        impressora.Add(Ui.TituloSecao("Impressora", Ui.GImpressora));
        _rodapeLateral.Add(impressora);
        impressora.Add(SeletorDeImpressora());
        var procurar = Ui.Botao("Impressoras do Windows…", Impressoras.AbrirConfiguracoes, glifo: Ui.GBusca, dica: "Abre as configurações de impressoras do Windows para adicionar uma impressora ou escolher a padrão");
        procurar.HorizontalAlignment = HorizontalAlignment.Stretch;
        impressora.Add(procurar);
        var ajustes = Ui.Botao("Ajustes…", AbrirAjustes, glifo: Ui.GEngrenagem, dica: "Aparência, dados e backup (Ctrl+,)");
        ajustes.HorizontalAlignment = HorizontalAlignment.Stretch;
        _rodapeLateral.Add(ajustes);
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
        catch (Exception ex) { Ui.Erro($"Erro ao sincronizar a pasta de modelos: {ex.Message}"); }
        SincronizacaoMudou?.Invoke();
        if (SecaoAtual == Secao.Gerar) _gerar?.AoAparecer();
    }

    /// <summary>A tela Modelos acompanha o andamento da sincronização.</summary>
    public event Action? SincronizacaoMudou;

    public void AbrirAjustes()
    {
        new AjustesJanela(this) { Owner = this }.ShowDialog();
    }

    private void AoTeclar(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) return;
        if (shift && e.Key == Key.N)
        {
            // Próximo paciente (⇧⌘N no Mac).
            Mostrar(Secao.Gerar);
            _gerar?.ProximoPaciente();
            e.Handled = true;
        }
        else if (e.Key is Key.OemPlus or Key.Add) { MudarTamanho(+1); e.Handled = true; }
        else if (e.Key is Key.OemMinus or Key.Subtract) { MudarTamanho(-1); e.Handled = true; }
        else if (e.Key is Key.D0 or Key.NumPad0) { MudarTamanho(0, absoluto: true); e.Handled = true; }
        else if (e.Key == Key.OemComma) { AbrirAjustes(); e.Handled = true; }
    }

    public void MudarTamanho(int passo, bool absoluto = false)
    {
        var novo = absoluto ? passo : Math.Clamp((int)Ajustes.Atual.TamanhoTexto + passo, 0, 2);
        Ajustes.Atual.TamanhoTexto = (TamanhoTexto)novo;
        Ajustes.Salvar();
        Tema.Aplicar(Application.Current, Ajustes.Atual.TamanhoTexto, Ajustes.Atual.Aparencia);
    }
}
