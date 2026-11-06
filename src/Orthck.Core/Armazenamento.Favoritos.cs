namespace Orthck.Core;

/// <summary>Favoritos de cada local de atendimento (atalhos na tela Gerar documento), como no Mac.</summary>
public sealed partial class Armazenamento
{
    /// <summary>Id do local como o Mac grava (uuidString em maiúsculas); "" = sem local.</summary>
    public static string ChaveDosFavoritos(Guid? local) => local?.ToString().ToUpperInvariant() ?? "";

    private List<string> ListaDeFavoritos(Guid? local) =>
        Dados.FavoritosPorLocal.TryGetValue(ChaveDosFavoritos(local), out var lista) ? lista : [];

    public bool EhFavorito(ModeloDocumento modelo, Guid? local) => ListaDeFavoritos(local).Any(n => Texto.MesmoNome(n, modelo.Nome));

    public void AlternarFavorito(ModeloDocumento modelo, Guid? local)
    {
        var lista = ListaDeFavoritos(local).ToList();
        if (lista.Any(n => Texto.MesmoNome(n, modelo.Nome))) lista.RemoveAll(n => Texto.MesmoNome(n, modelo.Nome));
        else lista.Add(modelo.Nome);
        if (lista.Count == 0) Dados.FavoritosPorLocal.Remove(ChaveDosFavoritos(local));
        else Dados.FavoritosPorLocal[ChaveDosFavoritos(local)] = lista;
        Salvar();
    }

    /// <summary>Os favoritos deste local que têm modelo nele, na ordem escolhida.</summary>
    public List<ModeloDocumento> FavoritosPara(LocalAtendimento? local)
    {
        var disponiveis = ModelosPara(local);
        return ListaDeFavoritos(local?.Id)
            .Select(nome => disponiveis.FirstOrDefault(m => Texto.MesmoNome(m.Nome, nome)))
            .OfType<ModeloDocumento>().ToList();
    }

    /// <summary>Muda a posição de um favorito: para antes de <paramref name="destino"/> (null = para o fim).
    /// Os favoritos sem modelo neste local continuam guardados, no fim.</summary>
    public void MoverFavorito(ModeloDocumento modelo, ModeloDocumento? destino, LocalAtendimento? local)
    {
        var visiveis = FavoritosPara(local).Select(m => m.Nome).ToList();
        var origem = visiveis.FindIndex(n => Texto.MesmoNome(n, modelo.Nome));
        if (origem < 0) return;
        var nome = visiveis[origem];
        visiveis.RemoveAt(origem);
        var posicao = destino == null ? -1 : visiveis.FindIndex(n => Texto.MesmoNome(n, destino.Nome));
        visiveis.Insert(posicao < 0 ? visiveis.Count : posicao, nome);
        var escondidos = ListaDeFavoritos(local?.Id).Where(n => !visiveis.Any(v => Texto.MesmoNome(v, n)));
        Dados.FavoritosPorLocal[ChaveDosFavoritos(local?.Id)] = [.. visiveis, .. escondidos];
        Salvar();
    }

    /// <summary>Favoritos de versões anteriores (lista única): cada local fica com os que tem modelo com esse nome.</summary>
    private bool SepararFavoritosPorLocal()
    {
        if (Dados.Favoritos.Count == 0) return false;
        foreach (var local in new LocalAtendimento?[] { null }.Concat(Dados.Locais))
        {
            var disponiveis = ModelosPara(local);
            var nomes = Dados.Favoritos.Where(n => disponiveis.Any(m => Texto.MesmoNome(m.Nome, n))).ToList();
            var chave = ChaveDosFavoritos(local?.Id);
            if (nomes.Count > 0 && !Dados.FavoritosPorLocal.ContainsKey(chave)) Dados.FavoritosPorLocal[chave] = nomes;
        }
        Dados.Favoritos = [];
        return true;
    }
}
