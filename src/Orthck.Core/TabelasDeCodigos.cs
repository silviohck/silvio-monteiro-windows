using System.Reflection;

namespace Orthck.Core;

/// <summary>Capítulo (CID-10) ou grupo (SIGTAP), usado no filtro.</summary>
public sealed record GrupoDaTabela(int Numero, string Nome);

public sealed class CodigoDaTabela
{
    /// <summary>Código como é escrito nos documentos: "M54.5", "03.01.01.007-2", "10101012".</summary>
    public string Codigo { get; }
    public string Descricao { get; }
    public int Grupo { get; }
    /// <summary>Linha de título (categoria do CID, subgrupo do SIGTAP), mostrada em negrito.</summary>
    public bool EhTitulo { get; }
    /// <summary>Código + descrição sem acentos e em minúsculas, para a busca.</summary>
    public string TextoDeBusca { get; }
    /// <summary>Código só com letras e números ("m545", "0301010072"), para achar pelo começo.</summary>
    public string CodigoSimples { get; }
    /// <summary>"M54.5 - Dor lombar baixa".</summary>
    public string TextoCompleto => $"{Codigo} - {Descricao}";

    public CodigoDaTabela(string codigo, string descricao, int grupo, bool ehTitulo)
    {
        Codigo = codigo; Descricao = descricao; Grupo = grupo; EhTitulo = ehTitulo;
        CodigoSimples = TabelaDeCodigos.Simplificar(codigo);
        TextoDeBusca = Texto.Normalizar($"{codigo} {CodigoSimples} {descricao}");
    }
}

/// <summary>Tabela de códigos embutida no app (funciona sem internet): CID-10, SIGTAP ou TUSS — as mesmas do Mac.</summary>
public sealed class TabelaDeCodigos
{
    public required string Nome { get; init; }
    /// <summary>Origem e versão dos dados.</summary>
    public required string Fonte { get; init; }
    /// <summary>"capítulo", "grupo"…; vazio quando a tabela não tem filtro.</summary>
    public required string NomeDoGrupo { get; init; }
    public required IReadOnlyList<GrupoDaTabela> Grupos { get; init; }
    public required IReadOnlyList<CodigoDaTabela> Codigos { get; init; }
    public required string ExemploDeBusca { get; init; }

