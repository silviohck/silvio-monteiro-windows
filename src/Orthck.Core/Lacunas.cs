using System.Text;
using System.Text.RegularExpressions;

namespace Orthck.Core;

/// <summary>
/// Lacunas "___" que ficaram sem preencher num texto de prontuário (vindas dos modelos de atendimento), como no Mac.
/// Ao gerar o documento, saem do texto sem deixar frase quebrada:
/// trecho curto que era só um campo vazio ("DERRAME ___", "TRAUMA: ___", "RETORNO EM ___") sai inteiro;
/// em trecho maior, sai a lacuna e a palavra que a liga ("DOR NO JOELHO HÁ ___" → "DOR NO JOELHO").
/// Quebras de linha são mantidas, para o texto continuar nas mesmas linhas da ficha.
/// </summary>
public static partial class Lacunas
{
    /// <summary>Trechos com até este número de palavras (fora a lacuna) são campos e saem inteiros.</summary>
    private const int PalavrasDeUmCampo = 4;

    public static string Remover(string texto)
    {
        if (!texto.Contains("___")) return texto;
        var resultado = new StringBuilder();
        var trecho = new StringBuilder();
        var parenteses = 0;
        foreach (var c in texto)
        {
            if (c == '(') parenteses++;
            if (c == ')') parenteses = Math.Max(0, parenteses - 1);
            // Dentro de parênteses não separa: "(FORTALECIMENTO DE QUADRÍCEPS, ALONGAMENTO)" fica junto.
            if (parenteses == 0 && c is '.' or ';' or ',')
            {
                resultado.Append(Limpar(trecho.ToString(), c.ToString()));
                trecho.Clear();
            }
            else trecho.Append(c);
        }
        resultado.Append(Limpar(trecho.ToString(), ""));
        return Arrumar(resultado.ToString());
    }

    /// <summary>Títulos de seção do prontuário: ficam mesmo quando o que vem depois deles some.</summary>
    [GeneratedRegex(@"^\s*(EXAMES? F[IÍ]SICOS?|EXAMES COMPLEMENTARES|DIAGN[OÓ]STICO|CONDUTA|PROGRAMA[CÇ][AÃ]O TERAP[EÊ]UTICA|HIST[OÓ]RIA|ANAMNESE)\s*:")]
    private static partial Regex Titulo();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(HÁ|EM|DE|DO|DA|DOS|DAS|POR|COM|A|AO|À|E|OU)\s+_{3,}[^\s,.;)]*")]
    private static partial Regex LacunaComLigacao();

    [GeneratedRegex(@"_{3,}[^\s,.;)]*")]
    private static partial Regex Lacuna();

    private static string Limpar(string trecho, string separador)
    {
        if (!trecho.Contains("___")) return trecho + separador;
        var titulo = Titulo().Match(trecho);
        if (titulo.Success)
        {
            var fim = titulo.Index + titulo.Length;
            return trecho[..fim] + Limpar(trecho[fim..], separador);
        }
        var quebras = new string('\n', trecho.Count(c => c == '\n'));
        var palavras = trecho.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(p => !p.Contains("___") && p.Any(char.IsLetterOrDigit));
        if (palavras <= PalavrasDeUmCampo) return quebras;
        var limpo = LacunaComLigacao().Replace(trecho, "");
        limpo = Lacuna().Replace(limpo, "");
        return limpo + separador;
    }

    [GeneratedRegex(@"[ \t]{2,}")] private static partial Regex Espacos();
    [GeneratedRegex(@"\s+([.,;:)])")] private static partial Regex EspacoAntesDaPontuacao();
    [GeneratedRegex(@"\(\s*\)")] private static partial Regex ParentesesVazios();
    [GeneratedRegex(@"([.,;:])(\s*[.,;])+")] private static partial Regex PontuacaoRepetida();
    [GeneratedRegex(@"^[\s.,;]+")] private static partial Regex PontuacaoNoComeco();

    /// <summary>Espaços repetidos, espaço antes de pontuação, pontuação repetida e pontuação no começo da linha.</summary>
    private static string Arrumar(string texto) => string.Join("\n", texto.Split('\n').Select(linha =>
    {
        var s = Espacos().Replace(linha, " ");
        s = EspacoAntesDaPontuacao().Replace(s, "$1");
        s = ParentesesVazios().Replace(s, "");
        s = PontuacaoRepetida().Replace(s, "$1");
        s = PontuacaoNoComeco().Replace(s, "");
        s = Espacos().Replace(s, " ");
        return s.Trim(' ', '\t');
    }));
}
