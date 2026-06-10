using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExcelDataReader;
using Imbalances.Core.Services;

namespace Imbalances.Infrastructure.Services;

public class ExcelProvider : IExcelProvider
{
    public async Task<IExcelWorkbook> OpenAsync(Stream fileStream)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var reader = ExcelReaderFactory.CreateReader(fileStream);
        var result = reader.AsDataSet(new ExcelDataSetConfiguration()
        {
            ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
            {
                UseHeaderRow = false
            }
        });

        return new ExcelWorkbookWrapper(result);
    }
}

public class ExcelWorkbookWrapper : IExcelWorkbook
{
    private readonly DataSet _dataSet;

    public ExcelWorkbookWrapper(DataSet dataSet)
    {
        _dataSet = dataSet;
    }

    public IEnumerable<IExcelWorksheet> Worksheets => _dataSet.Tables.Cast<DataTable>().Select(t => new ExcelWorksheetWrapper(t));

    public IExcelWorksheet? GetWorksheet(string name)
    {
        // Try exact match first (case-insensitive, DataSet default)
        var table = _dataSet.Tables[name];
        if (table != null)
            return new ExcelWorksheetWrapper(table);

        // Fallback: normalized comparison (accent-insensitive, punctuation-insensitive)
        var nameNorm = EmpresaDetectionService.NormalizeForComparison(name);
        foreach (var dt in _dataSet.Tables.Cast<DataTable>())
        {
            var dtNorm = EmpresaDetectionService.NormalizeForComparison(dt.TableName);
            if (dtNorm == nameNorm)
                return new ExcelWorksheetWrapper(dt);
        }

        return null;
    }
}

public class ExcelWorksheetWrapper : IExcelWorksheet
{
    private readonly DataTable _table;

    public ExcelWorksheetWrapper(DataTable table)
    {
        _table = table;
    }

    public string Name => _table.TableName;

    public int RowCount => _table.Rows.Count;

    public IExcelRow? GetRow(int rowNumber)
    {
        var index = rowNumber - 1;
        if (index < 0 || index >= _table.Rows.Count)
            return null;

        return new ExcelRowWrapper(_table.Rows[index]);
    }

    public IEnumerable<IExcelRow> Rows => _table.Rows.Cast<DataRow>().Select(r => new ExcelRowWrapper(r));
}

public class ExcelRowWrapper : IExcelRow
{
    private readonly DataRow _row;

    public ExcelRowWrapper(DataRow row)
    {
        _row = row;
    }

    public string GetCell(string columnName)
    {
        int index = ColumnLetterToNumber(columnName) - 1;
        if (index >= 0 && index < _row.Table.Columns.Count)
        {
            return _row[index]?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    public string Texto => string.Join(" ", _row.ItemArray.Select(i => i?.ToString() ?? ""));

    private int ColumnLetterToNumber(string columnLetter)
    {
        int result = 0;
        foreach (char c in columnLetter.ToUpper())
        {
            result *= 26;
            result += (c - 'A' + 1);
        }
        return result;
    }
}
