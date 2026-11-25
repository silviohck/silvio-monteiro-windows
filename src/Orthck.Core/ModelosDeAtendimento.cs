using System.Text.RegularExpressions;

namespace Orthck.Core;

/// <summary>
/// Modelos de atendimento do Prontuário (como no Mac): os 1.050 que vêm com o app (todos os CID ortopédicos, em
/// Tabelas/modelos_atendimento.txt) e os salvos pelo médico. Busca por doença ou CID e troca do [LADO].
/// </summary>
public static class ModelosDeAtendimento
{
    public static readonly string[] Lados = ["DIREITO", "ESQUERDO", "BILATERAL"];

    private static readonly Lazy<List<ModeloDeAtendimento>> _prontos = new(Carregar);
    /// <summary>Os modelos que vêm com o app.</summary>
    public static IReadOnlyList<ModeloDeAtendimento> Prontos => _prontos.Value;

    private static readonly Lazy<Dictionary<string, ModeloDeAtendimento>> _porChave = new(() =>
    {
        var mapa = new Dictionary<string, ModeloDeAtendimento>();
        foreach (var m in Prontos) mapa.TryAdd(Chave(m), m);
        return mapa;
    });
    public static IReadOnlyDictionary<string, ModeloDeAtendimento> PorChave => _porChave.Value;

    // Uma linha por modelo: grupo, doença, CID, diagnóstico, Q.P., H.D.A., exame físico, programação, genérico (1/0).
    private static List<ModeloDeAtendimento> Carregar()
    {
        using var stream = typeof(ModelosDeAtendimento).Assembly.GetManifestResourceStream("Orthck.Core.Tabelas.modelos_atendimento.txt")
            ?? throw new InvalidOperationException("Os modelos de atendimento não estão embutidos no aplicativo.");
        using var leitor = new StreamReader(stream);
        var lista = new List<ModeloDeAtendimento>();
        while (leitor.ReadLine() is { } linha)
        {
            var p = linha.Split('\t');
            if (p.Length != 9) continue;
            lista.Add(new ModeloDeAtendimento
            {
                Id = Guid.Empty, Grupo = p[0], Nome = p[1], Cid = p[2], Diagnostico = p[3], Qp = p[4], Hda = p[5],
                Exame = p[6], Conduta = p[7], Generico = p[8] == "1"
            });
        }
        return lista;
    }

    /// <summary>Identificação estável do modelo, para os favoritos (igual ao Mac: "CID|nome" ou "meu:ID").</summary>
    public static string Chave(ModeloDeAtendimento modelo) =>
        modelo.Meu ? $"meu:{modelo.Id.ToString().ToUpperInvariant()}" : $"{modelo.Cid}|{modelo.Nome}";

    public static string Simples(string texto) => Texto.Normalizar(texto);

    /// <summary>Busca pelo nome da doença, pelo diagnóstico ou pelo CID (com ou sem ponto). Primeiro os seus modelos,
    /// depois os escritos um a um e por último os modelos por tipo.</summary>
    /// <summary>CID, onde procurar e nome já sem acentos; os dos modelos do app ficam prontos uma vez só.</summary>
    private static (string Cid, string Onde, string Nome) TextoDeBusca(ModeloDeAtendimento modelo) =>
        _textosDosProntos.Value.TryGetValue(modelo, out var pronto) ? pronto : Preparar(modelo);

    private static (string Cid, string Onde, string Nome) Preparar(ModeloDeAtendimento modelo) =>
        (Simples(modelo.Cid).Replace(".", ""), Simples(modelo.Nome + " " + modelo.Diagnostico + " " + modelo.Cid), Simples(modelo.Nome));

    private static readonly Lazy<Dictionary<ModeloDeAtendimento, (string, string, string)>> _textosDosProntos =
        new(() => Prontos.ToDictionary(m => m, Preparar));

    public static List<ModeloDeAtendimento> Buscar(string termo, IEnumerable<ModeloDeAtendimento> meus, int limite = 12)
    {
        var texto = Simples(termo).Trim();
        if (texto.Length < 2) return [];
        var palavras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var codigo = texto.Replace(".", "");
        int? Nota(ModeloDeAtendimento modelo)
        {
            var (cid, onde, nome) = TextoDeBusca(modelo);
            int nota;
            if (cid == codigo) nota = 0;
            else if (codigo.Length > 1 && char.IsLetter(codigo[0]) && char.IsDigit(codigo[1]) && cid.StartsWith(codigo, StringComparison.Ordinal)) nota = 1;
            else if (palavras.All(p => onde.Contains(p, StringComparison.Ordinal))) nota = nome.StartsWith(texto, StringComparison.Ordinal) ? 2 : 3;
            else return null;
            if (modelo.Meu) nota -= 10; else if (modelo.Generico) nota += 5;
            return nota;
        }
        return meus.Concat(Prontos)
            .Select(m => (Modelo: m, Nota: Nota(m)))
            .Where(x => x.Nota != null)
            .OrderBy(x => x.Nota).ThenBy(x => x.Modelo.Nome, Texto.Ordem)
            .Take(limite).Select(x => x.Modelo).ToList();
    }

