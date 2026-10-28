namespace Orthck.Core;

/// <summary>Uma pasta na árvore de subpastas ("1 - APAC" › "Fisioterapia" › "coluna"), para o menu em níveis.</summary>
public sealed class NoSubpasta
{
    public required string Nome { get; init; }
    /// <summary>Caminho completo, como guardado nos modelos: "1 - APAC / Fisioterapia".</summary>
    public required string Caminho { get; init; }
    /// <summary>Há modelos diretamente nesta pasta (não só nas de dentro).</summary>
    public bool TemModelos { get; set; }
    public List<NoSubpasta> Filhos { get; set; } = [];

    /// <summary>Monta a árvore a partir dos caminhos, em ordem alfabética.</summary>
    public static List<NoSubpasta> Arvore(IEnumerable<string> caminhos)
    {
        var raizes = new List<NoSubpasta>();
        foreach (var caminho in caminhos) Inserir(caminho.Split(Armazenamento.SeparadorDeSubpastas), [], raizes);
        return Ordenar(raizes);
    }

    private static void Inserir(string[] partes, List<string> prefixo, List<NoSubpasta> nos)
    {
        if (partes.Length == 0) return;
        var primeira = partes[0];
        var no = nos.FirstOrDefault(n => n.Nome == primeira);
        if (no == null)
        {
            no = new NoSubpasta { Nome = primeira, Caminho = string.Join(Armazenamento.SeparadorDeSubpastas, prefixo.Append(primeira)) };
            nos.Add(no);
        }
        if (partes.Length == 1) no.TemModelos = true;
        else Inserir(partes[1..], [.. prefixo, primeira], no.Filhos);
    }

    private static List<NoSubpasta> Ordenar(List<NoSubpasta> nos)
    {
        foreach (var no in nos) no.Filhos = Ordenar(no.Filhos);
        return nos.OrderBy(n => n.Nome, Texto.Ordem).ToList();
    }
}
