using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>Tela Modelos (ModelosView do Mac): pasta sincronizada, importar, filtros por local e tipo, editor do modelo.</summary>
public sealed class ModelosView : UserControl
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;
    private readonly ListaDetalhe _tela = new(320);
    private readonly ContentControl _painelSincronizacao = new(), _filtros = new();
    private readonly TextBox _busca = Ui.CaixaDeTexto(dica: "Buscar modelo");
    private readonly Button _importar;
    private Guid? _selecao;

    public ModelosView(JanelaPrincipal janela)
    {
        _janela = janela;
        Content = _tela;
        _tela.Topo.Add(_painelSincronizacao);
        _importar = Ui.Botao("Importar modelo", ImportarArquivos, glifo: Ui.GImportar, dica: "Importa arquivos avulsos, fora da pasta sincronizada");
        _importar.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tela.Topo.Add(_importar);
        var importarPasta = Ui.Botao("Importar pasta…", ImportarPasta, glifo: Ui.GPasta,
            dica: "Importa todos os modelos de uma pasta e das subpastas dela (cada subpasta vira o tipo)");
        importarPasta.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tela.Topo.Add(importarPasta);
        _tela.Topo.Add(_filtros);
        _tela.Topo.Add(_busca);
        _busca.TextChanged += (_, _) => Recarregar();
        _tela.Lista.SelectionChanged += (_, _) =>
        {
            if (_tela.Lista.SelectedItem is ListBoxItem { Tag: Guid id } && id != _selecao) { _selecao = id; MostrarDetalhe(); }
        };
        _tela.Lista.KeyDown += (_, e) => { if (e.Key == Key.Delete && Arm.Modelo(_selecao) is { Origem: null } m) Excluir(m); };
        // Arrastar arquivos .docx para a janela importa para o local e o tipo dos filtros.
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] arquivos) Importar(arquivos); };
        _janela.SincronizacaoMudou += AoSincronizar;
        Unloaded += (_, _) => _janela.SincronizacaoMudou -= AoSincronizar;
        EscolherLocalSeNecessario();
        AtualizarTudo();
        MostrarDetalhe();
    }

    private void AoSincronizar() => Dispatcher.Invoke(() =>
    {
        _painelSincronizacao.Content = PainelSincronizacao();
        if (!Arm.Sincronizacao.EmAndamento) { EscolherLocalSeNecessario(); AtualizarTudo(); if (Arm.Modelo(_selecao) == null) { _selecao = null; MostrarDetalhe(); } }
    });

    private LocalAtendimento? LocalFiltrado => Arm.Dados.Locais.FirstOrDefault(l => l.Id.ToString() == Ajustes.Atual.FiltroLocalModelos);

    /// <summary>Sempre há um local escolhido no filtro, se houver locais cadastrados.</summary>
    private void EscolherLocalSeNecessario()
    {
        if (LocalFiltrado != null) return;
        Ajustes.Atual.FiltroLocalModelos = Arm.LocaisOrdenados.FirstOrDefault()?.Id.ToString() ?? "";
        Ajustes.Salvar();
    }

    /// <summary>Tipos do local escolhido e dos modelos que aparecem para ele.</summary>
    private List<string> TiposDoFiltro
    {
        get
        {
            if (LocalFiltrado is not { } local) return Arm.Tipos(null);
            return Texto.EmOrdemAlfabetica(Arm.Tipos(local.Id).Concat(Arm.ModelosPara(local).Select(m => m.Categoria)).Distinct());
        }
    }

    private void AtualizarTudo()
    {
        _painelSincronizacao.Content = PainelSincronizacao();
        if (Ajustes.Atual.FiltroTipoModelos.Length > 0 && !TiposDoFiltro.Contains(Ajustes.Atual.FiltroTipoModelos)) Ajustes.Atual.FiltroTipoModelos = "";
        var tipo = Ajustes.Atual.FiltroTipoModelos.Length == 0 ? "modelo" : Ajustes.Atual.FiltroTipoModelos;
        _importar.Content = Ui.ConteudoDoBotao(LocalFiltrado is { } l ? $"Importar {tipo} para {(l.Nome.Length == 0 ? "este local" : l.Nome)}" : $"Importar {tipo}", Ui.GImportar);
        _filtros.Content = Filtros();
        Recarregar();
    }

    private FrameworkElement Filtros()
    {
        var p = Ui.Coluna(8);
        if (Arm.Dados.Locais.Count > 0)
        {
            var locais = Arm.LocaisOrdenados.Select(l => (l.Nome.Length == 0 ? "Local sem nome" : l.Nome, l.Id.ToString()));
            var local = Ui.Escolha(locais, Ajustes.Atual.FiltroLocalModelos, v => { Ajustes.Atual.FiltroLocalModelos = v; Ajustes.Salvar(); AtualizarTudo(); });
            local.HorizontalAlignment = HorizontalAlignment.Stretch;
            p.Add(local);
        }
        var tipos = new List<(string, string)> { ("Todos os tipos", "") };
        tipos.AddRange(TiposDoFiltro.Select(t => (t, t)));
        var tipo = Ui.Escolha(tipos, Ajustes.Atual.FiltroTipoModelos, v => { Ajustes.Atual.FiltroTipoModelos = v; Ajustes.Salvar(); AtualizarTudo(); }, 100);
        var gerenciar = Ui.Botao("Gerenciar…", () =>
        {
            new GerenciarTiposJanela(Arm, LocalFiltrado?.Id) { Owner = _janela }.ShowDialog();
            AtualizarTudo(); MostrarDetalhe();
        }, dica: LocalFiltrado == null ? "Escolha um local acima para gerenciar os tipos dele" : "Criar, renomear e excluir os tipos deste local");
        gerenciar.IsEnabled = LocalFiltrado != null;
        ToolTipService.SetShowOnDisabled(gerenciar, true);
        gerenciar.Margin = new Thickness(6, 0, 0, 0);
        var linha = new DockPanel();
        DockPanel.SetDock(gerenciar, Dock.Right);
        linha.Children.Add(gerenciar);
        linha.Children.Add(tipo);
        p.Add(linha);
        return p;
    }

    private void Recarregar()
    {
        _tela.Lista.Items.Clear();
        var termo = _busca.Text.Trim();
        var filtroTipo = Ajustes.Atual.FiltroTipoModelos;
        var local = LocalFiltrado;
        var modelos = Arm.Dados.Modelos
            .Where(m => termo.Length == 0 || Texto.Contem(m.Nome, termo))
            .Where(m => filtroTipo.Length == 0 || m.Categoria == filtroTipo)
            .Where(m => local == null || Arm.LocalDoModelo(m) is not { } dono || dono.Id == local.Id)
            .OrderBy(m => m.Nome, Texto.Ordem).ToList();
        if (local != null)
        {
            // Agrupa por tipo.
            var tipos = TiposDoFiltro;
            tipos.AddRange(modelos.Select(m => m.Categoria).Where(c => !tipos.Contains(c)).Distinct());
            foreach (var tipo in tipos)
            {
                var doTipo = modelos.Where(m => m.Categoria == tipo).ToList();
                if (doTipo.Count == 0) continue;
                _tela.AdicionarGrupo($"{tipo} ({doTipo.Count})");
                foreach (var m in doTipo) _tela.AdicionarItem(Linha(m), m.Id);
            }
        }
        else
        {
            // Sem nenhum local cadastrado, agrupa por local.
            foreach (var l in Arm.LocaisOrdenados)
            {
                var doLocal = modelos.Where(m => m.LocalId == l.Id).ToList();
                if (doLocal.Count == 0) continue;
                _tela.AdicionarGrupo($"{(l.Nome.Length == 0 ? "Local sem nome" : l.Nome)} ({doLocal.Count})");
                foreach (var m in doLocal) _tela.AdicionarItem(Linha(m), m.Id);
            }
            var gerais = modelos.Where(m => Arm.LocalDoModelo(m) == null).ToList();
            if (gerais.Count > 0)
            {
                _tela.AdicionarGrupo($"Todos os locais ({gerais.Count})");
                foreach (var m in gerais) _tela.AdicionarItem(Linha(m), m.Id);
            }
        }
        if (_selecao != null) _tela.Selecionar(_selecao);
    }

    private UIElement Linha(ModeloDocumento modelo)
    {
        var marcas = Ui.Linha(4);
        if (Arm.LocalDoModelo(modelo) == null) { var g = Ui.Icone(Ui.GMundo, 11); g.ToolTip = "Vale para todos os locais"; marcas.Add(g); }
        if (modelo.Origem != null) { var s = Ui.Icone(Ui.GSincronizar, 11, Tema.TextoSecundario); s.ToolTip = "Vem da pasta sincronizada"; marcas.Add(s); }
        return ListaDetalhe.LinhaDupla(modelo.Nome, $"{modelo.Categoria} · {Texto.Plural(modelo.Campos.Count, "campo", "campos")}", marcas);
    }

    private void MostrarDetalhe()
    {
        if (Arm.Modelo(_selecao) is { } modelo) _tela.Detalhe = Ui.Pagina(new ModeloEditor(Arm, modelo, () => Excluir(modelo), () => { AtualizarTudo(); }), 780);
        else
        {
            var texto = Ajustes.Atual.FiltroTipoModelos.Length == 0 ? "modelo" : Ajustes.Atual.FiltroTipoModelos;
            _tela.Detalhe = Ui.AvisoVazio(Arm.Dados.Modelos.Count == 0 ? "Nenhum modelo importado" : "Nenhum modelo selecionado", Ui.GDocumento,
                "Escolha o local no filtro à esquerda e importe os modelos dele. No Word, escreva os campos entre chaves duplas onde o texto deve mudar, por exemplo: Atesto que {{NOME_PACIENTE}}, CPF {{CPF}}, esteve aqui em {{DATA}}. Você também pode arrastar arquivos .docx para esta janela.",
                LocalFiltrado is { } l ? $"Importar {texto} para {(l.Nome.Length == 0 ? "este local" : l.Nome)}" : $"Importar {texto}", ImportarArquivos);
        }
    }

    // MARK: Pasta sincronizada

    private FrameworkElement PainelSincronizacao()
    {
        var pasta = Arm.Dados.PastaSincronizada;
        if (pasta.Length == 0)
        {
            var b = Ui.Botao("Sincronizar com uma pasta…", () => ConfigurarSincronizacao(), principal: true, glifo: Ui.GSincronizar,
                dica: "Os modelos da pasta entram sozinhos no app e se atualizam quando você muda os arquivos");
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Padding = new Thickness(12, 9, 12, 9);
            return b;
        }
        var p = Ui.Coluna(4);
        var nome = Ui.Texto(Path.GetFileName(pasta.TrimEnd('\\')), Estilo.Normal);
        nome.FontWeight = FontWeights.SemiBold; nome.TextWrapping = TextWrapping.NoWrap; nome.TextTrimming = TextTrimming.CharacterEllipsis;
        nome.ToolTip = pasta;
        var sincronizar = Ui.BotaoIcone(Ui.GAtualizar, "Sincronizar agora", () => Ui.ExecutarAsync(() => _janela.Sincronizar(forcar: true)), Tema.Destaque);
        sincronizar.IsEnabled = !Arm.Sincronizacao.EmAndamento;
        var configurar = Ui.BotaoIcone(Ui.GEngrenagem, "Pasta sincronizada e subpastas", ConfigurarSincronizacao, Tema.Destaque);
        var icone = Ui.Icone(Ui.GSincronizar, 13);
        icone.Margin = new Thickness(0, 0, 6, 0);
        var linha = new DockPanel();
        DockPanel.SetDock(icone, Dock.Left);
        var botoes = Ui.Linha(0, sincronizar, configurar);
        DockPanel.SetDock(botoes, Dock.Right);
        linha.Children.Add(icone); linha.Children.Add(botoes); linha.Children.Add(nome);
        p.Add(linha);
        var estado = Arm.Sincronizacao;
        if (estado.EmAndamento)
        {
            p.Add(Ui.Texto(estado.Total == 0 ? "Lendo a pasta…" : $"Sincronizando {estado.Feitos} de {estado.Total}…", Estilo.Legenda));
            p.Add(new ProgressBar { Height = 4, Minimum = 0, Maximum = Math.Max(estado.Total, 1), Value = estado.Feitos, IsIndeterminate = estado.Total == 0 });
        }
        else if (estado.Erros.Count > 0)
        {
            var t = Ui.Texto(estado.Erros.Count == 1 && estado.Total == 0 ? estado.Erros[0] : Texto.Plural(estado.Erros.Count, "arquivo com problema", "arquivos com problema"), Estilo.Legenda, Tema.Aviso);
            t.ToolTip = string.Join("\n", estado.Erros);
            p.Add(t);
        }
        else if (estado.Ultima is { } ultima)
        {
            var mudancas = new List<string>();
            if (estado.Novos > 0) mudancas.Add(Texto.Plural(estado.Novos, "novo", "novos"));
            if (estado.Atualizados > 0) mudancas.Add(Texto.Plural(estado.Atualizados, "alterado", "alterados"));
            if (estado.Removidos > 0) mudancas.Add(Texto.Plural(estado.Removidos, "removido", "removidos"));
            p.Add(Ui.Texto($"Atualizado às {ultima:HH:mm}" + (mudancas.Count == 0 ? "" : " · " + string.Join(", ", mudancas)), Estilo.Legenda));
        }
        return Ui.Cartao(p, Tema.Fundo, 8, 10);
    }

    private void ConfigurarSincronizacao()
    {
        new AjustesSincronizacaoJanela(_janela) { Owner = _janela }.ShowDialog();
        AtualizarTudo();
        Ui.ExecutarAsync(() => _janela.Sincronizar(forcar: true));
    }

    // MARK: Importar e excluir

    private void ImportarArquivos() => Importar(Ui.EscolherDocumentos(varios: true));

    private void ImportarPasta()
    {
        if (Ui.EscolherPasta("Escolha a pasta com os modelos (as subpastas também são importadas)") is not { } pasta) return;
        var tipo = Ajustes.Atual.FiltroTipoModelos.Length == 0 ? null : Ajustes.Atual.FiltroTipoModelos;
        try
        {
            var (importados, erros) = Arm.ImportarPasta(pasta, LocalFiltrado?.Id, tipo);
            AtualizarTudo();
            MostrarDetalhe();
            var destino = LocalFiltrado is { } l ? $" para {l.Nome}" : "";
            var mensagem = importados == 0 && erros.Count == 0
                ? "Nenhum documento do Word (.docx, .doc, .rtf, .odt) encontrado nesta pasta."
                : $"{Texto.Plural(importados, "modelo importado", "modelos importados")}{destino}.";
            if (erros.Count > 0) Ui.Erro($"{mensagem}\n\nNão foi possível importar {erros.Count}:\n\n{string.Join("\n\n", erros.Take(10))}");
            else Ui.Informar(mensagem);
        }
        catch (Exception ex) { Ui.Erro(ex.Message); }
    }

    /// <summary>Importa para o local e o tipo escolhidos nos filtros.</summary>
    private void Importar(IEnumerable<string> arquivos)
    {
        var erros = new List<string>();
        foreach (var arquivo in arquivos)
        {
            try
            {
                var tipo = Ajustes.Atual.FiltroTipoModelos.Length == 0 ? null : Ajustes.Atual.FiltroTipoModelos;
                _selecao = Arm.ImportarModelo(arquivo, LocalFiltrado?.Id, tipo).Id;
            }
            catch (Exception ex) { erros.Add(ex.Message); }
        }
        AtualizarTudo();
        MostrarDetalhe();
        if (erros.Count > 0) Ui.Erro(string.Join("\n\n", erros));
    }

    private void Excluir(ModeloDocumento modelo)
    {
        if (modelo.Origem != null) return;
        if (!Ui.Confirmar($"Excluir o modelo “{modelo.Nome}”?", "A cópia do modelo guardada pelo app será apagada. Seu arquivo original não é afetado.", true)) return;
        Arm.RemoverModelo(modelo);
        if (_selecao == modelo.Id) _selecao = null;
        AtualizarTudo();
        MostrarDetalhe();
    }
}

