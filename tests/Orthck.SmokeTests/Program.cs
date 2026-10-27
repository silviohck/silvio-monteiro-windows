using Orthck.Core;
using System.IO.Compression;
using System.Xml.Linq;

static void Assert(bool ok, string message)
{
    if (!ok) throw new Exception("Falhou: " + message);
    Console.WriteLine("OK: " + message);
}
var root = Path.Combine(Path.GetTempPath(), "orthck-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var template = Path.Combine(root, "modelo.docx");
    using (var zip = ZipFile.Open(template, ZipArchiveMode.Create))
    {
        var types = zip.CreateEntry("[Content_Types].xml");
        using (var w = new StreamWriter(types.Open())) w.Write("<Types/>");
        var body = zip.CreateEntry("word/document.xml");
        using var writer = new StreamWriter(body.Open());
        writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Atesto: {{NOME_</w:t></w:r><w:r><w:t>PACIENTE}} CID {{CID}}</w:t></w:r></w:p></w:body></w:document>");
    }
    Assert(DocxService.Fields(template).SequenceEqual(["NOME_PACIENTE", "CID"]), "localiza campos divididos entre runs");
    var fileA = Path.Combine(root, "a.docx");
    var fileB = Path.Combine(root, "b.docx");
    DocxService.Fill(template, fileA, new Dictionary<string, string> { ["NOME_PACIENTE"] = "Paciente A", ["CID"] = "M17.1" });
    DocxService.Fill(template, fileB, new Dictionary<string, string> { ["NOME_PACIENTE"] = "Paciente B", ["CID"] = "S83.5" });
    Assert(DocxService.Fields(fileA).Count == 0, "nenhum campo pendente após preenchimento");
    using (var a = ZipFile.OpenRead(fileA))
    using (var b = ZipFile.OpenRead(fileB))
    {
        string Read(ZipArchive z) { using var sr = new StreamReader(z.GetEntry("word/document.xml")!.Open()); return sr.ReadToEnd(); }
        var aa = Read(a); var bb = Read(b);
        Assert(aa.Contains("Paciente A") && !aa.Contains("Paciente B") && bb.Contains("Paciente B") && !bb.Contains("Paciente A"), "documentos consecutivos não misturam pacientes");
        Assert(bb.Contains("S83.5") && !bb.Contains("M17.1"), "CID do atendimento anterior não é reaproveitado");
    }
    var storage = new Storage(Path.Combine(root, "dados"));
    var clinic = new ClinicLocation { Nome = "Clínica", Cidade = "Campina Grande" };
    storage.Data.Locais.Add(clinic);
    storage.Data.Pacientes.Add(new Patient { Nome = "A", Cpf = "000" });
    storage.Save();
    var reopened = new Storage(storage.DirectoryPath);
    Assert(reopened.Data.Pacientes.Single().Cpf == "000", "cadastros persistem após reabrir");
    var imported = Storage.Load(storage.FilePath);
    Assert(imported.Locais[0].Nome == "Clínica", "dados.json mantém campos do Mac");
    var fields = ClinicalFields.Defaults(["NOME_PACIENTE", "CID", "DATA", "CIDADE"], reopened.Data.Pacientes[0], clinic, DateTimeOffset.Now);
    Assert(fields["CID"] == "" && fields["CIDADE"] == "Campina Grande", "dados clínicos começam vazios");
    var age = ClinicalFields.Defaults(["IDADE"], new Patient { DataNascimento = " 15/03/1990 " }, null, new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.Zero));
    Assert(age["IDADE"] == "35", "idade ignora espaços e conta aniversário ainda não feito");
    var doc = new GeneratedDocument { Caminho = fileA, Modelo = "Laudo", Paciente = "A" };
    var mock = new TestPrinter();
    var queue = new PrintQueue(reopened, mock);
    var q = queue.Add(doc, 2);
    Assert(queue.Add(doc, 2).Id == q.Id, "não duplica documento na fila");
    await queue.SubmitAsync([q.Id], null);
    Assert(q.Situacao == "enviado" && mock.Sent == 1, "imprime uma vez e registra envio");
    await queue.SubmitAsync([q.Id], null);
    Assert(mock.Sent == 1, "não imprime novamente de forma automática");
    q.Situacao = "enviando"; reopened.Save();
    Assert(new Storage(reopened.DirectoryPath).Data.FilaImpressao.Single().Situacao == "incerto", "envio interrompido exige conferência");
    Assert(PathRules.SafeComponent("Relatório: Joelho/Esquerdo") == "Relatório- Joelho-Esquerdo", "protege nomes de arquivos");
    Assert(PathRules.SafeComponent("CON") == "_CON" && PathRules.SafeComponent("com1.txt") == "_com1.txt", "evita nomes reservados do Windows");

    // Datas no mesmo formato do Mac (ISO 8601 sem frações), lidas no fuso local.
    var json = File.ReadAllText(reopened.FilePath);
    Assert(System.Text.RegularExpressions.Regex.IsMatch(json, "\"criadoEm\": \"\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\dZ\""), "datas gravadas como o Mac lê");
    var macFile = Path.Combine(root, "mac.json");
    File.WriteAllText(macFile, """
        {"pacientes":[{"id":"E621E1F8-C36C-495A-93FC-0C247A3E6E5F","nome":"Maria"}],
         "historico":[{"id":"9C68F6E3-E189-4BCA-98F9-15E71C8534E5","modelo":"Laudo","paciente":"Maria","caminho":"/Users/x/a.docx","data":"2026-10-27T13:00:00Z"}],
         "filaImpressao":[{"id":"0FC3EB25-CA47-4D35-B100-035AF4739E3E","documentoID":"9C68F6E3-E189-4BCA-98F9-15E71C8534E5","caminho":"/Users/x/a.docx","modelo":"Laudo","paciente":"Maria","copias":1,"criadoEm":"2026-10-27T13:00:00Z","situacao":"enviando"}],
         "pastaSincronizada":"/Users/x/Modelos","pastasCompartilhadas":["1 - APAC"],"campoFuturo":42}
        """);
    var macStore = new Storage(Path.Combine(root, "importado"));
    macStore.ImportMacData(macFile);
    Assert(macStore.Data.Historico[0].Data == new DateTimeOffset(2026, 10, 27, 13, 0, 0, TimeSpan.Zero), "lê datas do Mac");
    Assert(macStore.Data.Historico[0].Data.Offset == TimeZoneInfo.Local.GetUtcOffset(macStore.Data.Historico[0].Data), "data exibida no fuso local");
    Assert(macStore.Data.FilaImpressao[0].Situacao == "incerto", "importação não deixa item preso em 'enviando'");
    Assert(macStore.Data.PastaSincronizada == "", "caminho do Mac não é usado como pasta do Windows");
    Assert(File.ReadAllText(macStore.FilePath).Contains("campoFuturo"), "preserva campos desconhecidos do Mac");

    // Reimpressão explícita de um documento já enviado.
    q.Situacao = "enviado";
    var again = queue.Add(doc, 1);
    Assert(again.Id != q.Id && again.Situacao == "pendente", "documento enviado pode voltar à fila para reimpressão");
    Assert(queue.Add(doc, 1).Id == again.Id, "reimpressão também não duplica");

    // Pastas compartilhadas e ignoradas, como no Mac.
    var models = Path.Combine(root, "modelos");
    foreach (var rel in new[] { "Clínica A/Atestados/Atestado.docx", "1 - APAC/Joelho/APAC joelho.docx", "1 - APAC/Geral.docx", "Arquivo/Velhos/Antigo.docx" })
    {
        var full = Path.Combine(models, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.Copy(template, full);
    }
    var scanned = TemplateCatalog.Scan(models, ["Arquivo"], ["1 - Apac"]);
    Assert(scanned.Count == 3 && !scanned.Any(x => x.DisplayName == "Antigo"), "ignora pastas que não são locais");
    var apac = scanned.Where(x => x.Clinic == "").ToArray();
    Assert(apac.Length == 2 && apac.All(x => x.Category == "1 - APAC" && x.AppliesTo("Clínica A") && x.AppliesTo("Outra")), "pasta compartilhada vale para todos os locais");
    Assert(apac.Single(x => x.DisplayName == "APAC joelho").Subpath == "Joelho", "subpastas da pasta compartilhada");
    Assert(scanned.Single(x => x.DisplayName == "Atestado").AppliesTo("CLINICA A") && !scanned.Single(x => x.DisplayName == "Atestado").AppliesTo("Outra"), "modelo do local só aparece nele");

    // Quebra de linha em campo vira w:br.
    var multi = Path.Combine(root, "multi.docx");
    DocxService.Fill(template, multi, new Dictionary<string, string> { ["NOME_PACIENTE"] = "A", ["CID"] = "linha 1\nlinha 2" });
    using (var m = ZipFile.OpenRead(multi))
    {
        var x = XDocument.Load(m.GetEntry("word/document.xml")!.Open());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        Assert(x.Descendants(w + "br").Count() == 1 && x.Descendants(w + "t").Any(t => t.Value == "linha 2"), "quebra de linha vira w:br");
    }

    // Texto colado do Word/Excel: quebra manual (0x0B), tabulação e caracteres de controle.
    var pasted = Path.Combine(root, "colado.docx");
    DocxService.Fill(template, pasted, new Dictionary<string, string> { ["NOME_PACIENTE"] = "A\u0001", ["CID"] = "M17.1\vS83\tok 👍" });
    using (var m = ZipFile.OpenRead(pasted))
    {
        var x = XDocument.Load(m.GetEntry("word/document.xml")!.Open());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var text = string.Concat(x.Descendants(w + "t").Select(t => t.Value));
        Assert(x.Descendants(w + "br").Count() == 1 && x.Descendants(w + "tab").Count() == 1 && text.Contains("ok 👍") && !text.Contains('\u0001'),
            "texto colado com caracteres de controle não impede gerar");
    }
    Console.WriteLine("Todos os testes passaram.");
}
finally { try { Directory.Delete(root, recursive: true); } catch { } }

sealed class TestPrinter : IWordPrinter
{
    public int Sent { get; private set; }
    public Task<PrintResult> SubmitAsync(string path, int copies, string? printer, CancellationToken token = default)
    { Sent++; return Task.FromResult(new PrintResult(PrintOutcome.Submitted, "Enviado ao Word")); }
}
