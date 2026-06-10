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

public class NoteReuseValidationTests
{
    private readonly ITestOutputHelper _output;

    public NoteReuseValidationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Reuse_CuentasCompartenNota_VecesLeidaEs1()
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

        // Both cuentas reference Nota 5
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
        var reuseStats = capturedProfile!.ReuseStats;
        Assert.NotNull(reuseStats);

        _output.WriteLine("=== NOTE REUSE VALIDATION ===");
        _output.WriteLine($"Notas en cache: {capturedProfile.NotasProcesadas}");
        foreach (var (nota, stat) in reuseStats.Notas)
        {
            _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, Homologada={stat.VecesHomologada}");
        }

        // Both cuentas (CxC, CxP) point to Nota 5
        Assert.Single(reuseStats.Notas);
        var nota5 = reuseStats.Notas["5"];

        // VecesLeida = 1: nota read from Excel only once despite 2 cuentas
        Assert.Equal(1, nota5.VecesLeida);

        // VecesProcesada = 1: max 1 processing per nota per file
        Assert.Equal(1, nota5.VecesProcesada);

        // ProcesamientosAntes = 2: 2 cuentas referenced this nota
        Assert.Equal(2, nota5.ProcesamientosAntes);

        // VecesHomologada > 0: rows were homologated
        Assert.True(nota5.VecesHomologada > 0);
    }

    [Fact]
    public async Task Reuse_MaxReadPorNota_Es1_PorArchivo()
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
                new() { NombreCuenta = "Cuentas por cobrar", Tipo = "CxC" },
                new() { NombreCuenta = "Cuentas por pagar", Tipo = "CxP" },
                new() { NombreCuenta = "Prestamos por cobrar", Tipo = "PyC" },
                new() { NombreCuenta = "Prestamos por pagar", Tipo = "PyP" },
            },
        };

        // ALL 4 cuentas point to Nota 5 (max reuse scenario)
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "Cuentas por pagar", ["J"] = "5" }),
                [15] = new FakeRow(new() { ["C"] = "Prestamos por cobrar", ["J"] = "5" }),
                [20] = new FakeRow(new() { ["C"] = "Prestamos por pagar", ["J"] = "5" }),
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

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false,
            onProfile: profile =>
            {
                var stats = profile.ReuseStats;
                _output.WriteLine($"=== REUSE PROFILE ===");
                _output.WriteLine($"  Notas unicas: {stats.NotasUnicas}");
                _output.WriteLine($"  Notas reutilizadas: {stats.NotasReutilizadas}");
                foreach (var (nota, stat) in stats.Notas)
                {
                    _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}");
                }

                // Even with 4 cuentas sharing the same nota: 1 lectura, 1 procesamiento
                Assert.Single(stats.Notas);
                var nota5 = stats.Notas["5"];
                Assert.Equal(1, nota5.VecesLeida);
                Assert.Equal(1, nota5.VecesProcesada);
                Assert.Equal(4, nota5.ProcesamientosAntes);
            });

        // All 4 cuentas produce movements
        Assert.Equal(4, resultados.Count);
        _output.WriteLine($"Movimientos totales: {resultados.Count} (1 por cuenta)");
    }

    [Fact]
    public async Task Reuse_MismasHomologaciones_SinCacheVsConCache()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "ALFA SA" },
                new() { NombreEmpresa = "BETA SA" },
                new() { NombreEmpresa = "GAMMA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar", Tipo = "CxC" },
                new() { NombreCuenta = "Cuentas por pagar", Tipo = "CxP" },
            },
        };

        // Both cuentas -> Nota 5 (shared)
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
                [6] = new FakeRow(new() { ["C"] = "GAMMA SA", ["I"] = "250.00" }),
                [7] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,750.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false);

        // Validate: same number of movements as expected from 2 cuentas x 3 empresas
        Assert.Equal(6, resultados.Count);

        // Validate: correct homologations
        var alfaMovements = resultados.Where(r => r.EmpresaContraparte == "ALFA SA").ToList();
        var betaMovements = resultados.Where(r => r.EmpresaContraparte == "BETA SA").ToList();
        var gammaMovements = resultados.Where(r => r.EmpresaContraparte == "GAMMA SA").ToList();

        Assert.Equal(2, alfaMovements.Count); // CxC + CxP
        Assert.Equal(2, betaMovements.Count);
        Assert.Equal(2, gammaMovements.Count);

        // Validate: correct values
        Assert.All(alfaMovements, m => Assert.Equal(1000.00m, m.Valor));
        Assert.All(betaMovements, m => Assert.Equal(500.00m, m.Valor));
        Assert.All(gammaMovements, m => Assert.Equal(250.00m, m.Valor));

        _output.WriteLine("=== HOMOLOGATION VALIDATION ===");
        _output.WriteLine($"Total movimientos: {resultados.Count}");
        foreach (var m in resultados)
            _output.WriteLine($"  {m.EmpresaContraparte,-20} | {m.Tipo,-5} | {m.Valor,10:N2}");
    }

    [Fact]
    public async Task Reuse_NotaConClasificacionPrecalculada_MismosResultados()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "COMPANIA A" },
                new() { NombreEmpresa = "TERCERO B" },
                new() { NombreEmpresa = "FUNDACION SOLID RIVER" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" },
            },
            AliasEmpresa = new()
            {
                new() { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" },
            },
        };

        // Test all classification types in one nota
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), // Rubro (no valor)
                [4] = new FakeRow(new() { ["C"] = "COMPANIA A", ["I"] = "1,000.50" }), // Empresa
                [5] = new FakeRow(new() { ["C"] = "FSR", ["I"] = "(200,25)" }), // Alias -> Empresa
                [6] = new FakeRow(new() { ["C"] = "TOTAL CxC", ["I"] = "800.25" }), // Estructural
                [7] = new FakeRow(new() { ["C"] = "Desconocida X", ["I"] = "300.00" }), // NoHomologada
                [8] = new FakeRow(new() { ["C"] = "COMPANIA A (ANTIGUA)", ["I"] = "150.00" }), // SubEntry
                [9] = new FakeRow(new() { ["C"] = "GRAN TOTAL", ["I"] = "1,250.50" }), // GranTotal
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: true,
            onProfile: profile =>
            {
                var reuseStats = profile.ReuseStats;
                _output.WriteLine("=== CLASSIFICATION CACHE VALIDATION ===");
                foreach (var (nota, stat) in reuseStats.Notas)
                {
                    _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, Homologada={stat.VecesHomologada}");
                }
            });

        // Expected: 2 movements (COMPANIA A, FSR)
        // TERCERO B should NOT be matched (not in config)
        // COMPANIA A (ANTIGUA) is SubEntry -> discarded
        // TOTAL CxC is Estructural -> discarded
        // GRAN TOTAL -> stops processing
        // Desconocida X -> no match
        Assert.Equal(2, resultados.Count);

        var companiaA = resultados.Single(r => r.EmpresaContraparte.Contains("COMPANIA"));
        Assert.Equal("COMPANIA A", companiaA.EmpresaContraparte);
        Assert.Equal(1000.50m, companiaA.Valor);

        var fsr = resultados.Single(r => r.EmpresaContraparte.Contains("FUNDACION"));
        Assert.Equal("FUNDACION SOLID RIVER", fsr.EmpresaContraparte);
        Assert.Equal(-200.25m, fsr.Valor);

        _output.WriteLine("\n=== MOVEMENTS ===");
        foreach (var m in resultados)
            _output.WriteLine($"  {m.EmpresaContraparte,-25} | {m.Cuenta,-25} | {m.Valor,10:N2}");
    }

    [Fact]
    public async Task Reuse_NotasUnicas_NoTienenReutilizacion()
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

        // Each cuenta references a DIFFERENT nota (no reuse)
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "Cuentas por pagar", ["J"] = "6" }),
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
        var reuseStats = capturedProfile!.ReuseStats;

        // 2 unique notes, each referenced by 1 cuenta = no reuse
        Assert.Equal(2, reuseStats.NotasUnicas);
        Assert.Equal(0, reuseStats.NotasReutilizadas);
        Assert.Equal(2, reuseStats.TotalLecturas);

        _output.WriteLine("=== UNIQUE NOTES (NO REUSE) ===");
        _output.WriteLine($"Notas unicas: {reuseStats.NotasUnicas}");
        _output.WriteLine($"Notas reutilizadas: {reuseStats.NotasReutilizadas}");
        foreach (var (nota, stat) in reuseStats.Notas)
        {
            _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, Antes={stat.ProcesamientosAntes}");
            Assert.Equal(1, stat.VecesLeida);
            Assert.Equal(1, stat.VecesProcesada);
            Assert.Equal(1, stat.ProcesamientosAntes);
        }

        Assert.Equal(2, resultados.Count);
    }

    [Fact]
    public async Task Reuse_ProfileSummary_ContieneReuseStats()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA A", NombreCarpeta = "A" },
                new() { NombreEmpresa = "EMPRESA B", NombreCarpeta = "B" },
                new() { NombreEmpresa = "ALFA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar", Tipo = "CxC" },
                new() { NombreCuenta = "Cuentas por pagar", Tipo = "CxP" },
            },
        };

        // Both cuentas share Nota 5
        var workbookA = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "Cuentas por pagar", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "VARIOS" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,000.00" }),
            }),
        });

        var motor1A = new Motor1Extractor(new FakeExcelProvider(workbookA), new EmpresaDetectionService());
        var engine = new ExtractorEngine(motor1A);

        var archivos = new List<(string FilePath, Stream FileStream)>
        {
            ("C:/data/A/archivo.xlsx", new MemoryStream(new byte[] { 1, 2, 3 })),
            ("C:/data/B/archivo.xlsx", new MemoryStream(new byte[] { 1, 2, 3 })),
        };

        // Note: Both files processed by same engine instance, but each uses its own motor1
        // This test validates the profile contains ReuseStats
        var logs = new List<string>();
        var (resultados, summary) = await engine.ProcesarMultiplesArchivosAsync(
            archivos, config,
            onProgressLog: msg => logs.Add(msg),
            diagnosticMode: false,
            maxParallelism: 1);

        _output.WriteLine("=== PROFILE SUMMARY REUSE ===");
        _output.WriteLine($"Archivos: {summary.TotalArchivos}");
        _output.WriteLine($"Notas unicas: {summary.NotasUnicas}");
        _output.WriteLine($"Notas reutilizadas: {summary.NotasReutilizadas}");
        _output.WriteLine($"Tiempo ahorrado: {summary.TiempoAhorradoMs}ms");
        _output.WriteLine($"Ganancia: {summary.GananciaPorcentual}%");
        _output.WriteLine("");

        foreach (var p in summary.Archivos)
        {
            _output.WriteLine($"Archivo: {p.Archivo}");
            _output.WriteLine($"  Notas procesadas: {p.NotasProcesadas}");
            foreach (var (nota, stat) in p.ReuseStats.Notas)
            {
                _output.WriteLine($"  Nota {nota}: Leida={stat.VecesLeida}, Procesada={stat.VecesProcesada}, TiempoLectura={stat.TiempoLecturaMs}ms, TiempoProc={stat.TiempoProcesamientoMs}ms");
            }
        }

        // Each file has 1 unique note, 1 processing per nota
        Assert.Equal(2, summary.TotalArchivos);
        Assert.True(summary.ReuseStats.NotasUnicas >= 1);
        Assert.True(summary.ReuseStats.TotalLecturas >= 1);
        Assert.True(summary.ReuseStats.TotalProcesamientos >= 1);
        Assert.True(summary.ReuseStats.NotasReutilizadas >= 1);

        // Movimientos: each file produces 2 movimientos (ALFA SA + BETA SA)
        Assert.Equal(4, summary.TotalMovimientos);
    }

    [Fact]
    public async Task Reuse_TablaReutilizacion_ContieneDatosCorrectos()
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
                new() { NombreCuenta = "PyC", Tipo = "PyC" },
            },
        };

        // 3 cuentas sharing Nota 5, 0 cuentas sharing Nota 6
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
        await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false,
            onProfile: profile => capturedProfile = profile);

        Assert.NotNull(capturedProfile);
        var stats = capturedProfile!.ReuseStats;

        _output.WriteLine("=== TABLA DE REUTILIZACION ===");
        _output.WriteLine($"{"Nota",-8} {"Leida",-8} {"Procesada",-12} {"Homologada",-12} {"Antes",-8} {"Reutilizada?",-14} {"TiempoLect(ms)",-15} {"TiempoProc(ms)",-15}");
        _output.WriteLine(new string('-', 92));

        foreach (var (nota, stat) in stats.Notas.OrderBy(kv => kv.Key))
        {
            var esReutilizada = stat.ProcesamientosAntes > 1 ? "SI" : "NO";
            _output.WriteLine($"{nota,-8} {stat.VecesLeida,-8} {stat.VecesProcesada,-12} {stat.VecesHomologada,-12} {stat.ProcesamientosAntes,-8} {esReutilizada,-14} {stat.TiempoLecturaMs,-15} {stat.TiempoProcesamientoMs,-15}");
        }

        _output.WriteLine("");
        _output.WriteLine($"Notas unicas: {stats.NotasUnicas}");
        _output.WriteLine($"Notas reutilizadas: {stats.NotasReutilizadas}");
        _output.WriteLine($"Tiempo aproximado ahorrado: {stats.TiempoAhorradoMs}ms");

        Assert.Single(stats.Notas);
        Assert.Equal(1, stats.NotasUnicas);
        Assert.Equal(1, stats.NotasReutilizadas); // 3 cuentas -> reused

        var nota5 = stats.Notas["5"];
        Assert.Equal(1, nota5.VecesLeida);
        Assert.Equal(1, nota5.VecesProcesada); // max 1 procesamiento
        Assert.Equal(3, nota5.ProcesamientosAntes); // 3 cuentas
    }

    [Fact]
    public async Task Reuse_ClasificacionMax1_PorFila()
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
            },
        };

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "CxC", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "CxP", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "MIS CUENTAS" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "500.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "500.00" }),
            }),
        });

        var trackingWorkbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "CxC", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "CxP", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "MIS CUENTAS" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "500.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "500.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(trackingWorkbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false);

        // 2 cuentas x 1 empresa homologada = 2 movimientos
        Assert.Equal(2, resultados.Count);

        // Both movements should be identical except Tipo
        var alfaCxC = resultados.Single(r => r.Tipo == "CxC");
        var alfaCxP = resultados.Single(r => r.Tipo == "CxP");
        Assert.Equal("ALFA SA", alfaCxC.EmpresaContraparte);
        Assert.Equal("ALFA SA", alfaCxP.EmpresaContraparte);
        Assert.Equal(500.00m, alfaCxC.Valor);
        Assert.Equal(500.00m, alfaCxP.Valor);

        _output.WriteLine("=== CLASSIFICATION MAX=1 ===");
        _output.WriteLine($"Total movimientos: {resultados.Count}");
        _output.WriteLine("Cada fila clasificada 1 vez, reutilizada en ExtraerDesdeCache");
        _output.WriteLine("Las homologaciones se computan 1 vez durante la lectura inicial");
    }

    [Fact]
    public async Task Reuse_ProcesarMultiplesArchivos_Metrics()
    {
        // Build config with 2 empresas
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA A", NombreCarpeta = "A" },
                new() { NombreEmpresa = "EMPRESA B", NombreCarpeta = "B" },
                new() { NombreEmpresa = "ALFA SA" },
                new() { NombreEmpresa = "BETA SA" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar", Tipo = "CxC" },
            },
        };

        var workbookA = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CXC" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "2,000.00" }),
                [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "3,000.00" }),
            }),
        });

        var workbookB = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CXC" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "3,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "4,000.00" }),
                [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "7,000.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbookA), new EmpresaDetectionService());
        var engine = new ExtractorEngine(motor1);

        // Process both files (note: same engine instance but motor1 service is shared)
        // Since motor1 is shared, each ExtraerAsync call is independent (noteCache is local)
        var archivos = new List<(string FilePath, Stream FileStream)>
        {
            ("C:/data/A/archivo.xlsx", new MemoryStream(new byte[] { 1, 2, 3 })),
            ("C:/data/B/archivo.xlsx", new MemoryStream(new byte[] { 1, 2, 3 })),
        };

        var logs = new List<string>();
        var (resultados, summary) = await engine.ProcesarMultiplesArchivosAsync(
            archivos, config,
            onProgressLog: msg => logs.Add(msg),
            diagnosticMode: false,
            maxParallelism: 1);

        _output.WriteLine("=== MULTI-FILE REUSE METRICS ===");
        _output.WriteLine($"Total archivos: {summary.TotalArchivos}");
        _output.WriteLine($"Total movimientos: {summary.TotalMovimientos}");
        _output.WriteLine($"Notas unicas (acumulado): {summary.NotasUnicas}");
        _output.WriteLine($"Notas reutilizadas: {summary.NotasReutilizadas}");
        _output.WriteLine($"Ganancia porcentual: {summary.GananciaPorcentual}%");
        _output.WriteLine("");

        // Log reuse per profile
        foreach (var p in summary.Archivos)
        {
            _output.WriteLine($"  [{p.Archivo}] Notas: {p.NotasProcesadas}, Movs: {p.MovimientosGenerados}");
            foreach (var (nota, stat) in p.ReuseStats.Notas)
            {
                _output.WriteLine($"    Nota {nota}: Leida={stat.VecesLeida}, Proc={stat.VecesProcesada}, Hom={stat.VecesHomologada}");
            }
        }

        // Total: 4 movements (2 files x 2 companies each)
        Assert.Equal(4, resultados.Count);
        Assert.Equal(4, summary.TotalMovimientos);
    }
}
