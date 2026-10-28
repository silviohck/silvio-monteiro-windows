namespace Orthck.Windows;

/// <summary>
/// Registro de erros em %LOCALAPPDATA%\Orthck\erros.log, para diagnóstico (fica só neste computador). Guarda o tipo, a
/// mensagem e onde aconteceu; o arquivo é renovado ao passar de 1 MB.
/// </summary>
public static class Registro
{
    private static readonly object Trava = new();
    public static string Arquivo => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orthck", "erros.log");

    public static void Erro(string contexto, Exception ex)
    {
        try
        {
            lock (Trava)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
                if (File.Exists(Arquivo) && new FileInfo(Arquivo).Length > 1_000_000) File.Move(Arquivo, Arquivo + ".anterior", overwrite: true);
                File.AppendAllText(Arquivo, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {contexto}: {ex}\n\n");
            }
        }
        catch { /* O registro nunca pode derrubar o app. */ }
    }
}
