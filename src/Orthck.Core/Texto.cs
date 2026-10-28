using System.Globalization;
using System.Text;

namespace Orthck.Core;

/// <summary>Comparações de texto como as do Mac: sem diferenciar maiúsculas e acentos, ordem do Finder.</summary>
public static class Texto
{
    private static readonly CompareInfo Comparador = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
    private const CompareOptions SemCaixaNemAcento = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    /// <summary>Iguais sem diferenciar maiúsculas e acentos (e espaços nas pontas).</summary>
    public static bool Iguais(string? a, string? b) =>
        Comparador.Compare((a ?? "").Trim(), (b ?? "").Trim(), SemCaixaNemAcento) == 0;

    /// <summary>"Contém", sem diferenciar maiúsculas e acentos ("colera" encontra "Cólera").</summary>
    public static bool Contem(string? texto, string? trecho) =>
        string.IsNullOrEmpty(trecho) || Comparador.IndexOf(texto ?? "", trecho, SemCaixaNemAcento) >= 0;

    /// <summary>Ordem alfabética do Finder: "1 - APAC" antes de "2 - RX" e de "10 - RX", "RX" antes de "RX / Joelho".</summary>
    public static int Comparar(string? a, string? b) =>
        Comparador.Compare(a ?? "", b ?? "", CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

    public static readonly IComparer<string> Ordem = Comparer<string>.Create(Comparar);

    public static List<string> EmOrdemAlfabetica(IEnumerable<string> nomes) => nomes.Order(Ordem).ToList();

    /// <summary>Minúsculas e sem acentos, para busca ("Lombalgia" → "lombalgia").</summary>
    public static string Normalizar(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Nome que pode virar pasta ou arquivo no Windows.</summary>
    public static string NomeSeguro(string nome)
    {
        var invalidos = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var limpo = new string(nome.Select(c => invalidos.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim().TrimEnd('.', ' ');
        if (limpo.Length > 120) limpo = limpo[..120].TrimEnd(' ', '.');
        // CON, PRN, NUL, COM1… são nomes reservados no Windows, mesmo com extensão.
        var raiz = limpo.Split('.')[0].Trim().ToUpperInvariant();
        if (raiz is "CON" or "PRN" or "AUX" or "NUL" || (raiz.Length == 4 && (raiz.StartsWith("COM") || raiz.StartsWith("LPT")) && char.IsDigit(raiz[3])))
            limpo = "_" + limpo;
        return limpo;
    }

    /// <summary>Mesmo nome de pasta, comparando os nomes seguros sem maiúsculas e acentos.</summary>
    public static bool MesmoNome(string a, string b) => Iguais(NomeSeguro(a), NomeSeguro(b));

    /// <summary>"1 cópia" / "3 cópias".</summary>
    public static string Plural(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";
}
