using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Orthck.Core;

/// <summary>Um documento encontrado na pasta sincronizada.</summary>
public sealed record ArquivoDaPasta(
    string Caminho,      // arquivo de verdade, para ler
    string Relativo,     // caminho dentro da pasta sincronizada, com "/", usado para reconhecer o mesmo arquivo depois
    string Nome,
    string? Local,       // subpasta principal; null = arquivo solto, vale para todos os locais
    string? Tipo,        // subpasta dentro do local; null = tipo adivinhado pelo nome do arquivo
    IReadOnlyList<string> Subpastas, // pastas dentro do tipo até o arquivo (ex.: ["Joelho"])
    DateTimeOffset ModificadoEm);

/// <summary>Situação da sincronização, mostrada na aba Modelos.</summary>
public sealed class EstadoSincronizacao
{
    public bool EmAndamento { get; set; }
    public int Feitos { get; set; }
    public int Total { get; set; }
    public DateTimeOffset? Ultima { get; set; }
    public int Novos { get; set; }
    public int Atualizados { get; set; }
    public int Removidos { get; set; }
    public List<string> Erros { get; set; } = [];
}

/// <summary>
/// Lê a pasta no formato: Pasta → LOCAL → (arquivos soltos | TIPO → arquivos [→ subpastas]), como no Mac.
/// Atalhos (.lnk) para pastas dentro da pasta de um local são seguidos: o atalho "1 - APAC" dentro de ALAGOA GRANDE
/// vira o tipo "1 - APAC" desse local, com as subpastas dele.
/// </summary>
public static partial class LeituraDaPasta
{
    private const int ProfundidadeMaxima = 4;

