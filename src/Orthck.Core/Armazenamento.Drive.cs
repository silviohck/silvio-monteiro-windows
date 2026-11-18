using System.Security.Cryptography;
using System.Text.Json;

namespace Orthck.Core;

/// <summary>
/// Cópia dos dados no Google Drive (como no Mac): o app envia "Orthck - dados.json" ao Drive (até 30 segundos depois de
/// cada mudança e ao fechar) e, a cada 30 segundos e ao voltar para o app, junta o que mudou nele em outro computador
/// (pacientes, locais, histórico e favoritos; a fila de impressão é de cada computador). Apagar não se propaga.
/// Só funciona depois de "Conectar ao Google Drive…"; sem isso, nada sai do computador.
/// </summary>
public sealed partial class Armazenamento
{
    public const string NomeDoArquivoNuvem = "Orthck - dados.json";
    public const string NomeDoZipNoDrive = "Orthck - modelos (backup).zip";
    /// <summary>Ao voltar para o app, o Drive é consultado no máximo uma vez a cada 30 segundos.</summary>
    private static readonly TimeSpan IntervaloMinimoAoVoltar = TimeSpan.FromSeconds(30);

    private GoogleDrive? _drive;
    /// <summary>Cliente do Google Drive (criado na primeira vez; a credencial embutida vem do app Windows).</summary>
    public GoogleDrive Drive => _drive ??= new GoogleDrive(Pasta, CredencialGoogleEmbutida);
    /// <summary>Credencial colocada no app pela compilação do GitHub (Secrets do repositório), se houver.</summary>
    public (string ClientID, string ClientSecret)? CredencialGoogleEmbutida { get; set; }

    public bool GoogleConectado => Drive.Conectado;
    /// <summary>Situação da cópia no Google Drive, mostrada na barra lateral.</summary>
    public string SituacaoNuvem { get; private set; } = "";
    public bool SincronizandoDrive { get; private set; }
    /// <summary>Há mudanças esperando o próximo envio ao Drive.</summary>
    public bool EnvioPendente { get; private set; }
    public bool EnviandoPastaAoDrive { get; private set; }
    private bool _outraSincronizacaoPedida;
    private DateTime _ultimaConsultaAoVoltar = DateTime.MinValue;

    /// <summary>A situação do Google Drive mudou (para a barra lateral).</summary>
    public event Action? DriveMudou;

    /// <summary>Arquivo no Drive e a versão dele já lida ou enviada por este computador (ficam só aqui).</summary>
    private sealed class EstadoDoDrive
    {
        public string? ArquivoId { get; set; }
        public string VersaoConhecida { get; set; } = "";
        public string UltimoEnviado { get; set; } = "";
    }

    private string ArquivoEstadoDoDrive => Path.Combine(Pasta, "google-estado.json");
    private EstadoDoDrive? _estadoDoDrive;

    private EstadoDoDrive EstadoDrive
    {
        get
        {
            if (_estadoDoDrive != null) return _estadoDoDrive;
            try { _estadoDoDrive = JsonSerializer.Deserialize<EstadoDoDrive>(File.ReadAllText(ArquivoEstadoDoDrive)); } catch { }
            return _estadoDoDrive ??= new EstadoDoDrive();
        }
    }

    private void GravarEstadoDoDrive()
    {
        try { File.WriteAllText(ArquivoEstadoDoDrive, JsonSerializer.Serialize(EstadoDrive)); } catch { }
    }

    public string? EnderecoNoDrive => EstadoDrive.ArquivoId is { } id ? $"https://drive.google.com/file/d/{id}/view" : null;

    /// <summary>Chamado depois de cada gravação: a mudança vai para o Drive no próximo envio (em até 30 segundos).</summary>
    private void MarcarEnvioAoDrive()
    {
        if (_drive == null && !File.Exists(Path.Combine(Pasta, "google.json"))) return;
        if (!Drive.Conectado || EnvioPendente) return;
        EnvioPendente = true;
        DriveMudou?.Invoke();
    }

