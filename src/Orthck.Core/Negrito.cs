namespace Orthck.Core;

/// <summary>
/// Negrito nos campos (Prontuário e Gerar documento), como no Mac: o trecho fica entre "**" na tela e sai em
/// negrito no Word (<see cref="DocxTemplate"/>).
/// </summary>
public static class Negrito
{
    public const string Marca = "**";

    /// <summary>
    /// Ctrl+B: põe "**" antes e depois do trecho selecionado; se ele já estiver em negrito, tira. Sem seleção,
    /// abre "****" com o cursor no meio, para digitar já em negrito. Devolve o texto novo e a seleção nova.
    /// </summary>
    public static (string Texto, int Inicio, int Tamanho) Alternar(string texto, int inicio, int tamanho)
    {
        inicio = Math.Clamp(inicio, 0, texto.Length);
        tamanho = Math.Clamp(tamanho, 0, texto.Length - inicio);
        var selecionado = texto.Substring(inicio, tamanho);
        var m = Marca.Length;
        // A seleção já inclui as marcas: **TRECHO** → TRECHO.
        if (selecionado.Length >= 2 * m && selecionado.StartsWith(Marca) && selecionado.EndsWith(Marca))
        {
            var interno = selecionado[m..^m];
            return (texto[..inicio] + interno + texto[(inicio + tamanho)..], inicio, interno.Length);
        }
        // As marcas estão logo antes e logo depois da seleção.
        if (inicio >= m && inicio + tamanho + m <= texto.Length
            && texto.Substring(inicio - m, m) == Marca && texto.Substring(inicio + tamanho, m) == Marca)
            return (texto[..(inicio - m)] + selecionado + texto[(inicio + tamanho + m)..], inicio - m, tamanho);
        return (texto[..inicio] + Marca + selecionado + Marca + texto[(inicio + tamanho)..], inicio + m, tamanho);
    }

    /// <summary>Fecha e reabre o negrito em cada linha de um campo com várias linhas (H.D.A., exame físico…),
    /// para que um trecho em negrito que passa de uma linha para a outra continue certo no Word.</summary>
    public static List<string> Balancear(IEnumerable<string> linhas)
    {
        var aberto = false;
        var saida = new List<string>();
        foreach (var linha in linhas)
        {
            if (linha.Length == 0) { saida.Add(linha); continue; }
            var marcas = linha.Split(Marca).Length - 1;
            var nova = aberto ? Marca + linha : linha;
            var terminaAberto = aberto != (marcas % 2 == 1);
            if (terminaAberto) nova += Marca;
            aberto = terminaAberto;
            saida.Add(nova);
        }
        return saida;
    }
}