/// <summary>Editor do modelo (ModeloEditor do Mac): nome, local, tipo, campos, cópias e ações.</summary>
public sealed class ModeloEditor : StackPanel
{
    public ModeloEditor(Armazenamento arm, ModeloDocumento modelo, Action aoExcluir, Action aoMudar)
    {
        Tag = 0.0;
        var dados = Ui.Coluna(8);
        var nome = Ui.CaixaDeTexto(modelo.Nome);
        nome.TextChanged += (_, _) => { modelo.Nome = nome.Text; arm.Salvar(); aoMudar(); };
        dados.Add(Ui.Rotulado("Nome", nome));
        var locais = new List<(string, Guid?)> { ("Todos os locais", null) };
        locais.AddRange(arm.LocaisOrdenados.Select(l => (l.Nome.Length == 0 ? "Local sem nome" : l.Nome, (Guid?)l.Id)));
        var tipoHost = new ContentControl();
        void MontarTipos()
        {
            var lista = arm.Tipos(modelo.LocalId);
            if (!lista.Contains(modelo.Categoria)) lista.Add(modelo.Categoria);
            var tipo = Ui.Escolha(lista.Select(t => (t, t)), modelo.Categoria, v => { modelo.Categoria = v; arm.Salvar(); aoMudar(); });
            var novo = Ui.Botao("Novo tipo…", () =>
            {
                var nomeNovo = Pergunta.Texto("Novo tipo de documento", "O tipo é a subpasta onde os documentos gerados com este modelo são salvos, dentro da pasta do local.", "Ex.: Atestados");
                if (string.IsNullOrWhiteSpace(nomeNovo)) return;
                arm.AdicionarCategoria(nomeNovo, modelo.LocalId);
                modelo.Categoria = nomeNovo.Trim();
                arm.Salvar(); aoMudar(); MontarTipos();
            });
            novo.Margin = new Thickness(8, 0, 0, 0);
            var linha = new DockPanel();
            DockPanel.SetDock(novo, Dock.Right);
            linha.Children.Add(novo); linha.Children.Add(tipo);
            tipoHost.Content = linha;
        }
        dados.Add(Ui.Rotulado("Local de atendimento", Ui.Escolha(locais, modelo.LocalId, v => { modelo.LocalId = v; arm.Salvar(); aoMudar(); MontarTipos(); })));
        MontarTipos();
        dados.Add(Ui.Rotulado("Tipo", tipoHost));
        Children.Add(Ui.Secao("Modelo", Ui.GDocumento, dados));

        if (modelo.Origem is { } origem)
        {
            var o = Ui.Texto(origem, Estilo.Normal, Tema.TextoSecundario);
            Children.Add(Ui.Secao("Vem da pasta sincronizada", Ui.GSincronizar, Ui.Rotulado("Arquivo", o, 110),
                "Edite o arquivo no Word: ao voltar para o app, as mudanças entram sozinhas. Para tirar este modelo do app, apague ou mova o arquivo na pasta."));
        }

        var campos = Ui.Coluna(6);
        if (modelo.Campos.Count == 0)
            campos.Add(Ui.Texto(modelo.Origem == null
                ? "Nenhum campo {{...}} encontrado. Abra no Word, troque o nome do paciente por {{NOME_PACIENTE}} (e a data por {{DATA}}, etc.), salve e clique em “Atualizar campos”."
                : "Nenhum campo {{...}} encontrado. Abra no Word, troque o nome do paciente por {{NOME_PACIENTE}} (e a data por {{DATA}}, etc.) e salve; o app atualiza sozinho.", Estilo.Normal, Tema.TextoSecundario));
        foreach (var campo in modelo.Campos)
        {
            var c = Ui.Texto($"{{{{{campo}}}}}", Estilo.Codigo);
            var d = Ui.Texto(CamposAutomaticos.Descricao(campo), Estilo.Normal, Tema.TextoSecundario);
            d.TextAlignment = TextAlignment.Right;
            campos.Add(Ui.Extremos(c, d));
        }
        Children.Add(Ui.Secao($"Campos do modelo ({modelo.Campos.Count})", Ui.GCampos, campos));

        Children.Add(Ui.Secao("Impressão", Ui.GImpressora,
            Ui.Rotulado("Cópias", GerarView.SeletorDeCopias(modelo.Copias, v => { modelo.Copias = v; arm.Salvar(); }, "Cópias ao imprimir este modelo", 10), 110),
            "Quantas cópias sair quando este modelo é impresso pelo app (tela Gerar documento e Histórico)."));

        var acoes = new WrapPanel { Tag = 8.0 };
        acoes.Add(Ui.Botao("Editar no Word", () => Ui.Abrir(arm.UrlParaEditar(modelo)), glifo: Ui.GAbrir));
        if (modelo.Origem != null)
            acoes.Add(Ui.Botao("Mostrar no Explorador", () => Ui.MostrarNaPasta(arm.UrlParaEditar(modelo)), glifo: Ui.GPastaAberta));
        else
        {
            acoes.Add(Ui.Botao("Atualizar campos", () => { arm.AtualizarCampos(modelo); aoMudar(); Recriar(); }, glifo: Ui.GAtualizar, dica: "Depois de editar e salvar no Word, relê os campos do modelo"));
            acoes.Add(Ui.Botao("Substituir arquivo…", () =>
            {
                if (Ui.EscolherDocumentos(varios: false).FirstOrDefault() is not { } arquivo) return;
                arm.SubstituirArquivo(modelo, arquivo); aoMudar(); Recriar();
            }));
            var excluir = Ui.Botao("Excluir modelo…", aoExcluir, glifo: Ui.GLixeira);
            excluir.SetResourceReference(Control.ForegroundProperty, Tema.Aviso);
            acoes.Add(excluir);
        }
        Children.Add(acoes);

        void Recriar()
        {
            if (Parent is ScrollViewer s) s.Content = new ModeloEditor(arm, modelo, aoExcluir, aoMudar) { Margin = Margin, MaxWidth = MaxWidth };
        }
    }
}

