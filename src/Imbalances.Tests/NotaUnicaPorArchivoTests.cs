using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

/// <summary>
/// TEST OBLIGATORIO: FASE 5.1A
/// Debe FALLAR si una Nota se procesa más de una vez por archivo.
/// Cada Nota: max 1 lectura, max 1 procesamiento.
/// </summary>
public class NotaUnicaPorArchivoTests
{
    private readonly ITestOutputHelper _output;

    public NotaUnicaPorArchivoTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task NotaUnica_2CuentasMismaNota_VecesProcesadaEs1()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "ALFA SA" },
                new() { NombreEmpresa = "BETA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar", Tipo = "CxC" },
                new() { NombreCuenta = "Cuentas por pagar", Tipo = "CxP" },
            },
        };

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "Cuentas por pagar", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR / PAGAR" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "500.00" }),
                [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,500.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        PipelineProfile? capturedProfile = null;

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false,
            onProfile: profile => capturedProfile = profile);

        Assert.NotNull(capturedProfile);
        var stats = capturedProfile!.ReuseStats;

        _output.WriteLine("=== NOTA UNICA POR ARCHIVO ===");
        foreach (var (nota, stat) in stats.Notas)
        {
            _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, Antes={stat.ProcesamientosAntes}");

            // ASSERT CRITICO: ninguna nota debe procesarse mas de 1 vez
            Assert.True(stat.VecesProcesada <= 1,
                $"FALLO: Nota {nota} procesada {stat.VecesProcesada} veces (max 1 permitido)");
        }

        // 2 cuentas, 1 nota, max 1 procesamiento
        Assert.Single(stats.Notas);
        var nota5 = stats.Notas["5"];
        Assert.Equal(1, nota5.VecesLeida);
        Assert.Equal(1, nota5.VecesProcesada);
        Assert.Equal(2, nota5.ProcesamientosAntes);

        // Same number of movements as without optimization (2 cuentas x 2 empresas)
        Assert.Equal(4, resultados.Count);
        _output.WriteLine($"Movimientos totales: {resultados.Count}");
    }

    [Fact]
    public async Task NotaUnica_4CuentasMismaNota_VecesProcesadaEs1()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "ALFA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "CxC", Tipo = "CxC" },
                new() { NombreCuenta = "CxP", Tipo = "CxP" },
                new() { NombreCuenta = "PyC", Tipo = "PyC" },
                new() { NombreCuenta = "PyP", Tipo = "PyP" },
            },
        };

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "CxC", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "CxP", ["J"] = "5" }),
                [15] = new FakeRow(new() { ["C"] = "PyC", ["J"] = "5" }),
                [20] = new FakeRow(new() { ["C"] = "PyP", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "VARIOS" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "2,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "2,000.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        PipelineProfile? capturedProfile = null;

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false,
            onProfile: profile => capturedProfile = profile);

        Assert.NotNull(capturedProfile);
        var stats = capturedProfile!.ReuseStats;

        _output.WriteLine("=== 4 CUENTAS, 1 NOTA ===");
        foreach (var (nota, stat) in stats.Notas)
        {
            _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, Antes={stat.ProcesamientosAntes}");
            Assert.True(stat.VecesProcesada <= 1,
                $"FALLO: Nota {nota} procesada {stat.VecesProcesada} veces");
        }

        Assert.Single(stats.Notas);
        var nota5 = stats.Notas["5"];
        Assert.Equal(1, nota5.VecesLeida);
        Assert.Equal(1, nota5.VecesProcesada);
        Assert.Equal(4, nota5.ProcesamientosAntes);

        // 4 cuentas x 1 empresa homologada = 4 movimientos
        Assert.Equal(4, resultados.Count);
        _output.WriteLine($"Movimientos totales: {resultados.Count} (1 por cuenta)");
    }

    [Fact]
    public async Task NotaUnica_NotasDiferentes_CadaUnaProcesada1Vez()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "ALFA SA" },
                new() { NombreEmpresa = "BETA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "CxC", Tipo = "CxC" },
                new() { NombreCuenta = "CxP", Tipo = "CxP" },
            },
        };

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "CxC", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "CxP", ["J"] = "6" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CXC" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,000.00" }),
            }),
            new FakeWorksheet("Nota 6", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CXP" }),
                [4] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "500.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "500.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        PipelineProfile? capturedProfile = null;
        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false,
            onProfile: profile => capturedProfile = profile);

        Assert.NotNull(capturedProfile);
        var stats = capturedProfile!.ReuseStats;

        _output.WriteLine("=== 2 NOTAS DIFERENTES ===");
        foreach (var (nota, stat) in stats.Notas.OrderBy(kv => kv.Key))
        {
            _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, Antes={stat.ProcesamientosAntes}");
            Assert.True(stat.VecesProcesada <= 1,
                $"FALLO: Nota {nota} procesada {stat.VecesProcesada} veces");
        }

        Assert.Equal(2, stats.NotasUnicas);
        Assert.Equal(0, stats.NotasReutilizadas);
        Assert.Equal(2, resultados.Count);
    }

    [Fact]
    public async Task NotaUnica_ReporteAntesDespues_ContieneDatosCorrectos()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "ALFA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "CxC", Tipo = "CxC" },
                new() { NombreCuenta = "CxP", Tipo = "CxP" },
                new() { NombreCuenta = "PyC", Tipo = "PyC" },
            },
        };

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "CxC", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "CxP", ["J"] = "5" }),
                [15] = new FakeRow(new() { ["C"] = "PyC", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "VARIOS" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,000.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        PipelineProfile? capturedProfile = null;

        var logs = new List<string>();
        await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false,
            onProgressLog: msg => logs.Add(msg),
            onProfile: profile => capturedProfile = profile);

        Assert.NotNull(capturedProfile);
        var stats = capturedProfile!.ReuseStats;

        // Validate before/after report
        var reuseLogs = logs.Where(l => l.Contains("[Reuse]")).ToList();
        _output.WriteLine("=== REPORTE ANTES/DESPUES ===");
        foreach (var log in reuseLogs)
            _output.WriteLine($"  {log}");

        Assert.NotEmpty(reuseLogs);

        var nota5 = stats.Notas["5"];
        Assert.Equal(1, nota5.VecesLeida);
        Assert.Equal(1, nota5.VecesProcesada);
        Assert.Equal(3, nota5.ProcesamientosAntes);

        _output.WriteLine("");
        _output.WriteLine("--- REPORTE ---");
        _output.WriteLine($"Archivo: archivo.xlsx");
        _output.WriteLine($"Nota: 5");
        _output.WriteLine($"Procesamientos antes: {nota5.ProcesamientosAntes}");
        _output.WriteLine($"Procesamientos despues: {nota5.VecesProcesada}");
        _output.WriteLine($"Ahorro: {nota5.ProcesamientosAntes - nota5.VecesProcesada} procesamiento(s) eliminado(s)");
    }
}
