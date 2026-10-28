using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>Tela Pacientes (PacientesView do Mac): lista por local, busca e cadastro com gravação automática.</summary>
public sealed class PacientesView : UserControl
{
    private const string SemLocal = "sem";
    private readonly JanelaPrincipal _janela;
    private Armazenamento Arm => _janela.Arm;
    private readonly ListaDetalhe _tela = new();
    private readonly TextBox _busca = Ui.CaixaDeTexto(dica: "Buscar por nome ou CPF");
    private Guid? _selecao;

    public PacientesView(JanelaPrincipal janela)
    {
        _janela = janela;
        Content = _tela;
        var novo = Ui.Botao("Novo paciente", NovoPaciente, principal: true, glifo: Ui.GPessoaMais);
        novo.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tela.Topo.Add(novo);
        var opcoes = new List<(string, string)> { ("Todos os locais", "") };
        opcoes.AddRange(Arm.LocaisOrdenados.Select(l => (l.Nome.Length == 0 ? "Local sem nome" : l.Nome, l.Id.ToString())));
        opcoes.Add(("Sem local de atendimento", SemLocal));
        if (!opcoes.Any(o => o.Item2 == Ajustes.Atual.FiltroLocalPacientes)) Ajustes.Atual.FiltroLocalPacientes = "";
        var filtro = Ui.Escolha(opcoes, Ajustes.Atual.FiltroLocalPacientes, v => { Ajustes.Atual.FiltroLocalPacientes = v; Ajustes.Salvar(); Recarregar(); });
        filtro.HorizontalAlignment = HorizontalAlignment.Stretch;
        _tela.Topo.Add(filtro);
        _tela.Topo.Add(_busca);
        _busca.TextChanged += (_, _) => Recarregar();
        _tela.Lista.SelectionChanged += (_, _) =>
        {
            if (_tela.Lista.SelectedItem is ListBoxItem { Tag: Guid id } && id != _selecao) { _selecao = id; MostrarDetalhe(); }
        };
        _tela.Lista.KeyDown += (_, e) => { if (e.Key == Key.Delete && Arm.Paciente(_selecao) is { } p) Excluir(p); };
        Recarregar();
        MostrarDetalhe();
    }

    private void Recarregar()
    {
        _tela.Lista.Items.Clear();
        var termo = _busca.Text.Trim();
        var pacientes = Arm.PacientesOrdenados.Where(p => termo.Length == 0 || Texto.Contem(p.Nome, termo) || p.Cpf.Contains(termo)).ToList();
        var filtro = Ajustes.Atual.FiltroLocalPacientes;
        foreach (var local in Arm.LocaisOrdenados.Where(l => filtro.Length == 0 || filtro == l.Id.ToString()))
        {
            // Pacientes do local e os de outros locais que já foram atendidos aqui.
            var doLocal = pacientes.Where(p => p.LocalId == local.Id || Arm.LocaisDoPaciente(p).Any(l => l.Id == local.Id)).ToList();
            if (doLocal.Count == 0) continue;
            _tela.AdicionarGrupo($"{(local.Nome.Length == 0 ? "Local sem nome" : local.Nome)} ({doLocal.Count})");
            foreach (var p in doLocal) _tela.AdicionarItem(Linha(p), p.Id);
        }
        if (filtro.Length == 0 || filtro == SemLocal)
        {
            var semLocal = pacientes.Where(p => Arm.Local(p.LocalId) == null).ToList();
            if (semLocal.Count > 0)
            {
                _tela.AdicionarGrupo($"Sem local de atendimento ({semLocal.Count})");
                foreach (var p in semLocal) _tela.AdicionarItem(Linha(p), p.Id);
            }
        }
        if (_selecao != null) _tela.Selecionar(_selecao);
    }

