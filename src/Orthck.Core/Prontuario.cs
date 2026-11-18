namespace Orthck.Core;

/// <summary>
/// Regras da aba Prontuário (ProntuarioView do Mac) sem interface: campos da ficha, linhas como no papel, modelo
/// de atendimento, troca do lado e os valores que vão para o Word. A tela só mostra e chama estas funções.
/// </summary>
public static class Prontuario
{
    /// <summary>Letras que cabem numa linha da ficha (largura da página, letra 9,5).</summary>
    public const int CaracteresPorLinha = 76;

    /// <summary>{{HDA_LINHA_2}}, {{HDA_LINHA_3}}…: as outras linhas de um campo da ficha (não aparecem na tela).</summary>
    public static bool EhContinuacao(string campo)
    {
        var partes = CamposAutomaticos.Chave(campo).Split("_LINHA_");
        return partes.Length == 2 && partes[0].Length > 0 && int.TryParse(partes[1], out _);
    }

    /// <summary>As linhas seguintes do campo, em ordem (linha 2, 3…).</summary>
    public static List<string> Continuacoes(string campo, ModeloDocumento ficha)
    {
        var prefixo = CamposAutomaticos.Chave(campo) + "_LINHA_";
        return ficha.Campos
            .Select(outro => (Chave: CamposAutomaticos.Chave(outro), Campo: outro))
            .Where(x => x.Chave.StartsWith(prefixo, StringComparison.Ordinal) && int.TryParse(x.Chave[prefixo.Length..], out _))
            .OrderBy(x => int.Parse(x.Chave[prefixo.Length..]))
            .Select(x => x.Campo).ToList();
    }

    /// <summary>O campo e as linhas seguintes dele ({{HDA}}, {{HDA_LINHA_2}}…).</summary>
    public static List<string> Grupo(string campo, ModeloDocumento ficha) => [campo, .. Continuacoes(campo, ficha)];

    /// <summary>Campos que vêm do cadastro, do local ou da data (menos o nome, que é a caixa do paciente).</summary>
    public static List<string> CamposDeIdentificacao(ModeloDocumento ficha) =>
        ficha.Campos.Where(c => CamposAutomaticos.EhAutomatico(c) && !CamposAutomaticos.EhNomeDoPaciente(c)).ToList();

    /// <summary>Campos do atendimento: queixa, história, exame, CID, conduta…</summary>
    public static List<string> CamposClinicos(ModeloDocumento ficha) =>
        ficha.Campos.Where(c => !CamposAutomaticos.EhAutomatico(c) && !EhContinuacao(c)).ToList();

    /// <summary>Todos os campos do atendimento, com as linhas, na ordem da tela (para o Tab e a troca de lado).</summary>
    public static List<string> LinhasClinicas(ModeloDocumento ficha) => CamposClinicos(ficha).SelectMany(c => Grupo(c, ficha)).ToList();