/// <summary>Criar, renomear e excluir os tipos de documento de um local (GerenciarTiposView do Mac).</summary>
public sealed class GerenciarTiposJanela : Dialogo
{
    private readonly Armazenamento _arm;
    private readonly Guid? _localId;
    private readonly StackPanel _lista = Ui.Coluna(4);

    public GerenciarTiposJanela(Armazenamento arm, Guid? localId) : base("Tipos de documento", 580, 620)
    {
        _arm = arm; _localId = localId;
        var nome = arm.Local(localId)?.Nome ?? "";
        var p = Ui.Coluna(12);
        p.Margin = new Thickness(22, 18, 22, 12);
        p.Add(Ui.Texto(localId == null ? "Tipos de documento — todos os locais" : nome.Length == 0 ? "Tipos de documento" : $"Tipos de documento — {nome}", Estilo.Titulo));
        p.Add(Ui.Texto("Cada tipo vira uma subpasta dentro da pasta do local, onde ficam os documentos gerados. Clique no nome para renomear (Enter confirma). A lista fica sempre em ordem alfabética (use números, como “1 - RX”, para pôr um tipo na frente).", Estilo.Normal, Tema.TextoSecundario));
        var rolagem = new ScrollViewer { Content = Ui.Cartao(_lista, Tema.Lateral, 8, 8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 300 };
        p.Add(rolagem);
        var novo = Ui.CaixaDeTexto(dica: "Novo tipo (ex.: Radiografias)");
        var adicionar = Ui.Botao("Adicionar", () => { arm.AdicionarCategoria(novo.Text, localId); novo.Text = ""; Montar(); });
        adicionar.Margin = new Thickness(8, 0, 0, 0);
        novo.KeyDown += (_, e) => { if (e.Key == Key.Enter) { arm.AdicionarCategoria(novo.Text, localId); novo.Text = ""; Montar(); e.Handled = true; } };
        var linha = new DockPanel();
        DockPanel.SetDock(adicionar, Dock.Right);
        linha.Children.Add(adicionar); linha.Children.Add(novo);
        p.Add(linha);
        Corpo = p;
        AdicionarBotao("Concluído", Close, principal: true);
        Montar();
    }

    private void Montar()
    {
        _lista.Children.Clear();
        var tipos = _arm.Tipos(_localId);
        foreach (var tipo in tipos)
        {
            var caixa = new TextBox { Text = tipo, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 30 };
            void Confirmar()
            {
                var novo = caixa.Text.Trim();
                if (novo.Length == 0) caixa.Text = tipo;
                else if (novo != tipo) { _arm.RenomearCategoria(tipo, novo, _localId); Montar(); }
            }
            caixa.LostKeyboardFocus += (_, _) => Confirmar();
            caixa.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Confirmar(); e.Handled = true; } };
            var quantidade = _arm.QuantidadeDeModelos(tipo, _localId);
            var contagem = Ui.Texto(Texto.Plural(quantidade, "modelo", "modelos"), Estilo.Legenda);
            contagem.VerticalAlignment = VerticalAlignment.Center;
            contagem.Margin = new Thickness(8, 0, 8, 0);
            var excluir = Ui.BotaoIcone(Ui.GLixeira, "Excluir este tipo", () => Excluir(tipo, tipos));
            excluir.IsEnabled = tipos.Count > 1;
            var linha = new DockPanel();
            var direita = Ui.Linha(0, contagem, excluir);
            DockPanel.SetDock(direita, Dock.Right);
            linha.Children.Add(direita); linha.Children.Add(caixa);
            _lista.Add(linha);
        }
    }

    private void Excluir(string tipo, List<string> tipos)
    {
        // Os modelos vão para "Outros", se existir, senão para o primeiro tipo restante.
        var restantes = tipos.Where(t => t != tipo).ToList();
        var destino = restantes.FirstOrDefault(t => Texto.Iguais(t, "Outros")) ?? restantes.FirstOrDefault() ?? "Outros";
        var quantidade = _arm.QuantidadeDeModelos(tipo, _localId);
        var detalhe = quantidade == 0 ? "Nenhum modelo usa este tipo."
            : $"{quantidade} {(quantidade == 1 ? "modelo passará" : "modelos passarão")} para “{destino}”. Documentos já gerados não são movidos.";
        if (!Ui.Confirmar($"Excluir o tipo “{tipo}”?", detalhe, true)) return;
        _arm.RemoverCategoria(tipo, destino, _localId);
        Montar();
    }
}

