using System.Printing;
using System.Runtime.InteropServices;
using Orthck.Core;

namespace Orthck.Windows;

/// <summary>
/// Automação do Microsoft Word por COM: imprimir e converter .doc/.rtf/.odt para .docx.
/// Cada operação roda numa thread STA própria, numa instância própria do Word, e usa o documento aberto
/// (nunca o "documento ativo"). Uma sessão por vez: se o Word travar, nenhuma outra começa até ele terminar.
/// </summary>
public sealed class Word : IImpressoraWord, IConversorDocumentos
{
    private static readonly TimeSpan TempoLimite = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim UmaSessao = new(1, 1);

    public static bool Instalado => Type.GetTypeFromProgID("Word.Application") != null;

    public async Task<ResultadoImpressao> ImprimirAsync(string arquivo, string? impressora, int copias, bool manterAberto, CancellationToken ct = default)
    {
        if (!Instalado)
            return new ResultadoImpressao(DesfechoImpressao.Falhou, "Para imprimir pelo app é preciso ter o Microsoft Word (desktop) instalado.");
        if (!await UmaSessao.WaitAsync(0, ct))
            return new ResultadoImpressao(DesfechoImpressao.Incerto, "Outra impressão do Word ainda está em andamento ou sem resposta. Nada novo foi enviado.");
        var concluido = new TaskCompletionSource<ResultadoImpressao>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? wordObj = null, docObj = null;
            string? impressoraAnterior = null;
            var wordDoUsuario = false;
            var enviando = false;
            var resultado = new ResultadoImpressao(DesfechoImpressao.Incerto, "Não foi possível confirmar o término do envio.");
            try
            {
                ct.ThrowIfCancellationRequested();
                wordObj = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)
                    ?? throw new InvalidOperationException("Não foi possível iniciar o Microsoft Word.");
                dynamic word = wordObj;
                // Se o COM devolver o Word que o usuário já tem aberto, não escondê-lo nem fechá-lo.
                wordDoUsuario = word.Documents.Count > 0;
                if (!wordDoUsuario) { word.Visible = false; word.DisplayAlerts = 0; }
                // O documento precisa de uma janela ativa para trocar a impressora e imprimir ("uma janela de documento
                // não está ativa"); com o Word oculto, a janela também fica oculta.
                docObj = word.Documents.Open(arquivo, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: true);
                dynamic doc = docObj;
                try { doc.Activate(); } catch { }
                if (!string.IsNullOrWhiteSpace(impressora))
                {
                    try
                    {
                        // Escolhe a impressora só para este Word, sem mudar a padrão do Windows.
                        word.WordBasic.FilePrintSetup(Printer: impressora, DoNotSetAsSysDefault: 1);
                    }
                    catch
                    {
                        // Plano B: ActivePrinter muda a padrão do Windows, que é restaurada no fim.
                        impressoraAnterior = word.ActivePrinter;
                        word.ActivePrinter = impressora;
                    }
                }
                ct.ThrowIfCancellationRequested();
                enviando = true;
                // Background:false espera o Word terminar de mandar ao spooler (não a impressão física).
                doc.PrintOut(Background: false, Copies: Math.Clamp(copias, 1, 20));
                resultado = new ResultadoImpressao(DesfechoImpressao.Enviado, "Comando enviado ao Word.");
            }
            catch (OperationCanceledException)
            {
                resultado = new ResultadoImpressao(DesfechoImpressao.Incerto, "Envio interrompido. Confira a impressora antes de reenviar.");
            }
            catch (Exception e)
            {
                // Antes do comando de imprimir, nada foi enviado (erro). Durante ele, não dá para garantir (incerto).
                resultado = enviando
                    ? new ResultadoImpressao(DesfechoImpressao.Incerto, $"O Word deu erro durante o envio. Confira a impressora antes de reenviar. {e.Message}")
                    : new ResultadoImpressao(DesfechoImpressao.Falhou, $"O Word não conseguiu preparar a impressão; nada foi enviado. {e.Message}");
            }
            finally
            {
                Encerrar(wordObj, docObj, wordDoUsuario, impressoraAnterior);
                concluido.TrySetResult(resultado);
            }
        }) { IsBackground = true, Name = "Orthck-Word-Imprimir" };
        thread.SetApartmentState(ApartmentState.STA);
        try { thread.Start(); }
        catch { UmaSessao.Release(); throw; }
        // Mesmo depois do tempo limite, a sessão só é liberada quando o Word realmente termina.
        _ = concluido.Task.ContinueWith(_ => UmaSessao.Release(), TaskScheduler.Default);
        if (await Task.WhenAny(concluido.Task, Task.Delay(TempoLimite, CancellationToken.None)) == concluido.Task) return await concluido.Task;
        return new ResultadoImpressao(DesfechoImpressao.Incerto,
            "O Word não respondeu em 5 minutos (pode haver uma janela do Word aguardando resposta). Confira a impressora antes de reenviar.");
    }

    /// <summary>Converte pelo Word (Abrir + Salvar como .docx). Chamado fora da thread da interface.</summary>
    public void ConverterParaDocx(string origem, string destino)
    {
        if (!Instalado) throw new InvalidDataException("Para usar arquivos .doc, .rtf ou .odt como modelo é preciso ter o Microsoft Word instalado. Ou salve o arquivo como .docx no Word.");
        if (!UmaSessao.Wait(TempoLimite)) throw new TimeoutException("O Word está ocupado com outra tarefa.");
        Exception? erro = null;
        var thread = new Thread(() =>
        {
            object? wordObj = null, docObj = null;
            var wordDoUsuario = false;
            try
            {
                wordObj = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!);
                dynamic word = wordObj!;
                wordDoUsuario = word.Documents.Count > 0;
                if (!wordDoUsuario) { word.Visible = false; word.DisplayAlerts = 0; }
                docObj = word.Documents.Open(origem, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                dynamic doc = docObj;
                doc.SaveAs2(destino, FileFormat: 16); // wdFormatXMLDocument (.docx)
            }
            catch (Exception e) { erro = e; }
            finally { Encerrar(wordObj, docObj, wordDoUsuario, null); }
        }) { IsBackground = true, Name = "Orthck-Word-Converter" };
        thread.SetApartmentState(ApartmentState.STA);
        try
        {
            thread.Start();
            if (!thread.Join(TempoLimite)) throw new TimeoutException("O Word não respondeu ao converter o arquivo.");
        }
        finally { if (!thread.IsAlive) UmaSessao.Release(); else _ = Task.Run(() => { thread.Join(); UmaSessao.Release(); }); }
        if (erro != null) throw new InvalidDataException($"O Word não conseguiu converter “{Path.GetFileName(origem)}”. {erro.Message}", erro);
    }

    private static void Encerrar(object? wordObj, object? docObj, bool wordDoUsuario, string? impressoraAnterior)
    {
        try { if (docObj != null) { dynamic d = docObj; d.Close(SaveChanges: false); } } catch { }
        try { if (wordObj != null && impressoraAnterior != null) { dynamic w = wordObj; w.ActivePrinter = impressoraAnterior; } } catch { }
        try { if (wordObj != null && !wordDoUsuario) { dynamic w = wordObj; w.Quit(SaveChanges: false); } } catch { }
        try { if (docObj != null && Marshal.IsComObject(docObj)) Marshal.FinalReleaseComObject(docObj); } catch { }
        try { if (wordObj != null && Marshal.IsComObject(wordObj)) Marshal.FinalReleaseComObject(wordObj); } catch { }
    }
}

/// <summary>Impressoras instaladas no Windows.</summary>
public static class Impressoras
{
    public static List<string> Instaladas()
    {
        try
        {
            using var servidor = new LocalPrintServer();
            return servidor.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
                .Select(f => f.FullName).Distinct().OrderBy(n => n, Texto.Ordem).ToList();
        }
        catch { return []; }
    }

    public static string Padrao()
    {
        try { return LocalPrintServer.GetDefaultPrintQueue().FullName; } catch { return ""; }
    }

    /// <summary>Abre Configurações › Bluetooth e dispositivos › Impressoras e scanners.</summary>
    public static void AbrirConfiguracoes() => Ui.Executar(() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:printers") { UseShellExecute = true }));
}

/// <summary>Atalhos (.lnk) do Windows, equivalentes aos atalhos (alias) do Finder.</summary>
public static class Atalhos
{
    public static string? Resolver(string lnk)
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic s = shell!;
            dynamic atalho = s.CreateShortcut(lnk);
            string destino = atalho.TargetPath;
            Marshal.FinalReleaseComObject(atalho);
            return string.IsNullOrWhiteSpace(destino) ? null : destino;
        }
        catch { return null; }
        finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
    }
}
