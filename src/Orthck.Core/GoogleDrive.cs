using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orthck.Core;

/// <summary>Credencial do Google (cliente OAuth "App para computador") e a autorização dada no navegador.
/// Fica só neste computador (google.json na pasta de dados), nunca no repositório.</summary>
public sealed class ContaGoogle
{
    public string ClientID { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string? RefreshToken { get; set; }
}

/// <summary>Um arquivo no Drive: o identificador e a data de modificação (texto do Google, comparado como está).</summary>
public sealed record ArquivoNoDrive(string Id, string Modificado);

public sealed class ErroGoogle(string mensagem) : Exception(mensagem)
{
    public static ErroGoogle SemCredencial => new("Falta a credencial do Google (ID do cliente e chave secreta).");
    public static ErroGoogle SemAutorizacao => new("O acesso ao Google Drive não está autorizado. Conecte de novo.");
    public static ErroGoogle Cancelado => new("O login no Google foi cancelado.");
    public static ErroGoogle TempoEsgotado => new("O login no Google não foi concluído a tempo. Tente de novo.");
}

/// <summary>
/// Conversa direto com o Google Drive pela internet, sem o app do Google (como no Mac): login uma vez pelo navegador e
/// depois envio e leitura de um arquivo criado pelo próprio app. A permissão pedida (drive.file) só alcança os arquivos
/// que o app criou, nada mais do Drive.
/// </summary>
public sealed class GoogleDrive
{
    public const string Escopo = "https://www.googleapis.com/auth/drive.file";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly JsonSerializerOptions JsonConta = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _arquivoConta;
    private (string Valor, DateTime ValidoAte)? _tokenDeAcesso;
    private ReceptorDeLogin? _receptorAtual;

    public ContaGoogle Conta { get; private set; }
    public bool Conectado => Conta.RefreshToken != null;

    /// <param name="credencialEmbutida">Credencial colocada no app pela compilação do GitHub (Secrets), se houver.</param>
    public GoogleDrive(string pasta, (string ClientID, string ClientSecret)? credencialEmbutida = null)
    {
        _arquivoConta = Path.Combine(pasta, "google.json");
        try { Conta = JsonSerializer.Deserialize<ContaGoogle>(File.ReadAllText(_arquivoConta), JsonConta) ?? new(); }
        catch { Conta = new(); }
        if (Conta.ClientID.Length == 0 && credencialEmbutida is { } embutida && embutida.ClientID.Length > 0 && embutida.ClientSecret.Length > 0)
        {
            Conta.ClientID = embutida.ClientID;
            Conta.ClientSecret = embutida.ClientSecret;
        }
    }

    private void SalvarConta()
    {
        try
        {
            var tmp = _arquivoConta + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Conta, JsonConta));
            File.Move(tmp, _arquivoConta, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Lê o ID do cliente e a chave secreta do arquivo .json baixado no Google Cloud Console.</summary>
    public static (string ClientID, string ClientSecret) LerCredencial(string arquivo)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(arquivo));
            var cliente = json?["installed"] ?? json?["web"];
            var id = cliente?["client_id"]?.GetValue<string>();
            var segredo = cliente?["client_secret"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(segredo)) return (id, segredo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException) { }
        throw new ErroGoogle($"“{Path.GetFileName(arquivo)}” não é o arquivo da credencial do Google (client_secret….json).");
    }

    /// <summary>Endereço colado da barra do navegador, quando ele não consegue voltar sozinho para o app.</summary>
    public bool EntregarEndereco(string texto) => _receptorAtual?.Entregar(texto) ?? false;

    public void CancelarLogin() => _receptorAtual?.Parar();

    public void Desconectar()
    {
        Conta.RefreshToken = null;
        _tokenDeAcesso = null;
        SalvarConta();
    }

    // MARK: Login pelo navegador