/// <summary>Escolha da pasta sincronizada e do papel de cada subpasta (AjustesSincronizacaoView do Mac).</summary>
public sealed class AjustesSincronizacaoJanela : Dialogo
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;

    public AjustesSincronizacaoJanela(JanelaPrincipal janela) : base("Pasta sincronizada", 700, 640)
    {
        _janela = janela;
        if (Arm.Dados.PastaSincronizada.Length == 0 && EscolherPasta() == null) { Loaded += (_, _) => Close(); }
        Montar();
        AdicionarBotao("Concluído", Close, principal: true);
    }

    private string? EscolherPasta()
    {
        var pasta = Ui.EscolherPasta("Escolha a pasta principal dos modelos (com uma subpasta para cada local).");
        if (pasta != null) Arm.DefinirPastaSincronizada(pasta);
        return pasta;
    }

    private void Montar()
    {
        var p = Ui.Coluna(12);
        p.Margin = new Thickness(22, 18, 22, 12);
        p.Add(Ui.Texto("Pasta sincronizada", Estilo.Titulo));
        p.Add(Ui.Texto("O app acompanha esta pasta: arquivos novos viram modelos, arquivos editados no Word são atualizados e arquivos apagados saem do app. Cada subpasta é um local de atendimento e, dentro dela, cada subpasta é um tipo de documento (a subpasta onde os documentos gerados são salvos). Seus arquivos nunca são alterados pelo app.", Estilo.Normal, Tema.TextoSecundario));
        var pasta = Arm.Dados.PastaSincronizada;
        var caminho = Ui.Texto(pasta.Length == 0 ? "Nenhuma pasta escolhida" : Ui.CaminhoAbreviado(pasta));
        caminho.TextWrapping = TextWrapping.NoWrap; caminho.TextTrimming = TextTrimming.CharacterEllipsis; caminho.ToolTip = pasta;
        p.Add(Ui.Cartao(caminho, Tema.Superficie, 8, 10));
        var botoes = new WrapPanel { Tag = 8.0 };
        botoes.Add(Ui.Botao(pasta.Length == 0 ? "Escolher pasta…" : "Trocar pasta…", () => { if (EscolherPasta() != null) Montar(); }, principal: true, glifo: Ui.GPasta));
        if (pasta.Length > 0)
        {
            botoes.Add(Ui.Botao("Abrir no Explorador", () => Ui.AbrirPasta(pasta), glifo: Ui.GPastaAberta));
            botoes.Add(Ui.Botao("Parar de sincronizar…", () =>
            {
                if (!Ui.Confirmar("Parar de sincronizar com esta pasta?", "Os modelos que vieram da pasta continuam no app como modelos comuns, mas deixam de ser atualizados.", true)) return;
                Arm.DefinirPastaSincronizada(null);
                Montar();
            }));
        }
        p.Add(botoes);

        var subpastas = pasta.Length > 0 && Directory.Exists(pasta) ? LeituraDaPasta.SubpastasPrincipais(pasta) : [];
        if (subpastas.Count > 0)
        {
            p.Add(Ui.Texto("O que é cada subpasta?", Estilo.Secao));
            var lista = Ui.Coluna(6);
            foreach (var sub in subpastas)
            {
                var papel = Ui.Escolha([("Local", PapelDaPasta.Local), ("Todos os locais", PapelDaPasta.TodosOsLocais), ("Ignorar", PapelDaPasta.Ignorar)],
                    Arm.PapelDaPastaDe(sub), v => Arm.DefinirPapelDaPasta(sub, v), 170);
                lista.Add(Ui.Extremos(Ui.Texto(sub), papel));
            }
            p.Add(Ui.Cartao(lista, Tema.Lateral, 8, 12));
            p.Add(Ui.Texto("Local: uma pasta por local de atendimento. Todos os locais: a pasta (ex.: 1 - APAC) aparece como subpasta de cada local, sem copiar nem criar atalhos; os documentos gerados vão para uma pasta com o mesmo nome dentro da pasta de cada local. Ignorar: pastas sem modelos (ex.: mídias). Arquivos soltos na pasta principal também valem para todos os locais.", Estilo.Legenda));
        }
        Corpo = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}

/// <summary>Pergunta de uma linha (o .alert com TextField do Mac).</summary>
public sealed class Pergunta : Dialogo
{
    private readonly TextBox _caixa;
    private string? _resposta;

    private Pergunta(string titulo, string mensagem, string dica) : base(titulo, 480, 260)
    {
        var p = Ui.Coluna(12);
        p.Margin = new Thickness(22, 18, 22, 12);
        p.Add(Ui.Texto(titulo, Estilo.Secao));
        p.Add(Ui.Texto(mensagem, Estilo.Apoio));
        _caixa = Ui.CaixaDeTexto(dica: dica);
        p.Add(_caixa);
        Corpo = p;
        AdicionarBotao("Cancelar", Close);
        AdicionarBotao("Adicionar", () => { _resposta = _caixa.Text; Close(); }, principal: true);
        Loaded += (_, _) => _caixa.Focus();
    }

    public static string? Texto(string titulo, string mensagem, string dica)
    {
        var p = new Pergunta(titulo, mensagem, dica) { Owner = Application.Current.MainWindow };
        p.ShowDialog();
        return p._resposta;
    }
}
