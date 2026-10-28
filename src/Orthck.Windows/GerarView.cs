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
    private string? _pastaAvulsa;
    private bool _imprimindo;
    private bool _ignorarNome;

    // Controles que não podem ser recriados enquanto se digita.
    private readonly TextBox _nome = Ui.CaixaDeTexto(dica: "Nome do paciente");
    private readonly TextBox _nascimentoDoNovo = Ui.CaixaDeTexto(dica: "Data de nascimento (dd/mm/aaaa) — ajuda a diferenciar");
    private readonly ContentControl _quadroDoLocal = new(), _documentoPronto = new(), _secaoDocumento = new(), _situacaoPaciente = new(),
        _secaoCampos = new(), _secaoSalvar = new();
    private readonly StackPanel _pagina = Ui.Coluna(0);
    private readonly ContentControl _raiz = new();

    public GerarView(JanelaPrincipal janela)
    {
        _janela = janela;
        Content = _raiz;
        _nome.TextChanged += (_, _) => { if (!_ignorarNome) NomeMudou(); };
        _nome.MinWidth = 260;

        _pagina.Children.Add(_quadroDoLocal);
        _pagina.Children.Add(_documentoPronto);
        _pagina.Children.Add(_secaoDocumento);
        _pagina.Children.Add(MontarSecaoPaciente());
        _pagina.Children.Add(_secaoCampos);
        _pagina.Children.Add(_secaoSalvar);
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
        if (_raiz.Content is not ScrollViewer) _raiz.Content = Ui.Pagina(_pagina, 940);
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
        _secaoDocumento.Content = SecaoDocumento();
        AtualizarSituacaoPaciente();
        _secaoCampos.Content = Modelo is { } m ? SecaoCampos(m) : null;
        AtualizarSecaoSalvar();
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
        _pastaAvulsa = null;
        _ignorarNome = true; _nome.Text = ""; _ignorarNome = false;
        RecomecarAtendimento();
        _copias = Modelo?.Copias ?? 1;
        if (_raiz.Content is ScrollViewer s) s.ScrollToTop();
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
        corpo.Add(Ui.Rotulado("Modelo", modelo, 150));
        return Ui.Secao("Documento", Ui.GDocumento, corpo);
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

    /// <summary>Pacientes de todos os locais com o trecho digitado; os já atendidos no local escolhido vêm primeiro.</summary>
    private List<Paciente> Sugestoes()
    {
        if (NomeDigitado.Length < 2 || Homonimos.Count > 0) return [];
        var encontrados = Arm.PacientesOrdenados.Where(p => Texto.Contem(p.Nome, NomeDigitado)).ToList();
        var daqui = encontrados.Where(p => Arm.LocaisDoPaciente(p).Any(l => l.Id == Local?.Id)).ToList();
        return daqui.Concat(encontrados.Except(daqui)).Take(8).ToList();
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

    private string PastaDeDestino => _pastaAvulsa ?? Arm.PastaDosDocumentos(Modelo?.Categoria ?? _subpasta, Local);

    /// <summary>"Pastas já existentes" ou quais serão criadas.</summary>
    private string SituacaoDasPastas()
    {
        var pastaDoLocal = Arm.PastaDoLocal(Local);
        var novas = new List<string>();
        if (!Directory.Exists(pastaDoLocal)) novas.Add($"“{Path.GetFileName(pastaDoLocal)}”");
        if (PastaDeDestino != pastaDoLocal && !Directory.Exists(PastaDeDestino)) novas.Add($"“{Path.GetFileName(PastaDeDestino)}”");
        return novas.Count == 0 ? "Pasta do local e do tipo já existem; o arquivo vai direto para lá." : "Será criada ao gerar: " + string.Join(" e ", novas) + ".";
    }

    private void AtualizarSecaoSalvar()
    {
        if (Modelo is not { } modelo) { _secaoSalvar.Content = null; return; }
        var corpo = Ui.Coluna(12);

        var destino = Ui.Coluna(4);
        var titulo = Ui.Linha(6, Ui.Texto("Será salvo em"));
        if (_pastaAvulsa != null) titulo.Add(Ui.Texto("(só este documento)", Estilo.Legenda));
        destino.Add(titulo);
        var caminho = Ui.Texto(Ui.CaminhoAbreviado(PastaDeDestino), Estilo.Normal, Tema.TextoSecundario);
        caminho.ToolTip = PastaDeDestino;
        destino.Add(caminho);
        if (_pastaAvulsa == null) destino.Add(Ui.Texto(SituacaoDasPastas(), Estilo.Legenda));

        var maisOpcoes = new Button { Content = Ui.ConteudoDoBotao("Mais opções", Ui.GSetaBaixo), Padding = new Thickness(14, 7, 14, 7), MinHeight = 34 };
        var menu = new ContextMenu { PlacementTarget = maisOpcoes, Placement = PlacementMode.Bottom };
        if (Local is { } local)
        {
            var nomeDoLocal = local.Nome.Length == 0 ? "este local" : local.Nome;
            menu.Items.Add(ItemDeMenu($"Usar outra pasta só para {nomeDoLocal}…", () =>
            {
                if (Ui.EscolherPasta($"Escolha a pasta de {nomeDoLocal}. Se ela não tiver o nome do local, o app cria a pasta do local dentro dela.") is { } pasta)
                {
                    local.Pasta = pasta; Arm.Salvar(); _pastaAvulsa = null; AtualizarSecaoSalvar();
                }
            }));
            if (local.Pasta.Length > 0)
                menu.Items.Add(ItemDeMenu("Voltar a usar a pasta principal", () => { local.Pasta = ""; Arm.Salvar(); AtualizarSecaoSalvar(); }));
        }
        menu.Items.Add(ItemDeMenu("Só este documento…", () => { _pastaAvulsa = Ui.EscolherPasta("Escolha onde salvar só este documento."); AtualizarSecaoSalvar(); }));
        if (_pastaAvulsa != null) menu.Items.Add(ItemDeMenu("Voltar para a pasta do local", () => { _pastaAvulsa = null; AtualizarSecaoSalvar(); }));
        maisOpcoes.Click += (_, _) => menu.IsOpen = true;

        var botoesPasta = new WrapPanel { Tag = 8.0 };
        botoesPasta.Add(Ui.Botao("Escolher pasta principal…", () =>
        {
            if (Ui.EscolherPasta("Escolha a pasta principal. Dentro dela, o app usa (ou cria) uma pasta para cada local e, dentro desta, uma pasta para cada tipo de documento.") is not { } pasta) return;
            Arm.Dados.PastaDocumentos = pasta;
            // A pasta principal passa a valer também para este local.
            if (Local is { Pasta.Length: > 0 } l) l.Pasta = "";
            Arm.Salvar();
            _pastaAvulsa = null;
            AtualizarSecaoSalvar();
        }, glifo: Ui.GPasta));
        botoesPasta.Add(maisOpcoes);
        botoesPasta.Add(Ui.Botao("Abrir no Explorador", () => Ui.AbrirPasta(PastaDeDestino), glifo: Ui.GPastaAberta));
        destino.Add(botoesPasta, 8);
        corpo.Add(destino);
        corpo.Add(Ui.Divisor());

        var abrir = new CheckBox { Content = "Abrir o documento depois de gerar", IsChecked = Ajustes.Atual.AbrirAoGerar };
        abrir.Click += (_, _) => { Ajustes.Atual.AbrirAoGerar = abrir.IsChecked == true; Ajustes.Salvar(); };
        corpo.Add(abrir);

        var acao = Ui.Escolha([("Adicionar à fila de impressão", "fila"), ("Imprimir agora", "imprimir"), ("Só salvar", "salvar")], Ajustes.Atual.AcaoDepoisDeGerar,
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
                if (!_imprimindo && !Arm.ProcessandoFila && !jaEnviado) aviso.Children.Add(Ui.Link("Imprimir agora", () => _ = ImprimirAgora(ultimo)));
                else if (_imprimindo) aviso.Children.Add(Ui.Texto("Enviando…", Estilo.Normal, Tema.TextoSecundario));
            }
        }
        var gerar = Ui.Botao("Gerar documento", () => _ = Gerar(modelo), principal: true, glifo: Ui.GSalvar);
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

    private static MenuItem ItemDeMenu(string titulo, Action acao)
    {
        var item = new MenuItem { Header = titulo };
        item.Click += (_, _) => Ui.Executar(acao);
        return item;
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

    private async Task Gerar(ModeloDocumento modelo)
    {
        if (_geracaoConcluida || PrecisaEscolherPaciente) return;
        // O documento vai para a subpasta do próprio modelo.
        if (!Arm.ModelosPara(Local).Any(m => m.Id == modelo.Id))
        {
            Ui.Erro("Este modelo não é do local escolhido. Escolha a subpasta e o modelo de novo.");
            return;
        }
        var nome = NomeDigitado;
        var valoresFinais = new Dictionary<string, string>(_valores);
        foreach (var campo in modelo.Campos.Where(CamposAutomaticos.EhNomeDoPaciente)) valoresFinais[campo] = nome;

        string destino;
        try
        {
            destino = Arm.CaminhoParaNovoDocumento(modelo, modelo.Categoria, Local, nome, _pastaAvulsa);
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
        Arm.Registrar(documento);
        // O nome do paciente fica copiado, pronto para colar (Ctrl+V) em outro sistema.
        var copiado = "";
        if (nome.Length > 0) { try { Clipboard.SetText(nome); copiado = " · nome copiado"; } catch { } }
        _aviso = $"Salvo: {Path.GetFileName(destino)}{copiado}";
        _ultimoDocumento = destino;
        _pastaAvulsa = null;
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
        if (Ajustes.Atual.AbrirAoGerar) Ui.Abrir(destino);
        if (item != null && acao == "imprimir")
        {
            _imprimindo = true; AtualizarSecaoSalvar();
            await Arm.ImprimirFilaAsync([item.Id], JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
            _imprimindo = false;
            if (item.Situacao != SituacaoImpressao.Enviado) Ui.Erro($"{item.Observacao}\n\nVeja a aba Fila de impressão.");
            AtualizarSecaoSalvar();
        }
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