    /// <summary>Abre o navegador para autorizar o app e guarda a autorização.</summary>
    public async Task ConectarAsync(string clientID, string clientSecret, Action<string> abrirNavegador)
    {
        clientID = clientID.Trim();
        clientSecret = clientSecret.Trim();
        if (clientID.Length == 0 || clientSecret.Length == 0) throw ErroGoogle.SemCredencial;
        Conta.ClientID = clientID;
        Conta.ClientSecret = clientSecret;
        SalvarConta();

        var verificador = TextoAleatorio();
        var desafio = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificador)));
        var estado = TextoAleatorio();
        _receptorAtual?.Parar();
        using var receptor = new ReceptorDeLogin();
        _receptorAtual = receptor;
        try
        {
            var enderecoDeVolta = $"http://127.0.0.1:{receptor.Porta}";
            var endereco = "https://accounts.google.com/o/oauth2/v2/auth?" + Formulario(new()
            {
                ["client_id"] = clientID, ["redirect_uri"] = enderecoDeVolta, ["response_type"] = "code", ["scope"] = Escopo,
                ["code_challenge"] = desafio, ["code_challenge_method"] = "S256", ["access_type"] = "offline",
                ["prompt"] = "consent", ["state"] = estado,
            });
            abrirNavegador(endereco);
            var parametros = await receptor.AguardarRespostaAsync();
            if (parametros.GetValueOrDefault("error") == "access_denied") throw ErroGoogle.Cancelado;
            if (parametros.GetValueOrDefault("state") != estado || parametros.GetValueOrDefault("code") is not { } codigo)
                throw new ErroGoogle($"O Google não devolveu a autorização ({parametros.GetValueOrDefault("error") ?? "resposta inválida"}).");
            var resposta = await PedirTokenAsync(new()
            {
                ["client_id"] = clientID, ["client_secret"] = clientSecret, ["code"] = codigo, ["code_verifier"] = verificador,
                ["grant_type"] = "authorization_code", ["redirect_uri"] = enderecoDeVolta,
            });
            Conta.RefreshToken = resposta["refresh_token"]?.GetValue<string>()
                ?? throw new ErroGoogle("O Google não devolveu a autorização permanente. Tente conectar de novo.");
            SalvarConta();
        }
        finally
        {
            if (_receptorAtual == receptor) _receptorAtual = null;
        }
    }

    private async Task<string> TokenAsync()
    {
        if (_tokenDeAcesso is { } t && t.ValidoAte > DateTime.UtcNow) return t.Valor;
        if (Conta.RefreshToken is not { } renovacao) throw ErroGoogle.SemAutorizacao;
        await PedirTokenAsync(new()
        {
            ["client_id"] = Conta.ClientID, ["client_secret"] = Conta.ClientSecret, ["refresh_token"] = renovacao, ["grant_type"] = "refresh_token",
        });
        return _tokenDeAcesso?.Valor ?? throw ErroGoogle.SemAutorizacao;
    }

    private async Task<JsonObject> PedirTokenAsync(Dictionary<string, string> campos)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
        {
            Content = new StringContent(Formulario(campos), Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        using var resposta = await Http.SendAsync(pedido);
        var json = LerJson(await resposta.Content.ReadAsStringAsync());
        if (resposta.StatusCode != HttpStatusCode.OK)
        {
            if (json["error"]?.GetValue<string>() == "invalid_grant")
            {
                // Autorização revogada ou expirada: precisa conectar de novo.
                Desconectar();
                throw ErroGoogle.SemAutorizacao;
            }
            throw new ErroGoogle($"O Google recusou o pedido: {json["error_description"]?.GetValue<string>() ?? json["error"]?.GetValue<string>() ?? "erro desconhecido"}.");
        }
        if (json["access_token"]?.GetValue<string>() is { } valor)
        {
            var segundos = json["expires_in"]?.GetValue<double>() ?? 3600;
            _tokenDeAcesso = (valor, DateTime.UtcNow.AddSeconds(segundos - 60));
        }
        return json;
    }

    // MARK: Arquivos

    /// <summary>O arquivo mais recente com este nome criado pelo app (em qualquer computador com a mesma credencial).</summary>
    public async Task<ArquivoNoDrive?> ProcurarArquivoAsync(string nome)
    {
        var q = $"name = '{nome.Replace("'", "\\'")}' and trashed = false";
        var url = "https://www.googleapis.com/drive/v3/files?" + Formulario(new()
        {
            ["q"] = q, ["spaces"] = "drive", ["orderBy"] = "modifiedTime desc", ["pageSize"] = "1", ["fields"] = "files(id,modifiedTime)",
        });
        var (dados, status) = await ChamarAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        if (status != 200) throw Erro(dados, status);
        return (LerJson(dados)["files"] as JsonArray)?.FirstOrDefault() is JsonObject primeiro ? Arquivo(primeiro) : null;
    }

    /// <summary>Dados atuais do arquivo; null se ele foi apagado (ou foi para a lixeira).</summary>
    public async Task<ArquivoNoDrive?> MetadadosAsync(string id)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}?fields=id,modifiedTime,trashed";
        var (dados, status) = await ChamarAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        if (status == 404) return null;
        if (status != 200) throw Erro(dados, status);
        var json = LerJson(dados);
        return json["trashed"]?.GetValue<bool>() == true ? null : Arquivo(json);
    }

    public async Task<byte[]> BaixarAsync(string id)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}?alt=media";
        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        using var resposta = await Http.SendAsync(pedido);
        var dados = await resposta.Content.ReadAsByteArrayAsync();
        if (resposta.StatusCode == HttpStatusCode.Unauthorized) _tokenDeAcesso = null;
        if (resposta.StatusCode != HttpStatusCode.OK) throw Erro(Encoding.UTF8.GetString(dados), (int)resposta.StatusCode);
        return dados;
    }

    /// <summary>Cria o arquivo (id null) ou troca o conteúdo dele.</summary>
    public async Task<ArquivoNoDrive> EnviarAsync(byte[] conteudo, string nome, string? id)
    {
        HttpRequestMessage Pedido()
        {
            if (id != null)
                return new HttpRequestMessage(HttpMethod.Patch, $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(id)}?uploadType=media&fields=id,modifiedTime")
                {
                    Content = new ByteArrayContent(conteudo) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
                };
            var divisa = $"orthck-{Guid.NewGuid():N}";
            var corpo = new MultipartContent("related", divisa)
            {
                new StringContent(JsonSerializer.Serialize(new { name = nome, mimeType = "application/json" }), Encoding.UTF8, "application/json"),
                new ByteArrayContent(conteudo) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } },
            };
            // Divisa sem aspas, como nos exemplos do Google.
            corpo.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/related; boundary={divisa}");
            return new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id,modifiedTime") { Content = corpo };
        }
        var (dados, status) = await ChamarAsync(Pedido);
        if (status != 200 || Arquivo(LerJson(dados)) is not { } arquivo) throw Erro(dados, status);
        return arquivo;
    }

    /// <summary>Envio "retomável" do Google, para arquivos grandes (como o .zip da pasta de modelos): cria o arquivo
    /// (id null) ou troca o conteúdo dele.</summary>
    public async Task<ArquivoNoDrive> EnviarArquivoGrandeAsync(string arquivoLocal, string nome, string tipo, string? id)
    {
        var tamanho = new FileInfo(arquivoLocal).Length;
        var endereco = id != null
            ? $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(id)}?uploadType=resumable&fields=id,modifiedTime"
            : "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,modifiedTime";
        using var inicio = new HttpRequestMessage(id == null ? HttpMethod.Post : HttpMethod.Patch, endereco)
        {
            Content = new StringContent(id == null ? JsonSerializer.Serialize(new { name = nome, mimeType = tipo }) : "{}", Encoding.UTF8, "application/json")
        };
        inicio.Headers.Add("X-Upload-Content-Type", tipo);
        inicio.Headers.Add("X-Upload-Content-Length", tamanho.ToString());
        inicio.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        using var respostaDoInicio = await Http.SendAsync(inicio);
        if (respostaDoInicio.StatusCode != HttpStatusCode.OK || respostaDoInicio.Headers.Location is not { } sessao)
        {
            if (respostaDoInicio.StatusCode == HttpStatusCode.Unauthorized) _tokenDeAcesso = null;
            throw Erro(await respostaDoInicio.Content.ReadAsStringAsync(), (int)respostaDoInicio.StatusCode);
        }
        await using var arquivo = File.OpenRead(arquivoLocal);
        using var envio = new HttpRequestMessage(HttpMethod.Put, sessao)
        {
            Content = new StreamContent(arquivo) { Headers = { ContentType = new MediaTypeHeaderValue(tipo) } }
        };
        using var resposta = await Http.SendAsync(envio);
        var dados = await resposta.Content.ReadAsStringAsync();
        var status = (int)resposta.StatusCode;
        if ((status != 200 && status != 201) || Arquivo(LerJson(dados)) is not { } enviado) throw Erro(dados, status);
        return enviado;
    }

    private async Task<(string Dados, int Status)> ChamarAsync(Func<HttpRequestMessage> criar)
    {
        using var pedido = criar();
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        using var resposta = await Http.SendAsync(pedido);
        if (resposta.StatusCode == HttpStatusCode.Unauthorized) _tokenDeAcesso = null;
        return (await resposta.Content.ReadAsStringAsync(), (int)resposta.StatusCode);
    }

    // MARK: Auxiliares

    private static JsonObject LerJson(string texto)
    {
        try { return JsonNode.Parse(texto) as JsonObject ?? []; } catch (JsonException) { return []; }
    }

    private static ArquivoNoDrive? Arquivo(JsonObject json) =>
        json["id"]?.GetValue<string>() is { } id ? new ArquivoNoDrive(id, json["modifiedTime"]?.GetValue<string>() ?? "") : null;

    private static ErroGoogle Erro(string dados, int status)
    {
        var mensagem = LerJson(dados)["error"] is JsonObject erro ? erro["message"]?.GetValue<string>() : null;
        return new ErroGoogle($"Google Drive respondeu {status}{(mensagem != null ? ": " + mensagem : ".")}");
    }

    internal static string Formulario(Dictionary<string, string> campos) =>
        string.Join("&", campos.Select(c => $"{Uri.EscapeDataString(c.Key)}={Uri.EscapeDataString(c.Value)}"));

    private static string TextoAleatorio() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] dados) => Convert.ToBase64String(dados).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}