    /// <summary>
    /// A cada 30 segundos, ao abrir, ao voltar para o app e antes de fechar: se a cópia no Drive mudou em outro
    /// computador, junta com os dados daqui; depois envia o resultado, se mudou. Erros ficam na situação (não abrem janela).
    /// </summary>
    public async Task SincronizarDriveAsync()
    {
        if (!Drive.Conectado) return;
        if (SincronizandoDrive) { _outraSincronizacaoPedida = true; return; }
        SincronizandoDrive = true;
        EnvioPendente = false;
        DriveMudou?.Invoke();
        try
        {
            var estado = EstadoDrive;
            ArquivoNoDrive? remoto = null;
            if (estado.ArquivoId is { } id) remoto = await Drive.MetadadosAsync(id);
            remoto ??= await Drive.ProcurarArquivoAsync(NomeDoArquivoNuvem);
            string? resumo = null;
            if (remoto != null && remoto.Modificado != estado.VersaoConhecida)
            {
                var conteudo = await Drive.BaixarAsync(remoto.Id);
                var temporario = Path.Combine(Path.GetTempPath(), $"orthck-drive-{Guid.NewGuid():N}.json");
                try
                {
                    await File.WriteAllBytesAsync(temporario, conteudo);
                    resumo = ImportarDados(temporario, ModoDeImportacao.Juntar, deOutroComputador: true);
                }
                finally { try { File.Delete(temporario); } catch { } }
                estado.VersaoConhecida = remoto.Modificado;
            }
            var local = JsonSerializer.SerializeToUtf8Bytes(Dados, Json);
            var marca = Convert.ToHexString(SHA256.HashData(local));
            if (remoto == null || marca != estado.UltimoEnviado || remoto.Id != estado.ArquivoId)
            {
                var enviado = await Drive.EnviarAsync(local, NomeDoArquivoNuvem, remoto?.Id);
                estado.ArquivoId = enviado.Id;
                estado.VersaoConhecida = enviado.Modificado;
                estado.UltimoEnviado = marca;
            }
            GravarEstadoDoDrive();
            var hora = DateTime.Now.ToString("HH:mm");
            SituacaoNuvem = resumo != null && !resumo.Contains(": 0 pacientes, 0 locais e 0 documentos")
                ? $"Atualizado às {hora} · {resumo}" : $"Atualizado às {hora}";
        }
        catch (Exception ex) when (ex is ErroGoogle or HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SituacaoNuvem = ex is HttpRequestException or TaskCanceledException ? "Sem conexão com o Google Drive; tenta de novo em 30 segundos." : ex.Message;
            // Não foi: tenta de novo no próximo intervalo.
            EnvioPendente = Drive.Conectado;
        }
        finally
        {
            SincronizandoDrive = false;
            if (_outraSincronizacaoPedida) { _outraSincronizacaoPedida = false; EnvioPendente = Drive.Conectado; }
            DriveMudou?.Invoke();
        }
    }

    /// <summary>Ao abrir o app e ao voltar para ele: consulta o Drive só se a última consulta por esse motivo foi há mais de 30 segundos.</summary>
    public async Task SincronizarDriveAoVoltarAsync()
    {
        if (!Drive.Conectado || DateTime.Now - _ultimaConsultaAoVoltar < IntervaloMinimoAoVoltar) return;
        _ultimaConsultaAoVoltar = DateTime.Now;
        await SincronizarDriveAsync();
    }

    /// <summary>Antes de fechar: envia na hora o que estiver pendente (uma sincronização em andamento já leva as mudanças).</summary>
    public async Task EnviarPendenteAoDriveAsync()
    {
        if (!EnvioPendente || !Drive.Conectado) return;
        while (SincronizandoDrive) await Task.Delay(200);
        if (EnvioPendente) await SincronizarDriveAsync();
    }

    /// <summary>Login no Google pelo navegador; em seguida junta a cópia que já existir no Drive.</summary>
    public async Task ConectarGoogleAsync(string clientID, string clientSecret, Action<string> abrirNavegador)
    {
        await Drive.ConectarAsync(clientID, clientSecret, abrirNavegador);
        _estadoDoDrive = new EstadoDoDrive();
        GravarEstadoDoDrive();
        DriveMudou?.Invoke();
        await SincronizarDriveAsync();
    }

    public void DesconectarGoogle()
    {
        EnvioPendente = false;
        Drive.Desconectar();
        SituacaoNuvem = "";
        DriveMudou?.Invoke();
    }

    /// <summary>Envio da pasta sincronizada ao Google Drive: um .zip com a pasta inteira e o backup dos dados, sempre no
    /// mesmo arquivo do Drive (substituído a cada envio). Devolve o tamanho enviado, para o aviso.</summary>
    public async Task<long> EnviarPastaAoGoogleDriveAsync()
    {
        if (Dados.PastaSincronizada.Length == 0) throw new InvalidOperationException("Escolha antes a pasta sincronizada (aba Modelos).");
        if (!Drive.Conectado) throw ErroGoogle.SemAutorizacao;
        EnviandoPastaAoDrive = true;
        DriveMudou?.Invoke();
        var zip = Path.Combine(Path.GetTempPath(), $"orthck-{Guid.NewGuid():N}.zip");
        try
        {
            await CompactarPastaSincronizadaAsync(zip);
            var existente = await Drive.ProcurarArquivoAsync(NomeDoZipNoDrive);
            await Drive.EnviarArquivoGrandeAsync(zip, NomeDoZipNoDrive, "application/zip", existente?.Id);
            return new FileInfo(zip).Length;
        }
        finally
        {
            try { File.Delete(zip); } catch { }
            EnviandoPastaAoDrive = false;
            DriveMudou?.Invoke();
        }
    }
}
