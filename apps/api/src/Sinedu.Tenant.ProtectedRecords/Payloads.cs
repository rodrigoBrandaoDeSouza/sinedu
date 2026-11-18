using System.Text.Json;
using System.Text.Json.Serialization;
using Sinedu.SharedKernel.Security;

namespace Sinedu.Tenant.ProtectedRecords;

/// <summary>Contato de emergência informado pela família.</summary>
[Sensitive]
public sealed record EmergencyContact(string Name, string? Phone, string? Relationship);

/// <summary>
/// Ficha de Saúde (spec 4.3). Todos os campos são opcionais e vêm de fonte legítima.
/// Não há indicador de risco, diagnóstico inferido nem recomendação automática.
/// </summary>
[Sensitive]
public sealed record HealthData(
    string? BloodType,
    string? Allergies,
    string? Conditions,
    string? Medications,
    string? Restrictions,
    string? FirstAidInstructions,
    IReadOnlyList<EmergencyContact>? EmergencyContacts,
    string? Source,
    string? Notes);

/// <summary>Pessoa autorizada ou impedida de retirar o aluno.</summary>
[Sensitive]
public sealed record PickupPerson(string Name, string? Document, string? Relationship, string? Note);

public enum VerificationLevel
{
    /// <summary>Documento oficial apresentado e conferido.</summary>
    Documentado,

    /// <summary>Informado por responsável ou órgão, sem documento.</summary>
    Declarado,

    NaoVerificado,
}

/// <summary>Conteúdo de um registro de Proteção/Sigilo (spec 4.4). Só o tipo, a situação e a validade ficam fora daqui.</summary>
[Sensitive]
public sealed record ProtectionData(
    string Title,
    string? Description,
    string? Authority,
    string? DocumentNumber,
    DateOnly? IssuedOn,
    DateOnly? ValidFrom,
    string? Basis,
    VerificationLevel Verification,
    IReadOnlyList<PickupPerson>? AuthorizedPeople,
    IReadOnlyList<PickupPerson>? UnauthorizedPeople,
    string? Notes);

/// <summary>O que vai dentro do envelope de cada versão: os dados e a justificativa da alteração.</summary>
[Sensitive]
internal sealed record VersionPayload<T>(T Data, string Reason);

/// <summary>Metadado cifrado de um anexo.</summary>
[Sensitive]
internal sealed record AttachmentMetadata(string FileName, string ContentType, string? Description);

internal static class PayloadJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Normalização e validação (mensagens nunca repetem o conteúdo).</summary>
internal static class PayloadRules
{
    public const int ShortText = 200;
    public const int LongText = 4000;
    public const int MaxPeople = 30;

    public static string? Clean(string? value, int max, string field, Dictionary<string, string[]> errors)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        if (v.Length > max)
        {
            errors[field] = [$"Use no máximo {max} caracteres."];
        }

        return v;
    }

    public static HealthData Normalize(HealthData d, Dictionary<string, string[]> errors)
    {
        var contacts = (d.EmergencyContacts ?? [])
            .Select((c, i) => new EmergencyContact(
                Clean(c.Name, ShortText, $"emergencyContacts[{i}].name", errors) ?? "",
                Clean(c.Phone, 40, $"emergencyContacts[{i}].phone", errors),
                Clean(c.Relationship, 60, $"emergencyContacts[{i}].relationship", errors)))
            .Where(c => c.Name.Length > 0 || c.Phone is not null)
            .Distinct()
            .ToList();
        if (contacts.Count > 10)
        {
            errors["emergencyContacts"] = ["Informe no máximo 10 contatos."];
        }

        if (contacts.Any(c => c.Name.Length == 0))
        {
            errors["emergencyContacts"] = ["Cada contato de emergência precisa de um nome."];
        }

        return new HealthData(
            Clean(d.BloodType, 10, "bloodType", errors),
            Clean(d.Allergies, LongText, "allergies", errors),
            Clean(d.Conditions, LongText, "conditions", errors),
            Clean(d.Medications, LongText, "medications", errors),
            Clean(d.Restrictions, LongText, "restrictions", errors),
            Clean(d.FirstAidInstructions, LongText, "firstAidInstructions", errors),
            contacts,
            Clean(d.Source, ShortText, "source", errors),
            Clean(d.Notes, LongText, "notes", errors));
    }

    public static ProtectionData Normalize(ProtectionData d, Dictionary<string, string[]> errors)
    {
        var title = Clean(d.Title, ShortText, "title", errors);
        if (title is null)
        {
            errors["title"] = ["Dê um título curto ao registro."];
        }

        return new ProtectionData(
            title ?? "",
            Clean(d.Description, LongText, "description", errors),
            Clean(d.Authority, ShortText, "authority", errors),
            Clean(d.DocumentNumber, 100, "documentNumber", errors),
            d.IssuedOn,
            d.ValidFrom,
            Clean(d.Basis, LongText, "basis", errors),
            d.Verification,
            People(d.AuthorizedPeople, "authorizedPeople", errors),
            People(d.UnauthorizedPeople, "unauthorizedPeople", errors),
            Clean(d.Notes, LongText, "notes", errors));
    }

    public static string? Reason(string? reason, Dictionary<string, string[]> errors)
    {
        var r = Clean(reason, 1000, "reason", errors);
        if (r is null || r.Length < 5)
        {
            errors["reason"] = ["Explique o motivo da alteração (fica no histórico, cifrado)."];
        }

        return r;
    }

    private static List<PickupPerson> People(IReadOnlyList<PickupPerson>? people, string field, Dictionary<string, string[]> errors)
    {
        var list = (people ?? [])
            .Select((p, i) => new PickupPerson(
                Clean(p.Name, ShortText, $"{field}[{i}].name", errors) ?? "",
                Clean(p.Document, 40, $"{field}[{i}].document", errors),
                Clean(p.Relationship, 60, $"{field}[{i}].relationship", errors),
                Clean(p.Note, 500, $"{field}[{i}].note", errors)))
            .Where(p => p.Name.Length > 0)
            .Distinct()
            .ToList();
        if (list.Count > MaxPeople)
        {
            errors[field] = [$"Informe no máximo {MaxPeople} pessoas."];
        }

        return list;
    }
}
