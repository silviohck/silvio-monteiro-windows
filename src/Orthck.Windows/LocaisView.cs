using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>Tela Locais de atendimento (LocaisView do Mac): nome, pasta dos documentos e exclusão com transferência.</summary>
public sealed class LocaisView : UserControl
{
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;
    private readonly ListaDetalhe _tela = new(280);
    private Guid? _selecao;

    public LocaisView(JanelaPrincipal janela)
    {
        _janela = janela;
        Content = _tela;
        var novo = Ui.Botao("Novo local", NovoLocal, principal: true, glifo: Ui.GMais);
        novo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tela.Topo.Add(novo);
        _tela.Lista.SelectionChanged += (_, _) =>
        {
            if (_tela.Lista.SelectedItem is ListBoxItem { Tag: Guid id } && id != _selecao) { _selecao = id; MostrarDetalhe(); }
        };
        _tela.Lista.KeyDown += (_, e) => { if (e.Key == Key.Delete && Arm.Local(_selecao) is { } l) Excluir(l); };
        Recarregar();
        MostrarDetalhe();
    }

    private void Recarregar()
    {
        _tela.Lista.Items.Clear();
        foreach (var local in Arm.LocaisOrdenados)
        {
            var linha = ListaDetalhe.LinhaDupla(local.Nome.Length == 0 ? "Novo local (sem nome)" : local.Nome,
                local.PastaSincronizada != null ? "da pasta sincronizada" : null);
            _tela.AdicionarItem(linha, local.Id);
        }
        if (_selecao != null) _tela.Selecionar(_selecao);
    }

    private void MostrarDetalhe()
    {
        if (Arm.Local(_selecao) is { } local) _tela.Detalhe = Ui.Pagina(Editor(local), 760);
        else _tela.Detalhe = Ui.AvisoVazio(Arm.Dados.Locais.Count == 0 ? "Nenhum local cadastrado" : "Nenhum local selecionado", Ui.GPredio,
            "Cadastre os lugares onde você atende (clínica, hospital, consultório) e escolha a pasta de cada um. Os documentos gerados são salvos lá automaticamente, separados em Receitas, Laudos e Relatórios médicos.",
            "Cadastrar local de atendimento", NovoLocal);
    }

    private FrameworkElement Editor(LocalAtendimento local)
    {
        var p = Ui.Coluna(0);
        var dados = Ui.Coluna(8);
        TextBox Caixa(string valor, string dica, Action<string> definir, Action? depois = null)
        {
            var caixa = Ui.CaixaDeTexto(valor, dica.Length > 0 ? dica : null);
            caixa.TextChanged += (_, _) => { definir(caixa.Text); Arm.Salvar(); depois?.Invoke(); };
            return caixa;
        }
        var pastaTexto = Ui.Texto("", Estilo.Normal, Tema.TextoSecundario);
        void AtualizarPasta()
        {
            pastaTexto.Text = Ui.CaminhoAbreviado(Arm.PastaDoLocal(local));
            pastaTexto.ToolTip = Arm.PastaDoLocal(local);
            pastaTexto.SetResourceReference(TextBlock.ForegroundProperty, local.Pasta.Length == 0 ? Tema.TextoSecundario : Tema.TextoPrincipal);
        }
        dados.Add(Ui.Rotulado("Nome", Caixa(local.Nome, "Ex.: Alagoa Grande", v => local.Nome = v, () => { AtualizarPasta(); Recarregar(); })));
        dados.Add(Ui.Rotulado("Cidade", Caixa(local.Cidade, "", v => local.Cidade = v)));
        dados.Add(Ui.Rotulado("Endereço", Caixa(local.Endereco, "", v => local.Endereco = v)));
        dados.Add(Ui.Rotulado("Telefone", Caixa(local.Telefone, "", v => local.Telefone = v)));
        dados.Add(Ui.Rotulado("E-mail", Caixa(local.Email, "", v => local.Email = v)));
        dados.Add(Ui.Rotulado("CNPJ", Caixa(local.Cnpj, "", v => local.Cnpj = v)));
        p.Add(Ui.Secao("Local de atendimento", Ui.GPredio, dados,
            "Cidade, endereço, telefone, e-mail e CNPJ preenchem campos como {{CIDADE}} e {{CNPJ}} nos modelos."));

        var pasta = Ui.Coluna(10);
        AtualizarPasta();
        pasta.Add(Ui.Rotulado("Pasta", pastaTexto, 110));
        var botoes = new WrapPanel { Tag = 8.0 };
        botoes.Add(Ui.Botao("Escolher pasta…", () =>
        {
            if (Ui.EscolherPasta($"Escolha a pasta onde ficam os documentos de {(local.Nome.Length == 0 ? "este local" : local.Nome)}.") is not { } escolhida) return;
            local.Pasta = escolhida; Arm.Salvar(); CriarSubpastas(local); MostrarDetalhe();
        }, glifo: Ui.GPasta));
        botoes.Add(Ui.Botao("Abrir no Explorador", () => { CriarSubpastas(local); Ui.AbrirPasta(Arm.PastaDoLocal(local)); }, glifo: Ui.GPastaAberta));
        if (local.Pasta.Length > 0) botoes.Add(Ui.Botao("Usar pasta padrão", () => { local.Pasta = ""; Arm.Salvar(); MostrarDetalhe(); }));
        pasta.Add(botoes);
        p.Add(Ui.Secao("Onde salvar os documentos deste local", Ui.GPasta, pasta,
            "Os documentos gerados são salvos aqui, separados em subpastas por tipo (Receitas, Laudos…)."));

        var excluir = Ui.Botao("Excluir local…", () => Excluir(local), glifo: Ui.GLixeira);
        excluir.HorizontalAlignment = HorizontalAlignment.Right;
        excluir.SetResourceReference(Control.ForegroundProperty, Tema.Aviso);
        p.Add(excluir);
        return p;
    }

