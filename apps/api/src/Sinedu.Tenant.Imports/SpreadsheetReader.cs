using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace Sinedu.Tenant.Imports;

public sealed record ParsedSheet(string Name, IReadOnlyList<(int RowNumber, string?[] Cells)> Rows);

public sealed class SpreadsheetException(string message) : Exception(message);

/// <summary>
/// Lê XLSX (todas as abas) e CSV (separador e codificação detectados) como texto.
/// Datas viram ISO (aaaa-mm-dd); números longos não viram notação científica (CGM de 10 dígitos).
/// </summary>
public static class SpreadsheetReader
{
    public const int MaxRows = 20_000;
    public const int MaxColumns = 60;
    public const long MaxBytes = 10 * 1024 * 1024;

    public static IReadOnlyList<ParsedSheet> Read(Stream content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        var buffer = new MemoryStream();
        content.CopyTo(buffer);
        if (buffer.Length == 0) throw new SpreadsheetException("O arquivo está vazio.");
        if (buffer.Length > MaxBytes) throw new SpreadsheetException("O arquivo passa de 10 MB.");

        buffer.Position = 0;
        var header = new byte[4];
        _ = buffer.Read(header, 0, 4);
        buffer.Position = 0;

        var isZip = header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
        var isOldExcel = header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (isOldExcel || extension == ".xls")
        {
            throw new SpreadsheetException("Formato .xls antigo não é aceito. Salve como .xlsx ou .csv.");
        }

        if (isZip && extension is ".xlsx" or ".xlsm")
        {
            return ReadXlsx(buffer);
        }

        if (!isZip && extension is ".csv" or ".txt")
        {
            return [ReadCsv(buffer.ToArray())];
        }

        throw new SpreadsheetException("Envie um arquivo .xlsx ou .csv.");
    }

    private static List<ParsedSheet> ReadXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheets = new List<ParsedSheet>();
        foreach (var worksheet in workbook.Worksheets)
        {
            var range = worksheet.RangeUsed();
            if (range is null) continue;

            var lastColumn = Math.Min(range.LastColumn().ColumnNumber(), MaxColumns);
            var rows = new List<(int, string?[])>();
            foreach (var row in range.Rows())
            {
                var number = row.RowNumber();
                if (rows.Count >= MaxRows) throw new SpreadsheetException($"A aba \"{worksheet.Name}\" tem mais de {MaxRows} linhas.");
                var cells = new string?[lastColumn];
                for (var c = 1; c <= lastColumn; c++)
                {
                    cells[c - 1] = CellText(worksheet.Cell(number, c));
                }

                if (cells.Any(v => v is not null)) rows.Add((number, cells));
            }

            sheets.Add(new ParsedSheet(worksheet.Name, rows));
        }

        if (sheets.Count == 0) throw new SpreadsheetException("Nenhuma aba com dados.");
        return sheets;
    }

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        string? text = value.Type switch
        {
            XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => FormatNumber(value.GetNumber()),
            XLDataType.Boolean => value.GetBoolean() ? "SIM" : "NÃO",
            XLDataType.Error => null,
            _ => cell.GetFormattedString(),
        };
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string FormatNumber(double number) =>
        number == Math.Floor(number) && Math.Abs(number) < 1e15
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("0.############", CultureInfo.InvariantCulture);

    private static ParsedSheet ReadCsv(byte[] bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [";", ",", "\t", "|"],
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
        };
        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, config);
        var rows = new List<(int, string?[])>();
        var number = 0;
        while (csv.Read())
        {
            number++;
            if (rows.Count >= MaxRows) throw new SpreadsheetException($"O arquivo tem mais de {MaxRows} linhas.");
            var cells = Enumerable.Range(0, Math.Min(csv.Parser.Count, MaxColumns))
                .Select(i => csv.GetField(i)?.Trim() is { Length: > 0 } v ? v : null)
                .ToArray();
            if (cells.Any(v => v is not null)) rows.Add((number, cells));
        }

        return new ParsedSheet("CSV", rows);
    }
}
