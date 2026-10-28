using System.Windows;
using System.Windows.Controls;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>Ajustes (Orthck › Ajustes… no Mac): tamanho do texto, tema e os dados (pasta, backups, importação do Mac).</summary>
public sealed class AjustesJanela : Dialogo
{
    private readonly JanelaPrincipal _janela;

    public AjustesJanela(JanelaPrincipal janela) : base("Ajustes", 640, 720)
    {
        _janela = janela;
        Montar();
        AdicionarBotao("Concluído", Close, principal: true);
    }

    private void Montar()
    {
        var p = Ui.Coluna(0);
        var aparencia = Ui.Coluna(10);
        aparencia.Add(Ui.Rotulado("Tamanho do texto", Ui.Escolha([("Normal", TamanhoTexto.Normal), ("Grande", TamanhoTexto.Grande), ("Muito grande", TamanhoTexto.MuitoGrande)],
            Ajustes.Atual.TamanhoTexto, v => { Ajustes.Atual.TamanhoTexto = v; Aplicar(); }), 170));
        aparencia.Add(Ui.Rotulado("Tema", Ui.Escolha([("Automático (igual ao Windows)", Aparencia.Automatica), ("Claro", Aparencia.Clara), ("Escuro", Aparencia.Escura)],
            Ajustes.Atual.Aparencia, v => { Ajustes.Atual.Aparencia = v; Aplicar(); }), 170));
        p.Add(Ui.Secao("Aparência", Ui.GPaleta, aparencia,
            "Paleta suave com azul-petróleo, cinzas neutros e contraste confortável. Atalhos: Ctrl + aumenta o texto, Ctrl − diminui e Ctrl 0 volta ao normal."));

        var arm = _janela.Arm;
        var dados = Ui.Coluna(10);
        var pasta = Ui.Texto(Ui.CaminhoAbreviado(arm.Pasta), Estilo.Normal, Tema.TextoSecundario);
        pasta.ToolTip = arm.Pasta;
        dados.Add(Ui.Rotulado("Pasta de dados", pasta, 140));
        var backups = arm.Backups();
        dados.Add(Ui.Rotulado("Backups", Ui.Texto(backups.Count == 0 ? "O primeiro backup automático é feito amanhã."
            : $"{Texto.Plural(backups.Count, "backup diário", "backups diários")}; o mais recente é de {Path.GetFileNameWithoutExtension(backups[0]).Replace("dados-", "")}.", Estilo.Normal, Tema.TextoSecundario), 140));
        var botoes = new WrapPanel { Tag = 8.0 };
        botoes.Add(Ui.Botao("Abrir pasta de dados (backup)", () => Ui.AbrirPasta(arm.Pasta), glifo: Ui.GPastaAberta));
        botoes.Add(Ui.Botao("Restaurar um backup…", RestaurarBackup, glifo: Ui.GBackup));
        dados.Add(botoes);
        p.Add(Ui.Secao("Dados e backup", Ui.GBackup, dados,
            "Tudo fica só neste computador: dados.json (pacientes, locais, histórico e fila), a pasta Modelos e os backups diários (os últimos 30). Guarde de vez em quando um .zip (Compactar, na barra lateral) fora do computador."));

        var mac = Ui.Coluna(10);
        mac.Add(Ui.Texto("Traz pacientes, locais, modelos, tipos, histórico e fila do Orthck para Mac. Copie para cá a pasta de dados do Mac inteira (~/Library/Application Support/GeradorDocumentos, com o dados.json e a pasta Modelos) e escolha o dados.json dela. Os dados atuais deste computador são guardados antes em Backups."));
        mac.Add(Ui.Botao("Importar dados do Mac…", ImportarDoMac, glifo: Ui.GImportar));
        p.Add(Ui.Secao("Importar do Mac", Ui.GImportar, mac));
        Corpo = Ui.Pagina(p, 600);
    }

    private void Aplicar()
    {
        Ajustes.Salvar();
        Tema.Aplicar(Application.Current, Ajustes.Atual.TamanhoTexto, Ajustes.Atual.Aparencia);
    }

    private void RestaurarBackup()
    {
        var arm = _janela.Arm;
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o backup para restaurar", InitialDirectory = arm.PastaBackups, Filter = "Backups do Orthck (*.json)|*.json"
        };
        if (dialogo.ShowDialog(this) != true) return;
        if (!Ui.Confirmar($"Restaurar “{Path.GetFileName(dialogo.FileName)}”?",
            "Os dados atuais serão substituídos por este backup. Uma cópia dos dados atuais fica guardada na pasta de dados. O Orthck será fechado; abra-o de novo em seguida.", true)) return;
        Ui.Executar(() =>
        {
            Armazenamento.RestaurarBackup(arm.Pasta, dialogo.FileName);
            Application.Current.Shutdown();
        });
    }

    private void ImportarDoMac()
    {
        if (_janela.ImportarDados()) Close();
    }
}
