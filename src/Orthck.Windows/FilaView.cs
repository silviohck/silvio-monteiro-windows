using System.Windows;
using System.Windows.Controls;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>
/// Fila de impressão (FilaImpressaoView do Mac): documentos aguardando, na ordem em que serão impressos. Fica salva ao
/// fechar o app. Remover um item nunca apaga o .docx nem o histórico; erros e envios incertos só voltam com confirmação.
/// </summary>
public sealed class FilaView : UserControl
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;
    private readonly HashSet<Guid> _marcados = [];

    public FilaView(JanelaPrincipal janela)
    {
        _janela = janela;
        Arm.Mudou += AoMudar;
        Unloaded += (_, _) => Arm.Mudou -= AoMudar;
        Montar();
    }

    private void AoMudar() => Dispatcher.InvokeAsync(Montar);

    private List<ItemFilaImpressao> Fila => Arm.Dados.FilaImpressao;
    private List<ItemFilaImpressao> Pendentes => Fila.Where(x => x.Situacao == SituacaoImpressao.Pendente).ToList();

    private void Montar()
    {
        if (Fila.Count == 0)
        {
            Content = Ui.AvisoVazio("Fila vazia", Ui.GImpressora,
                "Na tela Gerar documento, deixe “Depois de gerar” em “Adicionar à fila de impressão”. Ao fim dos atendimentos, imprima tudo aqui de uma vez.");
            return;
        }
        var doca = new DockPanel();
        var cabecalho = new Border { Padding = new Thickness(26, 18, 26, 18), BorderThickness = new Thickness(0, 0, 0, 1), Child = Cabecalho() };
        cabecalho.SetResourceReference(Border.BackgroundProperty, Tema.Superficie);
        cabecalho.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        DockPanel.SetDock(cabecalho, Dock.Top);
        doca.Children.Add(cabecalho);
        var nota = Ui.Texto("“Enviado” quer dizer que o Word recebeu o documento; confira na impressora. Itens com erro ou envio interrompido só são reenviados com sua confirmação, para não sair em dobro.", Estilo.Legenda);
        nota.Margin = new Thickness(26, 10, 26, 14);
        DockPanel.SetDock(nota, Dock.Bottom);
        doca.Children.Add(nota);
        var lista = Ui.Coluna(8);
        lista.Margin = new Thickness(22, 14, 22, 6);
        for (var i = 0; i < Fila.Count; i++) lista.Add(Linha(Fila[i], i + 1));
        var rolagem = new ScrollViewer { Content = lista, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        doca.Children.Add(rolagem);
        var fundo = new Border { Child = doca };
        fundo.SetResourceReference(Border.BackgroundProperty, Tema.Fundo);
        Content = fundo;
    }

    private FrameworkElement Cabecalho()
    {
        var p = Ui.Coluna(10);
        var titulo = Ui.TituloSecao("Fila de impressão", Ui.GImpressora);
        titulo.Margin = new Thickness(0);
        UIElement direita = Arm.ProcessandoFila
            ? Ui.Linha(8, new ProgressBar { IsIndeterminate = true, Width = 80, Height = 4, VerticalAlignment = VerticalAlignment.Center }, Ui.Texto("Imprimindo um por vez…", Estilo.Normal, Tema.TextoSecundario))
            : new TextBlock();
        p.Add(Ui.Extremos(titulo, direita));
        var pendentes = Pendentes;
        var folhas = pendentes.Sum(x => x.Copias);
        var enviados = Fila.Count(x => x.Situacao == SituacaoImpressao.Enviado);
        var partes = new List<string> { $"{pendentes.Count} aguardando" };
        if (folhas != pendentes.Count) partes.Add($"{folhas} cópias no total");
        if (enviados > 0) partes.Add(Texto.Plural(enviados, "enviado", "enviados"));
        p.Add(Ui.Texto(string.Join(" · ", partes) + $" — imprime em: {JanelaPrincipal.NomeDaImpressora} (troque na barra lateral)", Estilo.Apoio));

        var todos = Ui.Botao($"Imprimir todos ({pendentes.Count})", () => _ = Imprimir(pendentes.Select(x => x.Id).ToHashSet()), principal: true, glifo: Ui.GImpressora);
        todos.IsEnabled = pendentes.Count > 0 && !Arm.ProcessandoFila;
        todos.Padding = new Thickness(16, 9, 16, 9);
        var marcadosPendentes = pendentes.Where(x => _marcados.Contains(x.Id)).Select(x => x.Id).ToHashSet();
        var marcados = Ui.Botao($"Imprimir marcados ({marcadosPendentes.Count})", () => _ = Imprimir(marcadosPendentes));
        marcados.IsEnabled = marcadosPendentes.Count > 0 && !Arm.ProcessandoFila;
        marcados.Padding = new Thickness(16, 9, 16, 9);
        var esquerda = Ui.Linha(10, todos, marcados);
        UIElement limpar = new TextBlock();
        if (enviados > 0)
        {
            var b = Ui.Botao("Limpar enviados", Arm.LimparEnviadosDaFila, dica: "Tira da lista os já enviados; os arquivos Word continuam salvos");
            b.IsEnabled = !Arm.ProcessandoFila;
            limpar = b;
        }
        p.Add(Ui.Extremos(esquerda, limpar));
        return p;
    }

    private FrameworkElement Linha(ItemFilaImpressao item, int posicao)
    {
        var existe = File.Exists(item.Caminho);
        var pendente = item.Situacao == SituacaoImpressao.Pendente;
        var marcar = new CheckBox { IsChecked = _marcados.Contains(item.Id), IsEnabled = pendente && !Arm.ProcessandoFila, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Marcar" };
        marcar.Click += (_, _) => { if (marcar.IsChecked == true) _marcados.Add(item.Id); else _marcados.Remove(item.Id); Montar(); };
        var numero = Ui.Texto(posicao.ToString(), Estilo.Codigo, Tema.TextoSecundario);
        numero.MinWidth = 24; numero.TextAlignment = TextAlignment.Right; numero.VerticalAlignment = VerticalAlignment.Center;
        numero.Margin = new Thickness(4, 0, 12, 0);

        var textos = Ui.Coluna(2);
        var titulo = Ui.Texto(item.Paciente.Length == 0 ? item.Modelo : $"{item.Paciente} — {item.Modelo}");
        titulo.FontWeight = FontWeights.SemiBold; titulo.TextWrapping = TextWrapping.NoWrap; titulo.TextTrimming = TextTrimming.CharacterEllipsis;
        textos.Add(titulo);
        textos.Add(Ui.Texto($"{item.Local ?? "Sem local"} · {item.CriadoEm:dd/MM HH:mm}", Estilo.Legenda));
        if (item.Observacao is { } obs && !pendente)
            textos.Add(Ui.Texto(obs, Estilo.Legenda, item.Situacao is SituacaoImpressao.Erro or SituacaoImpressao.Incerto ? Tema.Aviso : Tema.TextoSecundario));
        if (!existe) textos.Add(Ui.Linha(6, Ui.Icone(Ui.GAviso, 11, Tema.Aviso), Ui.Texto("Arquivo não encontrado", Estilo.Legenda, Tema.Aviso)));
        textos.VerticalAlignment = VerticalAlignment.Center;

        var direita = Ui.Linha(8);
        var (rotulo, cor, fundo) = item.Situacao switch
        {
            SituacaoImpressao.Pendente => ("Aguardando", Tema.Destaque, Tema.DestaqueSuave),
            SituacaoImpressao.Enviando => ("Imprimindo…", Tema.Destaque, Tema.DestaqueSuave),
            SituacaoImpressao.Enviado => ("Enviado", Tema.Sucesso, Tema.SucessoSuave),
            SituacaoImpressao.Erro => ("Erro", Tema.Aviso, Tema.AvisoSuave),
            _ => ("Conferir", Tema.Aviso, Tema.AvisoSuave)
        };
        direita.Add(Ui.Etiqueta(rotulo, cor, fundo));
        if (pendente)
        {
            var copias = GerarView.SeletorDeCopias(item.Copias, v => Arm.DefinirCopias(v, item.Id), "Cópias deste documento");
            copias.IsEnabled = !Arm.ProcessandoFila;
            direita.Add(copias);
        }
        else direita.Add(Ui.Texto(item.Copias == 1 ? "1 cópia" : $"{item.Copias} cópias", Estilo.Normal, Tema.TextoSecundario));
        if (item.Situacao is SituacaoImpressao.Erro or SituacaoImpressao.Incerto)
        {
            var reenviar = Ui.Botao("Reenviar…", () => Reenviar(item));
            reenviar.IsEnabled = !Arm.ProcessandoFila && existe;
            direita.Add(reenviar);
        }
        var abrir = Ui.BotaoIcone(Ui.GAbrir, "Abrir no Word", () => Ui.Abrir(item.Caminho), Tema.Destaque); abrir.IsEnabled = existe;
        var pasta = Ui.BotaoIcone(Ui.GPastaAberta, "Mostrar no Explorador", () => Ui.MostrarNaPasta(item.Caminho), Tema.Destaque); pasta.IsEnabled = existe;
        var remover = Ui.BotaoIcone(Ui.GFechar, "Tirar da fila (o documento continua salvo)", () => { _marcados.Remove(item.Id); Arm.RemoverDaFila(item.Id); });
        remover.IsEnabled = !Arm.ProcessandoFila;
        direita.Add(abrir); direita.Add(pasta); direita.Add(remover);
        direita.VerticalAlignment = VerticalAlignment.Center;

        var linha = new DockPanel();
        var inicio = Ui.Linha(0, marcar, numero);
        DockPanel.SetDock(inicio, Dock.Left);
        DockPanel.SetDock(direita, Dock.Right);
        direita.Margin = new Thickness(12, 0, 0, 0);
        linha.Children.Add(inicio); linha.Children.Add(direita); linha.Children.Add(textos);
        var cartao = Ui.Cartao(linha, Tema.Superficie, 10, 12);
        cartao.Opacity = pendente || item.Situacao == SituacaoImpressao.Enviando ? 1 : 0.8;
        return cartao;
    }

    private void Reenviar(ItemFilaImpressao item)
    {
        if (!Ui.Confirmar("Reenviar este documento?", $"Confira antes se {item.Modelo} — {item.Paciente} já foi impresso. Reenviar pode produzir cópias duplicadas.", true)) return;
        if (Arm.Reenfileirar(item.Id)) _ = Imprimir([item.Id]);
    }

    private async Task Imprimir(HashSet<Guid> ids)
    {
        if (ids.Count == 0 || Arm.ProcessandoFila) return;
        _marcados.ExceptWith(ids);
        if (!Word.Instalado) { Ui.Erro("Para imprimir pelo app é preciso ter o Microsoft Word (desktop) instalado."); return; }
        await Arm.ImprimirFilaAsync(ids, JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
        var incerto = Arm.Dados.FilaImpressao.FirstOrDefault(x => ids.Contains(x.Id) && x.Situacao == SituacaoImpressao.Incerto);
        if (incerto != null)
            Ui.Erro($"O envio de “{incerto.Paciente} — {incerto.Modelo}” ficou incerto e a impressão parou para não sair em dobro.\n\n{incerto.Observacao}\n\nConfira a impressora; os demais continuam aguardando na fila.");
    }
}

/// <summary>Histórico (HistoricoView do Mac): documentos gerados, abrir, imprimir, pôr na fila.</summary>
public sealed class HistoricoView : UserControl
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;
    private readonly TextBox _busca = Ui.CaixaDeTexto(dica: "Buscar por paciente, modelo ou local");
    private readonly StackPanel _lista = Ui.Coluna(8);
    private readonly TextBlock _contagem = Ui.Texto("", Estilo.Normal, Tema.TextoSecundario);
    private Guid? _imprimindo;

    public HistoricoView(JanelaPrincipal janela)
    {
        _janela = janela;
        if (Arm.Dados.Historico.Count == 0) { Content = Ui.AvisoVazio("Nenhum documento gerado ainda", Ui.GRelogio); return; }
        var doca = new DockPanel();
        var limpar = Ui.Botao("Limpar histórico…", () =>
        {
            if (!Ui.Confirmar("Limpar o histórico?", "Apenas a lista é apagada. Os arquivos dos documentos não são apagados e continuam nas pastas onde foram salvos.", true)) return;
            Arm.Dados.Historico.Clear(); Arm.Salvar();
            Content = Ui.AvisoVazio("Nenhum documento gerado ainda", Ui.GRelogio);
        }, glifo: Ui.GLixeira);
        _busca.Width = 320;
        var topo = new Border { Padding = new Thickness(24, 14, 24, 14), BorderThickness = new Thickness(0, 0, 0, 1), Child = Ui.Extremos(Ui.Linha(16, _contagem, _busca), limpar) };
        topo.SetResourceReference(Border.BorderBrushProperty, Tema.Borda);
        topo.SetResourceReference(Border.BackgroundProperty, Tema.Superficie);
        _contagem.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(topo, Dock.Top);
        doca.Children.Add(topo);
        _lista.Margin = new Thickness(22, 14, 22, 18);
        doca.Children.Add(new ScrollViewer { Content = _lista, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var fundo = new Border { Child = doca };
        fundo.SetResourceReference(Border.BackgroundProperty, Tema.Fundo);
        Content = fundo;
        _busca.TextChanged += (_, _) => Recarregar();
        Recarregar();
    }

    private void Recarregar()
    {
        _lista.Children.Clear();
        var termo = _busca.Text.Trim();
        var docs = Arm.Dados.Historico.Where(d => termo.Length == 0 || Texto.Contem(d.Paciente, termo) || Texto.Contem(d.Modelo, termo) || Texto.Contem(d.Local, termo)).ToList();
        _contagem.Text = Texto.Plural(Arm.Dados.Historico.Count, "documento", "documentos");
        // Os mais recentes primeiro; no máximo 300 na tela para continuar rápida.
        foreach (var d in docs.Take(300)) _lista.Add(Linha(d));
        if (docs.Count > 300) _lista.Add(Ui.Texto($"Mostrando os 300 mais recentes de {docs.Count}. Use a busca para encontrar os outros.", Estilo.Legenda));
    }

    private FrameworkElement Linha(DocumentoGerado documento)
    {
        var existe = File.Exists(documento.Caminho);
        var textos = Ui.Coluna(2);
        var titulo = Ui.Texto(string.Join(" — ", new[] { documento.Modelo, documento.Paciente, documento.Local ?? "" }.Where(t => t.Length > 0)));
        titulo.FontWeight = FontWeights.SemiBold;
        textos.Add(titulo);
        textos.Add(Ui.Texto($"{documento.Data:dd/MM/yyyy HH:mm} · {(existe ? Path.GetFileName(documento.Caminho) : "arquivo não encontrado")}", Estilo.Legenda, existe ? null : Tema.Aviso));
        textos.VerticalAlignment = VerticalAlignment.Center;

        var abrir = Ui.Botao("Abrir", () => Ui.Abrir(documento.Caminho)); abrir.IsEnabled = existe;
        var pasta = Ui.BotaoIcone(Ui.GPastaAberta, "Mostrar no Explorador", () => Ui.MostrarNaPasta(documento.Caminho), Tema.Destaque); pasta.IsEnabled = existe;
        var copias = Arm.Modelo(documento.ModeloId)?.Copias ?? 1;
        var imprimir = Ui.Botao(_imprimindo == documento.Id ? "Imprimindo…" : "Imprimir", () => _ = Imprimir(documento, copias));
        imprimir.IsEnabled = existe && _imprimindo == null && !Arm.ProcessandoFila;
        var naFila = Arm.Dados.FilaImpressao.Any(x => x.DocumentoId == documento.Id && x.Situacao == SituacaoImpressao.Pendente);
        var porNaFila = Ui.Botao(naFila ? "Na fila" : "Pôr na fila", () =>
        {
            if (Arm.Dados.FilaImpressao.Any(x => x.DocumentoId == documento.Id && x.Situacao == SituacaoImpressao.Enviado)
                && !Ui.Confirmar("Este documento já foi enviado ao Word.", "Colocar novamente na fila para reimprimir?")) return;
            Arm.AdicionarAFila(documento, copias);
            Recarregar();
        }, dica: "Guardar na Fila de impressão para imprimir junto com os outros");
        porNaFila.IsEnabled = existe && !naFila;
        var remover = Ui.BotaoIcone(Ui.GFechar, "Remover da lista (o arquivo não é apagado)", () =>
        {
            Arm.Dados.Historico.RemoveAll(d => d.Id == documento.Id); Arm.Salvar(); Recarregar();
        });
        var direita = Ui.Linha(8, abrir, pasta, imprimir, porNaFila, remover);
        direita.VerticalAlignment = VerticalAlignment.Center;
        direita.Margin = new Thickness(12, 0, 0, 0);
        var linha = new DockPanel();
        DockPanel.SetDock(direita, Dock.Right);
        linha.Children.Add(direita); linha.Children.Add(textos);
        return Ui.Cartao(linha, Tema.Superficie, 10, 12);
    }

    /// <summary>Imprime passando pela fila (um por vez, sem concorrer com "Imprimir todos").</summary>
    private async Task Imprimir(DocumentoGerado documento, int copias)
    {
        if (Arm.Dados.FilaImpressao.Any(x => x.DocumentoId == documento.Id && x.Situacao == SituacaoImpressao.Enviado)
            && !Ui.Confirmar("Este documento já foi enviado ao Word.", "Imprimir de novo?")) return;
        var item = Arm.AdicionarAFila(documento, copias);
        _imprimindo = documento.Id; Recarregar();
        await Arm.ImprimirFilaAsync([item.Id], JanelaPrincipal.ImpressoraEscolhida, _janela.Word);
        _imprimindo = null; Recarregar();
        if (item.Situacao != SituacaoImpressao.Enviado) Ui.Erro($"{item.Observacao}\n\nVeja a aba Fila de impressão.");
    }
}

/// <summary>Pesquisa numa tabela completa (CID-10, SIGTAP, TUSS), com cópia do código (TabelaCodigosView do Mac).</summary>
public sealed class TabelaCodigosView : UserControl
{
    private readonly TabelaDeCodigos _tabela;
    private readonly string _chave;
    private readonly TextBox _busca;
    private readonly ListBox _lista = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, ItemContainerStyle = Ui.ItemDeLista() };
    private readonly TextBlock _contagem = Ui.Texto("", Estilo.Legenda);
    private readonly ContentControl _copiado = new();
    private readonly bool _recuar;
    private System.Windows.Threading.DispatcherTimer? _espera;

    public TabelaCodigosView(TabelaDeCodigos tabela, string chave)
    {
        _tabela = tabela; _chave = chave;
        _recuar = tabela.Codigos.Any(c => c.EhTitulo);
        _busca = Ui.CaixaDeTexto(dica: $"Buscar por código ou descrição (ex.: {tabela.ExemploDeBusca})");
        var topo = Ui.Coluna(8);
        topo.Margin = new Thickness(22, 16, 22, 10);
        topo.Add(Ui.TituloSecao(tabela.Nome, chave == "cid" ? Ui.GEstetoscopio : chave == "sigtap" ? Ui.GCruz : Ui.GLista));
        topo.Add(_busca);
        var linha = new WrapPanel { Tag = 12.0 };
        if (tabela.Grupos.Count > 0)
        {
            var opcoes = new List<(string, int)> { ($"Todos os {tabela.NomeDoGrupo}s", 0) };
            opcoes.AddRange(tabela.Grupos.Select(g => (g.Nome, g.Numero)));
            var grupo = Ajustes.Atual.GrupoDasTabelas.GetValueOrDefault(chave);
            var escolha = Ui.Escolha(opcoes, grupo, v => { Ajustes.Atual.GrupoDasTabelas[chave] = v; Ajustes.Salvar(); Recarregar(); }, 300);
            escolha.MaxWidth = 560;
            linha.Add(escolha);
        }
        var fonte = Ui.Texto(tabela.Fonte, Estilo.Legenda); fonte.VerticalAlignment = VerticalAlignment.Center;
        linha.Add(fonte);
        _contagem.VerticalAlignment = VerticalAlignment.Center;
        linha.Add(_contagem);
        topo.Add(linha);

        var doca = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        doca.Children.Add(topo);
        _copiado.Margin = new Thickness(22, 6, 22, 10);
        DockPanel.SetDock(_copiado, Dock.Bottom);
        doca.Children.Add(_copiado);
        _lista.Margin = new Thickness(14, 0, 14, 0);
        VirtualizingPanel.SetIsVirtualizing(_lista, true);
        VirtualizingPanel.SetVirtualizationMode(_lista, VirtualizationMode.Recycling);
        ScrollViewer.SetHorizontalScrollBarVisibility(_lista, ScrollBarVisibility.Disabled);
        doca.Children.Add(_lista);
        var fundo = new Border { Child = doca };
        fundo.SetResourceReference(Border.BackgroundProperty, Tema.Fundo);
        Content = fundo;

        _busca.TextChanged += (_, _) =>
        {
            // Espera o usuário parar de digitar um instante antes de buscar em milhares de códigos.
            _espera?.Stop();
            _espera = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _espera.Tick += (_, _) => { _espera!.Stop(); Recarregar(); };
            _espera.Start();
        };
        _lista.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.C && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)
                && _lista.SelectedItem is ListBoxItem { Tag: CodigoDaTabela c }) { Copiar(c.TextoCompleto); e.Handled = true; }
        };
        Loaded += (_, _) => _busca.Focus();
        Recarregar();
    }

    private void Recarregar()
    {
        var grupo = Ajustes.Atual.GrupoDasTabelas.GetValueOrDefault(_chave);
        var resultados = _tabela.Buscar(_busca.Text, grupo == 0 ? null : grupo);
        _contagem.Text = Texto.Plural(resultados.Count, "código", "códigos");
        _lista.Items.Clear();
        // A lista mostra no máximo 500 linhas; a busca afina o resto.
        foreach (var c in resultados.Take(500)) _lista.Items.Add(Linha(c));
        if (resultados.Count > 500)
            _lista.Items.Add(new ListBoxItem { Content = Ui.Texto($"Mostrando 500 de {resultados.Count}. Digite mais palavras para afinar.", Estilo.Legenda), IsEnabled = false });
        if (resultados.Count == 0)
            _lista.Items.Add(new ListBoxItem
            {
                Content = Ui.Texto(_tabela.Grupos.Count == 0 ? "Nenhum código encontrado. Tente outras palavras." : $"Nenhum código encontrado. Tente outras palavras ou escolha “Todos os {_tabela.NomeDoGrupo}s”.", Estilo.Normal, Tema.TextoSecundario),
                IsEnabled = false
            });
    }

    private ListBoxItem Linha(CodigoDaTabela codigo)
    {
        var c = Ui.Texto(codigo.Codigo, Estilo.Codigo);
        c.FontWeight = codigo.EhTitulo ? FontWeights.Bold : FontWeights.Normal;
        c.MinWidth = 120; c.TextWrapping = TextWrapping.NoWrap;
        var d = Ui.Texto(codigo.Descricao);
        d.FontWeight = codigo.EhTitulo ? FontWeights.SemiBold : FontWeights.Normal;
        d.Margin = new Thickness(10, 0, 8, 0);
        // Logo depois da descrição, para não precisar atravessar a tela.
        var copiar = Ui.BotaoIcone(Ui.GCopiar, $"Copiar “{codigo.TextoCompleto}”", () => Copiar(codigo.TextoCompleto), Tema.Destaque);
        copiar.Padding = new Thickness(4);
        var linha = new DockPanel();
        DockPanel.SetDock(c, Dock.Left);
        linha.Children.Add(c);
        var resto = new DockPanel { LastChildFill = false };
        resto.Children.Add(d);
        resto.Children.Add(copiar);
        linha.Children.Add(resto);
        d.MaxWidth = 900;
        var item = new ListBoxItem { Content = linha, Tag = codigo, Padding = new Thickness(codigo.EhTitulo || !_recuar ? 6 : 26, 3, 6, 3) };
        var menu = new ContextMenu();
        var tudo = new MenuItem { Header = "Copiar código e descrição" }; tudo.Click += (_, _) => Copiar(codigo.TextoCompleto);
        var so = new MenuItem { Header = "Copiar só o código" }; so.Click += (_, _) => Copiar(codigo.Codigo);
        menu.Items.Add(tudo); menu.Items.Add(so);
        item.ContextMenu = menu;
        return item;
    }

    private void Copiar(string texto)
    {
        Ui.Copiar(texto);
        _copiado.Content = Ui.Linha(6, Ui.Icone(Ui.GOkCirculo, 13, Tema.Sucesso), Ui.Texto($"Copiado: {texto}", Estilo.Normal, Tema.Sucesso));
    }
}
