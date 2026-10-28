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

    // Homônimos e locais do paciente
    var p1 = arm.NovoPaciente("José Silva"); var p2 = arm.NovoPaciente("JOSÉ SILVA"); p2.DataNascimento = "01/01/1980";
    Assert(arm.PacientesChamados("jose silva").Count == 2 && Armazenamento.Identificacao(p2) == "nasc. 01/01/1980", "homônimos sem diferenciar acentos e identificação");
    arm.Registrar(new DocumentoGerado { Modelo = "x", Paciente = p1.Nome, PacienteId = p1.Id, Local = "CAMPINA GRANDE" });
    Assert(arm.NomesDosLocais(p1) == "Campina Grande", "locais onde o paciente já foi atendido");

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
    Assert(Texto.NomeSeguro("CON") == "_CON" && Texto.NomeSeguro("a/b:c") == "a-b-c", "nomes reservados e inválidos do Windows");

    // Excluir local transferindo vínculos
    var outro = arm.NovoLocal(); outro.Nome = "Araruna";
    p1.LocalId = local.Id; arm.Salvar();
    arm.ExcluirLocal(local.Id, outro.Id);
    Assert(importado.LocalId == outro.Id && p1.LocalId == outro.Id && arm.Local(local.Id) == null, "excluir local transfere modelos e pacientes");

    // Persistência
    var reaberto = new Armazenamento(pastaDados);
    Assert(reaberto.Dados.Pacientes.Count == 2 && reaberto.Dados.Modelos.Single().Categoria == "Outros", "dados persistem ao reabrir");
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
    itens[2].Situacao = SituacaoImpressao.Enviando; arm.Salvar();
    Assert(new Armazenamento(pastaDados).Dados.FilaImpressao.Single(x => x.Id == itens[2].Id).Situacao == SituacaoImpressao.Incerto
        && File.ReadAllText(Path.Combine(pastaDados, "dados.json")).Contains("\"incerto\""), "app fechado durante envio: fica incerto e gravado");

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

    // ===== Subpastas em níveis =====
    var arvore = NoSubpasta.Arvore(["RX / Joelho", "1 - APAC / Fisioterapia / coluna", "RX", "1 - APAC / Fisioterapia", "10 - Laudos", "2 - Receitas"]);
    Assert(string.Join(",", arvore.Select(n => n.Nome)) == "1 - APAC,2 - Receitas,10 - Laudos,RX"
        && arvore[3].TemModelos && arvore[3].Filhos.Single().Caminho == "RX / Joelho"
        && !arvore[0].TemModelos && arvore[0].Filhos[0].TemModelos && arvore[0].Filhos[0].Filhos[0].Caminho == "1 - APAC / Fisioterapia / coluna",
        "árvore de subpastas em ordem do Explorador, com pastas que têm modelos próprios");

    // ===== Tabelas =====
    Assert(TabelaDeCodigos.Cid10.Codigos.Count == 14233 && TabelaDeCodigos.Sigtap.Codigos.Count(c => !c.EhTitulo) == 5039 && TabelaDeCodigos.Tuss.Codigos.Count == 5966,
        "tabelas CID-10, SIGTAP e TUSS embutidas completas");
    Assert(TabelaDeCodigos.Cid10.Buscar("m545", null).First().Codigo == "M54.5" && TabelaDeCodigos.Cid10.Buscar("dor lombar", null).Any(c => c.Codigo == "M54.5"),
        "CID: busca por código sem pontuação e por palavras");
    Assert(TabelaDeCodigos.Cid10.Buscar("colera", null).First().Descricao == "Cólera" && TabelaDeCodigos.Cid10.Grupos.Count == 22, "CID: sem acentos e 22 capítulos");
    Assert(TabelaDeCodigos.Sigtap.Buscar("0301010072", null).First().Codigo == "03.01.01.007-2" && TabelaDeCodigos.FormatarSigtap("0301") == "03.01", "SIGTAP: formato do código");

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
