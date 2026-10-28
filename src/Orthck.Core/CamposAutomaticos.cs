using System.Globalization;
using System.Text;

namespace Orthck.Core;

/// <summary>Campos do modelo preenchidos sozinhos a partir do paciente, do local ou da data (igual ao Mac).</summary>
public static class CamposAutomaticos
{
    public enum Tipo { Paciente, Local, Idade, DataHoje, DataExtenso }
    public sealed record Origem(Tipo Tipo, Func<Paciente, string>? DoPaciente = null, Func<LocalAtendimento, string>? DoLocal = null,
        Action<Paciente, string>? GuardarNoPaciente = null, bool EhNome = false);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly IReadOnlyDictionary<string, Origem> Mapa = CriarMapa();

    private static Dictionary<string, Origem> CriarMapa()
    {
        var mapa = new Dictionary<string, Origem>();
        void Registrar(string[] chaves, Origem origem) { foreach (var c in chaves) mapa[c] = origem; }
        Registrar(["NOME", "PACIENTE", "NOME_PACIENTE", "NOME_DO_PACIENTE", "NOME_COMPLETO"], new(Tipo.Paciente, p => p.Nome, EhNome: true));
        Registrar(["CPF", "CPF_PACIENTE", "CPF_DO_PACIENTE"], new(Tipo.Paciente, p => p.Cpf, GuardarNoPaciente: (p, v) => p.Cpf = v));
        Registrar(["RG", "RG_PACIENTE", "RG_DO_PACIENTE"], new(Tipo.Paciente, p => p.Rg, GuardarNoPaciente: (p, v) => p.Rg = v));
        Registrar(["DATA_NASCIMENTO", "DATA_DE_NASCIMENTO", "NASCIMENTO", "DN"], new(Tipo.Paciente, p => p.DataNascimento, GuardarNoPaciente: (p, v) => p.DataNascimento = v));
        Registrar(["TELEFONE", "CELULAR", "FONE", "WHATSAPP"], new(Tipo.Paciente, p => p.Telefone, GuardarNoPaciente: (p, v) => p.Telefone = v));
        Registrar(["EMAIL", "E_MAIL"], new(Tipo.Paciente, p => p.Email, GuardarNoPaciente: (p, v) => p.Email = v));
        Registrar(["ENDERECO", "ENDERECO_PACIENTE"], new(Tipo.Paciente, p => p.Endereco, GuardarNoPaciente: (p, v) => p.Endereco = v));
        Registrar(["CONVENIO", "PLANO", "PLANO_DE_SAUDE"], new(Tipo.Paciente, p => p.Convenio, GuardarNoPaciente: (p, v) => p.Convenio = v));
        Registrar(["LOCAL", "LOCAL_ATENDIMENTO", "LOCAL_DE_ATENDIMENTO", "NOME_LOCAL", "CLINICA", "NOME_CLINICA"], new(Tipo.Local, DoLocal: l => l.Nome));
        Registrar(["LOCAL_ENDERECO", "ENDERECO_LOCAL", "ENDERECO_CLINICA"], new(Tipo.Local, DoLocal: l => l.Endereco));
        Registrar(["CIDADE", "LOCAL_CIDADE", "CIDADE_LOCAL"], new(Tipo.Local, DoLocal: l => l.Cidade));
        Registrar(["LOCAL_TELEFONE", "TELEFONE_LOCAL", "TELEFONE_CLINICA"], new(Tipo.Local, DoLocal: l => l.Telefone));
        Registrar(["LOCAL_EMAIL", "EMAIL_LOCAL", "EMAIL_CLINICA"], new(Tipo.Local, DoLocal: l => l.Email));
        Registrar(["CNPJ", "LOCAL_CNPJ", "CNPJ_LOCAL", "CNPJ_CLINICA"], new(Tipo.Local, DoLocal: l => l.Cnpj));
        Registrar(["IDADE"], new(Tipo.Idade));
        Registrar(["DATA", "HOJE", "DATA_HOJE", "DATA_ATUAL"], new(Tipo.DataHoje));
        Registrar(["DATA_EXTENSO", "DATA_POR_EXTENSO"], new(Tipo.DataExtenso));
        return mapa;
    }