    public static List<ArquivoDaPasta> Ler(string raiz, ISet<string> ignorar, Func<string, bool> compartilhada, Func<string, string?>? resolverAtalho)
    {
        var resultado = Documentos(raiz).Select(a => Arquivo(a, a, raiz, null, null, [])).ToList();
        var principais = Subpastas(raiz).Where(p => !ignorar.Contains(Path.GetFileName(p))).ToList();
        var compartilhadas = principais.Where(p => compartilhada(Path.GetFileName(p))).ToList();
        // Atalhos dentro dos locais que apontam para uma pasta compartilhada seriam repetidos: ficam de fora.
        var caminhosCompartilhados = compartilhadas.Select(Real).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pasta in compartilhadas)
            resultado.AddRange(Arquivos(pasta, pasta, raiz, null, Path.GetFileName(pasta), [], 1, resolverAtalho));
        foreach (var pastaDoLocal in principais.Where(p => !compartilhada(Path.GetFileName(p))))
        {
            var local = Path.GetFileName(pastaDoLocal);
            resultado.AddRange(Documentos(pastaDoLocal).Select(a => Arquivo(a, a, raiz, local, null, [])));
            foreach (var (nome, real) in PastasEAtalhos(pastaDoLocal, resolverAtalho).Where(t => !caminhosCompartilhados.Contains(Real(t.Real))))
                resultado.AddRange(Arquivos(Path.Combine(pastaDoLocal, nome), real, raiz, local, nome, [], 1, resolverAtalho));
        }
        return resultado;
    }

    /// <summary>Nomes das subpastas principais (candidatas a local de atendimento).</summary>
    public static List<string> SubpastasPrincipais(string raiz) => Subpastas(raiz).Select(Path.GetFileName).OfType<string>().ToList();

    private static string Real(string pasta)
    {
        try
        {
            var info = new DirectoryInfo(pasta);
            return (info.LinkTarget != null ? info.ResolveLinkTarget(true)?.FullName : null) ?? info.FullName;
        }
        catch { return Path.GetFullPath(pasta); }
    }

    /// <param name="pasta">Caminho como aparece dentro da pasta sincronizada (passando pelos atalhos).</param>
    /// <param name="real">A pasta de verdade, para ler os arquivos.</param>
    private static List<ArquivoDaPasta> Arquivos(string pasta, string real, string raiz, string? local, string tipo, List<string> caminho, int nivel, Func<string, string?>? resolverAtalho)
    {
        var resultado = Documentos(real).Select(a => Arquivo(a, Path.Combine(pasta, Path.GetFileName(a)), raiz, local, tipo, caminho)).ToList();
        if (nivel >= ProfundidadeMaxima) return resultado;
        foreach (var (nome, subReal) in PastasEAtalhos(real, resolverAtalho))
            resultado.AddRange(Arquivos(Path.Combine(pasta, nome), subReal, raiz, local, tipo, [.. caminho, nome], nivel + 1, resolverAtalho));
        return resultado;
    }

    private static ArquivoDaPasta Arquivo(string arquivo, string caminhoNaPasta, string raiz, string? local, string? tipo, List<string> subpastas)
    {
        DateTimeOffset modificado;
        try { modificado = File.GetLastWriteTimeUtc(arquivo); } catch { modificado = DateTimeOffset.MinValue; }
        var relativo = Path.GetRelativePath(raiz, caminhoNaPasta).Replace(Path.DirectorySeparatorChar, '/');
        return new ArquivoDaPasta(arquivo, relativo, NomeLimpo(arquivo), local, tipo, subpastas, modificado);
    }

    /// <summary>Subpastas e atalhos para pastas: nome que aparece no Explorador e a pasta de verdade.</summary>
    private static List<(string Nome, string Real)> PastasEAtalhos(string pasta, Func<string, string?>? resolverAtalho)
    {
        var lista = new List<(string, string)>();
        foreach (var item in Conteudo(pasta))
        {
            if (item is DirectoryInfo d) lista.Add((d.Name, d.FullName));
            else if (item.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                     && resolverAtalho?.Invoke(item.FullName) is { } destino && Directory.Exists(destino))
                lista.Add((Path.GetFileNameWithoutExtension(item.Name), destino));
        }
        return lista;
    }

    private static List<FileSystemInfo> Conteudo(string pasta)
    {
        try
        {
            return new DirectoryInfo(pasta).EnumerateFileSystemInfos()
                .Where(i => !i.Attributes.HasFlag(FileAttributes.Hidden) && !i.Attributes.HasFlag(FileAttributes.System) && !i.Name.StartsWith('.'))
                .OrderBy(i => i.Name, Texto.Ordem).ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static List<string> Subpastas(string pasta) => Conteudo(pasta).OfType<DirectoryInfo>().Select(d => d.FullName).ToList();

    private static List<string> Documentos(string pasta) => Conteudo(pasta).OfType<FileInfo>()
        .Where(f => Armazenamento.Extensoes.Contains(f.Extension.ToLowerInvariant()) && !f.Name.StartsWith("~$"))
        .Select(f => f.FullName).ToList();

    [GeneratedRegex(@"^\d+\s*-\s*")]
    private static partial Regex NumeroNaFrente();

    /// <summary>"0 - atestado medico.docx" → "atestado medico".</summary>
    public static string NomeLimpo(string arquivo)
    {
        var nome = Path.GetFileNameWithoutExtension(arquivo);
        var limpo = NumeroNaFrente().Replace(nome, "", 1);
        return limpo.Length == 0 ? nome : limpo;
    }
}

/// <summary>Pasta sincronizada: o app fica igual à pasta (novos, alterados e apagados).</summary>
public sealed partial class Armazenamento
{
    public EstadoSincronizacao Sincronizacao { get; private set; } = new();

    public void DefinirPastaSincronizada(string? pasta)
    {
        Dados.PastaSincronizada = pasta ?? "";
        if (pasta == null)
        {
            // Modelos que vieram da pasta continuam no app, como modelos comuns.
            foreach (var m in Dados.Modelos.Where(m => m.Origem != null)) { m.Origem = null; m.OrigemModificadaEm = null; }
        }
        Salvar();
    }

    /// <summary>Muda o papel de uma subpasta principal: Local, Todos os locais ou Ignorar.</summary>
    public void DefinirPapelDaPasta(string pasta, PapelDaPasta papel)
    {
        Dados.PastasIgnoradas.RemoveAll(x => x == pasta);
        Dados.PastasCompartilhadas.RemoveAll(x => Texto.Iguais(x, pasta));
        if (papel == PapelDaPasta.TodosOsLocais) Dados.PastasCompartilhadas.Add(pasta);
        if (papel == PapelDaPasta.Ignorar) Dados.PastasIgnoradas.Add(pasta);
        Salvar();
    }

    public PapelDaPasta PapelDaPastaDe(string pasta) =>
        Dados.PastasIgnoradas.Contains(pasta) ? PapelDaPasta.Ignorar : Dados.EhCompartilhada(pasta) ? PapelDaPasta.TodosOsLocais : PapelDaPasta.Local;

    /// <summary>
    /// Deixa o app igual à pasta: adiciona arquivos novos, atualiza os alterados e remove os apagados. Modelos importados
    /// avulsos não são tocados. Se a pasta sumir ou aparecer vazia de repente, nada é removido.
    /// </summary>
    /// <param name="progresso">Chamado a cada arquivo (no contexto de quem chamou).</param>
    public async Task SincronizarAsync(Action? progresso = null)
    {
        var raiz = Dados.PastaSincronizada;
        if (raiz.Length == 0 || Sincronizacao.EmAndamento) return;
        if (!Directory.Exists(raiz))
        {
            Sincronizacao = new EstadoSincronizacao { Ultima = Sincronizacao.Ultima, Erros = [$"A pasta sincronizada não foi encontrada: {raiz}. Nenhum modelo foi removido."] };
            progresso?.Invoke();
            return;
        }
        var estado = new EstadoSincronizacao { EmAndamento = true, Ultima = Sincronizacao.Ultima };
        Sincronizacao = estado;
        progresso?.Invoke();
        try
        {
            var ignorar = Dados.PastasIgnoradas.ToHashSet();
            var compartilhadas = Dados.PastasCompartilhadas.ToList();
            var resolver = ResolverAtalho;
            var arquivos = await Task.Run(() => LeituraDaPasta.Ler(raiz, ignorar, p => compartilhadas.Any(c => Texto.Iguais(c, p)), resolver));
            var caminhos = arquivos.Select(a => a.Relativo).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (arquivos.Count == 0 && Dados.Modelos.Any(m => m.Origem != null))
            {
                // Pasta vazia de repente (OneDrive carregando, disco desconectado): não apaga nada.
                estado.Erros.Add("Nenhum documento encontrado na pasta sincronizada agora. Nada foi removido; tente “Sincronizar agora” daqui a pouco.");
                return;
            }

            AjustarSubpastas(arquivos);
            RemoverLocaisDePastasCompartilhadas();

            var pendentes = arquivos.Where(a =>
            {
                var existente = ModeloDaOrigem(a.Relativo);
                return existente?.OrigemModificadaEm is not { } copiadoEm
                    || Math.Abs((copiadoEm - a.ModificadoEm).TotalSeconds) > 1
                    || !File.Exists(UrlDoModelo(existente));
            }).ToList();
            estado.Total = pendentes.Count;
            progresso?.Invoke();

            foreach (var arquivo in pendentes)
            {
                var nomeCopia = $"{Guid.NewGuid().ToString().ToUpperInvariant()}.docx";
                var destino = Path.Combine(PastaModelos, nomeCopia);
                try
                {
                    var campos = await Task.Run(() => CopiarModelo(arquivo.Caminho, destino));
                    if (ModeloDaOrigem(arquivo.Relativo) is { } existente)
                    {
                        try { File.Delete(UrlDoModelo(existente)); } catch (IOException) { }
                        existente.Arquivo = nomeCopia;
                        existente.Campos = campos;
                        existente.OrigemModificadaEm = arquivo.ModificadoEm;
                        estado.Atualizados++;
                    }
                    else
                    {
                        var local = LocalIdParaPasta(arquivo.Local);
                        Dados.Modelos.Add(new ModeloDocumento
                        {
                            Nome = arquivo.Nome, Arquivo = nomeCopia, Campos = campos,
                            Categoria = Subpasta(TipoPara(arquivo, local), arquivo.Subpastas),
                            LocalId = local, Origem = arquivo.Relativo, OrigemModificadaEm = arquivo.ModificadoEm
                        });
                        estado.Novos++;
                    }
                }
                catch (Exception ex) { estado.Erros.Add($"{arquivo.Relativo}: {ex.Message}"); }
                estado.Feitos++;
                progresso?.Invoke();
            }

            var apagados = Dados.Modelos.Where(m => m.Origem != null && !caminhos.Contains(m.Origem)).ToList();
            foreach (var modelo in apagados)
                try { File.Delete(UrlDoModelo(modelo)); } catch (IOException) { }
            Dados.Modelos.RemoveAll(m => apagados.Contains(m));
            estado.Removidos = apagados.Count;
            estado.Ultima = DateTimeOffset.Now;
            Salvar();
        }
        finally
        {
            estado.EmAndamento = false;
            progresso?.Invoke();
        }
    }

    private ModeloDocumento? ModeloDaOrigem(string relativo) =>
        Dados.Modelos.FirstOrDefault(m => m.Origem != null && string.Equals(m.Origem, relativo, StringComparison.OrdinalIgnoreCase));

    /// <summary>Deixa local, subpasta e nome dos modelos sincronizados de acordo com o lugar do arquivo na pasta.</summary>
    private void AjustarSubpastas(List<ArquivoDaPasta> arquivos)
    {
        foreach (var arquivo in arquivos)
        {
            if (ModeloDaOrigem(arquivo.Relativo) is not { } modelo) continue;
            var local = LocalIdParaPasta(arquivo.Local);
            var fimEsperado = arquivo.Subpastas.Count == 0 ? "" : SeparadorDeSubpastas + string.Join(SeparadorDeSubpastas, arquivo.Subpastas);
            var subpastaCerta = arquivo.Tipo == null
                || (fimEsperado.Length == 0 ? !modelo.Categoria.Contains(SeparadorDeSubpastas) : modelo.Categoria.EndsWith(fimEsperado));
            if (modelo.LocalId != local || !subpastaCerta)
            {
                modelo.LocalId = local;
                if (arquivo.Tipo != null) modelo.Categoria = Subpasta(TipoPara(arquivo, local), arquivo.Subpastas);
            }
            if (modelo.Nome != arquivo.Nome && modelo.Nome.Contains(SeparadorDeSubpastas)) modelo.Nome = arquivo.Nome;
        }
    }

    /// <summary>Uma pasta que virou "Todos os locais" pode ter criado antes um local: ele sai se ficou sem modelos e pacientes.</summary>
    private void RemoverLocaisDePastasCompartilhadas()
    {
        var usados = Dados.Modelos.Select(m => m.LocalId).Concat(Dados.Pacientes.Select(p => p.LocalId)).OfType<Guid>().ToHashSet();
        Dados.Locais.RemoveAll(l => l.PastaSincronizada is { } pasta && Dados.EhCompartilhada(pasta) && !usados.Contains(l.Id));
    }

    /// <summary>Local ligado à subpasta: o que já foi ligado antes, um com o mesmo nome, ou um novo.</summary>
    private Guid? LocalIdParaPasta(string? pasta)
    {
        if (pasta == null) return null;
        if (Dados.Locais.FirstOrDefault(l => l.PastaSincronizada == pasta) is { } ligado) return ligado.Id;
        if (Dados.Locais.FirstOrDefault(l => l.PastaSincronizada == null && Texto.Iguais(l.Nome, pasta)) is { } mesmoNome)
        {
            mesmoNome.PastaSincronizada = pasta;
            return mesmoNome.Id;
        }
        var novo = new LocalAtendimento { Nome = pasta, PastaSincronizada = pasta, Tipos = [] };
        Dados.Locais.Add(novo);
        return novo.Id;
    }

    /// <summary>Subpasta → tipo do local com o mesmo nome (sem maiúsculas, acentos e plural) ou um tipo novo; arquivo solto → tipo pelo nome.</summary>
    private string TipoPara(ArquivoDaPasta arquivo, Guid? local)
    {
        var doLocal = Tipos(local);
        if (arquivo.Tipo is not { } pasta)
        {
            var sugerido = CategoriaSugerida(arquivo.Nome, doLocal.Count == 0 ? Dados.Categorias : doLocal);
            AdicionarCategoriaSemSalvar(sugerido, local);
            return sugerido;
        }
        static string Simples(string t)
        {
            var s = Texto.Normalizar(t).Trim();
            return s.EndsWith('s') ? s[..^1] : s;
        }
        if (doLocal.FirstOrDefault(t => Simples(t) == Simples(pasta)) is { } existente) return existente;
        AdicionarCategoriaSemSalvar(pasta, local);
        return pasta;
    }

    /// <summary>
    /// Compacta a pasta sincronizada num .zip (sem os temporários do Word "~$…"), junto com uma cópia dos dados
    /// ("Orthck - dados (backup).json"). A pasta original não é alterada.
    /// </summary>
    public async Task CompactarPastaSincronizadaAsync(string destinoZip)
    {
        var pasta = Dados.PastaSincronizada;
        if (pasta.Length == 0 || !Directory.Exists(pasta)) throw new DirectoryNotFoundException("A pasta sincronizada não foi encontrada.");
        var dados = ArquivoDados;
        await Task.Run(() =>
        {
            var temporaria = Path.Combine(Path.GetTempPath(), "orthck-zip-" + Guid.NewGuid().ToString("N"));
            var copia = Path.Combine(temporaria, Path.GetFileName(pasta.TrimEnd('\\', '/')));
            try
            {
                CopiarPasta(pasta, copia);
                if (File.Exists(dados)) File.Copy(dados, Path.Combine(copia, "Orthck - dados (backup).json"));
                if (File.Exists(destinoZip)) File.Delete(destinoZip);
                ZipFile.CreateFromDirectory(temporaria, destinoZip, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally { try { Directory.Delete(temporaria, true); } catch { } }
        });
    }

    private static void CopiarPasta(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var arquivo in Directory.EnumerateFiles(origem))
        {
            var nome = Path.GetFileName(arquivo);
            if (nome.StartsWith("~$") || nome.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || nome.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
            using var entrada = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var saida = File.Create(Path.Combine(destino, nome));
            entrada.CopyTo(saida);
        }
        foreach (var sub in Directory.EnumerateDirectories(origem)) CopiarPasta(sub, Path.Combine(destino, Path.GetFileName(sub)));
    }
}

/// <summary>O que uma subpasta da pasta sincronizada representa.</summary>
public enum PapelDaPasta { Local, TodosOsLocais, Ignorar }
