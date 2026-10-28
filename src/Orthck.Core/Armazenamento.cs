using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orthck.Core;

/// <summary>dados.json existe mas não pôde ser lido. Uma cópia dele foi guardada e nada foi sobrescrito.</summary>
public sealed class DadosIlegiveisException(string mensagem, string copia, Exception causa) : Exception(mensagem, causa)
{
    public string Copia { get; } = copia;
}

/// <summary>
/// Guarda modelos, pacientes, locais, histórico e fila em %LOCALAPPDATA%\Orthck (como o Mac em
/// Application Support/GeradorDocumentos): dados.json, a pasta Modelos (cópias dos modelos) e Backups.
/// Cada alteração é gravada na hora, de forma atômica; uma vez por dia o arquivo anterior vai para Backups.
/// </summary>
public sealed partial class Armazenamento
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new DataIso8601() }
    };
    private const int BackupsGuardados = 30;
    private string _diaDoUltimoBackup = "";

    public string Pasta { get; }
    public string PastaModelos => Path.Combine(Pasta, "Modelos");
    public string PastaBackups => Path.Combine(Pasta, "Backups");
    public string ArquivoDados => Path.Combine(Pasta, "dados.json");
    public DadosApp Dados { get; private set; } = new();

    /// <summary>Converte .doc/.rtf/.odt para .docx (pelo Word, no Windows). Sem ele, só .docx é aceito.</summary>
    public IConversorDocumentos? Conversor { get; set; }
    /// <summary>Resolve um atalho (.lnk) para a pasta de destino; null quando não é atalho para pasta.</summary>
    public Func<string, string?>? ResolverAtalho { get; set; }

    /// <summary>Disparado depois de cada gravação (a interface atualiza contagens e listas).</summary>
    public event Action? Mudou;

    public Armazenamento(string? pasta = null)
    {
        Pasta = pasta ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orthck");
        Directory.CreateDirectory(PastaModelos);
        Carregar();
    }

    // MARK: Disco

    private void Carregar()
    {
        if (!File.Exists(ArquivoDados)) { Dados = new DadosApp(); return; }
        try
        {
            Dados = JsonSerializer.Deserialize<DadosApp>(File.ReadAllText(ArquivoDados), Json)
                ?? throw new JsonException("O arquivo está vazio.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Guarda uma cópia do arquivo que não pôde ser lido; nada é gravado por cima dele.
            var copia = Path.Combine(Pasta, $"dados-ilegivel-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Copy(ArquivoDados, copia, overwrite: false); } catch { copia = ArquivoDados; }
            throw new DadosIlegiveisException($"Não foi possível ler os dados salvos ({ex.Message}). Uma cópia foi guardada em {Path.GetFileName(copia)}.", copia, ex);
        }
        Normalizar(Dados);
        // O app pode ter sido fechado enquanto o Word recebia o comando: o estado "incerto" precisa ficar gravado.
        if (MarcarEnviosInterrompidos(Dados)) Salvar();
    }

    /// <summary>Corrige listas ausentes de versões antigas e separa os tipos por local.</summary>
    private static void Normalizar(DadosApp dados)
    {
        dados.Modelos ??= []; dados.Pacientes ??= []; dados.Locais ??= []; dados.Historico ??= []; dados.FilaImpressao ??= [];
        dados.Categorias ??= [.. DadosApp.CategoriasPadrao];
        dados.PastasIgnoradas ??= []; dados.PastasCompartilhadas ??= [.. DadosApp.PastasCompartilhadasPadrao];
        dados.PastaSincronizada ??= ""; dados.PastaDocumentos ??= "";
        foreach (var m in dados.Modelos) { m.Campos ??= []; m.Categoria ??= "Outros"; m.Nome ??= ""; m.Arquivo ??= ""; }
        // Versões anteriores tinham uma lista de tipos só: cada local passa a ter a sua.
        foreach (var local in dados.Locais)
        {
            local.Tipos ??= [];
            if (local.Tipos.Count > 0) continue;
            var usados = dados.Modelos.Where(m => m.LocalId == local.Id).Select(m => m.Categoria).ToHashSet();
            var lista = dados.Categorias.Where(usados.Contains).ToList();
            lista.AddRange(usados.Except(lista).Order(Texto.Ordem));
            local.Tipos = lista.Count == 0 ? [.. dados.Categorias] : lista;
        }
    }

    private static bool MarcarEnviosInterrompidos(DadosApp dados)
    {
        var houve = false;
        foreach (var item in dados.FilaImpressao.Where(x => x.Situacao == SituacaoImpressao.Enviando))
        {
            item.Situacao = SituacaoImpressao.Incerto;
            item.Observacao = "O aplicativo foi fechado durante o envio. Confira a impressora antes de reenviar.";
            houve = true;
        }
        return houve;
    }

    /// <summary>Grava agora (gravação atômica: arquivo temporário + troca). Chamado depois de cada alteração.</summary>
    public void Salvar()
    {
        FazerBackupDoDia();
        Gravar(Dados, ArquivoDados);
        Mudou?.Invoke();
    }

    /// <summary>
    /// Grava de forma atômica. Antivírus, OneDrive e programas de backup costumam abrir o dados.json por um instante;
    /// a troca é tentada de novo por até ~2 segundos antes de desistir (e então o erro é mostrado, nada é perdido em memória).
    /// </summary>
    private static void Gravar(DadosApp dados, string destino)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var tmp = destino + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(dados, Json));
            for (var tentativa = 1; ; tentativa++)
            {
                try
                {
                    if (File.Exists(destino)) File.Replace(tmp, destino, null, ignoreMetadataErrors: true);
                    else File.Move(tmp, destino);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && tentativa < 8)
                {
                    Thread.Sleep(50 * tentativa);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Não foi possível gravar os dados ({ex.Message}). As alterações continuam na tela; tente de novo em instantes. Se repetir, feche programas que estejam usando a pasta de dados.", ex);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    /// <summary>Uma vez por dia, antes de gravar, guarda o dados.json como estava e apaga os mais antigos que os últimos 30.</summary>
    private void FazerBackupDoDia()
    {
        var hoje = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (hoje == _diaDoUltimoBackup) return;
        _diaDoUltimoBackup = hoje;
        if (!File.Exists(ArquivoDados)) return;
        var destino = Path.Combine(PastaBackups, $"dados-{hoje}.json");
        if (File.Exists(destino)) return;
        try
        {
            Directory.CreateDirectory(PastaBackups);
            File.Copy(ArquivoDados, destino);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        foreach (var antigo in Backups().Skip(BackupsGuardados))
            try { File.Delete(antigo); } catch { }
    }

    /// <summary>Backups diários, do mais novo para o mais antigo.</summary>
    public IReadOnlyList<string> Backups() => !Directory.Exists(PastaBackups) ? [] :
        Directory.GetFiles(PastaBackups, "dados-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Restaura um backup: o dados.json atual (se houver) é guardado antes como dados-antes-da-restauracao-….json.
    /// O backup precisa ser legível; senão nada muda.
    /// </summary>
    public static void RestaurarBackup(string pastaDados, string backup)
    {
        var lido = JsonSerializer.Deserialize<DadosApp>(File.ReadAllText(backup), Json)
            ?? throw new InvalidDataException("O backup está vazio.");
        _ = lido;
        var atual = Path.Combine(pastaDados, "dados.json");
        if (File.Exists(atual))
            File.Copy(atual, Path.Combine(pastaDados, $"dados-antes-da-restauracao-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
        var tmp = atual + ".restaurando.tmp";
        File.Copy(backup, tmp, overwrite: true);
        if (File.Exists(atual)) File.Replace(tmp, atual, null, ignoreMetadataErrors: true); else File.Move(tmp, atual);
    }

    // MARK: Importação do Mac

    /// <summary>
    /// Substitui os dados pelos do dados.json do Mac. Os dados atuais vão antes para Backups; se houver uma pasta
    /// Modelos ao lado do arquivo (pasta de dados do Mac copiada inteira), as cópias dos modelos vêm junto.
    /// Devolve um resumo do que foi importado.
    /// </summary>
    public string ImportarDadosDoMac(string arquivo)
    {
        if (ProcessandoFila) throw new InvalidOperationException("Aguarde a fila de impressão terminar antes de importar.");
        // A ausência do arquivo não é um cadastro vazio: nunca apagar os dados do Windows.
        if (!File.Exists(arquivo)) throw new FileNotFoundException("O arquivo de dados do Mac não foi encontrado. Nada foi alterado.", arquivo);
        DadosApp novos;
        try
        {
            novos = JsonSerializer.Deserialize<DadosApp>(File.ReadAllText(arquivo), Json) ?? throw new JsonException("O arquivo está vazio.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"O arquivo não é um dados.json válido do Orthck. Nada foi alterado. ({ex.Message})", ex);
        }
        Normalizar(novos);
        MarcarEnviosInterrompidos(novos);
        // Caminhos do Mac (/Users/…) não existem no Windows.
        if (novos.PastaSincronizada.StartsWith('/')) novos.PastaSincronizada = "";
        if (novos.PastaDocumentos.StartsWith('/')) novos.PastaDocumentos = "";
        foreach (var local in novos.Locais.Where(l => l.Pasta.StartsWith('/'))) local.Pasta = "";

        var modelosDoMac = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(arquivo))!, "Modelos");
        int copiados = 0, faltando = 0;
        foreach (var modelo in novos.Modelos)
        {
            var destino = UrlDoModelo(modelo);
            var origem = Path.Combine(modelosDoMac, modelo.Arquivo);
            if (File.Exists(destino)) continue;
            if (File.Exists(origem)) { File.Copy(origem, destino); copiados++; }
            else if (modelo.Origem == null) faltando++; // os da pasta sincronizada voltam ao sincronizar
        }

        Directory.CreateDirectory(PastaBackups);
        if (File.Exists(ArquivoDados))
            File.Copy(ArquivoDados, Path.Combine(PastaBackups, $"antes-importacao-{DateTime.Now:yyyyMMdd-HHmmss}.json"));
        // Primeiro grava; só então a sessão passa a usar os dados novos.
        Gravar(novos, ArquivoDados);
        Dados = novos;
        Mudou?.Invoke();

        var resumo = $"{Texto.Plural(novos.Pacientes.Count, "paciente", "pacientes")}, {Texto.Plural(novos.Locais.Count, "local", "locais")}, " +
            $"{Texto.Plural(novos.Modelos.Count, "modelo", "modelos")} e {Texto.Plural(novos.Historico.Count, "documento", "documentos")} no histórico.";
        if (copiados > 0) resumo += $" {Texto.Plural(copiados, "arquivo de modelo copiado", "arquivos de modelo copiados")}.";
        if (faltando > 0) resumo += $" {Texto.Plural(faltando, "modelo importado avulso ficou", "modelos importados avulsos ficaram")} sem arquivo: importe a pasta de dados do Mac inteira (com a pasta Modelos) ou importe esses modelos de novo.";
        if (novos.Modelos.Any(m => m.Origem != null)) resumo += " Escolha a pasta sincronizada dos modelos neste computador (aba Modelos) para trazer os demais.";
        return resumo;
    }

    // MARK: Pacientes

    public IEnumerable<Paciente> PacientesOrdenados => Dados.Pacientes.OrderBy(p => p.Nome, Texto.Ordem);

    public Paciente? Paciente(Guid? id) => id == null ? null : Dados.Pacientes.FirstOrDefault(p => p.Id == id);

    public Paciente NovoPaciente(string nome = "")
    {
        var paciente = new Paciente { Nome = nome };
        Dados.Pacientes.Add(paciente);
        Salvar();
        return paciente;
    }

    /// <summary>Todos os pacientes com exatamente este nome (homônimos), sem diferenciar maiúsculas e acentos.</summary>
    public List<Paciente> PacientesChamados(string nome)
    {
        nome = nome.Trim();
        return nome.Length == 0 ? [] : Dados.Pacientes.Where(p => Texto.Iguais(p.Nome, nome)).ToList();
    }

    /// <summary>"nasc. 01/02/1990 · CPF 123" — para diferenciar pacientes com o mesmo nome.</summary>
    public static string Identificacao(Paciente paciente)
    {
        var nascimento = paciente.DataNascimento.Trim();
        var partes = new List<string> { nascimento.Length == 0 ? "sem data de nascimento" : $"nasc. {nascimento}" };
        if (paciente.Cpf.Length > 0) partes.Add($"CPF {paciente.Cpf}");
        return string.Join(" · ", partes);
    }

    /// <summary>Locais onde o paciente já foi atendido: o do cadastro e os dos documentos gerados para ele.</summary>
    public List<LocalAtendimento> LocaisDoPaciente(Paciente paciente)
    {
        var ids = new List<Guid>();
        if (paciente.LocalId is Guid proprio) ids.Add(proprio);
        foreach (var documento in Dados.Historico.Where(d => d.PacienteId == paciente.Id))
        {
            var local = documento.Local == null ? null : Dados.Locais.FirstOrDefault(l => Texto.MesmoNome(l.Nome, documento.Local));
            if (local != null && !ids.Contains(local.Id)) ids.Add(local.Id);
        }
        return ids.Select(id => Local(id)).OfType<LocalAtendimento>().ToList();
    }

    /// <summary>Para listas grandes: ids dos locais de cada paciente (cadastro + histórico), numa passada só.</summary>
    public Dictionary<Guid, HashSet<Guid>> LocaisPorPaciente()
    {
        var mapa = new Dictionary<Guid, HashSet<Guid>>();
        HashSet<Guid> De(Guid id) => mapa.TryGetValue(id, out var s) ? s : mapa[id] = [];
        foreach (var p in Dados.Pacientes) if (p.LocalId is Guid l) De(p.Id).Add(l);
        var porNome = new Dictionary<string, Guid>();
        foreach (var documento in Dados.Historico)
        {
            if (documento.PacienteId is not Guid pid || documento.Local is not { } nome) continue;
            if (!porNome.TryGetValue(nome, out var lid))
            {
                lid = Dados.Locais.FirstOrDefault(l => Texto.MesmoNome(l.Nome, nome))?.Id ?? Guid.Empty;
                porNome[nome] = lid;
            }
            if (lid != Guid.Empty) De(pid).Add(lid);
        }
        return mapa;
    }

    /// <summary>"Alagoa Grande" ou "Alagoa Grande, Araruna".</summary>
    public string NomesDosLocais(Paciente paciente) =>
        string.Join(", ", LocaisDoPaciente(paciente).Select(l => l.Nome).Where(n => n.Length > 0));

    public void RemoverPaciente(Guid id)
    {
        Dados.Pacientes.RemoveAll(p => p.Id == id);
        Salvar();
    }

    // MARK: Locais

    public IEnumerable<LocalAtendimento> LocaisOrdenados => Dados.Locais.OrderBy(l => l.Nome, Texto.Ordem);

    public LocalAtendimento? Local(Guid? id) => id == null ? null : Dados.Locais.FirstOrDefault(l => l.Id == id);

    public LocalAtendimento NovoLocal()
    {
        var local = new LocalAtendimento { Tipos = [.. Dados.Categorias] };
        Dados.Locais.Add(local);
        Salvar();
        return local;
    }

    /// <summary>
    /// Exclui o local depois de decidir o destino dos modelos e pacientes dele:
    /// <paramref name="destino"/> = outro local (transferir) ou null (desvincular: modelos de todos os locais, pacientes sem local).
    /// </summary>
    public void ExcluirLocal(Guid id, Guid? destino)
    {
        var local = Local(id);
        if (local == null) return;
        foreach (var m in Dados.Modelos.Where(m => m.LocalId == id)) m.LocalId = destino;
        foreach (var p in Dados.Pacientes.Where(p => p.LocalId == id)) p.LocalId = destino;
        // Pasta sincronizada do local: passa para o destino, se ele ainda não tiver uma;
        // senão deixa de ser acompanhada, para o local não voltar na próxima sincronização.
        if (local.PastaSincronizada is string pasta)
        {
            var alvo = Local(destino);
            if (alvo != null && alvo.PastaSincronizada == null) alvo.PastaSincronizada = pasta;
            else if (!Dados.PastasIgnoradas.Contains(pasta)) Dados.PastasIgnoradas.Add(pasta);
        }
        Dados.Locais.RemoveAll(l => l.Id == id);
        Salvar();
    }

    // MARK: Histórico

    public void Registrar(DocumentoGerado documento)
    {
        Dados.Historico.Insert(0, documento);
        Salvar();
    }
}

/// <summary>
/// Datas como o Mac (JSONEncoder .iso8601): UTC, sem frações de segundo; o decodificador do Mac rejeita frações.
/// Na leitura, convertidas para o fuso local.
/// </summary>
internal sealed class DataIso8601 : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToLocalTime();
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