    private void CriarSubpastas(LocalAtendimento local)
    {
        try { Arm.CriarSubpastas(local); }
        catch (Exception ex) { Ui.Erro($"Não foi possível criar as pastas: {ex.Message}"); }
    }

    private void NovoLocal()
    {
        _selecao = Arm.NovoLocal().Id;
        Recarregar();
        MostrarDetalhe();
    }

    private void Excluir(LocalAtendimento local)
    {
        if (new ExcluirLocalJanela(Arm, local) { Owner = _janela }.ShowDialog() != true) return;
        if (_selecao == local.Id) _selecao = null;
        if (Ajustes.Atual.LocalSelecionado == local.Id.ToString()) { Ajustes.Atual.LocalSelecionado = ""; Ajustes.Salvar(); }
        Recarregar();
        MostrarDetalhe();
    }
}

/// <summary>Exclusão de local: exige decidir antes o que acontece com os modelos e pacientes dele.</summary>
public sealed class ExcluirLocalJanela : Dialogo
{
    public ExcluirLocalJanela(Armazenamento arm, LocalAtendimento local) : base("Excluir local", 600, 470)
    {
        var nome = local.Nome.Length == 0 ? "este local" : $"“{local.Nome}”";
        var modelos = arm.Dados.Modelos.Count(m => m.LocalId == local.Id);
        var pacientes = arm.Dados.Pacientes.Count(p => p.LocalId == local.Id);
        var outros = arm.LocaisOrdenados.Where(l => l.Id != local.Id).ToList();
        var temVinculos = modelos > 0 || pacientes > 0;

        var p = Ui.Coluna(12);
        p.Margin = new Thickness(24, 20, 24, 20);
        p.Add(Ui.Texto($"Excluir {nome}", Estilo.Titulo));
        var transferir = new RadioButton { Content = "Transferir para outro local" };
        var desvincular = new RadioButton { Content = new TextBlock { Text = "Desvincular: os modelos passam a valer para todos os locais e os pacientes ficam sem local", TextWrapping = TextWrapping.Wrap } };
        var opcoesDestino = new List<(string, Guid?)> { ("Escolha o local…", null) };
        opcoesDestino.AddRange(outros.Select(l => (l.Nome.Length == 0 ? "Local sem nome" : l.Nome, (Guid?)l.Id)));
        Guid? destino = null;
        var excluir = AdicionarBotao("Excluir local", () => { }, principal: false);
        ComboBox escolhaDestino = null!;
        escolhaDestino = Ui.Escolha(opcoesDestino, null, v => { destino = v; Atualizar(); });
        escolhaDestino.Margin = new Thickness(26, 0, 0, 0);
        escolhaDestino.HorizontalAlignment = HorizontalAlignment.Left;
        void Atualizar()
        {
            escolhaDestino.Visibility = transferir.IsChecked == true && outros.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            excluir.IsEnabled = !temVinculos || desvincular.IsChecked == true || (transferir.IsChecked == true && destino != null);
        }
        if (temVinculos)
        {
            p.Add(Ui.Texto($"Este local tem {Texto.Plural(modelos, "modelo", "modelos")} e {Texto.Plural(pacientes, "paciente", "pacientes")}. Antes de excluir, escolha o que fazer com eles:"));
            transferir.Checked += (_, _) => Atualizar();
            desvincular.Checked += (_, _) => Atualizar();
            p.Add(transferir);
            if (outros.Count == 0) p.Add(Ui.Texto("Não há outro local cadastrado para receber os modelos.", Estilo.Apoio, Tema.Aviso));
            p.Add(escolhaDestino, 4);
            p.Add(desvincular);
        }
        else p.Add(Ui.Texto("Este local não tem modelos nem pacientes vinculados."));
        if (local.PastaSincronizada is { } pasta)
            p.Add(Ui.Texto($"Este local vem da pasta sincronizada “{pasta}”. Ao transferir para um local sem pasta sincronizada, os arquivos dessa pasta passam a ser desse local; caso contrário, a pasta deixa de ser acompanhada e os modelos que vieram dela saem do app na próxima sincronização.", Estilo.Apoio));
        p.Add(Ui.Texto("Os documentos já gerados não são apagados.", Estilo.Apoio));
        Corpo = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        Botoes.Children.Remove(excluir);
        AdicionarBotao("Cancelar", Close);
        Botoes.Children.Add(excluir);
        excluir.SetResourceReference(Control.ForegroundProperty, Tema.Aviso);
        excluir.Click += (_, _) =>
        {
            arm.ExcluirLocal(local.Id, transferir.IsChecked == true ? destino : null);
            DialogResult = true;
        };
        Atualizar();
    }
}
