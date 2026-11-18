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
    /// <summary>Documentos gerados: ficam aqui só o tempo de imprimir ou abrir (ver LimparDocumentosTemporarios).</summary>
    public string PastaTemporaria => Path.Combine(Pasta, "Documentos gerados");
    /// <summary>Quanto tempo um documento gerado fica na pasta temporária antes de ser apagado.</summary>
    public static readonly TimeSpan TempoDosDocumentosTemporarios = TimeSpan.FromHours(12);
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
        var mudou = SepararFavoritosPorLocal();
        mudou |= InstalarFichasProntas();
        if (mudou) { try { Salvar(); } catch { } }
        LimparDocumentosTemporarios();
    }

    /// <summary>Apaga os documentos gerados há mais de 12 horas, menos os que ainda esperam na fila de impressão.
    /// O Histórico continua registrando o que foi gerado (sem o arquivo para abrir de novo).</summary>
    public void LimparDocumentosTemporarios(DateTime? agora = null)
    {
        if (!Directory.Exists(PastaTemporaria)) return;
        var naFila = Dados.FilaImpressao.Where(i => i.Situacao != SituacaoImpressao.Enviado)
            .Select(i => Path.GetFullPath(i.Caminho)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var limite = (agora ?? DateTime.Now) - TempoDosDocumentosTemporarios;
        foreach (var arquivo in Directory.GetFiles(PastaTemporaria))
        {
            if (naFila.Contains(Path.GetFullPath(arquivo))) continue;
            try { if (File.GetLastWriteTime(arquivo) < limite) File.Delete(arquivo); }
            catch (IOException) { } // aberto no Word: fica para a próxima limpeza
            catch (UnauthorizedAccessException) { }
        }
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
        dados.Favoritos ??= []; dados.FavoritosPorLocal ??= []; dados.Fichas ??= [];
        dados.ModelosDeAtendimento ??= []; dados.FavoritosDeAtendimento ??= [];
        foreach (var f in dados.Fichas) { f.Campos ??= []; f.Nome ??= ""; f.Arquivo ??= ""; f.Categoria ??= "Prontuário"; }
        foreach (var p in dados.Pacientes)
        {
            p.Nome ??= ""; p.Cpf ??= ""; p.Rg ??= ""; p.DataNascimento ??= ""; p.Telefone ??= ""; p.Email ??= ""; p.Endereco ??= "";
            p.Numero ??= ""; p.Bairro ??= ""; p.Cidade ??= ""; p.Uf ??= ""; p.Cep ??= ""; p.Cns ??= ""; p.Convenio ??= ""; p.Observacoes ??= "";
        }
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
        MarcarEnvioAoDrive();
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

    // MARK: Importar dados (.json)

    public enum ModoDeImportacao
    {
        /// <summary>Acrescenta pacientes, locais, histórico e fila que ainda não existem aqui.</summary>
        Juntar,
        /// <summary>Troca todos os dados pelos do arquivo (restaurar um backup ou trazer do Mac).</summary>
        Substituir
    }

    /// <summary>Lê um dados.json sem alterar nada; erro claro se o arquivo não existe ou não é do Orthck.</summary>
    private static DadosApp LerArquivoDeDados(string arquivo)
    {
        // A ausência do arquivo não é um cadastro vazio: nunca apagar os dados deste computador.
        if (!File.Exists(arquivo)) throw new FileNotFoundException("O arquivo de dados não foi encontrado. Nada foi alterado.", arquivo);
        try
        {
            var lidos = JsonSerializer.Deserialize<DadosApp>(File.ReadAllText(arquivo), Json) ?? throw new JsonException("O arquivo está vazio.");
            Normalizar(lidos);
            return lidos;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"“{Path.GetFileName(arquivo)}” não é um arquivo de dados do Orthck. Nada foi alterado. ({ex.Message})", ex);
        }
    }

    /// <summary>
    /// Importa um dados.json (do Mac, de um backup diário ou do .zip). Antes, guarda uma cópia dos dados atuais em Backups.
    /// <paramref name="deOutroComputador"/>: junção automática da cópia no Google Drive — sem cópia de segurança a cada vez e
    /// sem a fila de impressão (ela é de cada computador). Devolve um resumo do que entrou.
    /// </summary>
    public string ImportarDados(string arquivo, ModoDeImportacao modo, bool deOutroComputador = false)
    {
        if (ProcessandoFila && !deOutroComputador) throw new InvalidOperationException("Aguarde a fila de impressão terminar antes de importar.");
        var importados = LerArquivoDeDados(arquivo);
        if (!deOutroComputador && File.Exists(ArquivoDados))
        {
            Directory.CreateDirectory(PastaBackups);
            File.Copy(ArquivoDados, Path.Combine(PastaBackups, $"antes-de-importar-{DateTime.Now:yyyy-MM-dd-HHmmss}.json"), overwrite: true);
        }
        return modo == ModoDeImportacao.Substituir ? Substituir(arquivo, importados) : Juntar(importados, deOutroComputador);
    }

    /// <summary>Compatibilidade: importar do Mac é substituir os dados pelos do arquivo.</summary>
    public string ImportarDadosDoMac(string arquivo) => ImportarDados(arquivo, ModoDeImportacao.Substituir);

    private string Substituir(string arquivo, DadosApp novos)
    {
        MarcarEnviosInterrompidos(novos);
        // Pastas de outro computador (do Mac: /Users/…) podem não existir neste: mantém as daqui.
        if (novos.PastaSincronizada.Length > 0 && (novos.PastaSincronizada.StartsWith('/') || !Directory.Exists(novos.PastaSincronizada)))
            novos.PastaSincronizada = Dados.PastaSincronizada;
        if (novos.PastaDocumentos.Length > 0 && (novos.PastaDocumentos.StartsWith('/') || !Directory.Exists(novos.PastaDocumentos)))
            novos.PastaDocumentos = Dados.PastaDocumentos;
        foreach (var local in novos.Locais.Where(l => l.Pasta.StartsWith('/'))) local.Pasta = "";

        // Se houver a pasta Modelos ao lado do arquivo (pasta de dados do Mac copiada inteira), as cópias vêm junto.
        var modelosDoMac = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(arquivo))!, "Modelos");
        int copiados = 0;
        foreach (var modelo in novos.Modelos.Concat(novos.Fichas))
        {
            var destino = UrlDoModelo(modelo);
            var origem = Path.Combine(modelosDoMac, modelo.Arquivo);
            if (modelo.Arquivo.Length == 0 || File.Exists(destino) || !File.Exists(origem)) continue;
            File.Copy(origem, destino);
            copiados++;
        }
        // Modelos importados à mão cujo arquivo não está neste computador não funcionariam; os da pasta sincronizada
        // são copiados de novo na próxima sincronização.
        var semArquivo = novos.Modelos.RemoveAll(m => m.Origem == null && !File.Exists(UrlDoModelo(m)));

        // Primeiro grava; só então a sessão passa a usar os dados novos.
        Gravar(novos, ArquivoDados);
        Dados = novos;
        if (InstalarFichasProntas()) Gravar(Dados, ArquivoDados);
        Mudou?.Invoke();

        var resumo = $"Dados substituídos: {Texto.Plural(novos.Pacientes.Count, "paciente", "pacientes")}, {Texto.Plural(novos.Locais.Count, "local", "locais")}, " +
            $"{Texto.Plural(novos.Modelos.Count, "modelo", "modelos")} e {Texto.Plural(novos.Historico.Count, "documento", "documentos")} no histórico.";
        if (copiados > 0) resumo += $" {Texto.Plural(copiados, "arquivo de modelo copiado", "arquivos de modelo copiados")}.";
        if (semArquivo > 0) resumo += $" {Texto.Plural(semArquivo, "modelo importado à mão ficou", "modelos importados à mão ficaram")} de fora porque o arquivo não está neste computador: importe a pasta de dados do Mac inteira (com a pasta Modelos) ou importe esses modelos de novo.";
        if (novos.Modelos.Any(m => m.Origem != null) && Dados.PastaSincronizada.Length == 0)
            resumo += " Escolha a pasta sincronizada dos modelos neste computador (aba Modelos) para trazer os demais.";
        return resumo;
    }

    private string Juntar(DadosApp importados, bool deOutroComputador)
    {
        // As telas guardam referências aos pacientes e locais: a junção acrescenta nos dados que já estão abertos
        // (nada é trocado nem apagado) e desfaz os acréscimos se a gravação falhar.
        var mapaDeLocais = new Dictionary<Guid, Guid>();
        var locaisNovos = new List<LocalAtendimento>();
        foreach (var local in importados.Locais)
        {
            if (Dados.Locais.Any(l => l.Id == local.Id)) mapaDeLocais[local.Id] = local.Id;
            else if (Dados.Locais.Concat(locaisNovos).FirstOrDefault(l => Texto.MesmoNome(l.Nome, local.Nome)) is { } mesmo) mapaDeLocais[local.Id] = mesmo.Id;
            else
            {
                if (local.Pasta.StartsWith('/')) local.Pasta = "";
                locaisNovos.Add(local);
                mapaDeLocais[local.Id] = local.Id;
            }
        }
        var idsDosPacientes = Dados.Pacientes.Select(p => p.Id).ToHashSet();
        var pacientesNovos = importados.Pacientes.Where(p => idsDosPacientes.Add(p.Id)).ToList();
        foreach (var paciente in pacientesNovos)
            paciente.LocalId = paciente.LocalId is Guid l && mapaDeLocais.TryGetValue(l, out var novo) ? novo : null;
        var idsDoHistorico = Dados.Historico.Select(d => d.Id).ToHashSet();
        var historicoNovo = importados.Historico.Where(d => idsDoHistorico.Add(d.Id)).ToList();
        var idsDaFila = Dados.FilaImpressao.Select(i => i.Id).ToHashSet();
        var filaNova = deOutroComputador ? []
            : importados.FilaImpressao.Where(i => i.Situacao == SituacaoImpressao.Pendente && idsDaFila.Add(i.Id)).ToList();
        // Favoritos marcados no outro computador também valem aqui, cada um no seu local.
        var favoritosNovos = new List<(string Chave, string Nome)>();
        foreach (var (chave, nomes) in importados.FavoritosPorLocal)
        {
            var existentes = Dados.FavoritosPorLocal.TryGetValue(chave, out var lista) ? lista : [];
            foreach (var nome in nomes.Where(n => !existentes.Any(x => Texto.MesmoNome(x, n)) && !favoritosNovos.Any(f => f.Chave == chave && Texto.MesmoNome(f.Nome, n))))
                favoritosNovos.Add((chave, nome));
        }
        var resumo = $"Dados juntados: {Texto.Plural(pacientesNovos.Count, "paciente", "pacientes")}, {Texto.Plural(locaisNovos.Count, "local", "locais")} e " +
            $"{Texto.Plural(historicoNovo.Count, "documento", "documentos")} no histórico entraram.";
        if (locaisNovos.Count + pacientesNovos.Count + historicoNovo.Count + filaNova.Count + favoritosNovos.Count == 0) return resumo;

        var historicoAntes = Dados.Historico.ToList();
        var favoritosAntes = Dados.FavoritosPorLocal.ToDictionary(x => x.Key, x => x.Value.ToList());
        foreach (var local in locaisNovos) local.Tipos = local.Tipos.Count > 0 ? local.Tipos : [.. Dados.Categorias];
        Dados.Locais.AddRange(locaisNovos);
        Dados.Pacientes.AddRange(pacientesNovos);
        Dados.Historico = [.. Dados.Historico.Concat(historicoNovo).OrderByDescending(d => d.Data)];
        Dados.FilaImpressao.AddRange(filaNova);
        foreach (var (chave, nome) in favoritosNovos)
        {
            if (!Dados.FavoritosPorLocal.TryGetValue(chave, out var lista)) Dados.FavoritosPorLocal[chave] = lista = [];
            lista.Add(nome);
        }
        try { Gravar(Dados, ArquivoDados); }
        catch
        {
            Dados.Locais.RemoveAll(locaisNovos.Contains);
            Dados.Pacientes.RemoveAll(pacientesNovos.Contains);
            Dados.Historico = historicoAntes;
            Dados.FilaImpressao.RemoveAll(filaNova.Contains);
            Dados.FavoritosPorLocal = favoritosAntes;
            throw;
        }
        Mudou?.Invoke();
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
