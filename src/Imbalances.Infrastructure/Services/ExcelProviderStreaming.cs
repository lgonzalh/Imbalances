using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExcelDataReader;
using Imbalances.Core.Services;

namespace Imbalances.Infrastructure.Services;

/// <summary>
/// Streaming Excel provider that reads all worksheets in a single pass
/// but stores data as lightweight string arrays instead of DataSet/DataTable.
/// Eliminates overhead of DataColumn schema, DataRow infrastructure, and DataSet management.
/// </summary>
public class ExcelProviderStreaming : IExcelProvider
{
    private static bool _encodingRegistered;

    public async Task<IExcelWorkbook> OpenAsync(Stream fileStream)
    {
        if (!_encodingRegistered)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            _encodingRegistered = true;
        }

        var seekable = fileStream;
        if (!fileStream.CanSeek)
        {
            var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms);
            ms.Seek(0, SeekOrigin.Begin);
            seekable = ms;
        }

        var sheets = new List<SheetData>();

        using (var reader = ExcelReaderFactory.CreateReader(seekable))
        {
            do
            {
                var sheetName = reader.Name;
                var fieldCount = reader.FieldCount;
                var rows = new List<string[]>();

                while (reader.Read())
                {
                    var row = new string[fieldCount];
                    for (var i = 0; i < fieldCount; i++)
                        row[i] = reader[i]?.ToString() ?? string.Empty;
                    rows.Add(row);
                }

                sheets.Add(new SheetData(sheetName, fieldCount, rows));
            }
            while (reader.NextResult());
        }

        return new StreamingWorkbook(sheets);
    }
}

public sealed class SheetData
{
    public string Name { get; }
    public int FieldCount { get; }
    public List<string[]> Rows { get; }

    public SheetData(string name, int fieldCount, List<string[]> rows)
    {
        Name = name;
        FieldCount = fieldCount;
        Rows = rows;
    }
}

public sealed class StreamingWorkbook : IExcelWorkbook
{
    private readonly Dictionary<string, int> _sheetIndex;
    private readonly List<SheetData> _sheets;

    public IReadOnlyList<SheetData> Sheets => _sheets;
    public IReadOnlyList<string> SheetNames => _sheets.Select(s => s.Name).ToList();

    public StreamingWorkbook(List<SheetData> sheets)
    {
        _sheets = sheets;
        _sheetIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sheets.Count; i++)
        {
            if (!_sheetIndex.ContainsKey(sheets[i].Name))
                _sheetIndex[sheets[i].Name] = i;
        }
    }

    public IExcelWorksheet? GetWorksheet(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (_sheetIndex.TryGetValue(name, out var index))
            return new StreamingWorksheet(_sheets[index]);

        var nameNorm = EmpresaDetectionService.NormalizeForComparison(name);
        for (var i = 0; i < _sheets.Count; i++)
        {
            if (EmpresaDetectionService.NormalizeForComparison(_sheets[i].Name) == nameNorm)
                return new StreamingWorksheet(_sheets[i]);
        }

        return null;
    }

    public IEnumerable<IExcelWorksheet> Worksheets =>
        _sheets.Select(s => (IExcelWorksheet)new StreamingWorksheet(s));
}

public sealed class StreamingWorksheet : IExcelWorksheet
{
    private readonly SheetData _data;

    public StreamingWorksheet(SheetData data) => _data = data;

    public string Name => _data.Name;
    public int RowCount => _data.Rows.Count;

    public IExcelRow? GetRow(int rowNumber)
    {
        var index = rowNumber - 1;
        if (index < 0 || index >= _data.Rows.Count)
            return null;
        return new StreamingRow(_data.Rows[index]);
    }

    public IEnumerable<IExcelRow> Rows => _data.Rows.Select(r => (IExcelRow)new StreamingRow(r));
}

public sealed class StreamingRow : IExcelRow
{
    private readonly string[] _values;

    public StreamingRow(string[] values) => _values = values;

    public string GetCell(string columnName)
    {
        var index = ColumnLetterToNumber(columnName) - 1;
        if (index >= 0 && index < _values.Length)
            return _values[index];
        return string.Empty;
    }

    public string Texto => string.Join(" ", _values.Where(v => !string.IsNullOrEmpty(v)));

    private static int ColumnLetterToNumber(string columnLetter)
    {
        var result = 0;
        foreach (var c in columnLetter.ToUpperInvariant())
        {
            result *= 26;
            result += c - 'A' + 1;
        }
        return result;
    }
}