    /// <summary>Quebra o texto em linhas de até <paramref name="largura"/> letras, sem cortar palavras; Enter começa linha nova.</summary>
    public static List<string> QuebrarEmLinhas(string texto, int largura = CaracteresPorLinha)
    {
        var linhas = new List<string>();
        foreach (var paragrafo in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var atual = "";
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (atual.Length == 0) atual = palavra;
                else if (atual.Length + 1 + palavra.Length <= largura) atual += " " + palavra;
                else { linhas.Add(atual); atual = palavra; }
            }
            linhas.Add(atual);
        }
        while (linhas.Count > 1 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);
        return linhas;
    }

    private static readonly string[] PartesLongas = ["ATENDIMENTO", "QUEIXA", "HDA", "HISTORIA", "ANAMNESE", "EXAME", "PROGRAMACAO",
        "CONDUTA", "EVOLUCAO", "PLANO", "OBSERVAC", "TRATAMENTO", "PRESCRICAO", "HIPOTESE"];

    public static bool EhLongo(string campo)
    {
        var chave = CamposAutomaticos.Chave(campo);
        return PartesLongas.Any(chave.Contains);
    }

    private static readonly Dictionary<string, string> Titulos = new()
    {
        ["QUEIXA_PRINCIPAL"] = "Queixa principal (Q.P.)", ["QP"] = "Queixa principal (Q.P.)",
        ["HDA"] = "História da doença atual (H.D.A.)", ["ANAMNESE"] = "Anamnese",
        ["EXAME_FISICO"] = "Exame físico", ["EXAMES_COMPLEMENTARES"] = "Exames complementares",
        ["PROGRAMACAO_TERAPEUTICA"] = "Programação terapêutica", ["CONDUTA"] = "Conduta",
        ["DIAGNOSTICO"] = "Diagnóstico", ["CID"] = "CID-10", ["PERFIL_CER_II"] = "Perfil CER II",
        ["ATENDIMENTO"] = "Atendimento", ["EVOLUCAO"] = "Evolução",
        ["DATA_NASCIMENTO"] = "Data de nascimento", ["ENDERECO"] = "Endereço", ["NUMERO"] = "Número",
        ["CIDADE_PACIENTE"] = "Cidade", ["TELEFONE"] = "Telefone", ["DATA"] = "Data da avaliação",
        ["CNS"] = "CNS (cartão do SUS)", ["BAIRRO"] = "Bairro", ["CEP"] = "CEP", ["UF"] = "UF", ["CPF"] = "CPF",
    };

    /// <summary>Nome amigável na tela ({{HDA}} → "História da doença atual (H.D.A.)").</summary>
    public static string Titulo(string campo) => Titulos.GetValueOrDefault(CamposAutomaticos.Chave(campo), campo);

    // Campos da ficha que cada parte do modelo preenche (pelo nome do campo).
    private static string? Campo(ModeloDocumento f, Func<string, bool> teste) =>
        CamposClinicos(f).FirstOrDefault(c => teste(CamposAutomaticos.Chave(c)));

    /// <summary>A ficha tem um campo próprio para exames complementares?</summary>
    public static string? CampoDeExames(ModeloDocumento f) => f.Campos.FirstOrDefault(c => CamposAutomaticos.Chave(c).StartsWith("EXAMES"));
    public static string? CampoDoExameFisico(ModeloDocumento f) => f.Campos.FirstOrDefault(c => CamposAutomaticos.Chave(c).Contains("EXAME_FISICO"));
    public static string? CampoQp(ModeloDocumento f) => Campo(f, c => c == "QP" || c.Contains("QUEIXA"));
    public static string? CampoHda(ModeloDocumento f) => Campo(f, c => c == "HDA" || c.Contains("HISTORIA") || c.Contains("ANAMNESE"));
    public static string? CampoExame(ModeloDocumento f) => CampoDoExameFisico(f) ?? Campo(f, c => c.Contains("EXAME"));
    public static string? CampoDiagnostico(ModeloDocumento f) => Campo(f, c => c.Contains("DIAGNOSTICO") || c.Contains("HIPOTESE"));
    public static string? CampoCid(ModeloDocumento f) => CamposClinicos(f).FirstOrDefault(CamposAutomaticos.EhCampoCid);
    public static string? CampoConduta(ModeloDocumento f) =>
        Campo(f, c => c.Contains("PROGRAMACAO") || c.Contains("CONDUTA") || c.Contains("PLANO") || c.Contains("TRATAMENTO"));
    public static string? CampoPerfil(ModeloDocumento f) => Campo(f, c => c.Contains("PERFIL"));

    /// <summary>Ficha de texto livre (ex.: Alagoa Grande): um só campo com linhas, sem queixa/história/exame separados.</summary>
    public static string? CampoLivre(ModeloDocumento f) =>
        CampoQp(f) != null || CampoHda(f) != null || CampoConduta(f) != null ? null
            : Campo(f, c => c is "ATENDIMENTO" or "EVOLUCAO" or "TEXTO");

    public static bool TemLinhas(string? campo, ModeloDocumento ficha) => campo != null && Continuacoes(campo, ficha).Count > 0;

    /// <summary>Mostra a caixa "Exames complementares" à parte (vai junto do exame físico ao gerar)?</summary>
    public static bool PrecisaDeExamesExtras(ModeloDocumento f) =>
        CampoDeExames(f) == null && !TemLinhas(CampoDoExameFisico(f), f) && CampoLivre(f) == null;

    public static string Maiusculas(string texto) => texto.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    private static string Valor(IDictionary<string, string> valores, string campo) => valores.TryGetValue(campo, out var v) ? v : "";

    /// <summary>Passa para a linha de baixo as palavras que não cabem. Devolve true se a linha <paramref name="indice"/> transbordou.</summary>
    public static bool Fluir(IDictionary<string, string> valores, IReadOnlyList<string> campos, int indice)
    {
        var transbordou = false;
        for (var atual = indice; atual + 1 < campos.Count; atual++)
        {
            var texto = Valor(valores, campos[atual]);
            if (texto.Length <= CaracteresPorLinha) break;
            var partes = QuebrarEmLinhas(texto);
            if (partes.Count < 2) break;
            valores[campos[atual]] = partes[0];
            var resto = string.Join(" ", partes.Skip(1));
            var seguinte = Valor(valores, campos[atual + 1]);
            valores[campos[atual + 1]] = seguinte.Length == 0 ? resto : resto + " " + seguinte;
            if (atual == indice) transbordou = true;
        }
        return transbordou;
    }

    /// <summary>Enter numa linha da ficha: o texto depois do cursor desce para a linha de baixo (as seguintes descem
    /// junto, como num editor de texto). A última linha não perde texto: a sobra vai para o fim dela.</summary>
    public static bool QuebrarLinha(IDictionary<string, string> valores, IReadOnlyList<string> campos, int indice, int posicao)
    {
        if (indice + 1 >= campos.Count) return false;
        var texto = Valor(valores, campos[indice]);
        posicao = Math.Clamp(posicao, 0, texto.Length);
        var linhas = campos.Select(c => Valor(valores, c)).ToList();
        linhas[indice] = texto[..posicao].Trim(' ', '\t');
        linhas.Insert(indice + 1, texto[posicao..].Trim(' ', '\t'));
        var sobra = linhas[^1];
        linhas.RemoveAt(linhas.Count - 1);
        if (sobra.Length > 0) linhas[^1] = linhas[^1].Length == 0 ? sobra : linhas[^1] + " " + sobra;
        for (var i = 0; i < campos.Count; i++) valores[campos[i]] = linhas[i];
        Fluir(valores, campos, indice + 1);
        return true;
    }

    /// <summary>Preenche a ficha com o modelo, no lado escolhido; textos longos ocupam as linhas em ordem.
    /// O modelo troca o que estiver escrito (sem perguntar, como no Mac).</summary>
    public static void Aplicar(IDictionary<string, string> valores, ModeloDeAtendimento modelo, ModeloDocumento ficha, string lado)
    {
        if (CampoLivre(ficha) is { } livre) { AplicarNoTextoLivre(valores, modelo, livre, ficha, lado); return; }
        void Por(string? campo, string texto)
        {
            if (campo == null) return;
            var linhas = Grupo(campo, ficha);
            foreach (var linha in linhas) valores[linha] = "";
            valores[campo] = Maiusculas(ModelosDeAtendimento.ComLado(texto, lado));
            if (linhas.Count > 1) Fluir(valores, linhas, 0);
        }
        Por(CampoQp(ficha), modelo.Qp);
        Por(CampoHda(ficha), modelo.Hda);
        Por(CampoExame(ficha), modelo.Exame);
        Por(CampoDiagnostico(ficha), modelo.Diagnostico);
        Por(CampoCid(ficha), modelo.Cid);
        Por(CampoConduta(ficha), modelo.Conduta);
        if (CampoPerfil(ficha) is { } perfil && Valor(valores, perfil).Length == 0) valores[perfil] = "FÍSICO";
    }

    /// <summary>Ficha de texto livre: o modelo ocupa as linhas em sequência, cada parte começando numa linha nova
    /// (Q.P., H.D.A., exame físico, diagnóstico com CID e conduta).</summary>
    private static void AplicarNoTextoLivre(IDictionary<string, string> valores, ModeloDeAtendimento modelo, string livre, ModeloDocumento ficha, string lado)
    {
        var diagnostico = string.Join(" — ", new[] { modelo.Diagnostico, modelo.Cid.Length == 0 ? "" : $"CID 10: {modelo.Cid}" }.Where(x => x.Length > 0));
        var partes = new List<(string Titulo, string Texto)>
        {
            ("Q.P.: ", modelo.Qp), ("H.D.A.: ", modelo.Hda), ("EXAME FÍSICO: ", modelo.Exame),
            ("DIAGNÓSTICO: ", diagnostico), ("CONDUTA: ", modelo.Conduta),
        }.Where(p => p.Texto.Trim().Length > 0).ToList();
        // Um modelo seu salvo de uma ficha livre tem só o texto: entra sem títulos.
        if (partes.Count == 1) partes[0] = ("", partes[0].Texto);
        var linhasDoTexto = partes.SelectMany(p => QuebrarEmLinhas(Maiusculas(ModelosDeAtendimento.ComLado(p.Titulo + p.Texto, lado)))).ToList();
        var linhas = Grupo(livre, ficha);
        for (var i = 0; i < linhas.Count; i++)
            valores[linhas[i]] = i < linhas.Count - 1
                ? (i < linhasDoTexto.Count ? linhasDoTexto[i] : "")
                : string.Join(" ", linhasDoTexto.Skip(i)); // o que não couber fica na última linha
    }

    /// <summary>Trocou o lado no seletor: o lado muda em tudo o que já está escrito no atendimento, concordando com a
    /// palavra anterior (OMBRO DIREITO → OMBRO ESQUERDO, À DIREITA → À ESQUERDA, BILATERAL…).</summary>
    public static string TrocarLado(string texto, string antigo, string novo) =>
        texto.Length == 0 || antigo == novo ? texto : ModelosDeAtendimento.ComLado(ModelosDeAtendimento.SemLado(texto, antigo), novo);

    /// <summary>O que está escrito na ficha vira um modelo seu (o lado escolhido volta a ser [LADO]).</summary>
    public static ModeloDeAtendimento ComoMeuModelo(IDictionary<string, string> valores, ModeloDocumento ficha, string nome, string lado)
    {
        string Texto(string? campo) => campo == null ? "" : ModelosDeAtendimento.SemLado(
            string.Join(" ", Grupo(campo, ficha).Select(c => Valor(valores, c).Trim()).Where(t => t.Length > 0)), lado);
        // Ficha de texto livre: o texto todo fica no modelo (entra sem títulos ao usar de novo).
        if (CampoLivre(ficha) is { } livre) return new ModeloDeAtendimento { Nome = nome, Hda = Texto(livre), Meu = true };
        return new ModeloDeAtendimento
        {
            Nome = nome, Cid = Texto(CampoCid(ficha)), Diagnostico = Texto(CampoDiagnostico(ficha)), Qp = Texto(CampoQp(ficha)),
            Hda = Texto(CampoHda(ficha)), Exame = Texto(CampoExame(ficha)), Conduta = Texto(CampoConduta(ficha)), Meu = true
        };
    }

    /// <summary>Valores que vão para o Word: o nome digitado; sem campo próprio, os exames junto do exame físico;
    /// sem os ___ não preenchidos (se pedido); e o negrito fechado/reaberto em cada linha.</summary>
    public static Dictionary<string, string> ValoresFinais(IReadOnlyDictionary<string, string> valores, ModeloDocumento ficha,
        string nome, string examesExtras, bool tirarLacunas)
    {
        var finais = new Dictionary<string, string>(valores);
        // Campo deixado em branco sai em branco (não como {{DIAGNOSTICO}}).
        foreach (var campo in ficha.Campos) finais.TryAdd(campo, "");
        foreach (var campo in ficha.Campos.Where(CamposAutomaticos.EhNomeDoPaciente)) finais[campo] = nome;
        var exames = examesExtras.Trim();
        if (CampoDeExames(ficha) == null && exames.Length > 0 && CampoDoExameFisico(ficha) is { } alvo)
        {
            var exame = finais[alvo].Trim();
            finais[alvo] = string.Join("\n", new[] { exame, $"EXAMES COMPLEMENTARES: {exames}" }.Where(x => x.Length > 0));
        }
        foreach (var campo in ficha.Campos.Where(c => !EhContinuacao(c)))
        {
            var grupo = Grupo(campo, ficha);
            // Os ___ que ficaram sem preencher saem do documento (cada grupo de linhas junto, para as frases que
            // continuam na linha de baixo; as linhas continuam as mesmas).
            if (tirarLacunas)
            {
                var junto = string.Join("\n", grupo.Select(c => finais[c]));
                if (junto.Contains("___"))
                {
                    var partes = Lacunas.Remover(junto).Split('\n');
                    for (var i = 0; i < grupo.Count; i++) finais[grupo[i]] = i < partes.Length ? partes[i] : "";
                }
            }
            // O negrito (**…**) que passa de uma linha para a outra é fechado e reaberto em cada linha.
            var balanceadas = Negrito.Balancear(grupo.Select(c => finais[c]));
            for (var i = 0; i < grupo.Count; i++) finais[grupo[i]] = balanceadas[i];
        }
        return finais;
    }

    /// <summary>A lacuna ___ (inteira, mesmo com mais traços) a partir de <paramref name="inicio"/>; null se não houver.</summary>
    public static (int Inicio, int Tamanho)? LacunaDepois(string texto, int inicio)
    {
        if (inicio > texto.Length) return null;
        var achado = texto.IndexOf("___", inicio, StringComparison.Ordinal);
        return achado < 0 ? null : Inteira(texto, achado);
    }

    /// <summary>A última lacuna ___ que termina até <paramref name="fim"/>.</summary>
    public static (int Inicio, int Tamanho)? LacunaAntes(string texto, int fim)
    {
        var achado = texto[..Math.Clamp(fim, 0, texto.Length)].LastIndexOf("___", StringComparison.Ordinal);
        return achado < 0 ? null : Inteira(texto, achado);
    }

    private static (int, int) Inteira(string texto, int inicio)
    {
        int a = inicio, b = inicio + 3;
        while (a > 0 && texto[a - 1] == '_') a--;
        while (b < texto.Length && texto[b] == '_') b++;
        return (a, b - a);
    }
}