    /// <summary>"Data de Nascimento" → "DATA_DE_NASCIMENTO" (sem acentos, maiúsculas, separadores viram "_").</summary>
    public static string Chave(string campo)
    {
        var sb = new StringBuilder();
        var separador = false;
        foreach (var c in campo.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToUpperInvariant(c)); separador = false; }
            else if (!separador && sb.Length > 0) { sb.Append('_'); separador = true; }
        }
        return sb.ToString().TrimEnd('_');
    }

    public static Origem? OrigemDe(string campo) => Mapa.GetValueOrDefault(Chave(campo));
    public static bool EhAutomatico(string campo) => OrigemDe(campo) != null;
    /// <summary>Campos que vêm do nome digitado na caixa "Nome do paciente".</summary>
    public static bool EhNomeDoPaciente(string campo) => OrigemDe(campo)?.EhNome == true;
    public static bool EhDoLocal(string campo) => OrigemDe(campo)?.Tipo == Tipo.Local;

    /// <summary>Valor automático do campo; null quando o campo é digitado na hora (CID, dias…).</summary>
    public static string? Valor(string campo, Paciente? paciente, LocalAtendimento? local, DateTimeOffset? data = null)
    {
        var quando = data ?? DateTimeOffset.Now;
        return OrigemDe(campo) switch
        {
            { Tipo: Tipo.Paciente } o => paciente == null ? null : o.DoPaciente!(paciente),
            { Tipo: Tipo.Local } o => local == null ? null : o.DoLocal!(local),
            { Tipo: Tipo.Idade } => paciente == null ? null : Idade(paciente.DataNascimento, quando)?.ToString(PtBr),
            { Tipo: Tipo.DataHoje } => quando.ToString("dd/MM/yyyy", PtBr),
            { Tipo: Tipo.DataExtenso } => quando.ToString("d 'de' MMMM 'de' yyyy", PtBr),
            _ => null
        };
    }

    /// <summary>Guarda no cadastro o que foi digitado nos campos gerais (CPF, telefone…). CID e outros campos do modelo não.</summary>
    public static void Guardar(IReadOnlyDictionary<string, string> valores, IEnumerable<string> campos, Paciente paciente)
    {
        foreach (var campo in campos)
        {
            var texto = valores.GetValueOrDefault(campo, "").Trim();
            if (texto.Length > 0 && OrigemDe(campo)?.GuardarNoPaciente is { } guardar) guardar(paciente, texto);
        }
    }

    public static int? Idade(string? dataNascimento, DateTimeOffset? em = null)
    {
        if (!DateTime.TryParseExact((dataNascimento ?? "").Trim(), "dd/MM/yyyy", PtBr, DateTimeStyles.None, out var nascimento)) return null;
        var hoje = (em ?? DateTimeOffset.Now).Date;
        var anos = hoje.Year - nascimento.Year;
        if (hoje < nascimento.AddYears(anos)) anos--;
        return anos >= 0 ? anos : null;
    }

    /// <summary>{{CID}}, {{CID_PRINCIPAL}}, {{CID2}}, {{OUTRO_CID}}…</summary>
    public static bool EhCampoCid(string campo)
    {
        var chave = Chave(campo);
        return chave.StartsWith("CID") || chave.EndsWith("_CID");
    }

    /// <summary>De onde vem o valor do campo, para mostrar na aba Modelos.</summary>
    public static string Descricao(string campo) => OrigemDe(campo) switch
    {
        { EhNome: true } => "nome digitado na hora",
        { Tipo: Tipo.Paciente } => "cadastro do paciente",
        { Tipo: Tipo.Local } => "cadastro do local",
        { Tipo: Tipo.Idade } => "calculada pelo nascimento",
        { Tipo: Tipo.DataHoje or Tipo.DataExtenso } => "data de hoje",
        _ => "digitado na hora (só neste documento)"
    };
}
