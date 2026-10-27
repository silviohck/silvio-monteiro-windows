using System.Text.Json;

namespace Orthck.Core;

/// <summary>Compatibilidade com dados.json do macOS, sem migração destrutiva.</summary>
public sealed class Storage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "dados.json");
    public AppData Data { get; private set; }

    public Storage(string? directoryPath = null)
    {
        DirectoryPath = directoryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orthck");
        Directory.CreateDirectory(DirectoryPath);
        Data = Load(FilePath);
        // Em caso de interrupção entre disparo e confirmação não reenviar automaticamente.
        var interrupted = Data.FilaImpressao.Where(x => x.Situacao == "enviando").ToArray();
        foreach (var item in interrupted)
        {
            item.Situacao = "incerto";
            item.Observacao = "Aplicativo interrompido durante envio: confira a impressora antes de reenviar.";
        }
        // A recuperação deve sobreviver a outra reinicialização, sem reenviar automaticamente.
        if (interrupted.Length != 0) Save();
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
        var anterior = Data;
        SaveSnapshot(anterior, backupName: "antes-importacao");
        // Só altera a sessão depois de confirmar a gravação da nova versão.
        // Uma falha de disco não pode deixar a interface com dados diferentes do arquivo.
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