    // Partes do corpo, para o lado concordar: JOELHO DIREITO, MÃO DIREITA; outras palavras: À DIREITA.
    private static readonly HashSet<string> Masculinas =
    [
        "JOELHO", "JOELHOS", "OMBRO", "PÉ", "QUADRIL", "PUNHO", "TORNOZELO", "COTOVELO", "BRAÇO", "ANTEBRAÇO",
        "MEMBRO", "DEDO", "POLEGAR", "HÁLUX", "FÊMUR", "ÚMERO", "RÁDIO", "CALCÂNEO", "TÁLUS", "ESCAFOIDE",
        "CALCANHAR", "TENDÃO", "NERVO", "MALÉOLO", "METACARPO", "METATARSO", "OLÉCRANO", "COTO", "LADO",
        "PLEXO", "MANGUITO", "SEMILUNAR", "CARPO", "TARSO", "ANTEPÉ", "RETROPÉ", "MEDIOPÉ", "HEMITÓRAX",
        "HEMICORPO", "GLÚTEO", "TRAPÉZIO", "BÍCEPS", "TRÍCEPS", "QUADRÍCEPS", "MENISCO", "LIGAMENTO", "ESTERNOCLEIDOMASTOIDEO",
    ];
    private static readonly HashSet<string> Femininas =
    [
        "MÃO", "MÃOS", "PERNA", "COXA", "PATELA", "RÓTULA", "CLAVÍCULA", "ESCÁPULA", "TÍBIA", "FÍBULA", "ULNA",
        "NÁDEGA", "PANTURRILHA", "REGIÃO", "FACE", "ARTICULAÇÃO", "CABEÇA", "AXILA", "VIRILHA", "BORDA", "PLANTA",
        "SOLA", "PALMA", "FALANGE", "BACIA", "PELVE", "SACROILÍACA", "FÁSCIA", "BOLSA", "BAINHA", "COXOFEMORAL",
    ];

    public enum Genero { Masculino, Feminino, Nenhum }

    /// <summary>Gênero da expressão antes do [LADO]: "JOELHO" → masculino, "MÃO" → feminino; adjetivos como
    /// INFERIOR/MEDIAL olham a palavra anterior ("MEMBRO INFERIOR" → masculino); outros → nenhum (À DIREITA).</summary>
    public static Genero GeneroAntes(string texto)
    {
        var palavras = texto.Split([' ', ',', '(', '/'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var palavra in palavras.Reverse().Take(3))
        {
            if (Masculinas.Contains(palavra)) return Genero.Masculino;
            if (Femininas.Contains(palavra)) return Genero.Feminino;
            if (palavra.EndsWith('O') || palavra.EndsWith("OS")) return Genero.Masculino;
            if (palavra.EndsWith('A') || palavra.EndsWith("AS")) return Genero.Feminino;
            // Adjetivo sem gênero (INFERIOR, MEDIAL, PLANTAR…): decide a palavra antes dele.
            if (palavra.EndsWith('R') || palavra.EndsWith('L')) continue;
            return Genero.Nenhum;
        }
        return Genero.Nenhum;
    }

    /// <summary>Troca cada [LADO] pelo lado, concordando com o que vem antes: JOELHO DIREITO, MÃO DIREITA,
    /// GONARTROSE À DIREITA, POSITIVO À DIREITA / POSITIVO BILATERALMENTE.</summary>
    public static string ComLado(string texto, string lado)
    {
        var feminino = lado == "DIREITO" ? "DIREITA" : lado == "ESQUERDO" ? "ESQUERDA" : lado;
        var saida = new System.Text.StringBuilder();
        var resto = texto;
        int marca;
        while ((marca = resto.IndexOf("[LADO]", StringComparison.Ordinal)) >= 0)
        {
            var antes = resto[..marca];
            string palavra;
            if (antes.EndsWith(" À ") || antes == "À ")
            {
                if (lado == "BILATERAL")
                {
                    antes = antes[..^2];
                    // "POSITIVO À [LADO]" → "POSITIVO BILATERALMENTE"; "GONARTROSE À [LADO]" → "GONARTROSE BILATERAL".
                    palavra = GeneroAntes(antes) == Genero.Nenhum ? "BILATERAL" : "BILATERALMENTE";
                }
                else palavra = feminino;
            }
            else if (lado == "BILATERAL") palavra = lado;
            else palavra = GeneroAntes(antes) switch
            {
                Genero.Masculino => lado,
                Genero.Feminino => feminino,
                _ => "À " + feminino
            };
            saida.Append(antes).Append(palavra);
            resto = resto[(marca + "[LADO]".Length)..];
        }
        return saida.Append(resto).ToString();
    }

    /// <summary>O contrário de <see cref="ComLado"/>, para salvar um texto como modelo: o lado volta a ser [LADO].</summary>
    public static string SemLado(string texto, string lado)
    {
        // "À DIREITA" / "BILATERALMENTE" voltam a ser "À [LADO]" (o À decide a concordância de novo).
        var (primeiro, segundo) = lado switch
        {
            "DIREITO" => (@"\bÀ DIREITA\b", @"\bDIREIT[AO]\b"),
            "ESQUERDO" => (@"\bÀ ESQUERDA\b", @"\bESQUERD[AO]\b"),
            _ => (@"\bBILATERALMENTE\b", @"\bBILATERAL\b")
        };
        return Regex.Replace(Regex.Replace(texto, primeiro, "À [LADO]"), segundo, "[LADO]");
    }
}

/// <summary>Modelos salvos pelo médico e favoritos do Modelo de atendimento, separados por local (como no Mac).</summary>
public sealed partial class Armazenamento
{
    /// <summary>Guarda o modelo nos seus modelos; com o mesmo nome, substitui o anterior.</summary>
    public void SalvarModeloDeAtendimento(ModeloDeAtendimento modelo)
    {
        modelo.Meu = true;
        var indice = Dados.ModelosDeAtendimento.FindIndex(m => ModelosDeAtendimento.Simples(m.Nome) == ModelosDeAtendimento.Simples(modelo.Nome));
        if (indice >= 0)
        {
            modelo.Id = Dados.ModelosDeAtendimento[indice].Id;
            Dados.ModelosDeAtendimento[indice] = modelo;
        }
        else Dados.ModelosDeAtendimento.Add(modelo);
        Salvar();
    }

