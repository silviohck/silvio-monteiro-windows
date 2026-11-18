namespace Orthck.Core;

/// <summary>Fichas do Prontuário: modelos .docx próprios da aba Prontuário (separados dos modelos de documentos).</summary>
public sealed partial class Armazenamento
{
    /// <summary>Uma ficha que vem com o app (embutida, igual à do Mac) e o local a que ela se liga (pelo nome do local).
    /// Para mudar uma ficha: trocar o .docx e o nome do arquivo (nova versão), para o app regravar.</summary>
    public sealed record FichaPronta(Guid Id, string Nome, string Arquivo, string Local);

    public static readonly FichaPronta[] FichasProntas =
    [
        // Avaliação ortopédica do CER II de Araruna, igual à ficha em papel; H.D.A., exame físico e programação têm uma
        // linha por campo ({{HDA}}, {{HDA_LINHA_2}}…).
        new(Guid.Parse("5F0C8A2E-3B1D-4C6E-9A7F-0A1B2C3D4E5F"), "Avaliação ortopédica – Araruna (CER II)", "ficha-araruna-cer2-v4.docx", "araruna"),
        // Prontuário médico de Alagoa Grande: nome, endereço e CNS do cadastro e 19 linhas de texto livre.
        new(Guid.Parse("8B2E4F60-1C3D-4A5B-9E7F-2D4C6A8B0E1F"), "Prontuário médico – Alagoa Grande", "ficha-alagoa-grande-v2.docx", "alagoa grande"),
    ];

    public static bool EhFichaPronta(ModeloDocumento ficha) => FichasProntas.Any(f => f.Id == ficha.Id);

    /// <summary>Fichas deste local mais as de todos os locais, em ordem alfabética.</summary>
    public List<ModeloDocumento> FichasPara(LocalAtendimento? local) =>
        Dados.Fichas.Where(f => f.LocalId == null || f.LocalId == local?.Id).OrderBy(f => f.Nome, Texto.Ordem).ToList();

    /// <summary>Grava as fichas que vêm com o app (se faltarem) e liga cada uma ao local de mesmo nome
    /// (Araruna, Alagoa Grande). Devolve true se mudou algo nos dados.</summary>
    public bool InstalarFichasProntas()
    {
        var mudou = false;
        foreach (var pronta in FichasProntas) mudou |= Instalar(pronta);
        return mudou;
    }

    private bool Instalar(FichaPronta pronta)
    {
        var caminho = Path.Combine(PastaModelos, pronta.Arquivo);
        if (!File.Exists(caminho))
        {
            try
            {
                using var origem = typeof(Armazenamento).Assembly.GetManifestResourceStream($"Orthck.Core.Fichas.{pronta.Arquivo}");
                if (origem == null) return false;
                var tmp = caminho + ".tmp";
                using (var destino = File.Create(tmp)) origem.CopyTo(destino);
                File.Move(tmp, caminho, overwrite: true);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        List<string> campos;
        try { campos = [.. DocxTemplate.Placeholders(caminho)]; } catch { campos = []; }
        var localDaFicha = Dados.Locais.FirstOrDefault(l => Texto.Contem(l.Nome, pronta.Local));
        var ficha = Dados.Fichas.FirstOrDefault(f => f.Id == pronta.Id);
        if (ficha != null)
        {
            var mudou = false;
            // Versão nova da ficha embutida: passa a usar o arquivo novo.
            if (ficha.Arquivo != pronta.Arquivo)
            {
                var antigo = UrlDoModelo(ficha);
                ficha.Arquivo = pronta.Arquivo;
                ficha.Campos = campos;
                try { File.Delete(antigo); } catch { }
                mudou = true;
            }
            if (ficha.LocalId == null && localDaFicha != null) { ficha.LocalId = localDaFicha.Id; mudou = true; }
            return mudou;
        }
        Dados.Fichas.Add(new ModeloDocumento
        {
            Id = pronta.Id, Nome = pronta.Nome, Arquivo = pronta.Arquivo, Campos = campos, Categoria = "Prontuário", LocalId = localDaFicha?.Id
        });
        return true;
    }

    /// <summary>Acrescenta uma ficha (.docx com {{CAMPOS}}) do local escolhido (null = todos os locais).</summary>
    public ModeloDocumento ImportarFicha(string origem, Guid? localId)
    {
        var arquivo = $"{Guid.NewGuid().ToString().ToUpperInvariant()}.docx";
        var campos = CopiarModelo(origem, Path.Combine(PastaModelos, arquivo));
        var ficha = new ModeloDocumento
        {
            Nome = Path.GetFileNameWithoutExtension(origem), Arquivo = arquivo, Campos = campos, Categoria = "Prontuário", LocalId = localId
        };
        Dados.Fichas.Add(ficha);
        Salvar();
        return ficha;
    }

    /// <summary>As fichas que vêm com o app não são apagadas (voltariam ao abrir).</summary>
    public void RemoverFicha(ModeloDocumento ficha)
    {
        if (EhFichaPronta(ficha)) return;
        try { File.Delete(UrlDoModelo(ficha)); } catch { }
        Dados.Fichas.RemoveAll(f => f.Id == ficha.Id);
        Salvar();
    }
}
