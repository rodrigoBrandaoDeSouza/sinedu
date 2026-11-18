namespace Sinedu.SharedKernel.Storage;

/// <summary>Tipo de documento aceito como anexo, conferido pelos primeiros bytes (a extensão não vale nada).</summary>
public sealed record DocumentType(string ContentType, string Extension);

public static class DocumentTypes
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly (DocumentType Type, byte[] Magic)[] Allowed =
    [
        (new("application/pdf", ".pdf"), "%PDF-"u8.ToArray()),
        (new("image/png", ".png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        (new("image/jpeg", ".jpg"), [0xFF, 0xD8, 0xFF]),
    ];

    /// <summary>PDF, PNG ou JPG; null para qualquer outra coisa.</summary>
    public static DocumentType? Detect(ReadOnlySpan<byte> content)
    {
        foreach (var (type, magic) in Allowed)
        {
            if (content.StartsWith(magic))
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Nome seguro para exibir/baixar: só letras, números e separadores simples, com a extensão do tipo real.</summary>
    public static string SafeFileName(string? fileName, DocumentType type)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? "");
        var clean = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.').ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "documento";
        }

        return (clean.Length > 120 ? clean[..120] : clean) + type.Extension;
    }
}
