namespace Orthck.Core;

/// <summary>Pacientes atendidos (pelo Histórico), para o painel lateral da tela Gerar documento (como no Mac).</summary>
public sealed partial class Armazenamento
{
    /// <summary>Dias em que houve documento gerado, do mais recente para o mais antigo.</summary>
    public List<DateTime> DiasComAtendimento =>
        Dados.Historico.Select(d => d.Data.LocalDateTime.Date).Distinct().OrderByDescending(d => d).ToList();

    /// <summary>Os pacientes atendidos mais recentes, com o último documento de cada um.
    /// <paramref name="local"/> null = todos os locais; <paramref name="dia"/> = só quem teve documento nesse dia.</summary>
    public List<(Paciente Paciente, DocumentoGerado Ultimo)> PacientesRecentes(string? local, DateTime? dia, int limite)
    {
        IEnumerable<DocumentoGerado> documentos = Dados.Historico;
        if (local != null) documentos = documentos.Where(d => d.Local != null && Texto.MesmoNome(d.Local, local));
        if (dia is { } umDia) documentos = documentos.Where(d => d.Data.LocalDateTime.Date == umDia.Date);
        var vistos = new HashSet<Guid>();
        var lista = new List<(Paciente, DocumentoGerado)>();
        foreach (var documento in documentos.OrderByDescending(d => d.Data))
        {
            var paciente = Paciente(documento.PacienteId) ?? Dados.Pacientes.FirstOrDefault(p => Texto.Iguais(p.Nome, documento.Paciente));
            if (paciente == null || !vistos.Add(paciente.Id)) continue;
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