    private UIElement Linha(Paciente paciente)
    {
        var repetido = paciente.Nome.Length > 0 && Arm.PacientesChamados(paciente.Nome).Count > 1;
        var semNascimento = paciente.DataNascimento.Trim().Length == 0;
        UIElement? alerta = null;
        if (repetido && semNascimento)
        {
            alerta = Ui.Icone(Ui.GAviso, 12, Tema.Aviso);
            ((FrameworkElement)alerta).ToolTip = "Há outro paciente com este nome. Cadastre a data de nascimento para diferenciá-los.";
        }
        var legenda = repetido ? Armazenamento.Identificacao(paciente) : paciente.Cpf;
        return ListaDetalhe.LinhaDupla(paciente.Nome.Length == 0 ? "Sem nome" : paciente.Nome, legenda, alerta, repetido && semNascimento ? Tema.Aviso : null);
    }

    private void MostrarDetalhe()
    {
        if (Arm.Paciente(_selecao) is { } paciente)
            _tela.Detalhe = Ui.Pagina(new PacienteEditor(Arm, paciente, () => Excluir(paciente), () => Recarregar()), 760);
        else
            _tela.Detalhe = Ui.AvisoVazio("Nenhum paciente selecionado", Ui.GPessoa,
                "Escolha um paciente na lista ou cadastre um novo. Pacientes também são cadastrados sozinhos quando você gera um documento com um nome novo.",
                "Cadastrar paciente", NovoPaciente);
    }

    /// <summary>Novo paciente já ligado ao local que está sendo filtrado.</summary>
    private void NovoPaciente()
    {
        _busca.Text = "";
        var paciente = Arm.NovoPaciente();
        if (Arm.Dados.Locais.FirstOrDefault(l => l.Id.ToString() == Ajustes.Atual.FiltroLocalPacientes) is { } local)
        {
            paciente.LocalId = local.Id;
            Arm.Salvar();
        }
        _selecao = paciente.Id;
        Recarregar();
        MostrarDetalhe();
    }

    private void Excluir(Paciente paciente)
    {
        if (!Ui.Confirmar($"Excluir {(paciente.Nome.Length == 0 ? "este paciente" : paciente.Nome)}?", "O cadastro será apagado. Os documentos já gerados não são apagados.", true)) return;
        Arm.RemoverPaciente(paciente.Id);
        if (_selecao == paciente.Id) _selecao = null;
        Recarregar();
        MostrarDetalhe();
    }
}

