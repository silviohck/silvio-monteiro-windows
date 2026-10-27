using System.Runtime.InteropServices;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>
/// Cada impressão roda em STA, numa instância própria do Word. PrintOut é chamado
/// no objeto Document específico, NUNCA em ActiveDocument.
/// </summary>
public sealed class OfficeWordPrinter : IWordPrinter
{
    /// <summary>Um diálogo invisível do Word (modo protegido, macro, conversão) travaria a fila para sempre.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public async Task<PrintResult> SubmitAsync(string documentPath, int copies, string? printerName, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<PrintResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? wordObject = null, docObject = null;
            string? previousPrinter = null;
            var sharedWord = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                var wordType = Type.GetTypeFromProgID("Word.Application")
                    ?? throw new InvalidOperationException("Microsoft Word não está instalado neste Windows.");
                wordObject = Activator.CreateInstance(wordType)
                    ?? throw new InvalidOperationException("Não foi possível iniciar o Microsoft Word.");
                dynamic word = wordObject;
                // Se o COM devolver o Word que o usuário já tem aberto, não escondê-lo nem fechá-lo.
                sharedWord = word.Documents.Count > 0;
                if (!sharedWord)
                {
                    word.Visible = false;
                    word.DisplayAlerts = 0;
                }
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    // Word.ActivePrinter altera a impressora padrão do Windows: restaurada no finally.
                    previousPrinter = word.ActivePrinter;
                    word.ActivePrinter = printerName;
                }
                docObject = word.Documents.Open(documentPath, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                dynamic doc = docObject;
                ct.ThrowIfCancellationRequested();
                // Background:false espera o Word concluir o envio ao spooler, não a impressão física.
                doc.PrintOut(Background: false, Copies: Math.Clamp(copies, 1, 20));
                completion.TrySetResult(new PrintResult(PrintOutcome.Submitted, "Comando enviado ao Word. Confira a saída da impressora."));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(new PrintResult(PrintOutcome.Uncertain, "Operação interrompida. Confira a impressora antes de reenviar."));
            }
            catch (Exception e)
            {
                // Erro após iniciar o Word/abrir arquivo pode não garantir que nada foi enviado.
                var outcome = docObject is null ? PrintOutcome.Failed : PrintOutcome.Uncertain;
                completion.TrySetResult(new PrintResult(outcome, $"Não foi possível confirmar a impressão: {e.Message}"));
            }
            finally
            {
                try { if (docObject != null) { dynamic d = docObject; d.Close(SaveChanges: false); } } catch { /* Evitar bloquear a fila */ }
                try { if (wordObject != null && previousPrinter != null) { dynamic w = wordObject; w.ActivePrinter = previousPrinter; } } catch { }
                try { if (wordObject != null && !sharedWord) { dynamic w = wordObject; w.Quit(SaveChanges: false); } } catch { }
                try { if (docObject != null && Marshal.IsComObject(docObject)) Marshal.FinalReleaseComObject(docObject); } catch { }
                try { if (wordObject != null && Marshal.IsComObject(wordObject)) Marshal.FinalReleaseComObject(wordObject); } catch { }
            }
        }) { IsBackground = true, Name = "Orthck-Word-Printer" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (await Task.WhenAny(completion.Task, Task.Delay(Timeout, CancellationToken.None)) == completion.Task)
            return await completion.Task;
        return new PrintResult(PrintOutcome.Uncertain,
            "O Word não respondeu em 5 minutos (pode haver uma janela do Word aguardando resposta). Confira a impressora antes de reenviar.");
    }
}
