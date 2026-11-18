namespace Orthck.Core;

/// <summary>Pacientes atendidos (pelo Histórico), para o painel lateral da tela Gerar documento (como no Mac).</summary>
public sealed partial class Armazenamento
{
    /// <summary>Dias em que houve documento gerado, do mais recente para o mais antigo.</summary>
    public List<DateTime> DiasComAtendimento => DiasComAtendimentoDesde(null);

    /// <summary>Dias com documento gerado depois de <paramref name="desde"/> (quando a lista foi limpa).</summary>
    public List<DateTime> DiasComAtendimentoDesde(DateTimeOffset? desde) => Dados.Historico
        .Where(d => desde == null || d.Data > desde).Select(d => d.Data.LocalDateTime.Date).Distinct().OrderByDescending(d => d).ToList();

    /// <summary>Os pacientes atendidos mais recentes, com o último documento de cada um.
    /// <paramref name="local"/> null = todos os locais; <paramref name="dia"/> = só quem teve documento nesse dia.
    /// <paramref name="desde"/>: só documentos depois que a lista foi limpa; <paramref name="ocultos"/>: paciente tirado da
    /// lista até essa data (volta a aparecer se tiver documento novo). Limpar a lista não apaga o Histórico.</summary>
    public List<(Paciente Paciente, DocumentoGerado Ultimo)> PacientesRecentes(string? local, DateTime? dia, int limite,
        DateTimeOffset? desde = null, IReadOnlyDictionary<Guid, DateTimeOffset>? ocultos = null)
    {
        IEnumerable<DocumentoGerado> documentos = Dados.Historico;
        if (desde != null) documentos = documentos.Where(d => d.Data > desde);
        if (local != null) documentos = documentos.Where(d => d.Local != null && Texto.MesmoNome(d.Local, local));
        if (dia is { } umDia) documentos = documentos.Where(d => d.Data.LocalDateTime.Date == umDia.Date);
        var vistos = new HashSet<Guid>();
        var lista = new List<(Paciente, DocumentoGerado)>();
        foreach (var documento in documentos.OrderByDescending(d => d.Data))
        {
            var paciente = Paciente(documento.PacienteId) ?? Dados.Pacientes.FirstOrDefault(p => Texto.Iguais(p.Nome, documento.Paciente));
            if (paciente == null || !vistos.Add(paciente.Id)) continue;
            if (ocultos != null && ocultos.TryGetValue(paciente.Id, out var ate) && documento.Data <= ate) continue;
            lista.Add((paciente, documento));
            if (lista.Count == limite) break;
        }
        return lista;
    }

    /// <summary>Documentos gerados para o paciente, do mais recente para o mais antigo.</summary>
    public List<DocumentoGerado> DocumentosDe(Paciente paciente) => Dados.Historico
        .Where(d => d.PacienteId is Guid id ? id == paciente.Id : Texto.Iguais(d.Paciente, paciente.Nome))
        .OrderByDescending(d => d.Data).ToList();
}