    public void ApagarModeloDeAtendimento(ModeloDeAtendimento modelo)
    {
        Dados.ModelosDeAtendimento.RemoveAll(m => m.Id == modelo.Id);
        var chave = ModelosDeAtendimento.Chave(modelo);
        foreach (var lista in Dados.FavoritosDeAtendimento.Values) lista.RemoveAll(c => c == chave);
        Salvar();
    }

    private List<string> FavoritosDeAtendimentoDo(LocalAtendimento? local) =>
        Dados.FavoritosDeAtendimento.TryGetValue(ChaveDosFavoritos(local?.Id), out var lista) ? lista : [];

    public List<ModeloDeAtendimento> FavoritosDeAtendimento(LocalAtendimento? local)
    {
        var meus = new Dictionary<string, ModeloDeAtendimento>();
        foreach (var m in Dados.ModelosDeAtendimento) meus.TryAdd(ModelosDeAtendimento.Chave(m), m);
        return FavoritosDeAtendimentoDo(local)
            .Select(c => meus.GetValueOrDefault(c) ?? ModelosDeAtendimento.PorChave.GetValueOrDefault(c))
            .OfType<ModeloDeAtendimento>().ToList();
    }

    public bool EhFavoritoDeAtendimento(ModeloDeAtendimento modelo, LocalAtendimento? local) =>
        FavoritosDeAtendimentoDo(local).Contains(ModelosDeAtendimento.Chave(modelo));

    public void AlternarFavoritoDeAtendimento(ModeloDeAtendimento modelo, LocalAtendimento? local)
    {
        var chave = ModelosDeAtendimento.Chave(modelo);
        var lista = FavoritosDeAtendimentoDo(local).ToList();
        if (!lista.Remove(chave)) lista.Add(chave);
        Dados.FavoritosDeAtendimento[ChaveDosFavoritos(local?.Id)] = lista;
        Salvar();
    }

    /// <summary>Põe o favorito antes de <paramref name="destino"/> (null = no fim).</summary>
    public void MoverFavoritoDeAtendimento(ModeloDeAtendimento modelo, ModeloDeAtendimento? destino, LocalAtendimento? local)
    {
        var chave = ModelosDeAtendimento.Chave(modelo);
        var lista = FavoritosDeAtendimentoDo(local).ToList();
        if (!lista.Remove(chave)) return;
        var alvo = destino == null ? -1 : lista.IndexOf(ModelosDeAtendimento.Chave(destino));
        lista.Insert(alvo < 0 ? lista.Count : alvo, chave);
        Dados.FavoritosDeAtendimento[ChaveDosFavoritos(local?.Id)] = lista;
        Salvar();
    }
}
