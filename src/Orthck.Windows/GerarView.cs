using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>
/// Tela Gerar documento (GerarView do Mac): local, documento pronto, subpasta e modelo, paciente, campos, salvar e imprimir.
/// Regra do projeto: trocar nome, homônimo, modelo, subpasta ou local recomeça os campos do atendimento (CID, conduta…);
/// só os dados gerais do cadastro voltam a ser preenchidos.
/// </summary>
public sealed class GerarView : UserControl
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;

    // Estado (o @State do Mac).
    private string _subpasta = "";
    private Guid? _modeloId;
    private Guid? _pacienteEscolhido;
    private bool _outroPaciente;
    private Dictionary<string, string> _valores = [];
    private int _copias = 1;
    private string? _aviso;
    private string? _ultimoDocumento;
    private bool _geracaoConcluida;
    private bool _imprimindo;
    private bool _ignorarNome;

    // Controles que não podem ser recriados enquanto se digita.
    private readonly TextBox _nome = Ui.CaixaDeTexto(dica: "Nome do paciente");
    private readonly TextBox _nascimentoDoNovo = Ui.CaixaDeTexto(dica: "Data de nascimento (dd/mm/aaaa) — ajuda a diferenciar");
    private readonly ContentControl _quadroDoLocal = new(), _documentoPronto = new(), _favoritos = new(), _secaoDocumento = new(), _situacaoPaciente = new(),
        _secaoCampos = new(), _secaoSalvar = new(), _painel = new();
    private readonly StackPanel _pagina = Ui.Coluna(0);
    private readonly ContentControl _raiz = new();
    private readonly Grid _grade = new();
    private ScrollViewer? _rolagem;
    private DateTime? _filtroDia;
    private readonly HashSet<Guid> _abertosNoPainel = [];

    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");
    /// <summary>Nome e campos em maiúsculas, como no Mac ("CAPS LOCK ativo").</summary>
    public static string Maiusculas(string texto) => texto.ToUpper(PtBr);

    public GerarView(JanelaPrincipal janela)
    {
        _janela = janela;
        Content = _raiz;
        _nome.TextChanged += (_, _) =>
        {
            // Sempre em maiúsculas (digitado, colado ou vindo do cadastro).
            var maior = Maiusculas(_nome.Text);
            if (maior != _nome.Text) { var cursor = _nome.CaretIndex; _nome.Text = maior; _nome.CaretIndex = Math.Min(cursor, maior.Length); return; }
            if (!_ignorarNome) NomeMudou();
        };
        _nome.MinWidth = 260;

        _pagina.Children.Add(_quadroDoLocal);
        _pagina.Children.Add(_documentoPronto);
        _pagina.Children.Add(_favoritos);
        _pagina.Children.Add(_secaoDocumento);
        _pagina.Children.Add(MontarSecaoPaciente());
        _pagina.Children.Add(_secaoCampos);
        _pagina.Children.Add(_secaoSalvar);

        // Formulário à esquerda; à direita, o atalho da fila e os pacientes atendidos recentemente (como no Mac).
        _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var divisa = new Border { Width = 1 };
        divisa.SetResourceReference(Border.BackgroundProperty, Tema.Borda);
        Grid.SetColumn(divisa, 1);
        _grade.Children.Add(divisa);
        var rolagemDoPainel = new ScrollViewer { Content = _painel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 20, 16, 20) };
        Grid.SetColumn(rolagemDoPainel, 2);
        _grade.Children.Add(rolagemDoPainel);
        Arm.Mudou += () => Dispatcher.InvokeAsync(() => { if (IsVisible) AtualizarPainel(); });
    }

    private LocalAtendimento? Local => Arm.Dados.Locais.FirstOrDefault(l => l.Id.ToString() == Ajustes.Atual.LocalSelecionado);
    private ModeloDocumento? Modelo => Arm.Modelo(_modeloId);
    private string NomeDigitado => _nome.Text.Trim();
    private List<Paciente> Homonimos => Arm.PacientesChamados(NomeDigitado);

    /// <summary>O paciente escolhido; com nome repetido, só depois de dizer qual é.</summary>
    private Paciente? PacienteCadastrado
    {
        get
        {
            if (_outroPaciente) return null;
            var homonimos = Homonimos;
            if (_pacienteEscolhido is Guid id && homonimos.FirstOrDefault(p => p.Id == id) is { } escolhido) return escolhido;
            return homonimos.Count == 1 ? homonimos[0] : null;
        }
    }

    private bool PrecisaEscolherPaciente => Homonimos.Count > 1 && PacienteCadastrado == null && !_outroPaciente;

    /// <summary>Tipos que têm modelos para o local escolhido.</summary>
    private List<string> SubpastasDisponiveis => Arm.ModelosPara(Local).Select(m => m.Categoria).Distinct().ToList();

    private List<ModeloDocumento> ModelosDaSubpasta => Arm.ModelosPara(Local).Where(m => m.Categoria == _subpasta).OrderBy(m => m.Nome, Texto.Ordem).ToList();

    /// <summary>Chamado ao mostrar a tela e depois de sincronizar.</summary>
    public void AoAparecer()
    {
        if (Arm.Dados.Modelos.Count == 0)
        {
            _raiz.Content = Ui.AvisoVazio("Nenhum modelo importado", Ui.GDocumento,
                "Vá em “Modelos” e importe um arquivo .docx com campos como {{NOME_PACIENTE}}, ou sincronize com a sua pasta de modelos.",
                "Ir para Modelos", () => _janela.Mostrar(Secao.Modelos));
            return;
        }
        if (_raiz.Content != _grade)
        {
            if (_rolagem != null) _grade.Children.Remove(_rolagem);
            _rolagem = Ui.Pagina(_pagina, 940);
            Grid.SetColumn(_rolagem, 0);
            _grade.Children.Add(_rolagem);
            _raiz.Content = _grade;
        }
        if (Local == null && Ajustes.Atual.LocalSelecionado.Length > 0) { Ajustes.Atual.LocalSelecionado = ""; Ajustes.Salvar(); }
        ValidarModelo();
        if (_valores.Count == 0) Preencher();
        Atualizar();
        Dispatcher.BeginInvoke(() => { if (_nome.Text.Length == 0) _nome.Focus(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Redesenha as partes que dependem do estado (os campos de texto em uso não são recriados).</summary>
    private void Atualizar()
    {
        _quadroDoLocal.Content = QuadroDoLocal();
        _documentoPronto.Content = DocumentoPronto();
        _favoritos.Content = Favoritos();
        _secaoDocumento.Content = SecaoDocumento();
        AtualizarSituacaoPaciente();
        _secaoCampos.Content = Modelo is { } m ? SecaoCampos(m) : null;
        AtualizarSecaoSalvar();
        AtualizarPainel();
    }

    // MARK: Reações (onChange do Mac)

    private void RecomecarAtendimento()
    {
        _aviso = null; _ultimoDocumento = null; _geracaoConcluida = false;
        _valores = [];
        Preencher();
    }

    private void NomeMudou()
    {
        // Nome mudou: a escolha entre homônimos só vale se ainda for o mesmo nome.
        if (_pacienteEscolhido is Guid id && !Homonimos.Any(p => p.Id == id)) _pacienteEscolhido = null;
        if (Homonimos.Count == 0) { _outroPaciente = false; _nascimentoDoNovo.Text = ""; }
        RecomecarAtendimento();
        _documentoPronto.Content = null;
        AtualizarSituacaoPaciente();
        _secaoCampos.Content = Modelo is { } m ? SecaoCampos(m) : null;
        AtualizarSecaoSalvar();
    }

    private void EscolherPaciente(Paciente paciente)
    {
        _ignorarNome = true;
        _nome.Text = paciente.Nome;
        _nome.CaretIndex = _nome.Text.Length;
        _ignorarNome = false;
        _pacienteEscolhido = paciente.Id;
        _outroPaciente = false;
        RecomecarAtendimento();
        Atualizar();
    }

    private void TrocarModelo(Guid? id)
    {
        _modeloId = id;
        _copias = Modelo?.Copias ?? 1;
        RecomecarAtendimento();
        Atualizar();
    }

    private void TrocarSubpasta(string subpasta)
    {
        _subpasta = subpasta;
        RecomecarAtendimento();
        ValidarModelo();
        Preencher();
        Atualizar();
    }

    private void TrocarLocal(string id)
    {
        Ajustes.Atual.LocalSelecionado = id;
        Ajustes.Salvar();
        RecomecarAtendimento();
        ValidarModelo();
        Preencher();
        Atualizar();
    }

    /// <summary>Mantém subpasta e modelo coerentes com o local (a subpasta precisa ter modelos; o modelo precisa ser dela).</summary>
    private void ValidarModelo()
    {
        var tipos = SubpastasDisponiveis;
        if (_subpasta.Length > 0 && !tipos.Contains(_subpasta))
            _subpasta = Modelo is { } m && tipos.Contains(m.Categoria) ? m.Categoria : "";
        if (_subpasta.Length == 0) { _modeloId = null; return; }
        var modelos = ModelosDaSubpasta;
        if (!modelos.Any(m => m.Id == _modeloId))
        {
            // Ao trocar de local, fica com o modelo de mesmo nome deste local, se houver.
            var mesmoNome = Modelo is { } anterior ? modelos.FirstOrDefault(m => m.Nome == anterior.Nome) : null;
            _modeloId = (mesmoNome ?? modelos.FirstOrDefault())?.Id;
            _copias = Modelo?.Copias ?? 1;
        }
    }

    /// <summary>Preenche os campos com o cadastro do paciente, o local e a data; campos do atendimento começam vazios.</summary>
    private void Preencher()
    {
        if (Modelo is not { } modelo) return;
        foreach (var campo in modelo.Campos.Where(c => !CamposAutomaticos.EhNomeDoPaciente(c)))
            _valores[campo] = CamposAutomaticos.Valor(campo, PacienteCadastrado, Local) ?? "";
    }

    /// <summary>Recomeça do início para o próximo atendimento: só o local continua.</summary>
    public void ProximoPaciente()
    {
        _subpasta = "";
        _modeloId = null;
        LimparPaciente();
    }

    private void LimparPaciente()
    {
        _pacienteEscolhido = null;
        _outroPaciente = false;
        _nascimentoDoNovo.Text = "";
        _ignorarNome = true; _nome.Text = ""; _ignorarNome = false;
        RecomecarAtendimento();
        _copias = Modelo?.Copias ?? 1;
        _rolagem?.ScrollToTop();
        AoAparecer();
        _nome.Focus();
    }

    // MARK: Quadro do local

    private FrameworkElement QuadroDoLocal()
    {
        var local = Local;
        var cor = local == null ? Tema.Aviso : Tema.Destaque;
        var icone = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Child = Ui.Icone(Ui.GPredio, 22, cor) };
        ((TextBlock)icone.Child).HorizontalAlignment = HorizontalAlignment.Center;
        icone.SetResourceReference(Border.BackgroundProperty, Tema.Superficie);
        var textos = Ui.Coluna(2);
        var rotulo = Ui.Texto("LOCAL DE ATENDIMENTO", Estilo.Legenda);
        rotulo.FontWeight = FontWeights.SemiBold;
        textos.Add(rotulo);
        var nome = Ui.Texto(local == null ? "Escolha o local" : local.Nome.Length == 0 ? "Local sem nome" : local.Nome, Estilo.Destaque, cor);
        nome.FontWeight = FontWeights.Bold;
        nome.TextWrapping = TextWrapping.NoWrap; nome.TextTrimming = TextTrimming.CharacterEllipsis;
        textos.Add(nome);
        if (Arm.Dados.Locais.Count == 0)
            textos.Add(Ui.Link("Cadastre seus locais na aba “Locais de atendimento”.", () => _janela.Mostrar(Secao.Locais), estilo: Estilo.Apoio));
        textos.VerticalAlignment = VerticalAlignment.Center;
        textos.Margin = new Thickness(14, 0, 14, 0);

        var opcoes = new List<(string, string)> { ("Nenhum", "") };
        opcoes.AddRange(Arm.LocaisOrdenados.Select(l => (l.Nome.Length == 0 ? "Sem nome" : l.Nome, l.Id.ToString())));
        var trocar = Ui.Escolha(opcoes, Ajustes.Atual.LocalSelecionado, TrocarLocal, 200);
        trocar.ToolTip = "Trocar o local de atendimento";
        trocar.VerticalAlignment = VerticalAlignment.Center;

        var grade = new DockPanel();
        DockPanel.SetDock(icone, Dock.Left);
        DockPanel.SetDock(trocar, Dock.Right);
        grade.Children.Add(icone);
        grade.Children.Add(trocar);
        grade.Children.Add(textos);
        var quadro = Ui.Cartao(grade, Tema.DestaqueSuave, 14, 16);
        quadro.Margin = new Thickness(0, 0, 0, 22);
        return quadro;
    }

    // MARK: Documento pronto

    private FrameworkElement? DocumentoPronto()
    {
        if (_ultimoDocumento == null) return null;
        var textos = Ui.Coluna(2);
        var titulo = Ui.Texto("Documento pronto", Estilo.Secao);
        textos.Add(titulo);
        var arquivo = Ui.Texto(Path.GetFileName(_ultimoDocumento), Estilo.Apoio);
        arquivo.TextWrapping = TextWrapping.NoWrap; arquivo.TextTrimming = TextTrimming.CharacterEllipsis;
        textos.Add(arquivo);
        textos.VerticalAlignment = VerticalAlignment.Center;
        textos.Margin = new Thickness(12, 0, 12, 0);
        var icone = Ui.Icone(Ui.GOkCirculo, 28, Tema.Sucesso);
        var proximo = BotaoProximo();
        var linha = new DockPanel();
        DockPanel.SetDock(icone, Dock.Left);
        DockPanel.SetDock(proximo, Dock.Right);
        linha.Children.Add(icone);
        linha.Children.Add(proximo);
        linha.Children.Add(textos);
        var quadro = Ui.Cartao(linha, Tema.SucessoSuave, 12, 14);
        quadro.Margin = new Thickness(0, 0, 0, 22);
        return quadro;
    }

    private Button BotaoProximo()
    {
        // Botão comum (fundo claro, texto escuro) com o ícone verde: legível em qualquer tema.
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
        var icone = Ui.Icone(Ui.GVoltar, 14, Tema.Sucesso);
        icone.Margin = new Thickness(0, 1, 8, 0);
        conteudo.Children.Add(icone);
        conteudo.Children.Add(new TextBlock { Text = "Próximo paciente", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var b = new Button { Content = conteudo, Padding = new Thickness(14, 7, 14, 7), MinHeight = 34, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Recomeça do início para o próximo paciente (Ctrl+Shift+N). Só o local de atendimento continua." };
        b.Click += (_, _) => Ui.Executar(ProximoPaciente);
        return b;
    }

    // MARK: Documento (subpasta e modelo)

    private FrameworkElement SecaoDocumento()
    {
        var corpo = Ui.Coluna(10);
        if (Arm.ModelosPara(Local).Count == 0)
        {
            corpo.Add(Ui.Texto("Nenhum modelo para este local. Importe na aba “Modelos”.", Estilo.Apoio));
            return Ui.Secao("Documento", Ui.GDocumento, corpo);
        }
        // Menu em níveis, como no Mac: 1 - APAC › Fisioterapia › coluna.
        var texto = _subpasta.Length == 0 ? "Escolha…" : _subpasta.Replace(Armazenamento.SeparadorDeSubpastas, "  ›  ");
        var botao = new Button { Padding = new Thickness(12, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 34, MinWidth = 220, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var conteudo = new DockPanel();
        var seta = Ui.Icone(Ui.GSetaBaixo, 11, Tema.TextoSecundario);
        seta.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(seta, Dock.Right);
        conteudo.Children.Add(seta);
        conteudo.Children.Add(new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center });
        botao.Content = conteudo;
        var menu = new ContextMenu { PlacementTarget = botao, Placement = PlacementMode.Bottom };
        foreach (var item in ItensDoMenu(NoSubpasta.Arvore(SubpastasDisponiveis))) menu.Items.Add(item);
        botao.Click += (_, _) => { menu.MinWidth = botao.ActualWidth; menu.IsOpen = true; };
        corpo.Add(Ui.Rotulado("Subpasta", botao, 150));

        var opcoes = new List<(string, Guid?)>();
        if (_modeloId == null) opcoes.Add(("Escolha…", null));
        opcoes.AddRange(ModelosDaSubpasta.Select(m => (m.Nome, (Guid?)m.Id)));
        var modelo = Ui.Escolha(opcoes, _modeloId, id => { if (id != _modeloId) TrocarModelo(id); });
        modelo.HorizontalAlignment = HorizontalAlignment.Stretch;
        modelo.IsEnabled = _subpasta.Length > 0;
        var linhaDoModelo = new DockPanel();
        if (Modelo is { } escolhido)
        {
            var favorito = Arm.EhFavorito(escolhido, Local?.Id);
            var estrela = Ui.BotaoIcone(favorito ? Ui.GEstrelaCheia : Ui.GEstrela,
                favorito ? "Tirar dos favoritos deste local" : "Adicionar aos favoritos deste local",
                () => { Arm.AlternarFavorito(escolhido, Local?.Id); Atualizar(); }, favorito ? Tema.Aviso : Tema.TextoSecundario);
            estrela.Margin = new Thickness(6, 0, 0, 0);
            DockPanel.SetDock(estrela, Dock.Right);
            linhaDoModelo.Children.Add(estrela);
        }
        linhaDoModelo.Children.Add(modelo);
        corpo.Add(Ui.Rotulado("Modelo", linhaDoModelo, 150));
        return Ui.Secao("Documento", Ui.GDocumento, corpo);
    }

    // MARK: Favoritos (de cada local, na ordem escolhida)

    private FrameworkElement? Favoritos()
    {
        var favoritos = Arm.FavoritosPara(Local);
        if (favoritos.Count == 0) return null;
        var grade = new WrapPanel();
        for (var i = 0; i < favoritos.Count; i++)
        {
            var favorito = favoritos[i];
            var escolhido = favorito.Id == _modeloId;
            var textos = Ui.Coluna(0);
            var nome = Ui.Texto(favorito.Nome, Estilo.Normal, escolhido ? Tema.TextoNoDestaque : null);
            nome.FontWeight = FontWeights.SemiBold;
            nome.TextWrapping = TextWrapping.NoWrap; nome.TextTrimming = TextTrimming.CharacterEllipsis;
            textos.Add(nome);
            var tipo = Ui.Texto(favorito.Categoria.Replace(Armazenamento.SeparadorDeSubpastas, " › "), Estilo.Legenda, escolhido ? Tema.TextoNoDestaque : Tema.TextoSecundario);
            tipo.TextWrapping = TextWrapping.NoWrap; tipo.TextTrimming = TextTrimming.CharacterEllipsis;
            textos.Add(tipo);
            var botao = new Button
            {
                Content = textos, Width = 190, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 6, 10, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = $"{favorito.Nome}\nClique com o botão direito para mudar a ordem."
            };
            if (escolhido) botao.SetResourceReference(BackgroundProperty, Tema.Destaque);
            botao.Click += (_, _) => Ui.Executar(() => IrParaFavorito(favorito));
            var menu = new ContextMenu();
            void Opcao(string titulo, bool ativo, Action acao)
            {
                var item = new MenuItem { Header = titulo, IsEnabled = ativo };
                item.Click += (_, _) => Ui.Executar(() => { acao(); Atualizar(); });
                menu.Items.Add(item);
            }
            var posicao = i;
            Opcao("Mover para a esquerda", posicao > 0, () => Arm.MoverFavorito(favorito, favoritos[posicao - 1], Local));
            Opcao("Mover para a direita", posicao < favoritos.Count - 1, () => Arm.MoverFavorito(favorito, posicao + 2 < favoritos.Count ? favoritos[posicao + 2] : null, Local));
            Opcao("Mover para o início", posicao > 0, () => Arm.MoverFavorito(favorito, favoritos[0], Local));
            Opcao("Mover para o fim", posicao < favoritos.Count - 1, () => Arm.MoverFavorito(favorito, null, Local));
            menu.Items.Add(new Separator());
            Opcao("Tirar dos favoritos", true, () => Arm.AlternarFavorito(favorito, Local?.Id));
            botao.ContextMenu = menu;
            // Arrastar um favorito para cima de outro também muda a ordem.
            botao.AllowDrop = true;
            Point? inicio = null;
            botao.PreviewMouseLeftButtonDown += (_, e) => inicio = e.GetPosition(botao);
            botao.PreviewMouseMove += (_, e) =>
            {
                if (inicio is not { } p || e.LeftButton != MouseButtonState.Pressed) return;
                var agora = e.GetPosition(botao);
                if (Math.Abs(agora.X - p.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(agora.Y - p.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                inicio = null;
                DragDrop.DoDragDrop(botao, new DataObject("OrthckFavorito", favorito.Id.ToString()), DragDropEffects.Move);
            };
            botao.Drop += (_, e) =>
            {
                if (e.Data.GetData("OrthckFavorito") is not string id || Arm.Modelo(Guid.Parse(id)) is not { } arrastado || arrastado.Id == favorito.Id) return;
                Ui.Executar(() => { Arm.MoverFavorito(arrastado, favorito, Local); Atualizar(); });
            };
            grade.Children.Add(botao);
        }
        var secao = Ui.Secao("Favoritos", Ui.GEstrelaCheia, grade, "Arraste ou clique com o botão direito para mudar a ordem. A estrela ao lado do modelo adiciona ou tira.");
        return secao;
    }

    /// <summary>Abre o modelo favorito; o nome digitado continua.</summary>
    private void IrParaFavorito(ModeloDocumento modelo)
    {
        _subpasta = modelo.Categoria;
        _modeloId = modelo.Id;
        _copias = modelo.Copias;
        RecomecarAtendimento();
        Atualizar();
    }

    /// <summary>Pasta com subpastas vira submenu; uma pasta que também tem modelos próprios ganha "Documentos em …".</summary>
    private IEnumerable<MenuItem> ItensDoMenu(List<NoSubpasta> nos)
    {
        foreach (var no in nos)
        {
            if (no.Filhos.Count == 0) { yield return ItemDaSubpasta(no.Nome, no.Caminho); continue; }
            var sub = new MenuItem { Header = no.Nome };
            if (no.TemModelos)
            {
                sub.Items.Add(ItemDaSubpasta($"Documentos em {no.Nome}", no.Caminho));
                sub.Items.Add(new Separator());
            }
            foreach (var filho in ItensDoMenu(no.Filhos)) sub.Items.Add(filho);
            yield return sub;
        }
    }

    private MenuItem ItemDaSubpasta(string titulo, string caminho)
    {
        var item = new MenuItem { Header = titulo, IsCheckable = false, IsChecked = caminho == _subpasta };
        item.Click += (_, _) => TrocarSubpasta(caminho);
        return item;
    }

    // MARK: Paciente

    private FrameworkElement MontarSecaoPaciente()
    {
        var corpo = Ui.Coluna(10);
        var colar = Ui.BotaoIcone(Ui.GColar, "Colar o nome copiado", () =>
        {
            // Cola o nome copiado de outro sistema, numa linha só.
            if (!Clipboard.ContainsText()) return;
            var linha = Clipboard.GetText().Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            _nome.Text = linha;
            _nome.CaretIndex = linha.Length;
            _nome.Focus();
        });
        var copiar = Ui.BotaoIcone(Ui.GCopiar, "Copiar o nome do paciente", () => { if (NomeDigitado.Length > 0) Ui.Copiar(NomeDigitado); });
        var cadastro = Ui.Botao("Cadastro", AbrirCadastro, glifo: Ui.GPessoa, dica: "Abrir o cadastro do paciente para acrescentar informações (CPF, nascimento, contato)");
        cadastro.Padding = new Thickness(10, 5, 10, 5);
        cadastro.Margin = new Thickness(6, 0, 0, 0);
        var botoes = Ui.Linha(0, colar, copiar, cadastro);
        botoes.VerticalAlignment = VerticalAlignment.Center;
        botoes.Margin = new Thickness(6, 0, 0, 0);
        _nome.TextChanged += (_, _) =>
        {
            copiar.IsEnabled = NomeDigitado.Length > 0;
            cadastro.IsEnabled = NomeDigitado.Length > 0 && !PrecisaEscolherPaciente;
            cadastro.Content = Ui.ConteudoDoBotao("Cadastro", PacienteCadastrado == null ? Ui.GPessoaMais : Ui.GPessoa);
        };
        copiar.IsEnabled = false; cadastro.IsEnabled = false;
        var linha = new DockPanel();
        DockPanel.SetDock(botoes, Dock.Right);
        linha.Children.Add(botoes);
        linha.Children.Add(_nome);
        corpo.Add(linha);
        corpo.Add(_situacaoPaciente);
        _nome.KeyDown += (_, e) =>
        {
            // Enter no nome com uma única sugestão: usa o cadastro.
            if (e.Key == Key.Enter && Sugestoes() is [var unica]) { EscolherPaciente(unica); e.Handled = true; }
        };
        return Ui.Secao("Paciente", Ui.GPessoa, corpo);
    }

    /// <summary>Pacientes com o trecho digitado, só do local escolhido (cadastrados nele ou já atendidos nele); sem local, todos.</summary>
    private List<Paciente> Sugestoes()
    {
        if (NomeDigitado.Length < 2 || Homonimos.Count > 0) return [];
        var local = Local;
        return Arm.PacientesOrdenados
            .Where(p => Texto.Contem(p.Nome, NomeDigitado)
                && (local == null || Arm.LocaisDoPaciente(p).Any(l => l.Id == local.Id)))
            .Take(8).ToList();
    }

    private void AtualizarSituacaoPaciente()
    {
        var p = Ui.Coluna(6);
        if (PrecisaEscolherPaciente)
        {
            p.Add(Ui.Linha(8, Ui.Icone(Ui.GAviso, 14, Tema.Aviso), Ui.Texto($"Há {Homonimos.Count} pacientes chamados “{NomeDigitado}”. Qual deles é?", Estilo.Normal, Tema.Aviso)));
            foreach (var h in Homonimos)
                p.Add(Ui.Link(string.Join(" · ", new[] { Armazenamento.Identificacao(h), Arm.NomesDosLocais(h) }.Where(t => t.Length > 0)), () => EscolherPaciente(h), Ui.GPessoa));
            p.Add(Ui.Link("É outro paciente (novo cadastro)", () => { _outroPaciente = true; RecomecarAtendimento(); Atualizar(); }));
            if (Homonimos.Any(h => h.DataNascimento.Trim().Length == 0))
                p.Add(Ui.Texto("Dica: cadastre a data de nascimento desses pacientes para diferenciá-los com segurança.", Estilo.Apoio));
        }
        else if (PacienteCadastrado is { } paciente)
        {
            p.Add(Ui.Linha(8, Ui.Icone(Ui.GOkCirculo, 14, Tema.Sucesso),
                Ui.Texto($"Paciente cadastrado · {Armazenamento.Identificacao(paciente)} — dados preenchidos abaixo", Estilo.Normal, Tema.Sucesso)));
            if (Arm.Local(paciente.LocalId) is { } origem && origem.Id != Local?.Id)
                p.Add(Ui.Texto($"Paciente de {(origem.Nome.Length == 0 ? "outro local" : origem.Nome)} — o documento sai como {Local?.Nome ?? "sem local"} e fica na pasta deste local.", Estilo.Apoio));
            var links = new WrapPanel();
            if (Homonimos.Count > 1) links.Children.Add(MargemDireita(Ui.Link("Trocar de paciente", () => { _pacienteEscolhido = null; RecomecarAtendimento(); Atualizar(); }, estilo: Estilo.Apoio)));
            links.Children.Add(Ui.Link("É outro paciente com o mesmo nome", () => { _outroPaciente = true; RecomecarAtendimento(); Atualizar(); }, estilo: Estilo.Apoio));
            p.Add(links);
        }
        else if (_outroPaciente)
        {
            p.Add(Ui.Linha(8, Ui.Icone(Ui.GPessoaMais, 14), Ui.Texto("Novo cadastro, diferente dos que já têm este nome.")));
            if (_nascimentoDoNovo.Parent is Panel antigo) antigo.Children.Remove(_nascimentoDoNovo);
            p.Add(_nascimentoDoNovo);
            p.Add(Ui.Link("Cancelar: é um paciente já cadastrado", () => { _outroPaciente = false; _nascimentoDoNovo.Text = ""; RecomecarAtendimento(); Atualizar(); }, estilo: Estilo.Apoio));
        }
        else if (NomeDigitado.Length > 0)
        {
            foreach (var s in Sugestoes())
            {
                var texto = $"Usar o cadastro de {s.Nome} ({Armazenamento.Identificacao(s)})";
                var locais = Arm.NomesDosLocais(s);
                if (locais.Length > 0) texto += $" · {locais}";
                p.Add(Ui.Link(texto, () => EscolherPaciente(s), Ui.GPessoa));
            }
            p.Add(Ui.Texto("Paciente novo: o cadastro será criado ao gerar o documento.", Estilo.Apoio));
        }
        _situacaoPaciente.Content = p.Children.Count == 0 ? null : p;
    }

    private static FrameworkElement MargemDireita(FrameworkElement e) { e.Margin = new Thickness(0, 0, 18, 0); return e; }

    /// <summary>Cadastro novo com o nome digitado (e a data de nascimento informada, se for homônimo).</summary>
    private Paciente CriarPaciente()
    {
        var paciente = Arm.NovoPaciente(NomeDigitado);
        paciente.LocalId = Local?.Id;
        var nascimento = _nascimentoDoNovo.Text.Trim();
        if (nascimento.Length > 0) paciente.DataNascimento = nascimento;
        Arm.Salvar();
        _pacienteEscolhido = paciente.Id;
        _outroPaciente = false;
        _nascimentoDoNovo.Text = "";
        return paciente;
    }

    /// <summary>Abre o cadastro do paciente digitado, criando-o se ainda não existir.</summary>
    private void AbrirCadastro()
    {
        if (PrecisaEscolherPaciente || NomeDigitado.Length == 0) return;
        var paciente = PacienteCadastrado ?? CriarPaciente();
        new CadastroPacienteJanela(Arm, paciente) { Owner = _janela }.ShowDialog();
        // Depois de editar: acompanha mudança de nome e recarrega os dados gerais no formulário.
        if (Arm.Paciente(paciente.Id) is { Nome.Length: > 0 } atualizado)
        {
            _ignorarNome = true; _nome.Text = atualizado.Nome; _ignorarNome = false;
            _pacienteEscolhido = atualizado.Id;
        }
        // Só os campos que vêm do cadastro mudam; o que foi digitado para este atendimento continua.
        if (Modelo is { } modelo)
            foreach (var campo in modelo.Campos.Where(c => CamposAutomaticos.OrigemDe(c)?.Tipo is CamposAutomaticos.Tipo.Paciente or CamposAutomaticos.Tipo.Idade && !CamposAutomaticos.EhNomeDoPaciente(c)))
                _valores[campo] = CamposAutomaticos.Valor(campo, PacienteCadastrado, Local) ?? "";
        Atualizar();
    }

    // MARK: Campos do documento

    private FrameworkElement SecaoCampos(ModeloDocumento modelo)
    {
        var corpo = Ui.Coluna(12);
        var campos = modelo.Campos.Where(c => !CamposAutomaticos.EhNomeDoPaciente(c)).ToList();
        if (campos.Count == 0) corpo.Add(Ui.Texto("Nenhum outro campo para preencher neste modelo.", Estilo.Normal, Tema.TextoSecundario));
        foreach (var campo in campos)
        {
            var chave = CamposAutomaticos.Chave(campo);
            var longo = new[] { "DIAGNOSTICO", "CONDUTA", "OBSERVAC", "HISTORIA", "ANAMNESE", "EXAME", "RELATORIO", "JUSTIFICATIVA", "TRATAMENTO", "EVOLUCAO", "PRESCRICAO", "LAUDO" }
                .Any(chave.Contains);
            var caixa = Ui.CaixaDeTexto(_valores.GetValueOrDefault(campo, ""), multilinha: longo);
            caixa.CharacterCasing = CharacterCasing.Upper;
            System.Windows.Automation.AutomationProperties.SetName(caixa, campo);
            if (!longo) caixa.TextWrapping = TextWrapping.Wrap;
            var bloco = Ui.Coluna(4);
            var rotulo = Ui.Texto(campo, Estilo.Apoio);
            rotulo.FontWeight = FontWeights.SemiBold;
            bloco.Add(rotulo);
            bloco.Add(caixa);
            ContentControl? sugestoes = null;
            if (CamposAutomaticos.EhCampoCid(campo))
            {
                sugestoes = new ContentControl();
                bloco.Add(sugestoes, 2);
                sugestoes.Content = SugestoesCid(caixa);
            }
            caixa.TextChanged += (_, _) =>
            {
                _valores[campo] = caixa.Text;
                if (_geracaoConcluida || _aviso != null)
                {
                    _geracaoConcluida = false; _aviso = null; _ultimoDocumento = null;
                    _documentoPronto.Content = null;
                    AtualizarSecaoSalvar();
                }
                if (sugestoes != null) sugestoes.Content = SugestoesCid(caixa);
            };
            corpo.Add(bloco);
        }
        return Ui.Secao("Campos do documento", Ui.GCampos, corpo,
            "Preenchidos com o cadastro do paciente, o local e a data de hoje. Você pode alterar qualquer valor. Só os dados gerais (CPF, nascimento, contato) ficam no cadastro; campos como CID valem só para este documento.");
    }

    /// <summary>Sugestões da tabela CID-10 logo abaixo de um campo {{CID}}: digite o código ou palavras e clique.</summary>
    private static FrameworkElement? SugestoesCid(TextBox caixa)
    {
        var termo = caixa.Text.Trim();
        if (termo.Length < 2) return null;
        var tabela = TabelaDeCodigos.Cid10;
        if (tabela.Codigos.Any(c => c.Codigo.Equals(termo, StringComparison.OrdinalIgnoreCase))) return null;
        var lista = tabela.Buscar(termo, null).Take(6).ToList();
        if (lista.Count == 0) return null;
        var p = Ui.Coluna(2);
        foreach (var codigo in lista)
        {
            var linha = new DockPanel();
            var c = Ui.Texto(codigo.Codigo, Estilo.Codigo, Tema.Destaque);
            c.FontWeight = FontWeights.SemiBold; c.MinWidth = 64;
            DockPanel.SetDock(c, Dock.Left);
            linha.Children.Add(c);
            var d = Ui.Texto(codigo.Descricao);
            d.TextWrapping = TextWrapping.NoWrap; d.TextTrimming = TextTrimming.CharacterEllipsis;
            linha.Children.Add(d);
            var botao = new Button
            {
                Content = linha, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(6, 3, 6, 3), ToolTip = $"Usar {codigo.TextoCompleto}", HorizontalAlignment = HorizontalAlignment.Stretch
            };
            botao.Click += (_, _) => { caixa.Text = codigo.Codigo; caixa.CaretIndex = caixa.Text.Length; caixa.Focus(); };
            p.Add(botao);
        }
        return Ui.Cartao(p, Tema.DestaqueSuave, 8, 6);
    }

    // MARK: Salvar e imprimir

    private void AtualizarSecaoSalvar()
    {
        if (Modelo is not { } modelo) { _secaoSalvar.Content = null; return; }
        var corpo = Ui.Coluna(12);


        var abrir = new CheckBox { Content = "Abrir o documento depois de gerar", IsChecked = Ajustes.Atual.AbrirAoGerar };
        abrir.Click += (_, _) => { Ajustes.Atual.AbrirAoGerar = abrir.IsChecked == true; Ajustes.Salvar(); };
        corpo.Add(abrir);

        var acao = Ui.Escolha([("Adicionar à fila de impressão", "fila"), ("Imprimir agora", "imprimir"), ("Só abrir", "salvar")], Ajustes.Atual.AcaoDepoisDeGerar,
            v => { Ajustes.Atual.AcaoDepoisDeGerar = v; Ajustes.Salvar(); AtualizarSecaoSalvar(); }, 260);
        var copias = SeletorDeCopias(_copias, v => _copias = v, "Quantas cópias guardar na fila ou enviar à impressora");
        corpo.Add(Ui.Extremos(Ui.Rotulado("Depois de gerar", acao, 150), copias));
        if (Ajustes.Atual.AcaoDepoisDeGerar == "fila")
            corpo.Add(Ui.Texto("O Word será salvo normalmente e ficará aguardando na aba Fila de impressão.", Estilo.Apoio));
        else if (Ajustes.Atual.AcaoDepoisDeGerar == "imprimir")
            corpo.Add(Ui.Texto($"Imprime em: {JanelaPrincipal.NomeDaImpressora} (troque na barra lateral).", Estilo.Apoio));

        // Rodapé: aviso de salvo, próximo paciente e gerar.
        var aviso = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        if (_aviso != null)
        {
            aviso.Children.Add(MargemDireita(Ui.Linha(6, Ui.Icone(Ui.GOkCirculo, 14, Tema.Sucesso), Ui.Texto(_aviso, Estilo.Normal, Tema.Sucesso))));
            if (_ultimoDocumento is { } ultimo)
            {
                aviso.Children.Add(MargemDireita(Ui.Link("Mostrar no Explorador", () => Ui.MostrarNaPasta(ultimo))));
                var jaEnviado = Arm.Dados.FilaImpressao.Any(x => x.Caminho == ultimo && x.Situacao != SituacaoImpressao.Pendente);
                if (!_imprimindo && !Arm.ProcessandoFila && !jaEnviado) aviso.Children.Add(Ui.Link("Imprimir agora", () => Ui.ExecutarAsync(() => ImprimirAgora(ultimo))));
                else if (_imprimindo) aviso.Children.Add(Ui.Texto("Enviando…", Estilo.Normal, Tema.TextoSecundario));
            }
        }
        // O botão lê o modelo escolhido na hora do clique (nunca um modelo guardado de antes).
        var gerar = Ui.Botao("Gerar documento", () => Ui.ExecutarAsync(Gerar), principal: true, glifo: Ui.GSalvar);
        gerar.IsDefault = true;
        gerar.Padding = new Thickness(18, 9, 18, 9);
        gerar.IsEnabled = !_geracaoConcluida && !PrecisaEscolherPaciente && !(NomeDigitado.Length == 0 && modelo.Campos.Any(CamposAutomaticos.EhNomeDoPaciente));
        gerar.ToolTip = _geracaoConcluida ? "Este documento já foi gerado. Altere os dados, escolha outro modelo ou clique em Próximo paciente."
            : PrecisaEscolherPaciente ? "Escolha qual dos pacientes com este nome é o certo"
            : NomeDigitado.Length == 0 ? "Digite o nome do paciente" : "Gerar o documento (Enter)";
        ToolTipService.SetShowOnDisabled(gerar, true);
        var proximo = BotaoProximo();
        proximo.Padding = new Thickness(16, 9, 16, 9);
        var direita = Ui.Linha(10, proximo, gerar);
        corpo.Add(Ui.Divisor());
        corpo.Add(Ui.Extremos(aviso, direita));
        _secaoSalvar.Content = Ui.Secao("Salvar e imprimir", Ui.GSalvar, corpo);
    }

    /// <summary>"− 2 cópias +" (Stepper do Mac), de 1 a 20.</summary>
    public static FrameworkElement SeletorDeCopias(int inicial, Action<int> aoMudar, string dica, int maximo = 20)
    {
        var valor = inicial;
        var texto = Ui.Texto("");
        texto.MinWidth = 70; texto.TextAlignment = TextAlignment.Center; texto.VerticalAlignment = VerticalAlignment.Center;
        void Mostrar() => texto.Text = valor == 1 ? "1 cópia" : $"{valor} cópias";
        Mostrar();
        var menos = Ui.BotaoIcone("", "Menos uma cópia", () => { if (valor > 1) { valor--; Mostrar(); aoMudar(valor); } }, Tema.TextoPrincipal);
        var mais = Ui.BotaoIcone(Ui.GMais, "Mais uma cópia", () => { if (valor < maximo) { valor++; Mostrar(); aoMudar(valor); } }, Tema.TextoPrincipal);
        var linha = Ui.Linha(2, menos, texto, mais);
        linha.ToolTip = dica;
        linha.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Child = linha, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(2) };
        b.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        b.SetResourceReference(Border.BackgroundProperty, Tema.Superficie);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    /// <summary>Usado só pelo modo de capturas do CI: escolhe subpasta, modelo, nome e alguns campos.</summary>
    internal void Demonstrar(string subpasta, Guid modelo, string nome, Dictionary<string, string> campos)
    {
        _subpasta = subpasta;
        _modeloId = modelo;
        _ignorarNome = true; _nome.Text = nome; _ignorarNome = false;
        _pacienteEscolhido = null; _outroPaciente = false;
        RecomecarAtendimento();
        foreach (var (campo, valor) in campos) _valores[campo] = valor;
        AoAparecer();
    }

    // MARK: Gerar e imprimir

    private async Task Gerar()
    {
        if (_geracaoConcluida || PrecisaEscolherPaciente) return;
        // Sempre o modelo que está na tela agora, e só se for da subpasta e do local escolhidos.
        if (Modelo is not { } modelo || modelo.Categoria != _subpasta || !Arm.ModelosPara(Local).Any(m => m.Id == modelo.Id))
        {
            Ui.Erro("Escolha a subpasta e o modelo de novo: o modelo da tela não é deste local.");
            return;
        }
        // Se o arquivo foi editado na pasta de modelos, usa a versão nova; se os campos mudaram, refaz o formulário antes.
        if (Arm.AtualizarModeloDaPasta(modelo))
        {
            foreach (var campo in modelo.Campos.Where(c => !CamposAutomaticos.EhNomeDoPaciente(c) && !_valores.ContainsKey(c)))
                _valores[campo] = CamposAutomaticos.Valor(campo, PacienteCadastrado, Local) ?? "";
            Atualizar();
            Ui.Informar("O modelo foi alterado na pasta de modelos e os campos mudaram. Confira o formulário e clique em Gerar documento de novo.");
            return;
        }
        var nome = NomeDigitado;
        var valoresFinais = new Dictionary<string, string>(_valores);
        foreach (var campo in modelo.Campos.Where(CamposAutomaticos.EhNomeDoPaciente)) valoresFinais[campo] = nome;

        string destino;
        try
        {
            destino = Arm.CaminhoParaNovoDocumento(modelo, modelo.Categoria, Local, nome);
            DocxTemplate.Fill(Arm.UrlDoModelo(modelo), destino, valoresFinais);
        }
        catch (Exception ex)
        {
            Ui.Erro($"Não foi possível gerar o documento. {ex.Message}");
            return;
        }
        // Impede um segundo clique de gerar outro documento igual.
        _geracaoConcluida = true;

        Guid? pacienteId = null;
        if (nome.Length > 0)
        {
            var paciente = PacienteCadastrado ?? CriarPaciente();
            pacienteId = paciente.Id;
            if (Arm.Local(paciente.LocalId) == null) paciente.LocalId = Local?.Id;
            CamposAutomaticos.Guardar(valoresFinais, modelo.Campos, paciente);
        }
        var documento = new DocumentoGerado { Modelo = modelo.Nome, ModeloId = modelo.Id, Paciente = nome, PacienteId = pacienteId, Local = Local?.Nome, Caminho = destino };
        try { Arm.Registrar(documento); }
        catch (Exception ex)
        {
            // O documento já existe: o aviso diz onde ele está, para não se perder.
            Registro.Erro("registrar documento", ex);
            _ultimoDocumento = destino;
            Atualizar();
            Ui.Erro($"O documento foi salvo em:\n{destino}\n\nMas os dados do app (histórico) não puderam ser gravados agora; isso é tentado de novo na próxima alteração. Ele não foi colocado na fila.\n\n{ex.Message}");
            return;
        }
        // O nome do paciente fica copiado, pronto para colar (Ctrl+V) em outro sistema.
        var copiado = "";
        if (nome.Length > 0) { try { Clipboard.SetText(nome); copiado = " · nome copiado"; } catch { } }
        _aviso = $"Salvo: {Path.GetFileName(destino)}{copiado}";
        _ultimoDocumento = destino;
        var acao = Ajustes.Atual.AcaoDepoisDeGerar;
        ItemFilaImpressao? item = null;
        if (acao is "fila" or "imprimir")
        {
            var jaImprimindo = Arm.ProcessandoFila;
            item = Arm.AdicionarAFila(documento, _copias);
            if (acao == "imprimir" && !jaImprimindo) _aviso = $"Salvo e enviado para impressão{copiado}";
            else if (acao == "imprimir") _aviso = $"Salvo; a fila está imprimindo, este entra na fila ({Arm.PendentesNaFila} aguardando){copiado}";
            else _aviso = $"Salvo e na fila de impressão ({Arm.PendentesNaFila} aguardando){copiado}";
            if (acao == "imprimir" && jaImprimindo) item = null;
        }
        Atualizar();
        // "Só abrir" abre sempre: o documento não fica guardado, é para usar agora.
        if (Ajustes.Atual.AbrirAoGerar || acao == "salvar") Ui.Abrir(destino);
        Arm.LimparDocumentosTemporarios();
        if (item != null && acao == "imprimir")
        {
            _imprimindo = true; AtualizarSecaoSalvar();
            await Arm.ImprimirFilaAsync([item.Id], JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
            _imprimindo = false;
            if (item.Situacao != SituacaoImpressao.Enviado) Ui.Erro($"{item.Observacao}\n\nVeja a aba Fila de impressão.");
            AtualizarSecaoSalvar();
        }
    }

    // MARK: Painel lateral (fila e atendidos recentemente)

    private void AtualizarPainel()
    {
        var p = Ui.Coluna(12);
        var pendentes = Arm.Dados.FilaImpressao.Where(x => x.Situacao == SituacaoImpressao.Pendente).Select(x => x.Id).ToList();
        if (Arm.ProcessandoFila)
        {
            p.Add(Ui.Linha(8, new ProgressBar { IsIndeterminate = true, Width = 60, Height = 4, VerticalAlignment = VerticalAlignment.Center }, Ui.Texto("Imprimindo…", Estilo.Normal, Tema.TextoSecundario)));
            var parar = Ui.Botao("Parar impressões", _janela.PararImpressoes, glifo: Ui.GParar);
            parar.HorizontalAlignment = HorizontalAlignment.Stretch;
            p.Add(parar);
        }
        else if (pendentes.Count > 0)
        {
            var fila = Ui.Botao($"Imprimir fila ({pendentes.Count})", () => Ui.ExecutarAsync(async () =>
            {
                await Arm.ImprimirFilaAsync(pendentes, JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
                var problemas = Arm.Dados.FilaImpressao.Where(x => pendentes.Contains(x.Id) && x.Situacao is SituacaoImpressao.Erro or SituacaoImpressao.Incerto).ToList();
                if (problemas.Count > 0) Ui.Erro($"{Texto.Plural(problemas.Count, "documento não foi impresso", "documentos não foram impressos")}. Veja a aba Fila de impressão.");
            }), principal: true, glifo: Ui.GImpressora, dica: $"Imprime em {JanelaPrincipal.NomeDaImpressora}, um por vez");
            fila.HorizontalAlignment = HorizontalAlignment.Stretch;
            p.Add(fila);
        }

        p.Add(Ui.TituloSecao("Atendidos recentemente", Ui.GRelogio));
        var locais = new List<(string, string)> { ("Todos os locais", "") };
        locais.AddRange(Arm.LocaisOrdenados.Where(l => l.Nome.Length > 0).Select(l => (l.Nome, l.Nome)));
        var filtroLocal = Ajustes.Atual.FiltroLocalRecentes;
        if (filtroLocal.Length > 0 && !locais.Any(l => l.Item2 == filtroLocal)) filtroLocal = "";
        p.Add(Ui.Escolha(locais, filtroLocal, v => { Ajustes.Atual.FiltroLocalRecentes = v; Ajustes.Salvar(); AtualizarPainel(); }, 160));
        var dias = new List<(string, DateTime?)> { ("Todos os dias", null) };
        dias.AddRange(Arm.DiasComAtendimento.Take(60).Select(d => (d == DateTime.Today ? "Hoje" : d == DateTime.Today.AddDays(-1) ? "Ontem" : d.ToString("dd/MM/yyyy (ddd)", PtBr), (DateTime?)d)));
        if (_filtroDia != null && !dias.Any(d => d.Item2 == _filtroDia)) _filtroDia = null;
        p.Add(Ui.Escolha(dias, _filtroDia, v => { _filtroDia = v; AtualizarPainel(); }, 160));

        var recentes = Arm.PacientesRecentes(filtroLocal.Length == 0 ? null : filtroLocal, _filtroDia, 12)
            .OrderBy(r => r.Paciente.Nome, Texto.Ordem).ToList();
        if (recentes.Count == 0) p.Add(Ui.Texto("Nenhum paciente atendido com esses filtros.", Estilo.Apoio));
        foreach (var (paciente, ultimo) in recentes) p.Add(LinhaDoRecente(paciente, ultimo));
        _painel.Content = p;
    }

    private FrameworkElement LinhaDoRecente(Paciente paciente, DocumentoGerado ultimo)
    {
        var bloco = Ui.Coluna(4);
        var textos = Ui.Coluna(1);
        var nome = Ui.Texto(paciente.Nome);
        nome.FontWeight = FontWeights.SemiBold;
        nome.TextWrapping = TextWrapping.NoWrap; nome.TextTrimming = TextTrimming.CharacterEllipsis;
        textos.Add(nome);
        var detalhe = Ui.Texto(string.Join(" · ", new[] { ultimo.Modelo, ultimo.Local ?? "", ultimo.Data.LocalDateTime.ToString("dd/MM HH:mm") }.Where(t => t.Length > 0)), Estilo.Legenda, Tema.TextoSecundario);
        detalhe.TextWrapping = TextWrapping.NoWrap; detalhe.TextTrimming = TextTrimming.CharacterEllipsis;
        textos.Add(detalhe);
        var escolher = new Button
        {
            Content = textos, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(4, 3, 4, 3), ToolTip = $"{paciente.Nome}\nUsar este paciente no documento"
        };
        escolher.Click += (_, _) => Ui.Executar(() => EscolherPaciente(paciente));
        var aberto = _abertosNoPainel.Contains(paciente.Id);
        var mostrar = Ui.BotaoIcone(aberto ? Ui.GSetaBaixo : Ui.GProximo, aberto ? "Esconder os documentos" : "Ver os documentos deste paciente",
            () => { if (!_abertosNoPainel.Remove(paciente.Id)) _abertosNoPainel.Add(paciente.Id); AtualizarPainel(); });
        mostrar.VerticalAlignment = VerticalAlignment.Center;
        var linha = new DockPanel();
        DockPanel.SetDock(mostrar, Dock.Right);
        linha.Children.Add(mostrar);
        linha.Children.Add(escolher);
        bloco.Add(linha);
        if (aberto)
        {
            var documentos = Ui.Coluna(4);
            documentos.Margin = new Thickness(12, 0, 0, 4);
            foreach (var documento in Arm.DocumentosDe(paciente).Take(10))
            {
                var existe = File.Exists(documento.Caminho);
                var texto = $"{documento.Modelo} · {documento.Data.LocalDateTime:dd/MM/yy}";
                if (!existe) { documentos.Add(Ui.Texto(texto + " (arquivo não existe mais)", Estilo.Legenda, Tema.TextoSecundario)); continue; }
                var abrir = Ui.Link(texto, () => Ui.Abrir(documento.Caminho), Ui.GDocumento, Estilo.Apoio);
                var pasta = Ui.BotaoIcone(Ui.GPasta, "Mostrar no Explorador", () => Ui.MostrarNaPasta(documento.Caminho));
                var linhaDoc = new DockPanel();
                DockPanel.SetDock(pasta, Dock.Right);
                linhaDoc.Children.Add(pasta);
                linhaDoc.Children.Add(abrir);
                documentos.Add(linhaDoc);
            }
            bloco.Add(documentos);
        }
        return bloco;
    }

    /// <summary>Imprime já, passando pela fila (sem concorrer com "Imprimir todos").</summary>
    private async Task ImprimirAgora(string arquivo)
    {
        if (_imprimindo || Arm.ProcessandoFila) return;
        if (Arm.Dados.Historico.FirstOrDefault(d => d.Caminho == arquivo) is not { } documento) return;
        var item = Arm.AdicionarAFila(documento, _copias);
        if (item.Situacao != SituacaoImpressao.Pendente)
        {
            Ui.Erro("Este documento já foi enviado ou precisa de confirmação de reenvio. Veja a aba Fila de impressão.");
            return;
        }
        _imprimindo = true; AtualizarSecaoSalvar();
        await Arm.ImprimirFilaAsync([item.Id], JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
        _imprimindo = false;
        if (item.Situacao != SituacaoImpressao.Enviado) Ui.Erro($"{item.Observacao}\n\nVeja a aba Fila de impressão.");
        AtualizarSecaoSalvar();
    }
}
