namespace Orthck.Core;

public enum DesfechoImpressao { Enviado, Falhou, Incerto }
public sealed record ResultadoImpressao(DesfechoImpressao Desfecho, string Detalhe);

/// <summary>Imprime um arquivo pelo Microsoft Word.</summary>
public interface IImpressoraWord
{
    /// <param name="impressora">Nome da impressora; null = padrão do Windows.</param>
    /// <param name="manterAberto">Deixa o documento aberto no Word depois de imprimir.</param>
    Task<ResultadoImpressao> ImprimirAsync(string arquivo, string? impressora, int copias, bool manterAberto, CancellationToken ct = default);
}

/// <summary>Fila de impressão: persistente, um documento por vez, nunca reenvia sem confirmação.</summary>
public sealed partial class Armazenamento
{
    /// <summary>Bloqueio global: não dispara duas sequências de impressão ao mesmo tempo.</summary>
    public bool ProcessandoFila { get; private set; }

    public int PendentesNaFila => Dados.FilaImpressao.Count(x => x.Situacao == SituacaoImpressao.Pendente);

    /// <summary>
    /// Guarda uma referência ao documento, nunca uma cópia do arquivo. Se ele já está aguardando na fila, devolve esse item;
    /// se já foi impresso, entra de novo (ex.: pelo Histórico).
    /// </summary>
    public ItemFilaImpressao AdicionarAFila(DocumentoGerado documento, int copias)
    {
        var existente = Dados.FilaImpressao.FirstOrDefault(x => x.DocumentoId == documento.Id && x.Situacao == SituacaoImpressao.Pendente);
        if (existente != null) return existente;
        var item = ItemFilaImpressao.De(documento, copias);
        Dados.FilaImpressao.Add(item);
        Salvar();
        return item;
    }

    /// <summary>Remover da fila não apaga o documento nem o histórico.</summary>
    public void RemoverDaFila(Guid id)
    {
        if (ProcessandoFila) return;
        Dados.FilaImpressao.RemoveAll(x => x.Id == id);
        Salvar();
    }

    public void LimparEnviadosDaFila()
    {
        if (ProcessandoFila) return;
        Dados.FilaImpressao.RemoveAll(x => x.Situacao == SituacaoImpressao.Enviado);
        Salvar();
    }

    public void DefinirCopias(int copias, Guid id)
    {
        var item = Dados.FilaImpressao.FirstOrDefault(x => x.Id == id);
        if (ProcessandoFila || item == null || item.Situacao != SituacaoImpressao.Pendente) return;
        item.Copias = Math.Clamp(copias, 1, 20);
        Salvar();
    }

    /// <summary>Reenvio só depois de confirmação explícita na interface: pode causar impressão duplicada.</summary>
    public bool Reenfileirar(Guid id)
    {
        var item = Dados.FilaImpressao.FirstOrDefault(x => x.Id == id);
        if (ProcessandoFila || item == null || item.Situacao is not (SituacaoImpressao.Erro or SituacaoImpressao.Incerto)) return false;
        item.Situacao = SituacaoImpressao.Pendente;
        item.Observacao = null;
        Salvar();
        return true;
    }

    /// <summary>
    /// Envia os arquivos um por vez ao Word, na ordem da fila. "Enviado" significa comando aceito pelo Word, não papel impresso.
    /// Um envio incerto (Word sem resposta, app interrompido) para o restante do lote: os demais continuam aguardando.
    /// </summary>
    /// <returns>false quando outra rodada já estava imprimindo (nada foi enviado).</returns>
    public async Task<bool> ImprimirFilaAsync(IEnumerable<Guid> ids, string? impressora, IImpressoraWord word, bool manterAberto = false, CancellationToken ct = default)
    {
        if (ProcessandoFila) return false;
        ProcessandoFila = true;
        Mudou?.Invoke();
        try
        {
            // Fotografia da ordem no momento do clique; itens novos aguardam a próxima rodada.
            var escolhidos = ids.ToHashSet();
            var ordem = Dados.FilaImpressao.Where(x => escolhidos.Contains(x.Id) && x.Situacao == SituacaoImpressao.Pendente).Select(x => x.Id).ToList();
            foreach (var id in ordem)
            {
                if (ct.IsCancellationRequested) break;
                var item = Dados.FilaImpressao.FirstOrDefault(x => x.Id == id);
                if (item == null || item.Situacao != SituacaoImpressao.Pendente) continue;
                if (!File.Exists(item.Caminho))
                {
                    item.Situacao = SituacaoImpressao.Erro;
                    item.Observacao = "Arquivo não encontrado. Confira a pasta antes de tentar novamente.";
                    Salvar();
                    continue;
                }
                item.Situacao = SituacaoImpressao.Enviando;
                item.Observacao = null;
                Salvar();
                ResultadoImpressao resultado;
                try { resultado = await word.ImprimirAsync(item.Caminho, impressora, item.Copias, manterAberto, ct); }
                catch (Exception ex) { resultado = new ResultadoImpressao(DesfechoImpressao.Incerto, ex.Message); }
                item.Situacao = resultado.Desfecho switch
                {
                    DesfechoImpressao.Enviado => SituacaoImpressao.Enviado,
                    DesfechoImpressao.Falhou => SituacaoImpressao.Erro,
                    _ => SituacaoImpressao.Incerto
                };
                item.Observacao = resultado.Desfecho == DesfechoImpressao.Enviado
                    ? "Comando enviado ao Word. Confira o resultado na impressora."
                    : resultado.Detalhe;
                Salvar();
                if (resultado.Desfecho == DesfechoImpressao.Incerto) break;
            }
            return true;
        }
        finally
        {
            ProcessandoFila = false;
            Mudou?.Invoke();
        }
    }
}
