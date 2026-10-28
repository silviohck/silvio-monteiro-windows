using System.IO.Compression;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>
/// Modo usado pelo CI (Orthck.exe --capturar pasta): monta dados de demonstração fictícios numa pasta temporária,
/// abre cada tela e janela, salva uma imagem PNG de cada uma e registra erros em erros.txt. Nunca toca nos dados reais.
/// </summary>
public static class Capturas
{
    public static bool Ativo { get; private set; }

    public static void Executar(Application app, string saida)
    {
        Ativo = true;
        Directory.CreateDirectory(saida);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var erros = new List<string>();
        app.DispatcherUnhandledException += (_, e) => { erros.Add(e.Exception.ToString()); e.Handled = true; };
        var raiz = Path.Combine(Path.GetTempPath(), "orthck-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);
        Ajustes.Carregar(raiz);
        Tema.Aplicar(app, TamanhoTexto.Normal, Aparencia.Clara);
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Roteiro(app, raiz, saida, erros); }
            catch (Exception ex) { erros.Add(ex.ToString()); }
            File.WriteAllText(Path.Combine(saida, "erros.txt"), erros.Count == 0 ? "Nenhum erro.\n" : string.Join("\n\n", erros));
            try { Directory.Delete(raiz, true); } catch { }
            app.Shutdown(erros.Count == 0 ? 0 : 2);
        });
    }

    private static async Task Roteiro(Application app, string raiz, string saida, List<string> erros)
    {
        var arm = new Armazenamento(Path.Combine(raiz, "dados"));
        arm.Dados.PastaDocumentos = Path.Combine(raiz, "Documentos");
        // Pasta de modelos de demonstração, na estrutura Local / Tipo / [Subpasta] / Modelo.docx.
        var modelos = Path.Combine(raiz, "Modelos do consultório");
        Docx(Path.Combine(modelos, "CAMPINA GRANDE", "Atestados", "Atestado de comparecimento.docx"), "Atesto que {{NOME_PACIENTE}}, CPF {{CPF}}, esteve em atendimento em {{DATA}}. CID {{CID}}. {{DIAS_AFASTAMENTO}} dias de afastamento.");
        Docx(Path.Combine(modelos, "CAMPINA GRANDE", "Laudos", "Laudo ortopédico.docx"), "{{NOME_PACIENTE}} ({{IDADE}} anos). Diagnóstico: {{DIAGNOSTICO}}. CID {{CID}}. Conduta: {{CONDUTA}}. {{CIDADE}}, {{DATA_EXTENSO}}.");
        Docx(Path.Combine(modelos, "CAMPINA GRANDE", "RX", "Joelho", "RX joelho.docx"), "Solicito RX do joelho de {{NOME_PACIENTE}}. Indicação: {{INDICACAO}}.");
        Docx(Path.Combine(modelos, "CAMPINA GRANDE", "Receitas", "Receita simples.docx"), "Paciente: {{NOME}}. {{PRESCRICAO}}");
        Docx(Path.Combine(modelos, "ALAGOA GRANDE", "Atestados", "Atestado.docx"), "Atesto que {{NOME_PACIENTE}} esteve aqui em {{DATA}}.");
        Docx(Path.Combine(modelos, "1 - APAC", "Fisioterapia", "APAC fisioterapia.docx"), "{{NOME_PACIENTE}} {{CPF}} {{CID}} {{JUSTIFICATIVA}}");
        arm.DefinirPastaSincronizada(modelos);
        await arm.SincronizarAsync();
        var campina = arm.Dados.Locais.First(l => l.Nome == "CAMPINA GRANDE");
        campina.Cidade = "Campina Grande";
        var nomes = new[] { ("Ana Exemplo da Silva", "12/03/1985"), ("Bruno Fictício Souza", "05/07/1972"), ("Carla Demonstração Lima", ""), ("Carla Demonstração Lima", "21/11/1990"), ("Diego Teste Rocha", "30/01/2001") };
        foreach (var (nome, nascimento) in nomes)
        {
            var p = arm.NovoPaciente(nome);
            p.DataNascimento = nascimento; p.Cpf = nascimento.Length > 0 ? "000.000.000-00" : ""; p.LocalId = campina.Id;
        }
        arm.Salvar();
        Ajustes.Atual.LocalSelecionado = campina.Id.ToString();
        var laudo = arm.Dados.Modelos.First(m => m.Nome == "Laudo ortopédico");
        for (var i = 0; i < 4; i++)
        {
            var destino = arm.CaminhoParaNovoDocumento(laudo, laudo.Categoria, campina, nomes[i].Item1);
            DocxTemplate.Fill(arm.UrlDoModelo(laudo), destino, new Dictionary<string, string> { ["NOME_PACIENTE"] = nomes[i].Item1 });
            var doc = new DocumentoGerado { Modelo = laudo.Nome, ModeloId = laudo.Id, Paciente = nomes[i].Item1, PacienteId = arm.Dados.Pacientes[i].Id, Local = campina.Nome, Caminho = destino };
            arm.Registrar(doc);
            var item = arm.AdicionarAFila(doc, i + 1);
            item.Situacao = i switch { 0 => SituacaoImpressao.Enviado, 1 => SituacaoImpressao.Incerto, _ => SituacaoImpressao.Pendente };
            if (i == 0) item.Observacao = "Comando enviado ao Word. Confira o resultado na impressora.";
            if (i == 1) item.Observacao = "O Word não respondeu em 5 minutos. Confira a impressora antes de reenviar.";
        }
        arm.Salvar();

        var janela = new JanelaPrincipal(arm, new Word()) { Width = 1280, Height = 860, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        app.MainWindow = janela;
        janela.Show();
        await Esperar();

        async Task Foto(string nome, Window? w = null)
        {
            try
            {
                await Esperar();
                var alvo = w ?? janela;
                // A raiz visual da janela inclui a camada de adornos (textos de exemplo nas caixas).
                var conteudo = (FrameworkElement)VisualTreeHelper.GetChild(alvo, 0);
                var largura = (int)Math.Ceiling(conteudo.ActualWidth); var altura = (int)Math.Ceiling(conteudo.ActualHeight);
                var bmp = new RenderTargetBitmap(Math.Max(largura, 1), Math.Max(altura, 1), 96, 96, PixelFormats.Pbgra32);
                var fundo = new DrawingVisual();
                using (var dc = fundo.RenderOpen()) dc.DrawRectangle(Tema.Pincel(Tema.Fundo), null, new Rect(0, 0, largura, altura));
                bmp.Render(fundo);
                bmp.Render(conteudo);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using var arquivo = File.Create(Path.Combine(saida, nome + ".png"));
                png.Save(arquivo);
            }
            catch (Exception ex) { erros.Add($"{nome}: {ex}"); }
        }

        async Task Tela(Secao secao, string nome)
        {
            try { janela.Mostrar(secao); } catch (Exception ex) { erros.Add($"{nome}: {ex}"); }
            await Foto(nome);
        }

        async Task Dialogo(Func<Window> criar, string nome)
        {
            try
            {
                var d = criar();
                d.Owner = janela; d.WindowStartupLocation = WindowStartupLocation.Manual; d.Left = 40; d.Top = 40;
                d.Show();
                await Foto(nome, d);
                d.Close();
            }
            catch (Exception ex) { erros.Add($"{nome}: {ex}"); }
        }

        await Tela(Secao.Gerar, "01-gerar-vazio");
        var gerar = (GerarView)((System.Windows.Controls.ContentControl)((System.Windows.Controls.Grid)janela.Content).Children[1]).Content;
        gerar.Demonstrar("Laudos", laudo.Id, "Ana Exemplo da Silva", new() { ["CID"] = "lombalgia", ["DIAGNOSTICO"] = "Lombalgia mecânica" });
        await Foto("02-gerar-preenchido");
        gerar.Demonstrar("Laudos", laudo.Id, "Carla Demonstração Lima", []);
        await Foto("03-gerar-homonimos");
        gerar.Demonstrar("Laudos", laudo.Id, "Bru", []);
        await Foto("04-gerar-sugestoes");
        await Tela(Secao.Fila, "05-fila");
        await Tela(Secao.Pacientes, "06-pacientes");
        await Tela(Secao.Locais, "07-locais");
        await Tela(Secao.Modelos, "08-modelos");
        await Tela(Secao.Historico, "09-historico");
        await Tela(Secao.Cid, "10-cid");
        await Tela(Secao.Sigtap, "11-sigtap");
        await Tela(Secao.Tuss, "12-tuss");
        await Dialogo(() => new CadastroPacienteJanela(arm, arm.Dados.Pacientes[0]), "13-cadastro-paciente");
        await Dialogo(() => new ExcluirLocalJanela(arm, campina), "14-excluir-local");
        await Dialogo(() => new GerenciarTiposJanela(arm, campina.Id), "15-gerenciar-tipos");
        await Dialogo(() => new AjustesSincronizacaoJanela(janela), "16-pasta-sincronizada");
        await Dialogo(() => new AjustesJanela(janela), "17-ajustes");

        Tema.Aplicar(app, TamanhoTexto.Normal, Aparencia.Escura);
        await Tela(Secao.Gerar, "18-gerar-escuro");
        await Tela(Secao.Fila, "19-fila-escuro");
        await Tela(Secao.Modelos, "20-modelos-escuro");
        Tema.Aplicar(app, TamanhoTexto.MuitoGrande, Aparencia.Clara);
        await Tela(Secao.Gerar, "21-gerar-texto-muito-grande");
        janela.Close();
    }

    private static async Task Esperar()
    {
        await Task.Delay(350);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>DOCX mínimo com um parágrafo, para os modelos de demonstração.</summary>
    private static void Docx(string caminho, string texto)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        using var zip = ZipFile.Open(caminho, ZipArchiveMode.Create);
        void Entrada(string nome, string conteudo) { using var s = new StreamWriter(zip.CreateEntry(nome).Open()); s.Write(conteudo); }
        Entrada("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Entrada("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Entrada("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(texto)}</w:t></w:r></w:p></w:body></w:document>");
    }
}
