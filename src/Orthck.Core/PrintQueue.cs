namespace Orthck.Core;

public enum PrintOutcome { Submitted, Failed, Uncertain }
public sealed record PrintResult(PrintOutcome Outcome, string Detail);
public interface IWordPrinter
{
    Task<PrintResult> SubmitAsync(string documentPath, int copies, string? printerName, CancellationToken ct = default);
}

/// <summary>Serializa envios. Falha/interrupção nunca dispara reimpressão automática.</summary>
public sealed class PrintQueue
{
    private readonly Storage _store;
    private readonly IWordPrinter _printer;
    private readonly SemaphoreSlim _singleJob = new(1, 1);
    public bool IsProcessing { get; private set; }
    public event Action? Changed;

    public PrintQueue(Storage store, IWordPrinter printer) { _store = store; _printer = printer; }

    public PrintItem Add(GeneratedDocument document, int copies)
    {
        var existing = _store.Data.FilaImpressao.FirstOrDefault(x => x.DocumentoID == document.Id);
        if (existing != null) return existing;
        var item = PrintItem.From(document, copies);
        _store.Data.FilaImpressao.Add(item);
        Persist();
        return item;
    }
    public void Remove(Guid id)
    {
        if (IsProcessing) return;
        _store.Data.FilaImpressao.RemoveAll(x => x.Id == id);
        Persist();
    }
    public void ClearSent()
    {
        if (IsProcessing) return;
        _store.Data.FilaImpressao.RemoveAll(x => x.Situacao == "enviado");
        Persist();
    }
    public bool ConfirmRetry(Guid id)
    {
        if (IsProcessing) return false;
        var item = _store.Data.FilaImpressao.FirstOrDefault(x => x.Id == id);
        if (item == null || item.Situacao is not ("erro" or "incerto")) return false;
        item.Situacao = "pendente";
        item.Observacao = null;
        Persist();
        return true;
    }
    public void SetCopies(Guid id, int copies)
    {
        if (IsProcessing) return;
        var item = _store.Data.FilaImpressao.FirstOrDefault(x => x.Id == id);
        if (item == null || item.Situacao != "pendente") return;
        item.Copias = Math.Clamp(copies, 1, 20);
        Persist();
    }

    public async Task SubmitAsync(IEnumerable<Guid> ids, string? printerName, CancellationToken ct = default)
    {
        if (!await _singleJob.WaitAsync(0, ct)) return;
        IsProcessing = true;
        Changed?.Invoke();
        try
        {
            // Fotografia da fila no momento do início; novos arquivos aguardam próxima rodada.
            var selected = ids.ToHashSet();
            var pending = _store.Data.FilaImpressao.Where(x => selected.Contains(x.Id) && x.Situacao == "pendente").ToArray();
            foreach (var item in pending)
            {
                if (ct.IsCancellationRequested) break;
                if (!File.Exists(item.Caminho))
                {
                    item.Situacao = "erro";
                    item.Observacao = "Arquivo não encontrado. Reconfigure o caminho e confira no Finder/Explorador.";
                    Persist();
                    continue;
                }
                item.Situacao = "enviando";
                item.Observacao = null;
                Persist();
                PrintResult result;
                try { result = await _printer.SubmitAsync(item.Caminho, item.Copias, printerName, ct); }
                catch (Exception ex) { result = new PrintResult(PrintOutcome.Uncertain, ex.Message); }
                item.Situacao = result.Outcome switch
                {
                    PrintOutcome.Submitted => "enviado",
                    PrintOutcome.Failed => "erro",
                    _ => "incerto"
                };
                item.Observacao = result.Detail;
                Persist();
            }
        }
        finally
        {
            IsProcessing = false;
            Changed?.Invoke();
            _singleJob.Release();
        }
    }
    private void Persist() { _store.Save(); Changed?.Invoke(); }
}
