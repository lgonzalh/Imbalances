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

    public IExcelWorksheet GetWorksheet(string name)
    {
        var table = _dataSet.Tables[name];
        return table != null ? new ExcelWorksheetWrapper(table) : null;
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
