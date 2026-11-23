using Orthck.Core;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

var falhas = 0;
void Assert(bool ok, string mensagem)
{
    if (!ok) { falhas++; Console.WriteLine("FALHOU: " + mensagem); }
    else Console.WriteLine("OK: " + mensagem);
}
XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

var raiz = Path.Combine(Path.GetTempPath(), "orthck-testes-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(raiz);
string CriarDocx(string caminho, string corpoXml)
{
    Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
    using var zip = ZipFile.Open(caminho, ZipArchiveMode.Create);
    using (var s = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open())) s.Write("<Types/>");
    using (var s = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
        s.Write($"<w:document xmlns:w=\"{w}\"><w:body>{corpoXml}</w:body></w:document>");
    return caminho;
}
string TextoDo(string docx)
{
    using var zip = ZipFile.OpenRead(docx);
    return string.Concat(XDocument.Load(zip.GetEntry("word/document.xml")!.Open()).Descendants(w + "t").Select(t => t.Value));
}

try
{
    // ===== DOCX =====
    var modeloDocx = CriarDocx(Path.Combine(raiz, "modelo.docx"),
        "<w:p><w:r><w:t>Atesto: {{NOME_</w:t></w:r><w:r><w:t>PACIENTE}} CID {{CID}}</w:t></w:r></w:p>");
    Assert(DocxTemplate.Placeholders(modeloDocx).SequenceEqual(["NOME_PACIENTE", "CID"]), "localiza campos divididos entre runs");
    var a = Path.Combine(raiz, "a.docx"); var b = Path.Combine(raiz, "b.docx");
    DocxTemplate.Fill(modeloDocx, a, new Dictionary<string, string> { ["NOME_PACIENTE"] = "Paciente A", ["CID"] = "M17.1" });
    DocxTemplate.Fill(modeloDocx, b, new Dictionary<string, string> { ["NOME_PACIENTE"] = "Paciente B", ["CID"] = "S83.5" });
    Assert(DocxTemplate.Placeholders(a).Count == 0, "nenhum campo pendente após preencher");
    Assert(TextoDo(a).Contains("Paciente A") && !TextoDo(b).Contains("Paciente A") && TextoDo(b).Contains("S83.5") && !TextoDo(b).Contains("M17.1"),
        "documentos consecutivos não misturam pacientes nem CID");
    var antesDoModelo = File.ReadAllBytes(modeloDocx);
    try { DocxTemplate.Fill(modeloDocx, a, new Dictionary<string, string>()); Assert(false, "não sobrescreve documento existente"); }
    catch (IOException) { Assert(File.ReadAllBytes(modeloDocx).SequenceEqual(antesDoModelo), "não sobrescreve documento existente e não altera o modelo"); }
    var colado = Path.Combine(raiz, "colado.docx");
    DocxTemplate.Fill(modeloDocx, colado, new Dictionary<string, string> { ["NOME_PACIENTE"] = "A\u0001", ["CID"] = "M17.1\vS83\tok 👍\nfim" });
    using (var z = ZipFile.OpenRead(colado))
    {
        var x = XDocument.Load(z.GetEntry("word/document.xml")!.Open());
        Assert(x.Descendants(w + "br").Count() == 2 && x.Descendants(w + "tab").Count() == 1 && TextoDo(colado).Contains("ok 👍") && !TextoDo(colado).Contains('\u0001'),
            "quebras, tabulação e caracteres de controle colados do Word");
    }

    // ===== Campos automáticos =====
    var maria = new Paciente { Nome = "Maria", Cpf = "000", DataNascimento = " 15/03/1990 " };
    var clinica = new LocalAtendimento { Nome = "Clínica", Cidade = "Campina Grande" };
    var dia = new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.FromHours(-3));
    Assert(CamposAutomaticos.Valor("Data de Nascimento", maria, clinica, dia) == " 15/03/1990 " && CamposAutomaticos.Valor("idade", maria, clinica, dia) == "35",
        "aliases sem acento/maiúsculas e idade antes do aniversário");
    Assert(CamposAutomaticos.Valor("DATA_EXTENSO", null, null, dia) == "14 de março de 2026" && CamposAutomaticos.Valor("cidade", null, clinica, dia) == "Campina Grande",
        "data por extenso e dados do local");
    Assert(CamposAutomaticos.Valor("CID", maria, clinica, dia) == null && CamposAutomaticos.EhCampoCid("CID_PRINCIPAL") && CamposAutomaticos.EhCampoCid("outro cid"),
        "CID é digitado na hora e recebe sugestões");
    Assert(CamposAutomaticos.EhCampoCid("CID") && CamposAutomaticos.EhCampoCid("CID2")
        && !CamposAutomaticos.EhCampoCid("CIDADE") && !CamposAutomaticos.EhCampoCid("LOCAL_CIDADE"),
        "CIDADE não é campo de CID (sem sugestões da tabela CID-10)");
    var paciente = new Paciente { Nome = "Maria" };
    CamposAutomaticos.Guardar(new Dictionary<string, string> { ["CPF"] = " 111 ", ["CID"] = "M17.1", ["CONDUTA"] = "repouso" }, ["CPF", "CID", "CONDUTA"], paciente);
    Assert(paciente.Cpf == "111" && JsonSerializer.Serialize(paciente).Contains("M17.1") == false, "só dados gerais ficam no cadastro (CID e conduta não)");

    // ===== Armazenamento =====
    var pastaDados = Path.Combine(raiz, "dados");
    var arm = new Armazenamento(pastaDados);
    var local = arm.NovoLocal();
    local.Nome = "Campina Grande"; arm.Salvar();
    Assert(local.Tipos.SequenceEqual(DadosApp.CategoriasPadrao), "local novo começa com os tipos padrão");
    var importado = arm.ImportarModelo(CriarDocx(Path.Combine(raiz, "origem", "Receita simples.docx"), "<w:p><w:r><w:t>{{NOME}} {{CID}}</w:t></w:r></w:p>"), local.Id);
    Assert(importado.Categoria == "Receitas" && File.Exists(arm.UrlDoModelo(importado)) && importado.Campos.SequenceEqual(["NOME", "CID"]),
        "importar modelo copia o arquivo, sugere o tipo e lê os campos");
    Assert(Armazenamento.CategoriaSugerida("RX joelho", DadosApp.CategoriasPadrao) == "Radiografias" && Armazenamento.CategoriaSugerida("xpto", DadosApp.CategoriasPadrao) == "Outros",
        "tipo sugerido por apelido e Outros como padrão");
    arm.RenomearCategoria("Receitas", "Receitas médicas", local.Id);
    Assert(importado.Categoria == "Receitas médicas" && arm.Tipos(local.Id).Contains("Receitas médicas") && !arm.Tipos(local.Id).Contains("Receitas"), "renomear tipo leva os modelos junto");
    arm.RemoverCategoria("Receitas médicas", "Outros", local.Id);
    Assert(importado.Categoria == "Outros", "excluir tipo move os modelos");

    // Importar uma pasta inteira
    var pastaAvulsa = Path.Combine(raiz, "pasta para importar");
    CriarDocx(Path.Combine(pastaAvulsa, "Atestado simples.docx"), "<w:p><w:r><w:t>{{NOME}}</w:t></w:r></w:p>");
    CriarDocx(Path.Combine(pastaAvulsa, "RX", "Joelho", "rx joelho.docx"), "<w:p><w:r><w:t>{{NOME}}</w:t></w:r></w:p>");
    CriarDocx(Path.Combine(pastaAvulsa, "RX", "~$rx joelho.docx"), "<w:p/>");
    File.WriteAllText(Path.Combine(pastaAvulsa, "notas.txt"), "x");
    var antes = arm.Dados.Modelos.Count;
    var (qtd, errosPasta) = arm.ImportarPasta(pastaAvulsa, local.Id);
    var rxJoelho = arm.Dados.Modelos.Single(m => m.Nome == "rx joelho");
    Assert(qtd == 2 && errosPasta.Count == 0 && arm.Dados.Modelos.Count == antes + 2 && rxJoelho.Categoria == "RX / Joelho" && rxJoelho.LocalId == local.Id
        && arm.Dados.Modelos.Single(m => m.Nome == "Atestado simples").Categoria == "Atestados",
        "importar pasta inteira: subpasta vira o tipo, temporários e outros arquivos ficam de fora");

    // Homônimos e locais do paciente
    var p1 = arm.NovoPaciente("José Silva"); var p2 = arm.NovoPaciente("JOSÉ SILVA"); p2.DataNascimento = "01/01/1980";
    Assert(arm.PacientesChamados("jose silva").Count == 2 && Armazenamento.Identificacao(p2) == "nasc. 01/01/1980", "homônimos sem diferenciar acentos e identificação");
    arm.Registrar(new DocumentoGerado { Modelo = "x", Paciente = p1.Nome, PacienteId = p1.Id, Local = "CAMPINA GRANDE" });
    Assert(arm.NomesDosLocais(p1) == "Campina Grande", "locais onde o paciente já foi atendido");
    // Painel "Atendidos recentemente": por local e por dia, um por paciente.
    arm.Registrar(new DocumentoGerado { Modelo = "y", Paciente = p1.Nome, PacienteId = p1.Id, Local = "Campina Grande" });
    Assert(arm.PacientesRecentes(null, null, 12).Count(r => r.Paciente.Id == p1.Id) == 1 && arm.PacientesRecentes(null, null, 12).First().Ultimo.Modelo == "y"
        && arm.PacientesRecentes("campina grande", DateTime.Today, 12).Any(r => r.Paciente.Id == p1.Id)
        && arm.PacientesRecentes("Araruna", null, 12).Count == 0 && arm.PacientesRecentes(null, DateTime.Today.AddDays(-3), 12).Count == 0
        && arm.DocumentosDe(p1).Count == 2 && arm.DiasComAtendimento.SequenceEqual([DateTime.Today]),
        "atendidos recentemente: um por paciente, filtro de local e de dia, documentos do paciente");

    // Pastas dos documentos
    arm.Dados.PastaDocumentos = Path.Combine(raiz, "Documentos"); arm.Salvar();
    Directory.CreateDirectory(Path.Combine(raiz, "Documentos", "CAMPINA GRANDE", "rx"));
    Assert(arm.PastaDosDocumentos("RX / Joelho", local) == Path.Combine(raiz, "Documentos", "CAMPINA GRANDE", "rx", "Joelho"),
        "pasta do local e do tipo reaproveitadas sem diferenciar maiúsculas; subpastas em níveis");
    var caminho1 = arm.CaminhoParaNovoDocumento(importado, "Outros", local, "José: Silva", data: new DateTime(2026, 10, 27));
    File.WriteAllText(caminho1, "");
    var caminho2 = arm.CaminhoParaNovoDocumento(importado, "Outros", local, "José: Silva", data: new DateTime(2026, 10, 27));
    Assert(Path.GetFileName(caminho1) == "Receita simples - José- Silva - Campina Grande - 27-10-2026.docx" && caminho2.EndsWith(" (2).docx"),
        "nome do documento com modelo, paciente, local e data; nunca sobrescreve");
    Assert(Path.GetDirectoryName(caminho1) == arm.PastaTemporaria, "documentos gerados vão para a pasta temporária do app");
    // Nome único também em relação ao Histórico (o Word imprimiria o documento de mesmo nome que já estivesse aberto).
    var modeloUnico = new ModeloDocumento { Nome = "Unico" };
    var primeiroUnico = arm.CaminhoParaNovoDocumento(modeloUnico, "", null, "Ana", Path.Combine(raiz, "u1"), new DateTime(2026, 11, 1));
    arm.Registrar(new DocumentoGerado { Modelo = "Unico", Paciente = "Ana", Caminho = primeiroUnico });
    var segundoUnico = arm.CaminhoParaNovoDocumento(modeloUnico, "", null, "Ana", Path.Combine(raiz, "u2"), new DateTime(2026, 11, 1));
    Assert(segundoUnico.EndsWith(" (2).docx"), "nome do documento nunca repete um do Histórico, mesmo em outra pasta");
    var antigo = arm.CaminhoParaNovoDocumento(importado, "Outros", local, "Antigo", data: new DateTime(2026, 10, 1));
    File.WriteAllText(antigo, ""); File.SetLastWriteTime(antigo, DateTime.Now.AddDays(-2));
    File.SetLastWriteTime(caminho1, DateTime.Now.AddDays(-2));
    arm.Dados.FilaImpressao.Add(new ItemFilaImpressao { Caminho = caminho1, Situacao = SituacaoImpressao.Pendente });
    arm.LimparDocumentosTemporarios();
    Assert(!File.Exists(antigo) && File.Exists(caminho1), "limpeza apaga os antigos e mantém os que esperam na fila");
    arm.Dados.FilaImpressao.Clear();
    Assert(Texto.NomeSeguro("CON") == "_CON" && Texto.NomeSeguro("a/b:c") == "a-b-c", "nomes reservados e inválidos do Windows");

    // Excluir local transferindo vínculos
    var outro = arm.NovoLocal(); outro.Nome = "Araruna";
    p1.LocalId = local.Id; arm.Salvar();
    arm.ExcluirLocal(local.Id, outro.Id);
    Assert(importado.LocalId == outro.Id && p1.LocalId == outro.Id && arm.Local(local.Id) == null, "excluir local transfere modelos e pacientes");

    // Persistência
    var reaberto = new Armazenamento(pastaDados);
    Assert(reaberto.Dados.Pacientes.Count == 2 && reaberto.Modelo(importado.Id)!.Categoria == "Outros", "dados persistem ao reabrir");
    var json = File.ReadAllText(Path.Combine(pastaDados, "dados.json"));
    Assert(System.Text.RegularExpressions.Regex.IsMatch(json, "\"criadoEm\": \"\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ\"") && json.Contains("\"localID\"") && json.Contains("\"pastasCompartilhadas\""),
        "dados.json no formato do Mac (datas ISO 8601 sem frações, chaves iguais)");
    Assert(reaberto.Backups().Count == 0 || reaberto.Backups().All(File.Exists), "backups diários na pasta Backups");

    // Arquivo ilegível nunca é sobrescrito
    var pastaRuim = Path.Combine(raiz, "ruim"); Directory.CreateDirectory(pastaRuim);
    File.WriteAllText(Path.Combine(pastaRuim, "dados.json"), "{ quebrado");
    try { _ = new Armazenamento(pastaRuim); Assert(false, "dados ilegíveis são recusados"); }
    catch (DadosIlegiveisException ex) { Assert(File.ReadAllText(Path.Combine(pastaRuim, "dados.json")) == "{ quebrado" && File.Exists(ex.Copia), "dados ilegíveis: original intacto e cópia guardada"); }

    // ===== Importação do Mac =====
    var pastaMac = Path.Combine(raiz, "GeradorDocumentos"); Directory.CreateDirectory(Path.Combine(pastaMac, "Modelos"));
    CriarDocx(Path.Combine(pastaMac, "Modelos", "AAAA.docx"), "<w:p><w:r><w:t>{{NOME}}</w:t></w:r></w:p>");
    File.WriteAllText(Path.Combine(pastaMac, "dados.json"), """
        {"modelos":[{"id":"9C68F6E3-E189-4BCA-98F9-15E71C8534E5","nome":"Atestado","arquivo":"AAAA.docx","campos":["NOME"],"categoria":"Atestados","copias":2,"criadoEm":"2026-10-01T10:00:00Z"}],
         "pacientes":[{"id":"E621E1F8-C36C-495A-93FC-0C247A3E6E5F","nome":"Maria","cpf":"","rg":"","dataNascimento":"","telefone":"","email":"","endereco":"","convenio":"","observacoes":""}],
         "locais":[{"id":"0FC3EB25-CA47-4D35-B100-035AF4739E3E","nome":"Opera PB","pasta":"/Users/x/Docs"}],
         "historico":[{"id":"1FC3EB25-CA47-4D35-B100-035AF4739E3E","modelo":"Atestado","paciente":"Maria","caminho":"/Users/x/a.docx","data":"2026-10-27T13:00:00Z"}],
         "filaImpressao":[{"id":"2FC3EB25-CA47-4D35-B100-035AF4739E3E","documentoID":"1FC3EB25-CA47-4D35-B100-035AF4739E3E","caminho":"/Users/x/a.docx","modelo":"Atestado","paciente":"Maria","copias":1,"criadoEm":"2026-10-27T13:00:00Z","situacao":"enviando"}],
         "pastaSincronizada":"/Users/x/Modelos","campoFuturo":42}
        """);
    var imp = new Armazenamento(Path.Combine(raiz, "importado"));
    try { imp.ImportarDadosDoMac(Path.Combine(raiz, "nao-existe.json")); Assert(false, "importar arquivo ausente"); }
    catch (FileNotFoundException) { Assert(imp.Dados.Pacientes.Count == 0, "importar arquivo ausente não altera nada"); }
    var resumo = imp.ImportarDadosDoMac(Path.Combine(pastaMac, "dados.json"));
    Assert(imp.Dados.Modelos.Single().Copias == 2 && File.Exists(imp.UrlDoModelo(imp.Dados.Modelos[0])) && resumo.Contains("1 paciente"),
        "importa dados e cópias dos modelos do Mac");
    Assert(imp.Dados.FilaImpressao[0].Situacao == SituacaoImpressao.Incerto && imp.Dados.PastaSincronizada == "" && imp.Dados.Locais[0].Pasta == "",
        "importação: envio interrompido fica incerto e caminhos do Mac são descartados");
    Assert(imp.Dados.Historico[0].Data == new DateTimeOffset(2026, 10, 27, 13, 0, 0, TimeSpan.Zero) && imp.Dados.Locais[0].Tipos.Count > 0,
        "datas do Mac e tipos por local");
    Assert(File.ReadAllText(imp.ArquivoDados).Contains("campoFuturo"), "preserva campos desconhecidos do Mac");

    // ===== Fila de impressão =====
    var docs = Enumerable.Range(1, 3).Select(i =>
    {
        var arquivo = Path.Combine(raiz, $"fila{i}.docx"); File.Copy(a, arquivo);
        var d = new DocumentoGerado { Modelo = "Laudo", Paciente = $"P{i}", Caminho = arquivo };
        arm.Registrar(d); return d;
    }).ToList();
    var itens = docs.Select(d => arm.AdicionarAFila(d, 2)).ToList();
    Assert(arm.AdicionarAFila(docs[0], 2).Id == itens[0].Id && arm.PendentesNaFila == 3, "não duplica documento pendente na fila");
    var simulada = new ImpressoraSimulada(DesfechoImpressao.Enviado, DesfechoImpressao.Incerto, DesfechoImpressao.Enviado);
    await arm.ImprimirFilaAsync(itens.Select(i => i.Id), null, simulada);
    Assert(itens[0].Situacao == SituacaoImpressao.Enviado && itens[1].Situacao == SituacaoImpressao.Incerto && itens[2].Situacao == SituacaoImpressao.Pendente && simulada.Enviados == 2,
        "envio incerto interrompe o restante do lote");
    await arm.ImprimirFilaAsync(itens.Select(i => i.Id), null, simulada);
    Assert(itens[1].Situacao == SituacaoImpressao.Incerto, "incerto nunca é reenviado sem confirmação");
    Assert(arm.Reenfileirar(itens[1].Id) && itens[1].Situacao == SituacaoImpressao.Pendente, "reenvio só com confirmação explícita");
    var novoItem = arm.AdicionarAFila(docs[0], 1);
    Assert(novoItem.Id != itens[0].Id, "documento já enviado volta à fila pelo Histórico");
    // O mesmo arquivo não vai ao Word duas vezes em 20 segundos (clique duplo, dois caminhos).
    var enviadosAntes = simulada.Enviados;
    await arm.ImprimirFilaAsync([novoItem.Id], null, simulada);
    Assert(simulada.Enviados == enviadosAntes && novoItem.Situacao == SituacaoImpressao.Erro, "mesmo arquivo não é reenviado logo em seguida");
    arm.RemoverDaFila(novoItem.Id);
    // Impresso fica 2 minutos na fila e depois sai sozinho.
    Assert(itens[0].EnviadoEm != null && !arm.LimparEnviadosAntigos(DateTimeOffset.Now.AddMinutes(1)) && arm.Dados.FilaImpressao.Any(x => x.Id == itens[0].Id),
        "enviado continua na fila por 2 minutos");
    Assert(arm.LimparEnviadosAntigos(DateTimeOffset.Now.AddMinutes(3)) && !arm.Dados.FilaImpressao.Any(x => x.Id == itens[0].Id)
        && arm.Dados.FilaImpressao.Any(x => x.Id == itens[1].Id), "depois de 2 minutos o enviado sai; os outros ficam");
    // O teste seguinte usa o item 3 (também enviado e retirado acima): volta para a fila.
    if (!arm.Dados.FilaImpressao.Contains(itens[2])) arm.Dados.FilaImpressao.Add(itens[2]);
    // Parar sem nada imprimindo não faz nada (e não quebra).
    arm.PararFila();
    itens[2].Situacao = SituacaoImpressao.Enviando; arm.Salvar();
    Assert(new Armazenamento(pastaDados).Dados.FilaImpressao.Single(x => x.Id == itens[2].Id).Situacao == SituacaoImpressao.Incerto
        && File.ReadAllText(Path.Combine(pastaDados, "dados.json")).Contains("\"incerto\""), "app fechado durante envio: fica incerto e gravado");

    // Gravação bloqueada: nada é enviado ao Word e o item volta a aguardar (sem ficar preso em "enviando").
    var pastaBloqueada = Path.Combine(raiz, "bloqueada");
    var bloq = new Armazenamento(pastaBloqueada);
    var docBloq = new DocumentoGerado { Modelo = "Laudo", Paciente = "X", Caminho = docs[0].Caminho };
    bloq.Registrar(docBloq);
    var itemBloq = bloq.AdicionarAFila(docBloq, 1);
    File.Delete(bloq.ArquivoDados); Directory.CreateDirectory(bloq.ArquivoDados);
    var naoDeveImprimir = new ImpressoraSimulada(DesfechoImpressao.Enviado);
    try { await bloq.ImprimirFilaAsync([itemBloq.Id], null, naoDeveImprimir); Assert(false, "gravação bloqueada interrompe a impressão"); }
    catch (IOException) { Assert(naoDeveImprimir.Enviados == 0 && itemBloq.Situacao == SituacaoImpressao.Pendente && !bloq.ProcessandoFila, "gravação bloqueada: nada é enviado e o item volta a aguardar"); }

    // Caminho longo: o Word não abre arquivos acima de 259 caracteres.
    var pastaFunda = Path.Combine(raiz, new string('p', 40), new string('q', 40), new string('r', 40));
    var nomeLongo = new ModeloDocumento { Nome = new string('M', 80) };
    var longo = arm.CaminhoParaNovoDocumento(nomeLongo, "", null, new string('N', 80), pastaFunda, new DateTime(2026, 10, 28));
    Assert(Path.GetFullPath(longo).Length <= Armazenamento.MaximoDoCaminho - 5 && longo.EndsWith("28-10-2026.docx") && Path.GetFileName(longo).StartsWith("MMMM"),
        $"caminho longo encurta o nome mantendo o começo e a data ({Path.GetFullPath(longo).Length}: {Path.GetFileName(longo)})");

    // Locais por paciente numa passada só (lista grande de pacientes).
    var mapa = arm.LocaisPorPaciente();
    Assert(mapa[p1.Id].SetEquals(arm.LocaisDoPaciente(p1).Select(l => l.Id)), "locais por paciente iguais ao cálculo individual");

    // ===== Sincronização =====
    var pastaSync = Path.Combine(raiz, "Modelos sincronizados");
    var simples = "<w:p><w:r><w:t>{{NOME}}</w:t></w:r></w:p>";
    CriarDocx(Path.Combine(pastaSync, "OPERA PB", "RX", "Joelho", "1 - rx joelho.docx"), simples);
    CriarDocx(Path.Combine(pastaSync, "OPERA PB", "Laudos", "laudo.docx"), simples);
    CriarDocx(Path.Combine(pastaSync, "OPERA PB", "Laudos", "~$laudo.docx"), simples);
    CriarDocx(Path.Combine(pastaSync, "1 - APAC", "Fisioterapia", "apac fisio.docx"), simples);
    CriarDocx(Path.Combine(pastaSync, "Mídias", "video.docx"), simples);
    var sync = new Armazenamento(Path.Combine(raiz, "sync"));
    sync.Dados.PastasIgnoradas.Add("Mídias");
    sync.DefinirPastaSincronizada(pastaSync);
    await sync.SincronizarAsync();
    var rx = sync.Dados.Modelos.Single(m => m.Nome == "rx joelho");
    var apac = sync.Dados.Modelos.Single(m => m.Nome == "apac fisio");
    var opera = sync.Dados.Locais.Single(l => l.Nome == "OPERA PB");
    Assert(sync.Dados.Modelos.Count == 3 && rx.Categoria == "RX / Joelho" && rx.LocalId == opera.Id && rx.Origem == "OPERA PB/RX/Joelho/1 - rx joelho.docx",
        "sincroniza: local, tipo pela pasta, subpasta em níveis e nome sem o número da frente");
    Assert(apac.LocalId == null && apac.Categoria == "1 - APAC / Fisioterapia" && !sync.Dados.Locais.Any(l => l.Nome == "1 - APAC") && !sync.Dados.Modelos.Any(m => m.Nome == "video"),
        "pasta compartilhada vale para todos os locais; ignorada e temporários do Word ficam de fora");
    Assert(sync.ModelosPara(opera).Count == 3 && sync.ModelosPara(null).Count == 1, "modelos de cada local mais os de todos os locais");
    // Favoritos de cada local, na ordem escolhida; chave do local em maiúsculas como no Mac.
    sync.AlternarFavorito(rx, opera.Id); sync.AlternarFavorito(apac, opera.Id);
    Assert(sync.FavoritosPara(opera).Select(m => m.Id).SequenceEqual([rx.Id, apac.Id]) && sync.FavoritosPara(null).Count == 0
        && sync.Dados.FavoritosPorLocal.ContainsKey(opera.Id.ToString().ToUpperInvariant()) && sync.EhFavorito(rx, opera.Id) && !sync.EhFavorito(apac, null),
        "favoritos separados por local");
    sync.MoverFavorito(apac, rx, opera);
    Assert(sync.FavoritosPara(opera).Select(m => m.Id).SequenceEqual([apac.Id, rx.Id]), "favorito movido para antes de outro");
    sync.MoverFavorito(apac, null, opera);
    Assert(sync.FavoritosPara(opera).Select(m => m.Id).SequenceEqual([rx.Id, apac.Id]), "favorito movido para o fim");
    sync.Dados.FavoritosPorLocal.Clear(); sync.Dados.Favoritos = ["apac fisio"]; sync.Salvar();
    var migrado = new Armazenamento(sync.Pasta);
    Assert(migrado.Dados.Favoritos.Count == 0 && migrado.Dados.FavoritosPorLocal[""].SequenceEqual(["apac fisio"])
        && migrado.Dados.FavoritosPorLocal[opera.Id.ToString().ToUpperInvariant()].SequenceEqual(["apac fisio"]),
        "favoritos de versões anteriores passam para cada local que tem o modelo");
    sync.Dados.FavoritosPorLocal = migrado.Dados.FavoritosPorLocal; sync.Dados.Favoritos = [];
    var copiaAntiga = sync.UrlDoModelo(rx);
    File.SetLastWriteTimeUtc(rx.Origem!.Split('/').Aggregate(pastaSync, Path.Combine), DateTime.UtcNow.AddMinutes(5));
    File.Delete(Path.Combine(pastaSync, "OPERA PB", "Laudos", "laudo.docx"));
    await sync.SincronizarAsync();
    Assert(sync.Sincronizacao.Atualizados == 1 && sync.Sincronizacao.Removidos == 1 && !File.Exists(copiaAntiga) && sync.Dados.Modelos.Count == 2,
        "arquivo editado é atualizado e arquivo apagado sai do app");
    Directory.Move(pastaSync, pastaSync + " (desconectada)");
    await sync.SincronizarAsync();
    Assert(sync.Dados.Modelos.Count == 2 && sync.Sincronizacao.Erros.Count == 1, "pasta indisponível não remove nenhum modelo");
    Directory.Move(pastaSync + " (desconectada)", pastaSync);
    var zip = Path.Combine(raiz, "pasta.zip");
    await sync.CompactarPastaSincronizadaAsync(zip);
    using (var z = ZipFile.OpenRead(zip))
        Assert(z.Entries.Any(e => e.FullName.EndsWith("Orthck - dados (backup).json")) && !z.Entries.Any(e => e.Name.StartsWith("~$")), "compactar pasta leva os dados e deixa os temporários");
    // Modelo editado no Word antes da sincronização: na hora de gerar, a cópia é atualizada.
    var originalRx = rx.Origem!.Split('/').Aggregate(pastaSync, Path.Combine);
    Assert(!sync.AtualizarModeloDaPasta(rx), "modelo em dia não é copiado de novo");
    File.Delete(originalRx);
    CriarDocx(originalRx, "<w:p><w:r><w:t>{{NOME}} {{LADO}}</w:t></w:r></w:p>");
    File.SetLastWriteTimeUtc(originalRx, DateTime.UtcNow.AddMinutes(10));
    Assert(sync.AtualizarModeloDaPasta(rx) && rx.Campos.Contains("LADO") && !sync.AtualizarModeloDaPasta(rx),
        "modelo alterado na pasta é copiado de novo na hora de gerar e avisa que os campos mudaram");

    // ===== Subpastas em níveis =====
    var arvore = NoSubpasta.Arvore(["RX / Joelho", "1 - APAC / Fisioterapia / coluna", "RX", "1 - APAC / Fisioterapia", "10 - Laudos", "2 - Receitas"]);
    Assert(string.Join(",", arvore.Select(n => n.Nome)) == "1 - APAC,2 - Receitas,10 - Laudos,RX"
        && arvore[3].TemModelos && arvore[3].Filhos.Single().Caminho == "RX / Joelho"
        && !arvore[0].TemModelos && arvore[0].Filhos[0].TemModelos && arvore[0].Filhos[0].Filhos[0].Caminho == "1 - APAC / Fisioterapia / coluna",
        "árvore de subpastas em ordem do Explorador, com pastas que têm modelos próprios");

    // ===== Tabelas =====
    Assert(TabelaDeCodigos.Cid10.Codigos.Count == 14233 && TabelaDeCodigos.Sigtap.Codigos.Count(c => !c.EhTitulo) == 5039,
        "tabelas CID-10 e SIGTAP embutidas completas (a TUSS saiu, como no Mac)");
    Assert(TabelaDeCodigos.Cid10.Buscar("m545", null).First().Codigo == "M54.5" && TabelaDeCodigos.Cid10.Buscar("dor lombar", null).Any(c => c.Codigo == "M54.5"),
        "CID: busca por código sem pontuação e por palavras");
    Assert(TabelaDeCodigos.Cid10.Buscar("colera", null).First().Descricao == "Cólera" && TabelaDeCodigos.Cid10.Grupos.Count == 22, "CID: sem acentos e 22 capítulos");
    Assert(TabelaDeCodigos.Sigtap.Buscar("0301010072", null).First().Codigo == "03.01.01.007-2" && TabelaDeCodigos.FormatarSigtap("0301") == "03.01", "SIGTAP: formato do código");

    // ===== Paridade com o Mac (novembro): Prontuário, negrito, lacunas, juntar dados =====
    // Lacunas ___ não preenchidas (mesmos casos dos testes do Mac).
    Assert(Lacunas.Remover("DOR NO JOELHO DIREITO HÁ 6 MESES.") == "DOR NO JOELHO DIREITO HÁ 6 MESES."
        && Lacunas.Remover("Q.P.: DOR NO JOELHO DIREITO HÁ ___.") == "Q.P.: DOR NO JOELHO DIREITO."
        && Lacunas.Remover("DOR MECÂNICA EM JOELHO DIREITO HÁ ___, PIORA EM ESCADAS E AO LEVANTAR.") == "DOR MECÂNICA EM JOELHO DIREITO, PIORA EM ESCADAS E AO LEVANTAR.",
        "lacunas: texto sem lacuna não muda e a palavra de ligação sai junto");
    Assert(Lacunas.Remover("EXAME FÍSICO: MARCHA ___. CREPITAÇÃO PRESENTE. DERRAME ___. ADM 0° A 110°.") == "EXAME FÍSICO: CREPITAÇÃO PRESENTE. ADM 0° A 110°."
        && Lacunas.Remover("FISIOTERAPIA MOTORA 10 SESSÕES. RETORNO EM ___.") == "FISIOTERAPIA MOTORA 10 SESSÕES.",
        "lacunas: campo vazio curto sai inteiro");
    Assert(Lacunas.Remover("DOR NO OMBRO HÁ 3 MESES. TRAUMA: ___.\nFORÇA: ___. JOBE POSITIVO.") == "DOR NO OMBRO HÁ 3 MESES.\nJOBE POSITIVO."
        && Lacunas.Remover("FISIOTERAPIA MOTORA (FORTALECIMENTO DE QUADRÍCEPS, ALONGAMENTO) ___ SESSÕES. RETORNO EM ___.") == "FISIOTERAPIA MOTORA (FORTALECIMENTO DE QUADRÍCEPS, ALONGAMENTO) SESSÕES."
        && Lacunas.Remover("EXAMES COMPLEMENTARES: RX COM CARGA:\nKELLGREN-LAWRENCE ___.") == "EXAMES COMPLEMENTARES:\n"
        && Lacunas.Remover("FISIOTERAPIA MOTORA (FORTALECIMENTO DE QUADRÍCEPS) ___ SESSÕES. AVALIAR INFILTRAÇÃO.") == "FISIOTERAPIA MOTORA (FORTALECIMENTO DE QUADRÍCEPS) SESSÕES. AVALIAR INFILTRAÇÃO.",
        "lacunas: mantém as linhas, parênteses e títulos de seção");

    // Negrito: **trecho** sai em negrito no Word, com a formatação do run.
    var modeloNegrito = CriarDocx(Path.Combine(raiz, "negrito.docx"),
        "<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Times New Roman\"/><w:b w:val=\"0\"/><w:sz w:val=\"19\"/></w:rPr><w:t>{{HDA}}</w:t></w:r></w:p>");
    var comNegrito = Path.Combine(raiz, "com-negrito.docx");
    DocxTemplate.Fill(modeloNegrito, comNegrito, new Dictionary<string, string> { ["HDA"] = "DOR **INTENSA** À NOITE **SEM PAR" });
    using (var z = ZipFile.OpenRead(comNegrito))
    {
        var x = XDocument.Load(z.GetEntry("word/document.xml")!.Open());
        var runs = x.Descendants(w + "r").ToList();
        var negritos = runs.Where(r => r.Element(w + "rPr")?.Element(w + "b") is { } bb && bb.Attribute(w + "val") == null).ToList();
        Assert(TextoDo(comNegrito) == "DOR INTENSA À NOITE SEM PAR" && negritos.Count == 1 && string.Concat(negritos[0].Descendants(w + "t").Select(t => t.Value)) == "INTENSA"
            && negritos[0].Element(w + "rPr")!.Element(w + "rFonts") != null && negritos[0].Element(w + "rPr")!.Elements(w + "b").Count() == 1 && runs.Count == 3,
            "negrito: trecho entre ** num run em negrito com a mesma fonte; marca sem par removida");
    }
    Assert(Negrito.Alternar("DOR INTENSA", 4, 7) == ("DOR **INTENSA**", 6, 7) && Negrito.Alternar("DOR **INTENSA**", 6, 7) == ("DOR INTENSA", 4, 7)
        && Negrito.Alternar("DOR **INTENSA**", 4, 11) == ("DOR INTENSA", 4, 7) && Negrito.Alternar("AB", 1, 0) == ("A****B", 3, 0),
        "Ctrl+B põe e tira o negrito do trecho selecionado");
    Assert(Negrito.Balancear(["DOR **MUITO", "INTENSA** À NOITE", ""]).SequenceEqual(["DOR **MUITO**", "**INTENSA** À NOITE", ""]),
        "negrito que passa de uma linha para a outra é fechado e reaberto");

    // Modelos de atendimento (os 1.050 do Mac) e o lado.
    Assert(ModelosDeAtendimento.Prontos.Count == 1050 && ModelosDeAtendimento.Buscar("gonartrose", []).Count > 0
        && ModelosDeAtendimento.Buscar("M54.5", []).First().Cid == "M54.5" && ModelosDeAtendimento.Buscar("m545", []).First().Cid == "M54.5",
        "modelos de atendimento embutidos e busca por doença ou CID");
    Assert(ModelosDeAtendimento.ComLado("DOR NO JOELHO [LADO]", "DIREITO") == "DOR NO JOELHO DIREITO"
        && ModelosDeAtendimento.ComLado("DOR NA MÃO [LADO]", "ESQUERDO") == "DOR NA MÃO ESQUERDA"
        && ModelosDeAtendimento.ComLado("GONARTROSE À [LADO]", "DIREITO") == "GONARTROSE À DIREITA"
        && ModelosDeAtendimento.ComLado("GONARTROSE À [LADO]", "BILATERAL") == "GONARTROSE BILATERAL"
        && ModelosDeAtendimento.ComLado("LASÈGUE POSITIVO À [LADO]", "BILATERAL") == "LASÈGUE POSITIVO BILATERALMENTE"
        && ModelosDeAtendimento.ComLado("DOR LOMBAR [LADO]", "DIREITO") == "DOR LOMBAR À DIREITA",
        "lado concorda com a parte do corpo (DIREITO/DIREITA/À DIREITA/BILATERALMENTE)");
    Assert(Prontuario.TrocarLado("DOR NO JOELHO DIREITO. GONARTROSE À DIREITA.", "DIREITO", "ESQUERDO") == "DOR NO JOELHO ESQUERDO. GONARTROSE À ESQUERDA.",
        "trocar o lado troca no texto já escrito");

    // Fichas prontas (Araruna e Alagoa Grande), ligadas ao local pelo nome.
    var pront = new Armazenamento(Path.Combine(raiz, "prontuario"));
    var araruna = pront.NovoLocal(); araruna.Nome = "CER II Araruna"; pront.Salvar();
    pront.InstalarFichasProntas();
    var fichaAraruna = pront.Dados.Fichas.Single(f => f.Arquivo == "ficha-araruna-cer2-v4.docx");
    var fichaAlagoa = pront.Dados.Fichas.Single(f => f.Arquivo == "ficha-alagoa-grande-v2.docx");
    Assert(fichaAraruna.LocalId == araruna.Id && fichaAlagoa.LocalId == null && File.Exists(pront.UrlDoModelo(fichaAraruna))
        && fichaAraruna.Campos.Contains("HDA_LINHA_10") && Prontuario.Continuacoes("HDA", fichaAraruna).Count == 9
        && Prontuario.CamposClinicos(fichaAraruna).Contains("HDA") && !Prontuario.CamposClinicos(fichaAraruna).Contains("HDA_LINHA_2")
        && Prontuario.CamposDeIdentificacao(fichaAraruna).Contains("CNS") && !Prontuario.CamposDeIdentificacao(fichaAraruna).Contains("NOME")
        && Prontuario.CampoLivre(fichaAlagoa) == "ATENDIMENTO" && pront.FichasPara(araruna).Count == 2 && pront.FichasPara(null).Count == 1,
        "fichas prontas instaladas, ligadas ao local e com as linhas da ficha");
    // Linhas da ficha: o que não cabe desce; Enter quebra a linha no cursor.
    var linhasHda = Prontuario.Grupo("HDA", fichaAraruna);
    var valoresPront = new Dictionary<string, string> { ["HDA"] = string.Join(" ", Enumerable.Repeat("PALAVRA", 20)) };
    Assert(Prontuario.Fluir(valoresPront, linhasHda, 0) && valoresPront["HDA"].Length <= Prontuario.CaracteresPorLinha && valoresPront["HDA_LINHA_2"].Length > 0
        && string.Join(" ", linhasHda.Select(c => valoresPront.GetValueOrDefault(c, "")).Where(t => t.Length > 0)).Split(' ').Length == 20,
        "linha da ficha: o que não cabe desce para a linha de baixo sem cortar palavras");
    valoresPront = new() { ["HDA"] = "PRIMEIRA SEGUNDA", ["HDA_LINHA_2"] = "TERCEIRA" };
    Prontuario.QuebrarLinha(valoresPront, linhasHda, 0, "PRIMEIRA".Length);
    Assert(valoresPront["HDA"] == "PRIMEIRA" && valoresPront["HDA_LINHA_2"] == "SEGUNDA" && valoresPront["HDA_LINHA_3"] == "TERCEIRA",
        "Enter numa linha da ficha: o resto desce e as linhas seguintes descem junto");
    // Modelo de atendimento aplicado na ficha de Araruna e na de texto livre.
    var gonartrose = ModelosDeAtendimento.Buscar("gonartrose", []).First(m => !m.Generico);
    var valoresAraruna = new Dictionary<string, string>();
    Prontuario.Aplicar(valoresAraruna, gonartrose, fichaAraruna, "DIREITO");
    Assert(valoresAraruna["CID"] == gonartrose.Cid && valoresAraruna["QUEIXA_PRINCIPAL"].Length > 0 && valoresAraruna["HDA"].Length > 0
        && !valoresAraruna.Values.Any(v => v.Contains("[LADO]")) && valoresAraruna["PERFIL_CER_II"] == "FÍSICO"
        && Prontuario.Grupo("HDA", fichaAraruna).All(c => valoresAraruna.GetValueOrDefault(c, "").Length <= Prontuario.CaracteresPorLinha),
        "modelo de atendimento preenche Q.P., H.D.A., exame, CID e programação nas linhas da ficha");
    var valoresAlagoa = new Dictionary<string, string>();
    Prontuario.Aplicar(valoresAlagoa, gonartrose, fichaAlagoa, "ESQUERDO");
    Assert(valoresAlagoa["ATENDIMENTO"].StartsWith("Q.P.: ") && valoresAlagoa.Values.Any(v => v.StartsWith("DIAGNÓSTICO: ")) && valoresAlagoa.Values.Any(v => v.Contains("ESQUERD")),
        "ficha de texto livre: cada parte do modelo começa numa linha nova");
    // Gerar o prontuário: sem os ___, com o nome digitado, e só a identificação vai para o cadastro.
    var finais = Prontuario.ValoresFinais(valoresAraruna, fichaAraruna, "PACIENTE TESTE", "", tirarLacunas: true);
    var prontuarioGerado = Path.Combine(raiz, "prontuario-gerado.docx");
    DocxTemplate.Fill(pront.UrlDoModelo(fichaAraruna), prontuarioGerado, finais);
    // (a linha da assinatura, "_____…", é da própria ficha e fica)
    Assert(TextoDo(prontuarioGerado).Contains("PACIENTE TESTE") && !TextoDo(prontuarioGerado).Replace(new string('_', 45), "").Contains("___") && !TextoDo(prontuarioGerado).Contains("{{")
        && TextoDo(prontuarioGerado).Contains(gonartrose.Cid), "prontuário gerado sem ___ e sem campos pendentes");
    var pacienteFicha = new Paciente { Nome = "PACIENTE TESTE" };
    CamposAutomaticos.Guardar(new Dictionary<string, string> { ["CNS"] = "700000000000000", ["BAIRRO"] = "CENTRO", ["HDA"] = "DOR", ["CID"] = "M17.1" }, fichaAraruna.Campos, pacienteFicha);
    Assert(pacienteFicha.Cns == "700000000000000" && pacienteFicha.Bairro == "CENTRO" && !JsonSerializer.Serialize(pacienteFicha).Contains("M17.1"),
        "identificação completada no prontuário vai para o cadastro; anamnese e CID não");
    Assert(CamposAutomaticos.Valor("CIDADE", new Paciente { Cidade = "ARARUNA" }, new LocalAtendimento { Cidade = "Campina Grande" }) == "Campina Grande"
        && CamposAutomaticos.Valor("CIDADE_PACIENTE", new Paciente { Cidade = "ARARUNA" }, null) == "ARARUNA",
        "{{CIDADE}} continua sendo a do local; a do paciente é {{CIDADE_PACIENTE}}");
    // Meus modelos e favoritos de atendimento por local.
    var meu = Prontuario.ComoMeuModelo(new Dictionary<string, string> { ["QUEIXA_PRINCIPAL"] = "DOR NO JOELHO DIREITO", ["CID"] = "M17.1" }, fichaAraruna, "Meu joelho", "DIREITO");
    pront.SalvarModeloDeAtendimento(meu);
    pront.AlternarFavoritoDeAtendimento(meu, araruna);
    pront.AlternarFavoritoDeAtendimento(gonartrose, araruna);
    pront.MoverFavoritoDeAtendimento(gonartrose, meu, araruna);
    Assert(meu.Qp == "DOR NO JOELHO [LADO]" && ModelosDeAtendimento.Buscar("meu joelho", pront.Dados.ModelosDeAtendimento).First().Meu
        && pront.FavoritosDeAtendimento(araruna).Select(m => m.Nome).SequenceEqual([gonartrose.Nome, "Meu joelho"]) && pront.FavoritosDeAtendimento(null).Count == 0,
        "meus modelos (o lado volta a ser [LADO]) e favoritos de atendimento por local");
    pront.ApagarModeloDeAtendimento(meu);
    Assert(pront.FavoritosDeAtendimento(araruna).Count == 1 && File.ReadAllText(pront.ArquivoDados).Contains("favoritosDeAtendimento"),
        "apagar meu modelo tira dos favoritos; favoritos gravados no dados.json");

    // Importar dados: juntar acrescenta sem trocar os objetos abertos nas telas; substituir troca tudo.
    var destinoJuntar = new Armazenamento(Path.Combine(raiz, "juntar"));
    var aqui = destinoJuntar.NovoPaciente("ANA DAQUI");
    var outroComputador = new Armazenamento(Path.Combine(raiz, "outro-computador"));
    var deLa = outroComputador.NovoPaciente("BIA DE LA");
    var localDeLa = outroComputador.NovoLocal(); localDeLa.Nome = "Alagoa Grande"; deLa.LocalId = localDeLa.Id; outroComputador.Salvar();
    outroComputador.Registrar(new DocumentoGerado { Modelo = "Receita", Paciente = deLa.Nome, PacienteId = deLa.Id, Local = "Alagoa Grande", Caminho = "/Users/x/receita.docx" });
    outroComputador.Dados.FavoritosPorLocal[Armazenamento.ChaveDosFavoritos(localDeLa.Id)] = ["Receita"];
    outroComputador.AdicionarAFila(outroComputador.Dados.Historico[0], 1);
    var resumoJuntar = destinoJuntar.ImportarDados(outroComputador.ArquivoDados, Armazenamento.ModoDeImportacao.Juntar, deOutroComputador: true);
    Assert(destinoJuntar.Dados.Pacientes.Contains(aqui) && destinoJuntar.Dados.Pacientes.Any(p => p.Id == deLa.Id && p.LocalId == localDeLa.Id)
        && destinoJuntar.Dados.Historico.Count == 1 && destinoJuntar.Dados.FilaImpressao.Count == 0
        && destinoJuntar.Dados.FavoritosPorLocal[Armazenamento.ChaveDosFavoritos(localDeLa.Id)].SequenceEqual(["Receita"]) && resumoJuntar.Contains("1 paciente"),
        "juntar (Google Drive): entram pacientes, locais, histórico e favoritos; a fila é de cada computador");
    var depois = File.ReadAllText(destinoJuntar.ArquivoDados);
    destinoJuntar.ImportarDados(outroComputador.ArquivoDados, Armazenamento.ModoDeImportacao.Juntar, deOutroComputador: true);
    Assert(destinoJuntar.Dados.Pacientes.Count == 2 && File.ReadAllText(destinoJuntar.ArquivoDados) == depois, "juntar de novo não duplica nada");
    destinoJuntar.ImportarDados(outroComputador.ArquivoDados, Armazenamento.ModoDeImportacao.Juntar);
    Assert(destinoJuntar.Dados.FilaImpressao.Count == 1 && Directory.GetFiles(destinoJuntar.PastaBackups, "antes-de-importar-*.json").Length == 1,
        "juntar um backup à mão traz a fila pendente e guarda os dados de antes em Backups");
    try { destinoJuntar.ImportarDados(Path.Combine(raiz, "nao-existe.json"), Armazenamento.ModoDeImportacao.Substituir); Assert(false, "substituir com arquivo ausente"); }
    catch (FileNotFoundException) { Assert(destinoJuntar.Dados.Pacientes.Count == 2, "substituir com arquivo ausente não altera nada"); }

    // Cadastro ampliado: lê o dados.json antigo (sem as chaves novas) e grava todas as chaves (o Mac exige).
    File.WriteAllText(Path.Combine(raiz, "antigo.json"), """{"pacientes":[{"id":"E621E1F8-C36C-495A-93FC-0C247A3E6E5F","nome":"Antiga","cpf":"","rg":"","dataNascimento":"","telefone":"","email":"","endereco":"","convenio":"","observacoes":""}]}""");
    var dadosAntigos = new Armazenamento(Path.Combine(raiz, "antigo"));
    dadosAntigos.ImportarDados(Path.Combine(raiz, "antigo.json"), Armazenamento.ModoDeImportacao.Substituir);
    var gravado = File.ReadAllText(dadosAntigos.ArquivoDados);
    Assert(dadosAntigos.Dados.Pacientes.Single().Cns == "" && gravado.Contains("\"cns\"") && gravado.Contains("\"bairro\"") && gravado.Contains("\"fichas\"") && gravado.Contains("\"modelosDeAtendimento\""),
        "cadastro ampliado (CNS, bairro…) compatível com o dados.json antigo e com o do Mac");

    // Fila: "Limpar fila" e atendidos recentemente com "Limpar" e "Tirar desta lista".
    outroComputador.LimparFila();
    Assert(outroComputador.Dados.FilaImpressao.Count == 0 && outroComputador.Dados.Historico.Count == 1, "Limpar fila tira todos os itens e mantém o Histórico");
    var hist = outroComputador.Dados.Historico[0];
    Assert(outroComputador.PacientesRecentes(null, null, 12).Count == 1
        && outroComputador.PacientesRecentes(null, null, 12, hist.Data.AddSeconds(1)).Count == 0
        && outroComputador.PacientesRecentes(null, null, 12, null, new Dictionary<Guid, DateTimeOffset> { [deLa.Id] = hist.Data }).Count == 0
        && outroComputador.PacientesRecentes(null, null, 12, null, new Dictionary<Guid, DateTimeOffset> { [deLa.Id] = hist.Data.AddSeconds(-1) }).Count == 1,
        "atendidos recentemente: limpar a lista e tirar um paciente (volta com documento novo)");
    // Google Drive: credencial lida do arquivo client_secret….json.
    File.WriteAllText(Path.Combine(raiz, "client_secret.json"), """{"installed":{"client_id":"id-de-teste.apps.googleusercontent.com","client_secret":"segredo-de-teste"}}""");
    Assert(GoogleDrive.LerCredencial(Path.Combine(raiz, "client_secret.json")) == ("id-de-teste.apps.googleusercontent.com", "segredo-de-teste")
        && !new GoogleDrive(Path.Combine(raiz, "prontuario")).Conectado && !pront.GoogleConectado && !pront.EnvioPendente,
        "Google Drive: credencial do arquivo e nada conectado nem enviado sem autorização");

    // Trocar de subpasta não escolhe sozinho o primeiro modelo (saía "cibex" no lugar do escolhido).
    {
        ModeloDocumento M(string nome) => new() { Nome = nome, Arquivo = nome + ".docx", Campos = [] };
        var cibex = M("cibex"); var pregabalina = M("pregabalina + duloxetina"); var so = M("único");
        Assert(Armazenamento.ModeloAoTrocarSubpasta([cibex, pregabalina], null, "retorno") == null
            && Armazenamento.ModeloAoTrocarSubpasta([cibex, pregabalina], pregabalina.Id, null) == pregabalina.Id
            && Armazenamento.ModeloAoTrocarSubpasta([cibex, pregabalina], Guid.NewGuid(), "pregabalina + duloxetina") == pregabalina.Id
            && Armazenamento.ModeloAoTrocarSubpasta([so], null, null) == so.Id,
            "trocar subpasta: com vários modelos fica \"Escolha…\"; mantém o mesmo nome; modelo único já vem escolhido");
    }

    Console.WriteLine(falhas == 0 ? "Todos os testes passaram." : $"{falhas} teste(s) falharam.");
}
finally { try { Directory.Delete(raiz, recursive: true); } catch { } }
return falhas == 0 ? 0 : 1;

sealed class ImpressoraSimulada(params DesfechoImpressao[] respostas) : IImpressoraWord
{
    private int _vez;
    public int Enviados { get; private set; }
    public Task<ResultadoImpressao> ImprimirAsync(string arquivo, string? impressora, int copias, bool manterAberto, CancellationToken ct = default)
    {
        Enviados++;
        var desfecho = respostas[Math.Min(_vez++, respostas.Length - 1)];
        return Task.FromResult(new ResultadoImpressao(desfecho, desfecho.ToString()));
    }
}
