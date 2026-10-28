using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orthck.Core;

// Mesmos dados e nomes do Orthck para Mac (Modelos.swift): o dados.json é lido e gravado nos dois.
// O decodificador do Mac exige todas as chaves não opcionais; por isso todas são sempre gravadas.
// Chaves desconhecidas (de versões mais novas do Mac) são preservadas em "Outros" e gravadas de volta.

/// <summary>Um modelo .docx importado; o arquivo fica copiado na pasta Modelos do app.</summary>
public sealed class ModeloDocumento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    /// <summary>Nome do arquivo dentro da pasta Modelos (ex.: "3F2A….docx").</summary>
    public string Arquivo { get; set; } = "";
    public List<string> Campos { get; set; } = [];
    /// <summary>Tipo (subpasta) onde os documentos gerados são guardados: Receitas, Laudos, "RX / Joelho"…</summary>
    public string Categoria { get; set; } = "Outros";
    /// <summary>Local dono do modelo; null = disponível em todos os locais.</summary>
    [JsonPropertyName("localID")] public Guid? LocalId { get; set; }
    /// <summary>Cópias impressas quando o documento é impresso pelo app.</summary>
    public int Copias { get; set; } = 1;
    /// <summary>Caminho dentro da pasta sincronizada, com "/" (ex.: "OPERA PB/RX/rx joelho.docx"); null = importado avulso.</summary>
    public string? Origem { get; set; }
    /// <summary>Data de modificação do arquivo de origem na última cópia, para saber quando ele mudou.</summary>
    public DateTimeOffset? OrigemModificadaEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.Now;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}

public sealed class Paciente
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
    /// <summary>Local onde o paciente é atendido (opcional).</summary>
    [JsonPropertyName("localID")] public Guid? LocalId { get; set; }
    // Campos próprios dos modelos (CID, dias, turno…) não ficam no cadastro: valem só para o documento.
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}

public sealed class LocalAtendimento
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Cnpj { get; set; } = "";
    /// <summary>Pasta onde os documentos deste local são salvos; vazia = pasta principal.</summary>
    public string Pasta { get; set; } = "";
    /// <summary>Subpasta da pasta sincronizada que pertence a este local (ex.: "OPERA PB").</summary>
    public string? PastaSincronizada { get; set; }
    /// <summary>Tipos de documento (subpastas) deste local.</summary>
    public List<string> Tipos { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}

public sealed class DocumentoGerado
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Modelo { get; set; } = "";
    /// <summary>Para reaproveitar os ajustes de impressão do modelo no Histórico.</summary>
    [JsonPropertyName("modeloID")] public Guid? ModeloId { get; set; }
    public string Paciente { get; set; } = "";
    /// <summary>Cadastro do paciente (o nome sozinho pode se repetir).</summary>
    [JsonPropertyName("pacienteID")] public Guid? PacienteId { get; set; }
    public string? Local { get; set; }
    public string Caminho { get; set; } = "";
    public DateTimeOffset Data { get; set; } = DateTimeOffset.Now;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}

/// <summary>Situação na fila: "enviado" indica que o Word aceitou o comando, não que o papel saiu.</summary>
public static class SituacaoImpressao
{
    public const string Pendente = "pendente";
    public const string Enviando = "enviando";
    public const string Enviado = "enviado";
    public const string Erro = "erro";
    public const string Incerto = "incerto";
}

/// <summary>Metadados da impressão; o arquivo .docx continua na pasta onde foi salvo.</summary>
public sealed class ItemFilaImpressao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("documentoID")] public Guid DocumentoId { get; set; }
    public string Caminho { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Paciente { get; set; } = "";
    public string? Local { get; set; }
    public int Copias { get; set; } = 1;
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.Now;
    public string Situacao { get; set; } = SituacaoImpressao.Pendente;
    public string? Observacao { get; set; }

    public static ItemFilaImpressao De(DocumentoGerado documento, int copias) => new()
    {
        DocumentoId = documento.Id, Caminho = documento.Caminho, Modelo = documento.Modelo,
        Paciente = documento.Paciente, Local = documento.Local, Copias = Math.Clamp(copias, 1, 20)
    };
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}

public sealed class DadosApp
{
    public static readonly string[] CategoriasPadrao = ["Receitas", "Atestados", "Laudos", "Relatórios médicos", "Radiografias", "Outros"];
    /// <summary>A pasta APAC já começa como "Todos os locais".</summary>
    public static readonly string[] PastasCompartilhadasPadrao = ["1 - APAC"];

    public List<ModeloDocumento> Modelos { get; set; } = [];
    public List<Paciente> Pacientes { get; set; } = [];
    public List<LocalAtendimento> Locais { get; set; } = [];
    /// <summary>Tipos dos modelos de "Todos os locais" e lista inicial de cada local novo.</summary>
    public List<string> Categorias { get; set; } = [.. CategoriasPadrao];
    public List<DocumentoGerado> Historico { get; set; } = [];
    public List<ItemFilaImpressao> FilaImpressao { get; set; } = [];
    /// <summary>Pasta com os modelos (uma subpasta por local, uma por tipo) acompanhada pelo app.</summary>
    public string PastaSincronizada { get; set; } = "";
    /// <summary>Subpastas da pasta sincronizada que não são locais.</summary>
    public List<string> PastasIgnoradas { get; set; } = [];
    /// <summary>Subpastas da pasta sincronizada que valem para todos os locais (ex.: "1 - APAC").</summary>
    public List<string> PastasCompartilhadas { get; set; } = [.. PastasCompartilhadasPadrao];
    /// <summary>Pasta principal dos documentos gerados (vazia = Documentos\Orthck).</summary>
    public string PastaDocumentos { get; set; } = "";

    /// <summary>Sem diferenciar maiúsculas e acentos ("1 - Apac" = "1 - APAC").</summary>
    public bool EhCompartilhada(string pasta) => PastasCompartilhadas.Any(x => Texto.Iguais(x, pasta));
    [JsonExtensionData] public Dictionary<string, JsonElement>? Outros { get; set; }
}
