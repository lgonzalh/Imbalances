using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Imbalances.Core.Services;

public interface IExcelRow
{
    string GetCell(string columnName);
    string Texto { get; }
}

public interface IExcelWorksheet
{
    string Name { get; }
    IEnumerable<IExcelRow> Rows { get; }
}

public interface IExcelWorkbook
{
    IExcelWorksheet GetWorksheet(string name);
    IEnumerable<IExcelWorksheet> Worksheets { get; }
}

public interface IExcelProvider
{
    Task<IExcelWorkbook> OpenAsync(Stream fileStream);
}
