using System.Text.Json;

namespace Orthck.Windows;

/// <summary>
/// Preferências deste computador (o @AppStorage do Mac): aparência, impressora e o que estava escolhido nas telas.
/// Ficam em %LOCALAPPDATA%\Orthck\ajustes.json, separadas do dados.json.
/// </summary>
public sealed class Ajustes
{
    public TamanhoTexto TamanhoTexto { get; set; } = TamanhoTexto.Normal;
    public Aparencia Aparencia { get; set; } = Aparencia.Automatica;
    /// <summary>Impressora usada pelo app; vazia = padrão do Windows.</summary>
    public string Impressora { get; set; } = "";
    /// <summary>"fila", "imprimir" ou "salvar".</summary>
    public string AcaoDepoisDeGerar { get; set; } = "fila";
    public bool AbrirAoGerar { get; set; }
    public string LocalSelecionado { get; set; } = "";
    public string FiltroLocalModelos { get; set; } = "";
    public string FiltroTipoModelos { get; set; } = "";
    public string FiltroLocalPacientes { get; set; } = "";
    public Dictionary<string, int> GrupoDasTabelas { get; set; } = [];

    private static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orthck", "ajustes.json");
    public static Ajustes Atual { get; private set; } = new();

    public static void Carregar(string? pasta = null)
    {
        try
        {
            var arquivo = pasta == null ? Arquivo : Path.Combine(pasta, "ajustes.json");
            if (File.Exists(arquivo)) Atual = JsonSerializer.Deserialize<Ajustes>(File.ReadAllText(arquivo)) ?? new();
        }
        catch { Atual = new(); }
        _pasta = pasta;
    }

    private static string? _pasta;

    public static void Salvar()
    {
        try
        {
            var arquivo = _pasta == null ? Arquivo : Path.Combine(_pasta, "ajustes.json");
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
            File.WriteAllText(arquivo, JsonSerializer.Serialize(Atual, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Preferências não são críticas. */ }
    }
}