/// <summary>Cadastro do paciente (PacienteEditor do Mac): as alterações são salvas automaticamente.</summary>
public sealed class PacienteEditor : StackPanel
{
    public PacienteEditor(Armazenamento arm, Paciente paciente, Action? aoExcluir, Action? aoMudarNome)
    {
        Tag = 0.0;
        var identificacao = Ui.Coluna(8);
        var nome = Caixa(paciente.Nome, "Nome completo", v => { paciente.Nome = v; aoMudarNome?.Invoke(); });
        identificacao.Add(Ui.Rotulado("Nome completo", nome));
        var aviso = new ContentControl();
        identificacao.Add(aviso);
        var nascimentoTexto = Ui.Texto("", Estilo.Normal, Tema.TextoSecundario);
        void AtualizarAvisos()
        {
            aviso.Content = arm.PacientesChamados(paciente.Nome).Count > 1
                ? Ui.Linha(8, Ui.Icone(Ui.GAviso, 13, Tema.Aviso), Ui.Texto(paciente.DataNascimento.Trim().Length == 0
                    ? "Há outro paciente com este nome. Preencha a data de nascimento para diferenciá-los."
                    : "Há outro paciente com este nome; a data de nascimento diferencia os dois.", Estilo.Apoio, Tema.Aviso))
                : null;
            nascimentoTexto.Text = CamposAutomaticos.Idade(paciente.DataNascimento) is int idade ? $"{idade} anos" : "";
        }
        nome.TextChanged += (_, _) => AtualizarAvisos();

        var locais = new List<(string, Guid?)> { ("Nenhum", null) };
        locais.AddRange(arm.LocaisOrdenados.Select(l => (l.Nome.Length == 0 ? "Local sem nome" : l.Nome, (Guid?)l.Id)));
        identificacao.Add(Ui.Rotulado("Local de atendimento", Ui.Escolha(locais, paciente.LocalId, v => { paciente.LocalId = v; arm.Salvar(); })));
        if (arm.LocaisDoPaciente(paciente).Count > 1)
            identificacao.Add(Ui.Rotulado("Atendido em", Ui.Texto(arm.NomesDosLocais(paciente), Estilo.Normal, Tema.TextoSecundario)));
        identificacao.Add(Ui.Rotulado("CPF", Caixa(paciente.Cpf, "000.000.000-00", v => paciente.Cpf = v)));
        identificacao.Add(Ui.Rotulado("RG", Caixa(paciente.Rg, "", v => paciente.Rg = v)));
        var nascimento = Caixa(paciente.DataNascimento, "dd/mm/aaaa", v => paciente.DataNascimento = v);
        nascimento.Width = 160;
        nascimento.TextChanged += (_, _) => AtualizarAvisos();
        nascimentoTexto.Margin = new Thickness(12, 0, 0, 0);
        nascimentoTexto.VerticalAlignment = VerticalAlignment.Center;
        identificacao.Add(Ui.Rotulado("Data de nascimento", Ui.Linha(0, nascimento, nascimentoTexto)));
        AtualizarAvisos();
        Children.Add(Ui.Secao("Identificação", Ui.GPessoa, identificacao));

        var contato = Ui.Coluna(8);
        contato.Add(Ui.Rotulado("Telefone", Caixa(paciente.Telefone, "", v => paciente.Telefone = v)));
        contato.Add(Ui.Rotulado("E-mail", Caixa(paciente.Email, "", v => paciente.Email = v)));
        var endereco = Caixa(paciente.Endereco, "", v => paciente.Endereco = v);
        endereco.TextWrapping = TextWrapping.Wrap; endereco.AcceptsReturn = true; endereco.MaxHeight = 90;
        contato.Add(Ui.Rotulado("Endereço", endereco));
        contato.Add(Ui.Rotulado("Convênio", Caixa(paciente.Convenio, "", v => paciente.Convenio = v)));
        Children.Add(Ui.Secao("Contato", Ui.GTelefone, contato));

        var observacoes = Ui.CaixaDeTexto(paciente.Observacoes, "Observações", multilinha: true);
        observacoes.MinHeight = 90;
        observacoes.TextChanged += (_, _) => { paciente.Observacoes = observacoes.Text; arm.Salvar(); };
        Children.Add(Ui.Secao("Observações", Ui.GNota, observacoes));

        if (aoExcluir != null)
        {
            var excluir = Ui.Botao("Excluir paciente…", aoExcluir, glifo: Ui.GLixeira);
            excluir.HorizontalAlignment = HorizontalAlignment.Right;
            excluir.SetResourceReference(Control.ForegroundProperty, Tema.Aviso);
            Children.Add(excluir);
        }

        TextBox Caixa(string valor, string dica, Action<string> definir)
        {
            var caixa = Ui.CaixaDeTexto(valor, dica.Length > 0 ? dica : null);
            caixa.TextChanged += (_, _) => { definir(caixa.Text); arm.Salvar(); };
            return caixa;
        }
    }
}

/// <summary>Cadastro do paciente aberto a partir da tela Gerar documento.</summary>
public sealed class CadastroPacienteJanela : Dialogo
{
    public CadastroPacienteJanela(Armazenamento arm, Paciente paciente) : base("Cadastro do paciente", 700, 760)
    {
        Corpo = Ui.Pagina(new PacienteEditor(arm, paciente, () =>
        {
            if (!Ui.Confirmar("Excluir este paciente?", "O cadastro será apagado. Os documentos já gerados não são apagados.", true)) return;
            arm.RemoverPaciente(paciente.Id);
            Close();
        }, null), 640);
        TextoDoRodape("As alterações são salvas automaticamente.");
        AdicionarBotao("Concluído", Close, principal: true);
    }
}
