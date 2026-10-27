using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orthck.Core;

public sealed class AppData
{
    public List<TemplateRecord> Modelos { get; set; } = [];
    public List<Patient> Pacientes { get; set; } = [];
    public List<ClinicLocation> Locais { get; set; } = [];
    public List<string> Categorias { get; set; } = ["Receitas", "Atestados", "Laudos", "Relatórios médicos", "Radiografias", "Outros"];
    public List<GeneratedDocument> Historico { get; set; } = [];
    public List<PrintItem> FilaImpressao { get; set; } = [];
    public string PastaSincronizada { get; set; } = "";
    public List<string> PastasIgnoradas { get; set; } = [];
    public string PastaDocumentos { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}
public sealed class TemplateRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public string Arquivo { get; set; } = "";
    public List<string> Campos { get; set; } = [];
    public string Categoria { get; set; } = "Outros";
    public Guid? LocalID { get; set; }
    public int Copias { get; set; } = 1;
    public string? Origem { get; set; }
    public DateTimeOffset? OrigemModificadaEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.Now;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}
public sealed class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string Rg { get; set; } = "";
    public string DataNascimento { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Convenio { get; set; } = "";
    public string Observacoes { get; set; } = "";
    public Guid? LocalID { get; set; }
    public Dictionary<string, string> OutrosCampos { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}
public sealed class ClinicLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Cnpj { get; set; } = "";
    public string Pasta { get; set; } = "";
    public string? PastaSincronizada { get; set; }
    public List<string> Tipos { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}
public sealed class GeneratedDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Modelo { get; set; } = "";
    public Guid? ModeloID { get; set; }
    public string Paciente { get; set; } = "";
    public Guid? PacienteID { get; set; }
    public string? Local { get; set; }
    public string Caminho { get; set; } = "";
    public DateTimeOffset Data { get; set; } = DateTimeOffset.Now;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}
public enum PrintStatus { Pendente, Enviando, Enviado, Erro, Incerto }
public sealed class PrintItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentoID { get; set; }
    public string Caminho { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Paciente { get; set; } = "";
    public string? Local { get; set; }
    public int Copias { get; set; } = 1;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.Now;
    // Compatível com os valores em português usados pelo Orthck macOS.
    public string Situacao { get; set; } = "pendente";
    public string? Observacao { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
    public static PrintItem From(GeneratedDocument d, int copies) => new()
    {
        DocumentoID = d.Id, Caminho = d.Caminho, Modelo = d.Modelo,
        Paciente = d.Paciente, Local = d.Local, Copias = Math.Clamp(copies, 1, 20)
    };
}
