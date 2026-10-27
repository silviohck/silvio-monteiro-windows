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
    Console.WriteLine("Todos os testes passaram.");
}
finally { try { Directory.Delete(root, recursive: true); } catch { } }

sealed class TestPrinter : IWordPrinter
{
    public int Sent { get; private set; }
    public Task<PrintResult> SubmitAsync(string path, int copies, string? printer, CancellationToken token = default)
    { Sent++; return Task.FromResult(new PrintResult(PrintOutcome.Submitted, "Enviado ao Word")); }
}
