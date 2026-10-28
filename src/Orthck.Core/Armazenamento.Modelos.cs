namespace Orthck.Core;

/// <summary>Converte .doc, .rtf, .odt… para .docx (no Windows, pelo Word).</summary>
public interface IConversorDocumentos
{
    void ConverterParaDocx(string origem, string destino);
}

/// <summary>Modelos, tipos de documento e pastas dos documentos gerados.</summary>
public sealed partial class Armazenamento
{
    public static readonly string[] Extensoes = [".docx", ".doc", ".rtf", ".odt"];

    // MARK: Modelos

    public string UrlDoModelo(ModeloDocumento modelo) => Path.Combine(PastaModelos, modelo.Arquivo);

    /// <summary>Arquivo original na pasta sincronizada (para editar no Word) ou a cópia guardada pelo app.</summary>
    public string UrlParaEditar(ModeloDocumento modelo)
    {
        if (modelo.Origem != null && Dados.PastaSincronizada.Length > 0)
        {
            var original = Path.Combine(Dados.PastaSincronizada, modelo.Origem.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(original)) return original;
        }
        return UrlDoModelo(modelo);
    }

    public ModeloDocumento ImportarModelo(string origem, Guid? localId = null, string? tipo = null)
    {
        var arquivo = $"{Guid.NewGuid().ToString().ToUpperInvariant()}.docx";
        var campos = CopiarModelo(origem, Path.Combine(PastaModelos, arquivo));
        var nome = Path.GetFileNameWithoutExtension(origem);
        var categoria = tipo ?? CategoriaSugerida(nome, Tipos(localId));
        AdicionarCategoriaSemSalvar(categoria, localId);
        var modelo = new ModeloDocumento { Nome = nome, Arquivo = arquivo, Campos = campos, Categoria = categoria, LocalId = localId };
        Dados.Modelos.Add(modelo);
        Salvar();
        return modelo;
    }

