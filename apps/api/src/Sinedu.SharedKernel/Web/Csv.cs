using System.Text;

namespace Sinedu.SharedKernel.Web;

/// <summary>CSV para o Excel em pt-BR: BOM, ponto e vírgula e células que nunca viram fórmula.</summary>
public static class Csv
{
    /// <summary>Aspas quando preciso e neutraliza fórmula (=, +, -, @, tab, CR) — texto digitado não pode virar fórmula.</summary>
    public static string Cell(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.Contains(';') || value.Contains('"') || value.Contains('\n') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', headers.Select(Cell)).Append("\r\n");
        foreach (var row in rows)
        {
            sb.AppendJoin(';', row.Select(Cell)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }
}
