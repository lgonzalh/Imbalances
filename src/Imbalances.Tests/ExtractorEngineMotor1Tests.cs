using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Xunit;

namespace Imbalances.Tests;

public class ExtractorEngineMotor1Tests
{
    [Fact]
    public async Task ProcesarArchivoMotor1Async_ExtraeEmpresasYValores_YCortaEnTotal()
    {
        var balance = new FakeWorksheet(
            name: "Balance de situación",
            rowCount: 50_000,
            rows: new Dictionary<int, FakeRow>
            {
                [5] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "Cuentas por cobrar clientes",
                    ["J"] = "5"
                }),
                [20] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "GRAN TOTAL"
                })
            });

        var nota5 = new FakeWorksheet(
            name: "Nota 5",
            rowCount: 50_000,
            rows: new Dictionary<int, FakeRow>
            {
                [3] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "CUENTAS POR COBRAR"
                }),
                [4] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "Compañía Á",
                    ["I"] = "1,000.50"
                }),
                [5] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "Tercero B",
                    ["I"] = "(200,25)"
                }),
                [6] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "TOTAL",
                    ["I"] = "800,25"
                }),
                [50] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "No debería leerse",
                    ["I"] = "999"
                })
            });

        var workbook = new FakeWorkbook(new[] { balance, nota5 });
        var excelProvider = new FakeExcelProvider(workbook);
        var motor1 = new Motor1Extractor(excelProvider);

        var config = new ConfiguracionCore
        {
            Empresas =
            [
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }
            ],
            Cuentas =
            [
                new CuentaConfig
                {
                    NombreCuenta = "Cuentas por cobrar clientes",
                    Tipo = "CxC",
                    ColumnaValor = "C",
                    ColumnaNota = "K"
                }
            ]
        };

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/dummy/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-05");

        Assert.Equal(2, resultados.Count);
        Assert.All(resultados, r => Assert.Equal("5", r.Nota));
        Assert.All(resultados, r => Assert.Equal("2026-05", r.Periodo));

        var a = resultados.Single(r => r.EmpresaContraparte.Contains("COMPANIA"));
        Assert.Equal("COMPANIA A", a.EmpresaContraparte);
        Assert.Equal(1000.50m, a.Valor);

        var b = resultados.Single(r => r.EmpresaContraparte == "TERCERO B");
        Assert.Equal(-200.25m, b.Valor);

        Assert.True(balance.GetRowCalls <= 10, $"Balance GetRowCalls={balance.GetRowCalls}");
        Assert.True(nota5.GetRowCalls <= 20, $"Nota 5 GetRowCalls={nota5.GetRowCalls}");
    }

    [Fact]
    public async Task ProcesarArchivoMotor1Async_NoEncuentraCuentaFueraDeVentanaBalance_DejaVacio()
    {
        var balance = new FakeWorksheet(
            name: "Balance de situación",
            rowCount: 50_000,
            rows: new Dictionary<int, FakeRow>
            {
                [250] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "Cuentas por cobrar clientes",
                    ["J"] = "5"
                })
            });

        var nota5 = new FakeWorksheet(
            name: "Nota 5",
            rowCount: 50_000,
            rows: new Dictionary<int, FakeRow>
            {
                [3] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "CUENTAS"
                }),
                [4] = new FakeRow(new Dictionary<string, string>
                {
                    ["C"] = "Tercero X",
                    ["I"] = "100"
                })
            });

        var workbook = new FakeWorkbook(new[] { balance, nota5 });
        var excelProvider = new FakeExcelProvider(workbook);
        var motor1 = new Motor1Extractor(excelProvider);

        var config = new ConfiguracionCore
        {
            Empresas =
            [
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }
            ],
            Cuentas =
            [
                new CuentaConfig
                {
                    NombreCuenta = "Cuentas por cobrar clientes",
                    Tipo = "CxC"
                }
            ]
        };

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/dummy/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-05");

        Assert.Empty(resultados);
        Assert.True(balance.GetRowCalls <= 200, $"Balance GetRowCalls={balance.GetRowCalls}");
    }
}

internal sealed class FakeExcelProvider : IExcelProvider
{
    private readonly IExcelWorkbook _workbook;

    public FakeExcelProvider(IExcelWorkbook workbook)
    {
        _workbook = workbook;
    }

    public Task<IExcelWorkbook> OpenAsync(Stream fileStream) => Task.FromResult(_workbook);
}

internal sealed class FakeWorkbook : IExcelWorkbook
{
    private readonly Dictionary<string, IExcelWorksheet> _sheets;

    public FakeWorkbook(IEnumerable<IExcelWorksheet> worksheets)
    {
        _sheets = worksheets.ToDictionary(w => w.Name, w => w, StringComparer.OrdinalIgnoreCase);
    }

    public IExcelWorksheet? GetWorksheet(string name)
        => _sheets.TryGetValue(name, out var sheet) ? sheet : null;

    public IEnumerable<IExcelWorksheet> Worksheets => _sheets.Values;
}

internal sealed class FakeWorksheet : IExcelWorksheet
{
    private readonly Dictionary<int, FakeRow> _rows;

    public FakeWorksheet(string name, int rowCount, Dictionary<int, FakeRow> rows)
    {
        Name = name;
        RowCount = rowCount;
        _rows = rows;
    }

    public string Name { get; }
    public int RowCount { get; }

    public int GetRowCalls { get; private set; }

    public IExcelRow? GetRow(int rowNumber)
    {
        GetRowCalls++;

        if (rowNumber < 1 || rowNumber > RowCount)
            return null;

        return _rows.TryGetValue(rowNumber, out var row) ? row : new FakeRow(new Dictionary<string, string>());
    }

    public IEnumerable<IExcelRow> Rows
        => Enumerable.Range(1, RowCount).Select(i => GetRow(i)).Where(r => r != null)!
            .Cast<IExcelRow>();
}

internal sealed class FakeRow : IExcelRow
{
    private readonly Dictionary<string, string> _cells;

    public FakeRow(Dictionary<string, string> cells)
    {
        _cells = cells;
    }

    public string GetCell(string columnName)
        => _cells.TryGetValue(columnName.ToUpperInvariant(), out var value) ? value : string.Empty;

    public string Texto => string.Join(" ", _cells.OrderBy(k => k.Key).Select(k => k.Value));
}