    /// <summary>
    /// Importa todos os modelos de uma pasta (e das subpastas) para o local escolhido. A subpasta vira o tipo
    /// ("RX/Joelho/x.docx" → tipo "RX / Joelho"); arquivos soltos na pasta vão para <paramref name="tipo"/> ou para o tipo
    /// sugerido pelo nome. Temporários do Word (~$) e arquivos ocultos ficam de fora. Devolve quantos entraram e os erros.
    /// </summary>
    public (int Importados, List<string> Erros) ImportarPasta(string pasta, Guid? localId, string? tipo = null)
    {
        if (!Directory.Exists(pasta)) throw new DirectoryNotFoundException($"Pasta não encontrada: {pasta}");
        var opcoes = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var arquivos = Directory.EnumerateFiles(pasta, "*", opcoes)
            .Where(a => Extensoes.Contains(Path.GetExtension(a).ToLowerInvariant()) && !Path.GetFileName(a).StartsWith("~$"))
            .OrderBy(a => a, Texto.Ordem).ToList();
        int importados = 0;
        var erros = new List<string>();
        foreach (var arquivo in arquivos)
        {
            var subpastas = Path.GetRelativePath(pasta, Path.GetDirectoryName(arquivo)!)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".").ToList();
            var tipoDoArquivo = subpastas.Count > 0 ? string.Join(SeparadorDeSubpastas, subpastas) : tipo;
            try { ImportarModelo(arquivo, localId, tipoDoArquivo); importados++; }
            catch (Exception ex) { erros.Add(ex.Message); }
        }
        return (importados, erros);
    }

    /// <summary>Copia (ou converte, se não for .docx) o arquivo e devolve os campos encontrados. O original não é alterado.</summary>
    public List<string> CopiarModelo(string origem, string destino)
    {
        if (File.Exists(destino)) File.Delete(destino);
        try
        {
            if (Path.GetExtension(origem).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            {
                // Leitura compartilhada: funciona mesmo com o arquivo aberto no Word.
                using var entrada = new FileStream(origem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write);
                entrada.CopyTo(saida);
            }
            else if (Conversor != null) Conversor.ConverterParaDocx(origem, destino);
            else throw new InvalidDataException("Só arquivos .docx podem ser usados como modelo neste computador.");
            return DocxTemplate.Placeholders(destino).ToList();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or System.Runtime.InteropServices.COMException)
        {
            try { if (File.Exists(destino)) File.Delete(destino); } catch { }
            throw new InvalidDataException($"Não foi possível ler “{Path.GetFileName(origem)}”. Verifique se é um documento do Word sem senha de proteção e se ele abre normalmente no Word. ({ex.Message})", ex);
        }
    }

    /// <summary>Relê os campos depois que o modelo foi editado no Word.</summary>
    public void AtualizarCampos(ModeloDocumento modelo)
    {
        modelo.Campos = DocxTemplate.Placeholders(UrlDoModelo(modelo)).ToList();
        Salvar();
    }

    /// <summary>Troca o arquivo do modelo; o novo é validado antes de o atual ser apagado.</summary>
    public void SubstituirArquivo(ModeloDocumento modelo, string origem)
    {
        var novo = Path.Combine(PastaModelos, $"{Guid.NewGuid().ToString().ToUpperInvariant()}.docx");
        var campos = CopiarModelo(origem, novo);
        var atual = UrlDoModelo(modelo);
        if (File.Exists(atual)) File.Delete(atual);
        File.Move(novo, atual);
        modelo.Campos = campos;
        Salvar();
    }

    public ModeloDocumento? Modelo(Guid? id) => id == null ? null : Dados.Modelos.FirstOrDefault(m => m.Id == id);

    /// <summary>Local dono do modelo; null quando o modelo vale para todos (ou o local foi excluído).</summary>
    public LocalAtendimento? LocalDoModelo(ModeloDocumento modelo) => Local(modelo.LocalId);

    /// <summary>Modelos que aparecem para um local: os dele e os de todos os locais, por subpasta e nome.</summary>
    public List<ModeloDocumento> ModelosPara(LocalAtendimento? local) => Dados.Modelos
        .Where(m => LocalDoModelo(m) is not { } dono || dono.Id == local?.Id)
        .OrderBy(m => m.Categoria, Texto.Ordem).ThenBy(m => m.Nome, Texto.Ordem).ToList();

    public void RemoverModelo(ModeloDocumento modelo)
    {
        try { File.Delete(UrlDoModelo(modelo)); } catch (IOException) { }
        Dados.Modelos.RemoveAll(m => m.Id == modelo.Id);
        Salvar();
    }

    /// <summary>Sugere o tipo pelo nome do arquivo ("Receita simples" → o tipo cujo nome começa com "Receit").</summary>
    public static string CategoriaSugerida(string nome, IReadOnlyList<string> tipos)
    {
        var simples = Texto.Normalizar(nome);
        var apelidos = new Dictionary<string, string> { ["rx"] = "radiograf", ["raio x"] = "radiograf", ["raio-x"] = "radiograf", ["usg"] = "ultrassom" };
        foreach (var tipo in tipos)
        {
            var tipoSimples = Texto.Normalizar(tipo);
            var raiz = tipoSimples.Length > 5 ? tipoSimples[..5] : tipoSimples;
            if (raiz.Length >= 4 && simples.Contains(raiz)) return tipo;
            foreach (var (apelido, alvo) in apelidos)
                if (simples.Contains(apelido) && tipoSimples.StartsWith(alvo)) return tipo;
        }
        return tipos.FirstOrDefault(t => Texto.Normalizar(t) == "outros") ?? tipos.LastOrDefault() ?? "Outros";
    }

    // MARK: Tipos de documento — cada local tem a sua lista; local null = modelos de "Todos os locais".

    /// <summary>Tipos do local mais os usados por modelos dele que não estejam na lista, em ordem alfabética.</summary>
    public List<string> Tipos(Guid? localId)
    {
        var lista = new List<string>(ListaDeTipos(localId));
        foreach (var m in Dados.Modelos.Where(m => Pertence(m, localId) && !lista.Contains(m.Categoria))) lista.Add(m.Categoria);
        return Texto.EmOrdemAlfabetica(lista);
    }

    public bool Pertence(ModeloDocumento modelo, Guid? localId) =>
        localId == null ? LocalDoModelo(modelo) == null : modelo.LocalId == localId;

    private List<string> ListaDeTipos(Guid? id) => Local(id)?.Tipos ?? Dados.Categorias;

    private void DefinirListaDeTipos(List<string> lista, Guid? id)
    {
        if (Local(id) is { } local) local.Tipos = lista; else Dados.Categorias = lista;
    }

    public void AdicionarCategoria(string nome, Guid? localId)
    {
        if (AdicionarCategoriaSemSalvar(nome, localId)) Salvar();
    }

    private bool AdicionarCategoriaSemSalvar(string nome, Guid? localId)
    {
        nome = nome.Trim();
        var lista = ListaDeTipos(localId);
        if (nome.Length == 0 || lista.Contains(nome)) return false;
        DefinirListaDeTipos([.. lista, nome], localId);
        return true;
    }

    /// <summary>Renomeia o tipo e os modelos dele no local; se o novo nome já existir, junta os dois tipos.</summary>
    public void RenomearCategoria(string antigo, string novo, Guid? localId)
    {
        novo = novo.Trim();
        var lista = Tipos(localId);
        var indice = lista.IndexOf(antigo);
        if (novo.Length == 0 || novo == antigo || indice < 0) return;
        if (lista.Contains(novo)) lista.RemoveAt(indice); else lista[indice] = novo;
        foreach (var m in Dados.Modelos.Where(m => m.Categoria == antigo && Pertence(m, localId))) m.Categoria = novo;
        DefinirListaDeTipos(lista, localId);
        Salvar();
    }

    /// <summary>Exclui o tipo do local; os modelos dele passam para <paramref name="destino"/>.</summary>
    public void RemoverCategoria(string nome, string destino, Guid? localId)
    {
        var lista = Tipos(localId);
        if (nome == destino || lista.Count <= 1) return;
        foreach (var m in Dados.Modelos.Where(m => m.Categoria == nome && Pertence(m, localId))) m.Categoria = destino;
        lista.Remove(nome);
        DefinirListaDeTipos(lista, localId);
        Salvar();
    }

    public int QuantidadeDeModelos(string tipo, Guid? localId) => Dados.Modelos.Count(m => m.Categoria == tipo && Pertence(m, localId));

    // MARK: Pastas dos documentos gerados

    /// <summary>Separa as pastas de uma subpasta com mais de um nível: "RX / Joelho".</summary>
    public const string SeparadorDeSubpastas = " / ";

    /// <summary>"RX" + ["Joelho"] → "RX / Joelho".</summary>
    public static string Subpasta(string tipo, IEnumerable<string> pastas) => string.Join(SeparadorDeSubpastas, new[] { tipo }.Concat(pastas));

    /// <summary>Pasta escolhida como principal ou Documentos\Orthck.</summary>
    public string PastaPadraoDosDocumentos => Dados.PastaDocumentos.Length > 0
        ? Dados.PastaDocumentos
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Orthck");

    /// <summary>
    /// Pasta do local dentro da pasta principal: usa a que já existir com o nome do local (sem diferenciar maiúsculas e
    /// acentos: "CAMPINA GRANDE" = "Campina Grande") ou a que será criada. Se o local tiver pasta própria com o nome dele,
    /// usa direto; se for uma pasta geral, procura/cria a pasta do local dentro dela.
    /// </summary>
    public string PastaDoLocal(LocalAtendimento? local)
    {
        var nome = Texto.NomeSeguro(local?.Nome ?? "");
        if (nome.Length == 0) nome = "Sem local";
        if (local is { Pasta.Length: > 0 })
        {
            if (Texto.MesmoNome(Path.GetFileName(local.Pasta.TrimEnd('\\', '/')), nome)) return local.Pasta;
            return PastaEm(local.Pasta, nome);
        }
        return PastaEm(PastaPadraoDosDocumentos, nome);
    }

    /// <summary>
    /// Subpasta de <paramref name="pastaBase"/> com este nome, se já existir (sem diferenciar maiúsculas e acentos); senão, o
    /// caminho da que será criada. Um atalho (.lnk) com esse nome é seguido até a pasta original.
    /// </summary>
    public string PastaEm(string pastaBase, string nome)
    {
        if (Directory.Exists(pastaBase))
        {
            try
            {
                foreach (var item in new DirectoryInfo(pastaBase).EnumerateFileSystemInfos())
                {
                    if (item.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                    if (item is DirectoryInfo && Texto.MesmoNome(item.Name, nome)) return item.FullName;
                    if (item is FileInfo && item.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        && Texto.MesmoNome(Path.GetFileNameWithoutExtension(item.Name), nome)
                        && ResolverAtalho?.Invoke(item.FullName) is { } destino && Directory.Exists(destino))
                        return destino;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return Path.Combine(pastaBase, Texto.NomeSeguro(nome));
    }

    /// <summary>Pasta do local + subpasta (vazia = direto na pasta do local); "RX / Joelho" vira a pasta Joelho dentro de RX.</summary>
    public string PastaDosDocumentos(string subpasta, LocalAtendimento? local) => subpasta
        .Split(SeparadorDeSubpastas).Select(Texto.NomeSeguro).Where(n => n.Length > 0)
        .Aggregate(PastaDoLocal(local), PastaEm);

    /// <summary>Cria as subpastas de todos os tipos dentro da pasta do local.</summary>
    public void CriarSubpastas(LocalAtendimento? local)
    {
        foreach (var tipo in Tipos(local?.Id)) Directory.CreateDirectory(PastaDosDocumentos(tipo, local));
    }

    /// <summary>
    /// Caminho livre para o novo documento: "Modelo - Paciente - Local - 27-10-2026.docx", com (2), (3)… se já existir.
    /// O local entra no nome: o Word não abre dois documentos com o mesmo nome ao mesmo tempo.
    /// </summary>
    public string CaminhoParaNovoDocumento(ModeloDocumento modelo, string subpasta, LocalAtendimento? local, string paciente,
        string? pastaAvulsa = null, DateTime? data = null)
    {
        var pasta = pastaAvulsa ?? PastaDosDocumentos(subpasta, local);
        Directory.CreateDirectory(pasta);
        var partes = new[] { modelo.Nome, paciente, local?.Nome ?? "", (data ?? DateTime.Now).ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture) };
        var nomeBase = Texto.NomeSeguro(string.Join(" - ", partes.Where(p => p.Trim().Length > 0)));
        if (nomeBase.Length == 0) nomeBase = "Documento";
        var destino = Path.Combine(pasta, nomeBase + ".docx");
        for (var n = 2; File.Exists(destino); n++) destino = Path.Combine(pasta, $"{nomeBase} ({n}).docx");
        return destino;
    }
}
