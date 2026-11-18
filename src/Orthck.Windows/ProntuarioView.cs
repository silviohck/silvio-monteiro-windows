using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>
/// Prontuário (ProntuarioView do Mac): a ficha do local (ex.: Avaliação ortopédica de Araruna) preenchida no app —
/// identificação, anamnese, exame físico, exames complementares e conduta — e gerada em Word, como os outros documentos.
/// O que é digitado aqui vale só para este atendimento; só os dados gerais vão para o cadastro. As regras ficam em
/// <see cref="Prontuario"/> (Orthck.Core); esta tela só mostra e chama.
/// </summary>
public sealed class ProntuarioView : UserControl
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;

    // Estado (o @State do Mac).
    private Dictionary<string, string> _valores = [];
    private string _examesExtras = "";
    private int _copias = 1;
    private Guid? _pacienteEscolhido;
    private Guid? _ultimoPaciente;
    private string? _aviso;
    private string? _ultimoDocumento;
    private bool _geracaoConcluida;
    private bool _organizandoFavoritos;
    private bool _escrevendo;
    private bool _ignorarNome;
    private Guid? _fichaMontada;

    // Controles que não podem ser recriados enquanto se digita.
    private readonly TextBox _nome = Ui.CaixaDeTexto(dica: "Nome do paciente");
    private readonly TextBox _buscaModelo = Ui.CaixaDeTexto(dica: "Buscar doença ou CID (ex.: gonartrose, túnel do carpo, M54)");
    private readonly TextBox _buscaMeus = Ui.CaixaDeTexto(dica: "Buscar nos meus modelos");
    private readonly TextBox _caixaExames = Ui.CaixaDeTexto(multilinha: true);
    private readonly ContentControl _secaoFicha = new(), _situacaoPaciente = new(), _secaoIdentificacao = new(), _secaoMeus = new(),
        _secaoModelos = new(), _secaoAtendimento = new(), _painel = new(), _favoritos = new(), _resultados = new(), _listaMeus = new(), _lados = new();
    /// <summary>Caixas de cada campo da ficha (as linhas do H.D.A., do exame físico…), na ordem da tela.</summary>
    private readonly Dictionary<string, TextBox> _caixas = [];
    private readonly Dictionary<string, FrameworkElement> _limpar = [];
    private readonly StackPanel _pagina = Ui.Coluna(0);
    private readonly Grid _grade = new();

    public ProntuarioView(JanelaPrincipal janela)
    {
        _janela = janela;
        Content = _grade;
        _nome.TextChanged += (_, _) =>
        {
            // Sempre em maiúsculas (digitado, colado ou vindo do cadastro), como no Gerar documento.
            var maior = Prontuario.Maiusculas(_nome.Text);
            if (maior != _nome.Text) { var cursor = _nome.CaretIndex; _nome.Text = maior; _nome.CaretIndex = Math.Min(cursor, maior.Length); return; }
            if (!_ignorarNome) NomeMudou();
        };
        _nome.KeyDown += (_, e) => { if (e.Key == Key.Enter && Sugestoes() is [var unica]) { EscolherPaciente(unica); e.Handled = true; } };
        _buscaModelo.TextChanged += (_, _) => AtualizarResultados();
        _buscaMeus.TextChanged += (_, _) => AtualizarMeus();
        _caixaExames.CharacterCasing = CharacterCasing.Upper;
        _caixaExames.MinHeight = 80;
        _caixaExames.TextChanged += (_, _) => { if (_escrevendo) return; _examesExtras = Prontuario.Maiusculas(_caixaExames.Text); Editou(); };

        _pagina.Children.Add(_secaoFicha);
        _pagina.Children.Add(MontarSecaoPaciente());
        _pagina.Children.Add(_secaoIdentificacao);
        _pagina.Children.Add(_secaoMeus);
        _pagina.Children.Add(_secaoModelos);
        _pagina.Children.Add(_secaoAtendimento);

        // Formulário à esquerda; à direita, o painel fixo com Gerar, Próximo paciente e Negrito (sem rolar até o fim).
        _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        var rolagem = Ui.Pagina(_pagina, 960);
        _grade.Children.Add(rolagem);
        var divisa = new Border { Width = 1 };
        divisa.SetResourceReference(Border.BackgroundProperty, Tema.Borda);
        Grid.SetColumn(divisa, 1);
        _grade.Children.Add(divisa);
        var lateral = new ScrollViewer { Content = _painel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 20, 16, 20) };
        lateral.SetResourceReference(BackgroundProperty, Tema.Lateral);
        Grid.SetColumn(lateral, 2);
        _grade.Children.Add(lateral);

        // Tab pula para o próximo ___ (Shift+Tab volta); Ctrl+Enter gera (Enter nas linhas da ficha quebra a linha).
        PreviewKeyDown += AoTeclar;
        // Caixa em que se está escrevendo, para o botão Negrito (que não tira o foco dela).
        GotKeyboardFocus += (_, e) => { if (e.NewFocus is TextBox t && (_caixas.ContainsValue(t) || t == _caixaExames)) _ultimaCaixa = t; };
    }

    // MARK: Estado derivado

    private LocalAtendimento? Local => Arm.Dados.Locais.FirstOrDefault(l => l.Id.ToString() == Ajustes.Atual.LocalSelecionado);
    private List<ModeloDocumento> Fichas => Arm.FichasPara(Local);
    /// <summary>Só a ficha que está na tela e é deste local.</summary>
    private ModeloDocumento? Ficha => Fichas.FirstOrDefault(f => f.Id.ToString() == Ajustes.Atual.FichaSelecionada);
    private string NomeDigitado => _nome.Text.Trim();
    private List<Paciente> Homonimos => Arm.PacientesChamados(NomeDigitado);

    private Paciente? PacienteCadastrado
    {
        get
        {
            var homonimos = Homonimos;
            if (_pacienteEscolhido is Guid id && homonimos.FirstOrDefault(p => p.Id == id) is { } escolhido) return escolhido;
            return homonimos.Count == 1 ? homonimos[0] : null;
        }
    }

    private bool PrecisaEscolherPaciente => Homonimos.Count > 1 && PacienteCadastrado == null;

    /// <summary>Pacientes com o trecho digitado, só do local escolhido (cadastrados nele ou já atendidos nele); sem local, todos.</summary>
    private List<Paciente> Sugestoes()
    {
        if (NomeDigitado.Length < 2 || Homonimos.Count > 0) return [];
        var local = Local;
        return Arm.PacientesOrdenados
            .Where(p => Texto.Contem(p.Nome, NomeDigitado) && (local == null || Arm.LocaisDoPaciente(p).Any(l => l.Id == local.Id)))
            .Take(6).ToList();
    }

    private string Lado => Ajustes.Atual.LadoModelo is "DIREITO" or "ESQUERDO" or "BILATERAL" ? Ajustes.Atual.LadoModelo : "DIREITO";

    // MARK: Ciclo da tela

    /// <summary>Chamado ao mostrar a tela.</summary>
    public void AoAparecer()
    {
        if (Arm.InstalarFichasProntas()) Arm.Salvar();
        if (Local == null && Ajustes.Atual.LocalSelecionado.Length > 0) { Ajustes.Atual.LocalSelecionado = ""; Ajustes.Salvar(); }
        ValidarFicha();
        // O paciente que estava no Gerar documento continua aqui.
        if (_janela.NomeEmAtendimento != _nome.Text)
        {
            _ignorarNome = true; _nome.Text = _janela.NomeEmAtendimento; _nome.CaretIndex = _nome.Text.Length; _ignorarNome = false;
            _pacienteEscolhido = _janela.PacienteEmAtendimento;
            PacienteMudou();
        }
        if (_fichaMontada != Ficha?.Id) MontarFicha();
        else PreencherIdentificacao();
        AtualizarTudo();
        Dispatcher.BeginInvoke(() => { if (_nome.Text.Length == 0) _nome.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void AtualizarTudo()
    {
        _secaoFicha.Content = SecaoFicha();
        AtualizarSituacaoPaciente();
        AtualizarMeus();
        AtualizarFavoritos();
        AtualizarResultados();
        AtualizarPainel();
    }

    /// <summary>Fica com uma ficha deste local (a escolhida, se ainda valer; senão a primeira).</summary>
    private void ValidarFicha()
    {
        if (Ficha != null) return;
        Ajustes.Atual.FichaSelecionada = Fichas.FirstOrDefault()?.Id.ToString() ?? "";
        Ajustes.Salvar();
    }

    private void Editou()
    {
        if (!_geracaoConcluida && _aviso == null && _ultimoDocumento == null) return;
        _geracaoConcluida = false; _aviso = null; _ultimoDocumento = null;
        AtualizarPainel();
    }

    // MARK: Ficha

    private FrameworkElement SecaoFicha()
    {
        var corpo = Ui.Coluna(10);
        var locais = new List<(string, string)> { ("Nenhum", "") };
        locais.AddRange(Arm.LocaisOrdenados.Select(l => (l.Nome.Length == 0 ? "Sem nome" : l.Nome, l.Id.ToString())));
        corpo.Add(Ui.Rotulado("Local de atendimento", Ui.Escolha(locais, Ajustes.Atual.LocalSelecionado, TrocarLocal, 240), 170));
        var fichas = Fichas;
        if (fichas.Count == 0)
            corpo.Add(Ui.Texto("Nenhuma ficha para este local. Importe uma ficha .docx com campos como {{NOME}}, {{HDA}} e {{EXAME_FISICO}}.", Estilo.Apoio));
        else
        {
            var opcoes = new List<(string, string)>();
            if (Ficha == null) opcoes.Add(("Escolha…", ""));
            opcoes.AddRange(fichas.Select(f => (f.Nome, f.Id.ToString())));
            corpo.Add(Ui.Rotulado("Ficha", Ui.Escolha(opcoes, Ajustes.Atual.FichaSelecionada, TrocarFicha, 320), 170));
        }
        var links = new WrapPanel();
        links.Children.Add(Margem(Ui.Link("Importar ficha (.docx)…", ImportarFicha, Ui.GImportar, Estilo.Apoio)));
        if (Ficha is { } ficha)
        {
            var abrir = Ui.Link("Abrir a ficha no Word", () => Ui.Abrir(Arm.UrlDoModelo(ficha)), Ui.GAbrir, Estilo.Apoio);
            abrir.ToolTip = "Só para ver a ficha. Alterações feitas nela aqui podem ser substituídas por uma versão nova do app.";
            links.Children.Add(Margem(abrir));
            if (!Armazenamento.EhFichaPronta(ficha))
                links.Children.Add(Margem(Ui.Link("Remover ficha", () =>
                {
                    if (!Ui.Confirmar($"Remover a ficha “{ficha.Nome}”?", "A cópia da ficha guardada pelo app é apagada. Seu arquivo original não é afetado.", true)) return;
                    Arm.RemoverFicha(ficha);
                    Ajustes.Atual.FichaSelecionada = "";
                    ValidarFicha();
                    MontarFicha();
                    AtualizarTudo();
                }, Ui.GLixeira, Estilo.Apoio)));
        }
        corpo.Add(links);
        return Ui.Secao("Ficha do prontuário", Ui.GProntuario, corpo);
    }

    private static FrameworkElement Margem(FrameworkElement e) { e.Margin = new Thickness(0, 0, 18, 0); return e; }

    /// <summary>Tira o controle de onde ele estava (as caixas de busca e listas passam de uma ficha para a outra).</summary>
    private static T Solto<T>(T e) where T : FrameworkElement
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case ContentControl c: c.Content = null; break;
            case Decorator d: d.Child = null; break;
        }
        return e;
    }

    private void TrocarLocal(string id)
    {
        Ajustes.Atual.LocalSelecionado = id;
        Ajustes.Salvar();
        ValidarFicha();
        RecomecarAtendimento();
        MontarFicha();
        AtualizarTudo();
    }

    private void TrocarFicha(string id)
    {
        if (id == Ajustes.Atual.FichaSelecionada) return;
        Ajustes.Atual.FichaSelecionada = id;
        Ajustes.Salvar();
        RecomecarAtendimento();
        MontarFicha();
        AtualizarTudo();
    }

    private void ImportarFicha()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog { Title = "Escolha a ficha (.docx com campos como {{NOME}}, {{HDA}}, {{EXAME_FISICO}})", Filter = "Documento do Word (*.docx)|*.docx" };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var nova = Arm.ImportarFicha(dialogo.FileName, Local?.Id);
            Ajustes.Atual.FichaSelecionada = nova.Id.ToString();
            Ajustes.Salvar();
            RecomecarAtendimento();
            MontarFicha();
            AtualizarTudo();
        }
        catch (Exception ex) { Ui.Erro($"Não foi possível importar a ficha: {ex.Message}"); }
    }

    // MARK: Paciente

    private FrameworkElement MontarSecaoPaciente()
    {
        var corpo = Ui.Coluna(8);
        corpo.Add(_nome);
        corpo.Add(_situacaoPaciente);
        return Ui.Secao("Paciente", Ui.GPessoa, corpo);
    }

    private void NomeMudou()
    {
        _janela.NomeEmAtendimento = _nome.Text;
        if (_pacienteEscolhido is Guid id && !Homonimos.Any(p => p.Id == id)) _pacienteEscolhido = null;
        // Outro nome depois de gerar = outro paciente: nada do atendimento anterior continua.
        if (_geracaoConcluida || _ultimoDocumento != null) RecomecarAtendimento();
        PacienteMudou();
        AtualizarSituacaoPaciente();
        AtualizarPainel();
    }

    /// <summary>Trocou de um paciente para outro: a anamnese e o exame do anterior não seguem (regra A → B → A).</summary>
    private void PacienteMudou()
    {
        var novo = PacienteCadastrado?.Id;
        _janela.PacienteEmAtendimento = novo;
        if (_ultimoPaciente is Guid anterior && anterior != novo) LimparAtendimento();
        _ultimoPaciente = novo;
        PreencherIdentificacao();
        EscreverValores();
    }

    private void EscolherPaciente(Paciente paciente)
    {
        _ignorarNome = true;
        _nome.Text = Prontuario.Maiusculas(paciente.Nome);
        _nome.CaretIndex = _nome.Text.Length;
        _ignorarNome = false;
        _janela.NomeEmAtendimento = _nome.Text;
        _pacienteEscolhido = paciente.Id;
        PacienteMudou();
        AtualizarSituacaoPaciente();
        AtualizarPainel();
    }

    private void AtualizarSituacaoPaciente()
    {
        var p = Ui.Coluna(6);
        if (PrecisaEscolherPaciente)
        {
            p.Add(Ui.Linha(8, Ui.Icone(Ui.GAviso, 14, Tema.Aviso), Ui.Texto($"Há {Homonimos.Count} pacientes com este nome. Qual deles é?", Estilo.Normal, Tema.Aviso)));
            foreach (var h in Homonimos) p.Add(Ui.Link(Armazenamento.Identificacao(h), () => EscolherPaciente(h), Ui.GPessoa));
        }
        else if (PacienteCadastrado is { } paciente)
            p.Add(Ui.Linha(8, Ui.Icone(Ui.GOkCirculo, 14, Tema.Sucesso),
                Ui.Texto($"Paciente cadastrado · {Armazenamento.Identificacao(paciente)} — dados preenchidos abaixo", Estilo.Normal, Tema.Sucesso)));
        else if (NomeDigitado.Length > 0)
        {
            foreach (var s in Sugestoes())
                p.Add(Ui.Link($"Usar o cadastro de {s.Nome} ({Armazenamento.Identificacao(s)})", () => EscolherPaciente(s), Ui.GPessoa));
            p.Add(Ui.Texto("Paciente novo: o cadastro será criado ao gerar o prontuário.", Estilo.Apoio));
        }
        _situacaoPaciente.Content = p.Children.Count == 0 ? null : p;
    }

    // MARK: Identificação e atendimento (montados uma vez por ficha)

    /// <summary>Monta as caixas da ficha escolhida. Os valores ficam em _valores; as caixas só mostram.</summary>
    private void MontarFicha()
    {
        _fichaMontada = Ficha?.Id;
        _caixas.Clear();
        _limpar.Clear();
        if (Ficha is not { } ficha)
        {
            _secaoIdentificacao.Content = _secaoMeus.Content = _secaoModelos.Content = _secaoAtendimento.Content = null;
            return;
        }
        PreencherIdentificacao();
        _secaoIdentificacao.Content = SecaoIdentificacao(ficha);
        _secaoMeus.Content = SecaoMeusModelos();
        _secaoModelos.Content = SecaoModelos();
        _secaoAtendimento.Content = SecaoAtendimento(ficha);
        EscreverValores();
    }

    private TextBox Caixa(string campo, bool multilinha = false)
    {
        var caixa = Ui.CaixaDeTexto(multilinha: multilinha);
        caixa.CharacterCasing = CharacterCasing.Upper;
        System.Windows.Automation.AutomationProperties.SetName(caixa, Prontuario.Titulo(campo));
        if (!multilinha) caixa.TextWrapping = TextWrapping.Wrap;
        caixa.TextChanged += (_, _) =>
        {
            if (_escrevendo) return;
            _valores[campo] = Prontuario.Maiusculas(caixa.Text);
            Editou();
            AtualizarLimpar();
        };
        _caixas[campo] = caixa;
        return caixa;
    }

    private FrameworkElement? SecaoIdentificacao(ModeloDocumento ficha)
    {
        var campos = Prontuario.CamposDeIdentificacao(ficha);
        if (campos.Count == 0) return null;
        var grade = new WrapPanel();
        foreach (var campo in campos)
        {
            var bloco = Ui.Coluna(3);
            bloco.Width = 250;
            bloco.Margin = new Thickness(0, 0, 12, 10);
            bloco.Add(Ui.Texto(Prontuario.Titulo(campo), Estilo.Legenda, Tema.TextoSecundario));
            bloco.Add(Caixa(campo));
            grade.Children.Add(bloco);
        }
        return Ui.Secao("Identificação", Ui.GPessoa, grade,
            "Vem do cadastro do paciente, do local e da data de hoje. O que você completar aqui (CPF, bairro, CNS…) fica guardado no cadastro.");
    }

    private FrameworkElement SecaoAtendimento(ModeloDocumento ficha)
    {
        var corpo = Ui.Coluna(14);
        foreach (var campo in Prontuario.CamposClinicos(ficha))
        {
            var bloco = Ui.Coluna(4);
            var grupo = Prontuario.Grupo(campo, ficha);
            var titulo = Ui.Texto(Prontuario.Titulo(campo));
            titulo.FontWeight = FontWeights.SemiBold;
            var limpar = Ui.Link("Limpar", () => { foreach (var c in grupo) _valores[c] = ""; EscreverValores(); Editou(); }, estilo: Estilo.Apoio);
            limpar.ToolTip = $"Apagar o que está escrito em {Prontuario.Titulo(campo)}";
            _limpar[campo] = limpar;
            bloco.Add(Ui.Extremos(titulo, limpar));
            if (grupo.Count > 1) bloco.Add(LinhasDaFicha(grupo));
            else
            {
                var caixa = Caixa(campo, multilinha: Prontuario.EhLongo(campo));
                if (Prontuario.EhLongo(campo)) caixa.MinHeight = 90;
                bloco.Add(caixa);
                if (CamposAutomaticos.EhCampoCid(campo))
                {
                    var sugestoes = new ContentControl();
                    bloco.Add(sugestoes, 2);
                    caixa.TextChanged += (_, _) => sugestoes.Content = GerarView.SugestoesCid(caixa);
                }
            }
            corpo.Add(bloco);
        }
        if (Prontuario.PrecisaDeExamesExtras(ficha))
        {
            var bloco = Ui.Coluna(4);
            var titulo = Ui.Texto("Exames complementares");
            titulo.FontWeight = FontWeights.SemiBold;
            bloco.Add(Ui.Extremos(titulo, Ui.Link("Limpar", () => { _caixaExames.Text = ""; }, estilo: Estilo.Apoio)));
            bloco.Add(Solto(_caixaExames));
            bloco.Add(Ui.Texto("Esta ficha não tem campo próprio: os exames saem junto do exame físico.", Estilo.Legenda, Tema.TextoSecundario));
            corpo.Add(bloco);
        }
        return Ui.Secao("Atendimento", Ui.GEstetoscopio, corpo,
            "Tab pula para o próximo ___ (Shift+Tab volta). Negrito: selecione um trecho e aperte Ctrl+B (ou o botão Negrito ao lado); ele fica entre ** aqui e sai em negrito no Word. Anamnese, exame e conduta valem só para este prontuário: não ficam no cadastro e não passam para o próximo paciente.");
    }

    /// <summary>As linhas da ficha, como no papel: uma caixa por linha. Ao passar do tamanho da linha, o texto continua
    /// na de baixo; Enter quebra a linha no cursor e o resto desce.</summary>
    private FrameworkElement LinhasDaFicha(List<string> campos)
    {
        var p = Ui.Coluna(0);
        for (var i = 0; i < campos.Count; i++)
        {
            var indice = i;
            var campo = campos[i];
            var numero = Ui.Texto($"{i + 1}", Estilo.Legenda, Tema.TextoSecundario);
            numero.Width = 22; numero.TextAlignment = TextAlignment.Right; numero.Margin = new Thickness(0, 0, 8, 0);
            numero.VerticalAlignment = VerticalAlignment.Center;
            var caixa = new TextBox
            {
                CharacterCasing = CharacterCasing.Upper, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = Ui.Monoespacada, Padding = new Thickness(2, 4, 2, 4), VerticalContentAlignment = VerticalAlignment.Center
            };
            caixa.SetResourceReference(ForegroundProperty, Tema.TextoPrincipal);
            System.Windows.Automation.AutomationProperties.SetName(caixa, $"{Prontuario.Titulo(campos[0])}, linha {i + 1}");
            caixa.TextChanged += (_, _) =>
            {
                if (_escrevendo) return;
                var seguinteAntes = indice + 1 < campos.Count ? _valores.GetValueOrDefault(campos[indice + 1], "") : "";
                _valores[campo] = Prontuario.Maiusculas(caixa.Text);
                var cursor = caixa.CaretIndex;
                if (Prontuario.Fluir(_valores, campos, indice))
                {
                    EscreverValores();
                    // A palavra que não coube desceu: continua digitando logo depois dela, na linha de baixo.
                    var proxima = campos[indice + 1];
                    var descida = _valores[proxima].Length - (seguinteAntes.Length == 0 ? 0 : seguinteAntes.Length + 1);
                    if (cursor >= _valores[campo].Length) Focar(proxima, Math.Max(0, descida));
                    else caixa.CaretIndex = Math.Min(cursor, caixa.Text.Length);
                }
                Editou();
                AtualizarLimpar();
            };
            caixa.PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
                e.Handled = true;
                if (!Prontuario.QuebrarLinha(_valores, campos, indice, caixa.CaretIndex)) return;
                EscreverValores();
                Editou();
                Focar(campos[indice + 1], 0);
            };
            _caixas[campo] = caixa;
            var linha = new DockPanel();
            DockPanel.SetDock(numero, Dock.Left);
            linha.Children.Add(numero);
            linha.Children.Add(caixa);
            var borda = new Border { Child = linha, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 1, 0, 1) };
            borda.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
            // Clique fora do texto (no número): entra na linha com o cursor no fim, sem selecionar o que está escrito.
            numero.MouseLeftButtonDown += (_, _) => Focar(campo, null);
            p.Add(borda);
        }
        var cartao = Ui.Cartao(p, Tema.Superficie, 6, 8);
        cartao.BorderThickness = new Thickness(1);
        cartao.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        return cartao;
    }

    /// <summary>Passa o cursor para o campo, na posição <paramref name="cursor"/> (null = fim), sem selecionar o texto.</summary>
    private void Focar(string campo, int? cursor, int selecionar = 0)
    {
        if (!_caixas.TryGetValue(campo, out var caixa)) return;
        Dispatcher.BeginInvoke(() =>
        {
            caixa.Focus();
            var inicio = Math.Min(cursor ?? caixa.Text.Length, caixa.Text.Length);
            caixa.Select(inicio, Math.Min(selecionar, caixa.Text.Length - inicio));
            caixa.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Mostra nas caixas o que está em _valores (depois de um modelo, de limpar, de trocar o lado…).</summary>
    private void EscreverValores()
    {
        _escrevendo = true;
        try
        {
            foreach (var (campo, caixa) in _caixas)
            {
                var valor = Prontuario.Maiusculas(_valores.GetValueOrDefault(campo, ""));
                if (caixa.Text != valor)
                {
                    var cursor = caixa.CaretIndex;
                    caixa.Text = valor;
                    caixa.CaretIndex = Math.Min(cursor, valor.Length);
                }
            }
            if (_caixaExames.Text != _examesExtras) _caixaExames.Text = _examesExtras;
        }
        finally { _escrevendo = false; }
        AtualizarLimpar();
    }

    private void AtualizarLimpar()
    {
        if (Ficha is not { } ficha) return;
        foreach (var (campo, link) in _limpar)
            link.Visibility = Prontuario.Grupo(campo, ficha).Any(c => _valores.GetValueOrDefault(c, "").Trim().Length > 0) ? Visibility.Visible : Visibility.Hidden;
    }

    private void PreencherIdentificacao()
    {
        if (Ficha is not { } ficha) return;
        foreach (var campo in Prontuario.CamposDeIdentificacao(ficha))
            _valores[campo] = Prontuario.Maiusculas(CamposAutomaticos.Valor(campo, PacienteCadastrado, Local) ?? "");
    }

    private void LimparAtendimento()
    {
        _valores = [];
        _examesExtras = "";
        _aviso = null; _ultimoDocumento = null; _geracaoConcluida = false;
    }

    private void RecomecarAtendimento()
    {
        LimparAtendimento();
        PreencherIdentificacao();
        EscreverValores();
    }

    public void ProximoPaciente()
    {
        _ignorarNome = true; _nome.Text = ""; _ignorarNome = false;
        _janela.NomeEmAtendimento = "";
        _janela.PacienteEmAtendimento = null;
        _pacienteEscolhido = null;
        _ultimoPaciente = null;
        RecomecarAtendimento();
        AtualizarSituacaoPaciente();
        AtualizarPainel();
        _nome.Focus();
    }

    // MARK: Meus modelos e modelos de atendimento do app

    private FrameworkElement SecaoMeusModelos()
    {
        var corpo = Ui.Coluna(8);
        corpo.Add(Solto(_buscaMeus));
        corpo.Add(Solto(_listaMeus));
        var salvar = Ui.Link("Salvar o que está escrito como meu modelo…", SalvarComoMeuModelo, Ui.GSalvar, Estilo.Apoio);
        salvar.HorizontalAlignment = HorizontalAlignment.Right;
        corpo.Add(salvar);
        return Ui.Secao("Meus modelos", Ui.GPessoa, corpo,
            "Só os modelos que você salvou. Com o mesmo nome, substitui o anterior; clique com o botão direito para apagar. Não salve dados de um paciente específico.");
    }

    private void AtualizarMeus()
    {
        if (Ficha is not { } ficha) return;
        var todos = Arm.Dados.ModelosDeAtendimento;
        _buscaMeus.Visibility = todos.Count > 6 ? Visibility.Visible : Visibility.Collapsed;
        var termo = _buscaMeus.Text.Trim();
        var meus = todos.Where(m => termo.Length < 2 || ModelosDeAtendimento.Simples(m.Nome + " " + m.Cid).Contains(ModelosDeAtendimento.Simples(termo)))
            .OrderBy(m => m.Nome, Texto.Ordem).ToList();
        var p = Ui.Coluna(2);
        if (todos.Count == 0)
            p.Add(Ui.Texto("Você ainda não salvou nenhum modelo. Escreva o atendimento (ou parta de um modelo do app), ajuste e clique em “Salvar o que está escrito…”.", Estilo.Apoio));
        foreach (var modelo in meus) p.Add(LinhaDoResultado(modelo, ficha));
        _listaMeus.Content = p;
    }

    private void SalvarComoMeuModelo()
    {
        if (Ficha is not { } ficha) return;
        var sugestao = Prontuario.CampoDiagnostico(ficha) is { } d ? _valores.GetValueOrDefault(d, "") : "";
        var nome = Pergunta.Texto("Salvar como meu modelo", "Fica nos seus modelos e aparece primeiro na busca. Com o mesmo nome, substitui o anterior.",
            "Nome do modelo", sugestao, "Salvar")?.Trim();
        if (string.IsNullOrEmpty(nome)) return;
        Arm.SalvarModeloDeAtendimento(Prontuario.ComoMeuModelo(_valores, ficha, nome, Lado));
        AtualizarMeus();
        AtualizarFavoritos();
    }

    private FrameworkElement SecaoModelos()
    {
        var corpo = Ui.Coluna(8);
        corpo.Add(Solto(_favoritos));
        var linha = new DockPanel();
        DockPanel.SetDock(_lados, Dock.Right);
        _lados.Margin = new Thickness(10, 0, 0, 0);
        linha.Children.Add(Solto(_lados));
        linha.Children.Add(Solto(_buscaModelo));
        corpo.Add(linha);
        corpo.Add(Solto(_resultados));
        return Ui.Secao("Modelos de atendimento do app", Ui.GLista, corpo,
            "A estrela põe o modelo nos favoritos deste local (atalhos acima da busca). Escolha o lado e clique no modelo: Q.P., H.D.A., exame físico, diagnóstico, CID e programação são preenchidos nas linhas da ficha. Complete os ___ e ajuste o que precisar. Os textos são pontos de partida.");
    }

    /// <summary>Direito / Esquerdo / Bilateral: trocar o lado troca também no texto já escrito.</summary>
    private FrameworkElement SeletorDeLado()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = "O [LADO] do modelo sai como este lado" };
        foreach (var lado in ModelosDeAtendimento.Lados)
        {
            var botao = new ToggleButton
            {
                Content = lado[0] + lado[1..].ToLowerInvariant(), IsChecked = lado == Lado, Padding = new Thickness(12, 5, 12, 5), MinHeight = 32,
                Margin = new Thickness(0, 0, 2, 0)
            };
            botao.Click += (_, _) => Ui.Executar(() => TrocarLado(lado));
            p.Children.Add(botao);
        }
        return p;
    }

    private void TrocarLado(string novo)
    {
        var antigo = Lado;
        Ajustes.Atual.LadoModelo = novo;
        Ajustes.Salvar();
        if (antigo != novo && Ficha is { } ficha)
        {
            foreach (var campo in Prontuario.LinhasClinicas(ficha))
                if (_valores.TryGetValue(campo, out var texto)) _valores[campo] = Prontuario.TrocarLado(texto, antigo, novo);
            _examesExtras = Prontuario.TrocarLado(_examesExtras, antigo, novo);
            EscreverValores();
            Editou();
        }
        AtualizarFavoritos();
        AtualizarResultados();
    }

    private void Aplicar(ModeloDeAtendimento modelo)
    {
        if (Ficha is not { } ficha) return;
        // Sem pedir confirmação: o modelo troca o que estiver escrito (como no Mac).
        Prontuario.Aplicar(_valores, modelo, ficha, Lado);
        EscreverValores();
        Editou();
        _buscaModelo.Text = "";
        if (Prontuario.LinhasClinicas(ficha).FirstOrDefault(c => _valores.GetValueOrDefault(c, "").Contains("___")) is { } primeiro
            && Prontuario.LacunaDepois(_valores[primeiro], 0) is { } lacuna)
            Focar(primeiro, lacuna.Inicio, lacuna.Tamanho);
    }

    private void AtualizarFavoritos()
    {
        _lados.Content = SeletorDeLado();
        if (Ficha is null) { _favoritos.Content = null; return; }
        var favoritos = Arm.FavoritosDeAtendimento(Local);
        if (favoritos.Count == 0) { _favoritos.Content = null; return; }
        var p = Ui.Coluna(6);
        var reorganizar = Ui.Link(_organizandoFavoritos ? "Pronto" : "Reorganizar", () => { _organizandoFavoritos = !_organizandoFavoritos; AtualizarFavoritos(); }, estilo: Estilo.Apoio);
        reorganizar.ToolTip = "Mudar a ordem dos favoritos com as setas, ou tirar um deles";
        p.Add(Ui.Extremos(Ui.Texto("Favoritos deste local", Estilo.Legenda, Tema.TextoSecundario), reorganizar));
        var grade = new WrapPanel();
        for (var i = 0; i < favoritos.Count; i++)
        {
            var favorito = favoritos[i];
            var posicao = i;
            void Mover(ModeloDeAtendimento? destino) => Ui.Executar(() => { Arm.MoverFavoritoDeAtendimento(favorito, destino, Local); AtualizarFavoritos(); });
            if (_organizandoFavoritos)
            {
                var esquerda = Ui.BotaoIcone(Ui.GVoltar, "Mover para a esquerda", () => Mover(favoritos[posicao - 1]));
                esquerda.IsEnabled = posicao > 0;
                var direita = Ui.BotaoIcone(Ui.GProximo, "Mover para a direita", () => Mover(posicao + 2 < favoritos.Count ? favoritos[posicao + 2] : null));
                direita.IsEnabled = posicao < favoritos.Count - 1;
                var tirar = Ui.BotaoIcone(Ui.GFechar, "Tirar dos favoritos", () => Ui.Executar(() => { Arm.AlternarFavoritoDeAtendimento(favorito, Local); AtualizarFavoritos(); AtualizarResultados(); }));
                var nome = Ui.Texto($"{posicao + 1}. {favorito.Nome}");
                nome.TextTrimming = TextTrimming.CharacterEllipsis; nome.VerticalAlignment = VerticalAlignment.Center; nome.Width = 150;
                var cartao = Ui.Cartao(Ui.Linha(4, esquerda, nome, direita, tirar), Tema.Superficie, 6, 4);
                cartao.BorderThickness = new Thickness(1);
                cartao.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
                cartao.Margin = new Thickness(0, 0, 8, 8);
                grade.Children.Add(cartao);
                continue;
            }
            var texto = Ui.Texto(favorito.Nome);
            texto.TextTrimming = TextTrimming.CharacterEllipsis;
            var conteudo = Ui.Linha(6, Ui.Icone(Ui.GEstrelaCheia, 12, Tema.Aviso), texto);
            var botao = new Button
            {
                Content = conteudo, Width = 210, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 6, 10, 6), HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = $"{favorito.Cid} · {favorito.Nome} ({Lado.ToLowerInvariant()})\nClique com o botão direito para mudar a ordem."
            };
            botao.Click += (_, _) => Ui.Executar(() => Aplicar(favorito));
            var menu = new ContextMenu();
            void Opcao(string titulo, bool ativo, Action acao)
            {
                var item = new MenuItem { Header = titulo, IsEnabled = ativo };
                item.Click += (_, _) => acao();
                menu.Items.Add(item);
            }
            Opcao("Mover para a esquerda", posicao > 0, () => Mover(favoritos[posicao - 1]));
            Opcao("Mover para a direita", posicao < favoritos.Count - 1, () => Mover(posicao + 2 < favoritos.Count ? favoritos[posicao + 2] : null));
            Opcao("Mover para o início", posicao > 0, () => Mover(favoritos[0]));
            Opcao("Mover para o fim", posicao < favoritos.Count - 1, () => Mover(null));
            menu.Items.Add(new Separator());
            Opcao("Tirar dos favoritos", true, () => Ui.Executar(() => { Arm.AlternarFavoritoDeAtendimento(favorito, Local); AtualizarFavoritos(); AtualizarResultados(); }));
            botao.ContextMenu = menu;
            grade.Children.Add(botao);
        }
        p.Add(grade);
        _favoritos.Content = p;
    }

    private void AtualizarResultados()
    {
        if (Ficha is not { } ficha) { _resultados.Content = null; return; }
        var termo = _buscaModelo.Text.Trim();
        var resultados = ModelosDeAtendimento.Buscar(termo, []);
        var p = Ui.Coluna(2);
        foreach (var modelo in resultados) p.Add(LinhaDoResultado(modelo, ficha));
        if (termo.Length >= 2 && resultados.Count == 0) p.Add(Ui.Texto("Nenhum modelo encontrado.", Estilo.Apoio));
        _resultados.Content = p;
    }

    /// <summary>Uma linha da busca: estrela (favorito) e o modelo (clique preenche a ficha).</summary>
    private FrameworkElement LinhaDoResultado(ModeloDeAtendimento modelo, ModeloDocumento ficha)
    {
        var favorito = Arm.EhFavoritoDeAtendimento(modelo, Local);
        var estrela = Ui.BotaoIcone(favorito ? Ui.GEstrelaCheia : Ui.GEstrela,
            favorito ? "Tirar dos favoritos" : $"Favoritar em {Local?.Nome ?? "este local"}: vira um atalho acima da busca",
            () => Ui.Executar(() => { Arm.AlternarFavoritoDeAtendimento(modelo, Local); AtualizarFavoritos(); AtualizarResultados(); AtualizarMeus(); }),
            favorito ? Tema.Aviso : Tema.TextoSecundario);
        var cid = Ui.Texto(modelo.Cid.Length == 0 ? "—" : modelo.Cid, Estilo.Codigo, Tema.Destaque);
        cid.Width = 64;
        var nome = Ui.Texto(modelo.Nome);
        nome.TextTrimming = TextTrimming.CharacterEllipsis;
        var etiqueta = modelo.Meu ? Ui.Texto("meu", Estilo.Legenda, Tema.Sucesso) : modelo.Generico ? Ui.Texto("por tipo", Estilo.Legenda, Tema.TextoSecundario) : new TextBlock();
        etiqueta.Margin = new Thickness(8, 0, 0, 0);
        var conteudo = new DockPanel();
        DockPanel.SetDock(cid, Dock.Left);
        DockPanel.SetDock(etiqueta, Dock.Right);
        conteudo.Children.Add(cid);
        conteudo.Children.Add(etiqueta);
        conteudo.Children.Add(nome);
        var botao = new Button
        {
            Content = conteudo, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(6, 4, 6, 4), ToolTip = $"Preencher a ficha com este modelo ({Lado.ToLowerInvariant()})"
        };
        botao.Click += (_, _) => Ui.Executar(() => Aplicar(modelo));
        var menu = new ContextMenu();
        var alternar = new MenuItem { Header = favorito ? "Tirar dos favoritos" : "Adicionar aos favoritos" };
        alternar.Click += (_, _) => Ui.Executar(() => { Arm.AlternarFavoritoDeAtendimento(modelo, Local); AtualizarFavoritos(); AtualizarResultados(); AtualizarMeus(); });
        menu.Items.Add(alternar);
        if (modelo.Meu)
        {
            var apagar = new MenuItem { Header = "Apagar este modelo" };
            apagar.Click += (_, _) => Ui.Executar(() =>
            {
                if (!Ui.Confirmar($"Apagar o modelo “{modelo.Nome}”?", "Ele sai dos seus modelos e dos favoritos.", true)) return;
                Arm.ApagarModeloDeAtendimento(modelo);
                AtualizarMeus(); AtualizarFavoritos();
            });
            menu.Items.Add(apagar);
        }
        botao.ContextMenu = menu;
        var linha = new DockPanel();
        DockPanel.SetDock(estrela, Dock.Left);
        linha.Children.Add(estrela);
        linha.Children.Add(botao);
        return linha;
    }

    // MARK: Painel lateral (Gerar)

    private void AtualizarPainel()
    {
        var p = Ui.Coluna(12);
        p.Add(Ui.TituloSecao("Gerar", Ui.GDocumentoNovo));
        if (Ficha == null)
        {
            p.Add(Ui.Texto("Escolha a ficha do prontuário.", Estilo.Normal, Tema.TextoSecundario));
            _painel.Content = p;
            return;
        }
        var acao = Ui.Escolha([("Adicionar à fila de impressão", "fila"), ("Imprimir agora", "imprimir"), ("Só abrir", "salvar")], Ajustes.Atual.AcaoDepoisDeGerar,
            v => { Ajustes.Atual.AcaoDepoisDeGerar = v; Ajustes.Salvar(); }, 200);
        acao.HorizontalAlignment = HorizontalAlignment.Stretch;
        p.Add(acao);
        var copias = GerarView.SeletorDeCopias(_copias, v => _copias = v, "Quantas cópias guardar na fila ou enviar à impressora");
        copias.HorizontalAlignment = HorizontalAlignment.Left;
        p.Add(copias);
        var lacunas = new CheckBox
        {
            Content = "Tirar os ___ não preenchidos", IsChecked = Ajustes.Atual.TirarLacunas,
            ToolTip = "Ao gerar, os ___ que ficaram sem preencher saem do documento (\"HÁ ___\" some; \"DERRAME ___\" sai inteiro). Desligado, saem impressos como linha para escrever à mão."
        };
        lacunas.Click += (_, _) => { Ajustes.Atual.TirarLacunas = lacunas.IsChecked == true; Ajustes.Salvar(); };
        p.Add(lacunas);
        var gerar = Ui.Botao("Gerar prontuário", () => Ui.ExecutarAsync(Gerar), principal: true, glifo: Ui.GDocumentoNovo);
        gerar.HorizontalAlignment = HorizontalAlignment.Stretch;
        gerar.Padding = new Thickness(14, 9, 14, 9);
        gerar.IsEnabled = !_geracaoConcluida && !PrecisaEscolherPaciente && NomeDigitado.Length > 0;
        gerar.ToolTip = _geracaoConcluida ? "Este prontuário já foi gerado. Altere os dados ou clique em Próximo paciente."
            : NomeDigitado.Length == 0 ? "Digite o nome do paciente" : PrecisaEscolherPaciente ? "Escolha qual dos pacientes com este nome é o certo" : "Gerar o prontuário em Word (Ctrl+Enter)";
        ToolTipService.SetShowOnDisabled(gerar, true);
        p.Add(gerar);
        if (_aviso != null)
        {
            var aviso = Ui.Texto(_aviso, Estilo.Normal, Tema.Sucesso);
            aviso.TextWrapping = TextWrapping.Wrap;
            p.Add(Ui.Linha(6, Ui.Icone(Ui.GOkCirculo, 14, Tema.Sucesso), aviso));
            if (_ultimoDocumento is { } ultimo) p.Add(Ui.Link("Abrir o documento", () => Ui.Abrir(ultimo), Ui.GAbrir, Estilo.Apoio));
        }
        var proximo = Ui.Botao("Próximo paciente", ProximoPaciente, glifo: Ui.GVoltar, dica: "Limpa o paciente e o atendimento; o local e a ficha continuam");
        proximo.HorizontalAlignment = HorizontalAlignment.Stretch;
        p.Add(proximo);
        var limpar = Ui.Botao("Limpar ficha", () =>
        {
            if (!Ui.Confirmar("Limpar a ficha?", "Queixa, H.D.A., exame, diagnóstico, CID, programação e o que mais foi digitado nesta ficha são apagados. A identificação volta a vir do cadastro e o paciente continua escolhido.", true)) return;
            RecomecarAtendimento();
            AtualizarPainel();
        }, glifo: Ui.GBorracha, dica: "Apaga tudo o que foi escrito no atendimento desta ficha; o paciente continua");
        limpar.HorizontalAlignment = HorizontalAlignment.Stretch;
        limpar.SetResourceReference(ForegroundProperty, Tema.Aviso);
        p.Add(limpar);
        p.Add(Ui.Divisor());
        var negrito = Ui.Botao("Negrito (Ctrl+B)", () => { if (_ultimaCaixa != null) JanelaPrincipal.AlternarNegrito(_ultimaCaixa); }, glifo: Ui.GNegrito,
            dica: "Selecione um trecho no campo e clique (ou Ctrl+B): ele sai em negrito no Word");
        negrito.HorizontalAlignment = HorizontalAlignment.Stretch;
        // O botão não tira o foco da caixa em que se está escrevendo (a seleção continua).
        negrito.Focusable = false;
        p.Add(negrito);
        _painel.Content = p;
    }

    private TextBox? _ultimaCaixa;

    // MARK: Teclado (Tab nos ___ e Ctrl+Enter)

    private void AoTeclar(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Ui.ExecutarAsync(Gerar);
            return;
        }
        if (e.Key != Key.Tab || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        if (Keyboard.FocusedElement is not TextBox caixa || Ficha is not { } ficha) return;
        var campo = _caixas.FirstOrDefault(x => x.Value == caixa).Key;
        var ordem = Prontuario.LinhasClinicas(ficha);
        var posicao = campo == null ? -1 : ordem.IndexOf(campo);
        if (posicao < 0) return;
        var paraTras = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // Próxima lacuna no campo atual (depois do cursor) ou nos campos seguintes; sem lacuna, Tab normal.
        var aqui = paraTras ? Prontuario.LacunaAntes(caixa.Text, caixa.SelectionStart) : Prontuario.LacunaDepois(caixa.Text, caixa.SelectionStart + caixa.SelectionLength);
        if (aqui is { } l)
        {
            caixa.Select(l.Inicio, l.Tamanho);
            e.Handled = true;
            return;
        }
        var outros = paraTras ? ordem.Take(posicao).Reverse() : ordem.Skip(posicao + 1);
        foreach (var outro in outros)
        {
            var texto = _valores.GetValueOrDefault(outro, "");
            var achada = paraTras ? Prontuario.LacunaAntes(texto, texto.Length) : Prontuario.LacunaDepois(texto, 0);
            if (achada is { } a)
            {
                Focar(outro, a.Inicio, a.Tamanho);
                e.Handled = true;
                return;
            }
        }
    }

    // MARK: Gerar

    private async Task Gerar()
    {
        if (_geracaoConcluida || PrecisaEscolherPaciente) return;
        // Sempre a ficha que está na tela no momento do clique.
        if (Ficha is not { } ficha) { Ui.Erro("Escolha a ficha do prontuário."); return; }
        var nome = NomeDigitado;
        if (nome.Length == 0) { Ui.Erro("Digite o nome do paciente."); return; }
        var finais = Prontuario.ValoresFinais(_valores, ficha, nome, _examesExtras, Ajustes.Atual.TirarLacunas);
        string destino;
        try
        {
            destino = Arm.CaminhoParaNovoDocumento(ficha, "Prontuário", Local, nome);
            DocxTemplate.Fill(Arm.UrlDoModelo(ficha), destino, finais);
        }
        catch (Exception ex)
        {
            Ui.Erro($"Não foi possível gerar o prontuário. {ex.Message}");
            return;
        }
        // Impede um segundo clique de gerar outro prontuário igual.
        _geracaoConcluida = true;
        var paciente = PacienteCadastrado ?? Arm.NovoPaciente(nome);
        if (Arm.Local(paciente.LocalId) == null) paciente.LocalId = Local?.Id;
        // Identificação completada aqui (CPF, bairro, CNS…) vai para o cadastro; anamnese e exame não.
        CamposAutomaticos.Guardar(finais, ficha.Campos, paciente);
        _pacienteEscolhido = paciente.Id;
        _ultimoPaciente = paciente.Id;
        _janela.PacienteEmAtendimento = paciente.Id;
        var documento = new DocumentoGerado
        {
            Modelo = ficha.Nome, ModeloId = ficha.Id, Paciente = nome, PacienteId = paciente.Id, Local = Local?.Nome, Caminho = destino
        };
        try { Arm.Registrar(documento); }
        catch (Exception ex)
        {
            Registro.Erro("registrar prontuário", ex);
            _ultimoDocumento = destino;
            AtualizarPainel();
            Ui.Erro($"O prontuário foi salvo em:\n{destino}\n\nMas os dados do app (histórico) não puderam ser gravados agora; isso é tentado de novo na próxima alteração. Ele não foi colocado na fila.\n\n{ex.Message}");
            return;
        }
        _aviso = $"Prontuário pronto: {Path.GetFileName(destino)}";
        _ultimoDocumento = destino;
        var acao = Ajustes.Atual.AcaoDepoisDeGerar;
        ItemFilaImpressao? item = null;
        if (acao is "fila" or "imprimir")
        {
            var jaImprimindo = Arm.ProcessandoFila;
            item = Arm.AdicionarAFila(documento, _copias);
            if (acao == "imprimir" && !jaImprimindo) _aviso = "Prontuário enviado para impressão";
            else { _aviso = $"Prontuário na fila de impressão ({Arm.PendentesNaFila} aguardando)"; item = null; }
        }
        AtualizarSituacaoPaciente();
        AtualizarPainel();
        if (acao == "salvar") Ui.Abrir(destino);
        Arm.LimparDocumentosTemporarios();
        if (item != null)
        {
            await Arm.ImprimirFilaAsync([item.Id], JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
            if (item.Situacao != SituacaoImpressao.Enviado) Ui.Erro($"{item.Observacao}\n\nVeja a aba Fila de impressão.");
        }
    }

    /// <summary>Usado só pelo modo de capturas do CI: paciente e um modelo de atendimento aplicado.</summary>
    internal void Demonstrar(string nome, string busca)
    {
        _ignorarNome = true; _nome.Text = nome; _ignorarNome = false;
        _janela.NomeEmAtendimento = nome;
        PacienteMudou();
        if (ModelosDeAtendimento.Buscar(busca, []).FirstOrDefault() is { } modelo) Aplicar(modelo);
        AtualizarTudo();
    }
}
