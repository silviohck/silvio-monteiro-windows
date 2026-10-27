using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orthck.Core;

/// <summary>Compatibilidade com dados.json do macOS, sem migração destrutiva.</summary>
public sealed class Storage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new Iso8601DateConverter() }
    };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "dados.json");
    public AppData Data { get; private set; }

    public Storage(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orthck");
        Directory.CreateDirectory(DirectoryPath);
        Data = Load(FilePath);
        var interrupted = Data.FilaImpressao.Any(x => x.Situacao == "enviando");
        MarkInterruptedPrints(Data);
        if (interrupted) Save(); // Estado "incerto" precisa sobreviver à próxima inicialização.
    }

    /// <summary>Em caso de interrupção entre disparo e confirmação não reenviar automaticamente.</summary>
    private static void MarkInterruptedPrints(AppData data)
    {
        foreach (var item in data.FilaImpressao.Where(x => x.Situacao == "enviando"))
        {
            item.Situacao = "incerto";
            item.Observacao = "Aplicativo interrompido durante envio: confira a impressora antes de reenviar.";
        }
    }

    public static AppData Load(string path)
    {
        if (!File.Exists(path)) return new AppData();
        try
        {
            var result = JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), Options)
                ?? throw new InvalidDataException("O cadastro está vazio.");
            return result;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new InvalidDataException($"Não foi possível ler {path}. O arquivo original NÃO foi alterado. {ex.Message}", ex);
        }
    }

    public void ImportMacData(string source)
    {
        // A importação preserva integralmente a fonte e o estado anterior do Windows.
        var incoming = Load(source);
        MarkInterruptedPrints(incoming);
        // Caminhos do Mac (/Users/…) no Windows apontariam para pastas inexistentes na unidade atual.
        if (incoming.PastaSincronizada.StartsWith('/')) incoming.PastaSincronizada = "";
        if (incoming.PastaDocumentos.StartsWith('/')) incoming.PastaDocumentos = "";
        SaveSnapshot(Data, backupName: "antes-importacao");
        // Primeiro confirma a gravação do novo arquivo. Se houver erro, a sessão antiga permanece.
        SaveSnapshot(incoming);
        Data = incoming;
    }

    public void Save() => SaveSnapshot(Data);

    private void SaveSnapshot(AppData snapshot, string? backupName = null)
    {
        Directory.CreateDirectory(DirectoryPath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
        var tmp = Path.Combine(DirectoryPath, $"dados-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tmp, bytes);
            // O sistema nunca remove os dados válidos antes de criar e gravar o novo arquivo.
            var current = FilePath;
            if (backupName != null)
            {
                var backupPath = Path.Combine(DirectoryPath, $"{backupName}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(backupPath, JsonSerializer.Serialize(snapshot, Options));
            }
            if (File.Exists(current))
            {
                var backup = Path.Combine(DirectoryPath, "dados.backup.json");
                File.Replace(tmp, current, backup, ignoreMetadataErrors: true);
            }
            else File.Move(tmp, current);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}

/// <summary>
/// Mesmo formato do Mac (JSONEncoder .iso8601): UTC, sem frações de segundo. O decodificador do Mac
/// rejeita frações; na leitura, a hora é convertida para o fuso local para exibição correta.
/// </summary>
internal sealed class Iso8601DateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToLocalTime();
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