/// <summary>
/// Recebe no próprio computador (127.0.0.1, porta livre) a volta do navegador depois do login no Google. Atende várias
/// conexões (navegadores abrem conexões extras sem pedido) e aceita também o endereço colado à mão.
/// </summary>
internal sealed class ReceptorDeLogin : IDisposable
{
    private readonly TcpListener _ouvinte;
    private readonly TaskCompletionSource<Dictionary<string, string>> _resultado = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _parar = new();
    public int Porta { get; }

    public ReceptorDeLogin()
    {
        _ouvinte = new TcpListener(IPAddress.Loopback, 0);
        _ouvinte.Start();
        Porta = ((IPEndPoint)_ouvinte.LocalEndpoint).Port;
    }

    /// <summary>Fecha a porta; quem estiver esperando recebe "cancelado".</summary>
    public void Parar()
    {
        _resultado.TrySetException(ErroGoogle.Cancelado);
        try { _parar.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Endereço copiado da barra do navegador (http://127.0.0.1:…/?code=…); false se não for a volta do login.</summary>
    public bool Entregar(string endereco)
    {
        if (!Uri.TryCreate(endereco.Trim(), UriKind.Absolute, out var uri)) return false;
        var parametros = Parametros(uri.Query);
        if (!parametros.ContainsKey("code") && !parametros.ContainsKey("error")) return false;
        return _resultado.TrySetResult(parametros);
    }

    /// <summary>Espera a volta do navegador (até 10 minutos) e devolve os parâmetros do endereço (code, state, error).</summary>
    public async Task<Dictionary<string, string>> AguardarRespostaAsync()
    {
        _ = EscutarAsync();
        var limite = Task.Delay(TimeSpan.FromMinutes(10), _parar.Token);
        var primeiro = await Task.WhenAny(_resultado.Task, limite);
        if (primeiro != _resultado.Task) _resultado.TrySetException(_parar.IsCancellationRequested ? ErroGoogle.Cancelado : ErroGoogle.TempoEsgotado);
        return await _resultado.Task;
    }

    private async Task EscutarAsync()
    {
        try
        {
            while (!_resultado.Task.IsCompleted)
            {
                var cliente = await _ouvinte.AcceptTcpClientAsync(_parar.Token);
                _ = AtenderAsync(cliente);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    /// <summary>Lê o pedido do navegador, mostra a página final e fecha a conexão; pedidos como o do ícone são ignorados.</summary>
    private async Task AtenderAsync(TcpClient cliente)
    {
        using (cliente)
        {
            try
            {
                var fluxo = cliente.GetStream();
                var buffer = new byte[16384];
                using var tempo = CancellationTokenSource.CreateLinkedTokenSource(_parar.Token);
                tempo.CancelAfter(TimeSpan.FromSeconds(30));
                var lidos = await fluxo.ReadAsync(buffer, tempo.Token);
                if (lidos <= 0) return;
                var pedido = Encoding.UTF8.GetString(buffer, 0, lidos);
                var caminho = pedido.Split(' ').Skip(1).FirstOrDefault() ?? "/";
                var interrogacao = caminho.IndexOf('?');
                var parametros = Parametros(interrogacao >= 0 ? caminho[interrogacao..] : "");
                var ehVolta = parametros.ContainsKey("code") || parametros.ContainsKey("error");
                var html = $"""
                    <!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>Orthck</title>
                    <body style="font-family:Segoe UI,sans-serif;text-align:center;padding:60px">
                    <h2>{(parametros.ContainsKey("code") ? "Google Drive autorizado ✓" : "Login não concluído")}</h2>
                    <p>Pode fechar esta janela e voltar para o Orthck.</p></body></html>
                    """;
                var corpo = Encoding.UTF8.GetBytes(html);
                var cabecalho = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corpo.Length}\r\nConnection: close\r\n\r\n");
                await fluxo.WriteAsync(cabecalho, tempo.Token);
                await fluxo.WriteAsync(corpo, tempo.Token);
                if (ehVolta) _resultado.TrySetResult(parametros);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        }
    }

    internal static Dictionary<string, string> Parametros(string consulta)
    {
        var parametros = new Dictionary<string, string>();
        foreach (var par in consulta.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = par.Split('=', 2);
            parametros[Uri.UnescapeDataString(partes[0].Replace('+', ' '))] = partes.Length > 1 ? Uri.UnescapeDataString(partes[1].Replace('+', ' ')) : "";
        }
        return parametros;
    }

    public void Dispose()
    {
        Parar();
        try { _ouvinte.Stop(); } catch { }
        _parar.Dispose();
    }
}