    public static string Simplificar(string codigo) => new(Texto.Normalizar(codigo).Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Todas as palavras digitadas precisam aparecer, em qualquer ordem. Códigos que começam com o digitado vêm primeiro.</summary>
    public List<CodigoDaTabela> Buscar(string texto, int? grupo)
    {
        var termos = Texto.Normalizar(texto).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var filtrados = Codigos.Where(c => (grupo == null || c.Grupo == grupo) && termos.All(c.TextoDeBusca.Contains)).ToList();
        if (termos.Length != 1) return filtrados;
        var simples = Simplificar(termos[0]);
        if (simples.Length == 0) return filtrados;
        var porCodigo = filtrados.Where(c => c.CodigoSimples.StartsWith(simples, StringComparison.Ordinal)).ToList();
        if (porCodigo.Count == 0) return filtrados;
        var ids = porCodigo.ToHashSet();
        return [.. porCodigo, .. filtrados.Where(c => !ids.Contains(c))];
    }

    private static readonly Lazy<TabelaDeCodigos> _cid10 = new(CriarCid10);
    private static readonly Lazy<TabelaDeCodigos> _sigtap = new(CriarSigtap);
    private static readonly Lazy<TabelaDeCodigos> _tuss = new(CriarTuss);
    public static TabelaDeCodigos Cid10 => _cid10.Value;
    public static TabelaDeCodigos Sigtap => _sigtap.Value;
    public static TabelaDeCodigos Tuss => _tuss.Value;

    private static IEnumerable<string[]> Linhas(string recurso, int partes)
    {
        using var fluxo = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orthck.Core.Tabelas." + recurso)
            ?? throw new InvalidOperationException($"Tabela {recurso} não embutida no app.");
        using var leitor = new StreamReader(fluxo);
        var lista = new List<string[]>();
        while (leitor.ReadLine() is { } linha)
        {
            var campos = linha.Split('|', partes);
            if (campos.Length == partes) lista.Add(campos);
        }
        return lista;
    }

    private static string Versao(string chave) => Linhas("versoes.txt", 2).FirstOrDefault(c => c[0] == chave)?[1] ?? "";

    /// <summary>CID-10 do DATASUS: capítulos, categorias (em negrito) e subcategorias.</summary>
    private static TabelaDeCodigos CriarCid10()
    {
        var capitulos = Linhas("cid10_capitulos.txt", 3).ToList();
        var codigos = Linhas("cid10_codigos.txt", 2).Select(c =>
        {
            var categoria = c[0].Length >= 3 ? c[0][..3] : c[0];
            var capitulo = capitulos.FindIndex(x => string.CompareOrdinal(x[0], categoria) <= 0 && string.CompareOrdinal(categoria, x[1]) <= 0) + 1;
            return new CodigoDaTabela(c[0], c[1], capitulo, !c[0].Contains('.'));
        }).ToList();
        return new TabelaDeCodigos
        {
            Nome = "CID-10", Fonte = "Tabela CID-10 do DATASUS", NomeDoGrupo = "capítulo",
            Grupos = capitulos.Select((c, i) => new GrupoDaTabela(i + 1, c[2])).ToList(),
            Codigos = codigos, ExemploDeBusca = "M54.5, lombalgia, dor lombar"
        };
    }

    /// <summary>SIGTAP (Tabela de Procedimentos do SUS): subgrupos (em negrito) e procedimentos, filtrados por grupo.</summary>
    private static TabelaDeCodigos CriarSigtap()
    {
        var grupos = Linhas("sigtap_grupos.txt", 2).Where(c => int.TryParse(c[0], out _))
            .Select(c => new GrupoDaTabela(int.Parse(c[0]), $"{c[0]} - {c[1]}")).ToList();
        var subgrupos = new Dictionary<string, string>();
        foreach (var c in Linhas("sigtap_subgrupos.txt", 2)) subgrupos.TryAdd(c[0], c[1]);
        var codigos = new List<CodigoDaTabela>();
        var subgrupoAtual = "";
        foreach (var c in Linhas("sigtap_procedimentos.txt", 2))
        {
            var numero = c[0];
            if (numero.Length != 10) continue;
            var grupo = int.TryParse(numero[..2], out var g) ? g : 0;
            var subgrupo = numero[..4];
            if (subgrupo != subgrupoAtual)
            {
                subgrupoAtual = subgrupo;
                if (subgrupos.TryGetValue(subgrupo, out var nome)) codigos.Add(new CodigoDaTabela(FormatarSigtap(subgrupo), nome, grupo, true));
            }
            codigos.Add(new CodigoDaTabela(FormatarSigtap(numero), c[1], grupo, false));
        }
        var competencia = Versao("sigtap");
        return new TabelaDeCodigos
        {
            Nome = "SIGTAP",
            Fonte = competencia.Length == 0 ? "Tabela de Procedimentos do SUS (DATASUS)" : $"Tabela de Procedimentos do SUS (DATASUS), competência {competencia}",
            NomeDoGrupo = "grupo", Grupos = grupos, Codigos = codigos, ExemploDeBusca = "03.01.01.007-2, consulta, artroplastia joelho"
        };
    }

    /// <summary>TUSS (ANS), tabela 22: procedimentos e eventos em saúde.</summary>
    private static TabelaDeCodigos CriarTuss()
    {
        var versao = Versao("tuss");
        return new TabelaDeCodigos
        {
            Nome = "TUSS",
            Fonte = versao.Length == 0 ? "TUSS da ANS, tabela 22 (procedimentos e eventos em saúde)" : $"TUSS da ANS, tabela 22 (procedimentos e eventos em saúde), {versao}",
            NomeDoGrupo = "", Grupos = [],
            Codigos = Linhas("tuss_procedimentos.txt", 2).Select(c => new CodigoDaTabela(c[0], c[1], 0, false)).ToList(),
            ExemploDeBusca = "10101012, consulta, ressonância joelho"
        };
    }

    /// <summary>"0301010072" → "03.01.01.007-2"; "0301" → "03.01".</summary>
    public static string FormatarSigtap(string numero)
    {
        if (numero.Length != 10) return string.Join(".", Enumerable.Range(0, (numero.Length + 1) / 2).Select(i => numero.Substring(i * 2, Math.Min(2, numero.Length - i * 2))));
        return $"{numero[..2]}.{numero[2..4]}.{numero[4..6]}.{numero[6..9]}-{numero[9]}";
    }
}
