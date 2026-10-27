using System.Globalization;
using System.Text;

namespace Orthck.Core;

/// <summary>Informações permanentes vêm do cadastro; campos de atendimento NUNCA persistem no paciente.</summary>
public static class ClinicalFields
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public static string Key(string input)
    {
        string plain = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var separator = false;
        foreach (var c in plain)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToUpperInvariant(c)); separator = false; }
            else if (!separator && sb.Length > 0) { sb.Append('_'); separator = true; }
        }
        return sb.ToString().TrimEnd('_');
    }
    public static bool IsPatientName(string field) => new[] { "NOME", "PACIENTE", "NOME_PACIENTE", "NOME_DO_PACIENTE", "NOME_COMPLETO" }.Contains(Key(field));

    public static Dictionary<string, string> Defaults(IEnumerable<string> fields, Patient? patient, ClinicLocation? clinic, DateTimeOffset when)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            string v = Key(field) switch
            {
                "NOME" or "PACIENTE" or "NOME_PACIENTE" or "NOME_DO_PACIENTE" or "NOME_COMPLETO" => patient?.Nome ?? "",
                "CPF" or "CPF_PACIENTE" or "CPF_DO_PACIENTE" => patient?.Cpf ?? "",
                "RG" or "RG_PACIENTE" or "RG_DO_PACIENTE" => patient?.Rg ?? "",
                "DATA_NASCIMENTO" or "DATA_DE_NASCIMENTO" or "NASCIMENTO" or "DN" => patient?.DataNascimento ?? "",
                "TELEFONE" or "CELULAR" or "FONE" or "WHATSAPP" => patient?.Telefone ?? "",
                "EMAIL" or "E_MAIL" => patient?.Email ?? "",
                "ENDERECO" or "ENDERECO_PACIENTE" => patient?.Endereco ?? "",
                "CONVENIO" or "PLANO" or "PLANO_DE_SAUDE" => patient?.Convenio ?? "",
                "LOCAL" or "LOCAL_ATENDIMENTO" or "LOCAL_DE_ATENDIMENTO" or "NOME_LOCAL" or "CLINICA" or "NOME_CLINICA" => clinic?.Nome ?? "",
                "LOCAL_ENDERECO" or "ENDERECO_LOCAL" or "ENDERECO_CLINICA" => clinic?.Endereco ?? "",
                "CIDADE" or "LOCAL_CIDADE" or "CIDADE_LOCAL" => clinic?.Cidade ?? "",
                "LOCAL_TELEFONE" or "TELEFONE_LOCAL" or "TELEFONE_CLINICA" => clinic?.Telefone ?? "",
                "LOCAL_EMAIL" or "EMAIL_LOCAL" or "EMAIL_CLINICA" => clinic?.Email ?? "",
                "CNPJ" or "LOCAL_CNPJ" or "CNPJ_LOCAL" or "CNPJ_CLINICA" => clinic?.Cnpj ?? "",
                "DATA" or "HOJE" or "DATA_HOJE" or "DATA_ATUAL" => when.ToString("dd/MM/yyyy", PtBr),
                "DATA_EXTENSO" or "DATA_POR_EXTENSO" => when.ToString("d 'de' MMMM 'de' yyyy", PtBr),
                "IDADE" => Age(patient?.DataNascimento, when),
                _ => ""
            };
            result[field] = v;
        }
        return result;
    }
    public static void UpdatePermanentFields(Patient patient, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (field, raw) in values)
        {
            var value = raw.Trim();
            if (value.Length == 0) continue;
            switch (Key(field))
            {
                case "CPF": case "CPF_PACIENTE": case "CPF_DO_PACIENTE": patient.Cpf = value; break;
                case "RG": case "RG_PACIENTE": case "RG_DO_PACIENTE": patient.Rg = value; break;
                case "DATA_NASCIMENTO": case "DATA_DE_NASCIMENTO": case "NASCIMENTO": case "DN": patient.DataNascimento = value; break;
                case "TELEFONE": case "CELULAR": case "FONE": case "WHATSAPP": patient.Telefone = value; break;
                case "EMAIL": case "E_MAIL": patient.Email = value; break;
                case "ENDERECO": case "ENDERECO_PACIENTE": patient.Endereco = value; break;
                case "CONVENIO": case "PLANO": case "PLANO_DE_SAUDE": patient.Convenio = value; break;
            }
        }
    }
    private static string Age(string? birthday, DateTimeOffset when)
    {
        if (!DateTime.TryParseExact(birthday, "dd/MM/yyyy", PtBr, DateTimeStyles.None, out var born)) return "";
        var years = when.Year - born.Year - (new DateTime(when.Year, born.Month, born.Day > DateTime.DaysInMonth(when.Year, born.Month) ? DateTime.DaysInMonth(when.Year, born.Month) : born.Day) > when.Date ? 1 : 0);
        return years >= 0 ? years.ToString(PtBr) : "";
    }
}
