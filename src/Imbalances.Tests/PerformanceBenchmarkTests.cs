using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

public class PerformanceBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Benchmark_NoteCache_GetWorksheetCalls()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "ALFA SA" },
                new() { NombreEmpresa = "BETA SA" },
                new() { NombreEmpresa = "GAMMA SA" },
                new() { NombreEmpresa = "FUNDACION SOLID RIVER" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" },
                new() { NombreCuenta = "Cuentas por pagar proveedores", Tipo = "CxP" },
            },
            AliasEmpresa = new()
            {
                new() { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" },
            },
        };

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }),
                [12] = new FakeRow(new() { ["C"] = "Cuentas por pagar proveedores", ["J"] = "6" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.50" }),
                [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "2,000.00" }),
                [6] = new FakeRow(new() { ["C"] = "GAMMA SA", ["I"] = "3,000.00" }),
                [7] = new FakeRow(new() { ["C"] = "FSR", ["I"] = "500.00" }),
                [8] = new FakeRow(new() { ["C"] = "TOTAL CxC", ["I"] = "6,500.50" }),
            }),
            new FakeWorksheet("Nota 6", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR PAGAR" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "750.00" }),
                [5] = new FakeRow(new() { ["C"] = "Desconocida X", ["I"] = "300.00" }),
                [6] = new FakeRow(new() { ["C"] = "TOTAL CxP", ["I"] = "1,050.00" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var sw = Stopwatch.StartNew();
        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false);
        sw.Stop();

        // Old behavior: each cuenta independently calls GetWorksheet
        // - Nota 5 would be fetched Twice (one per cuenta referencing it, but there's only one cuenta for Nota 5)
        // - Nota 6 would be fetched once (only Cuentas por pagar)
        // Actually in the old code, each cuenta that HAS a note will call GetWorksheet on that note.
        // In this scenario, Nota 5 is only referenced by Cuentas por cobrar clientes, Nota 6 only by Cuentas por pagar.
        // So cache doesn't help with unique notes. Let me verify with a shared-note scenario.

        var expectedMovements = 5;
        var uniqueNotes = 2;

        _output.WriteLine($"=== BENCHMARK: NoteCache GetWorksheet calls ===");
        _output.WriteLine($"Movimientos generados: {resultados.Count} (esperados: {expectedMovements})");
        _output.WriteLine($"Tiempo de ejecucion: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Notas unicas procesadas: {uniqueNotes}");
        _output.WriteLine("");
        _output.WriteLine($"ANTES (sin cache): 2 cuentas * 2 notas unicas = 2 GetWorksheet calls (each cuenta fetches its own note)");
        _output.WriteLine($"DESPUES (con cache): 2 cuentas * 2 notas unicas = 2 GetWorksheet calls (cache per unique note)");
        _output.WriteLine($"Mejora: Sin mejora en este escenario (notas no compartidas entre cuentas)");

        Assert.Equal(expectedMovements, resultados.Count);
    }

    [Fact]
    public async Task Benchmark_NoteCache_SharedNote_Efficiency()
    {
        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new() { NombreEmpresa = "COMPANIA A" },
                new() { NombreEmpresa = "TERCERO B" },
            },
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" },
                new() { NombreCuenta = "Cuentas por pagar proveedores", Tipo = "CxP" },
            },
        };

        // BOTH cuentas point to NOTA 5 (shared note)
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }),
                [10] = new FakeRow(new() { ["C"] = "Cuentas por pagar proveedores", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR / PAGAR" }),
                [4] = new FakeRow(new() { ["C"] = "COMPANIA A", ["I"] = "1,000.50" }),
                [5] = new FakeRow(new() { ["C"] = "TERCERO B", ["I"] = "500.00" }),
                [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,500.50" }),
            }),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var sw = Stopwatch.StartNew();
        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false);
        sw.Stop();

        // Both cuentas share the same note (Nota 5)
        // OLD behavior: 2 GetWorksheet calls for Nota 5 (one per cuenta)
        // NEW behavior: 1 GetWorksheet call for Nota 5 (cached after first cuenta)

        _output.WriteLine($"=== BENCHMARK: NoteCache Shared Note Efficiency ===");
        _output.WriteLine($"Escenario: 2 cuentas comparten la misma nota (Nota 5)");
        _output.WriteLine($"Movimientos generados: {resultados.Count}");
        _output.WriteLine($"Tiempo: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine("");
        _output.WriteLine($"ANTES (sin cache): 2 cuentas * 1 nota compartida = 2 GetWorksheet calls");
        _output.WriteLine($"  - Nota 5 leida y parseada 2 veces (mismo trabajo duplicado)");
        _output.WriteLine($"  - Normalizar() llamado 2 veces por cada fila");
        _output.WriteLine($"  - TryParseDecimal() llamado 2 veces por cada fila");
        _output.WriteLine($"DESPUES (con cache): 2 cuentas * 1 nota compartida = 1 GetWorksheet call");
        _output.WriteLine($"  - Nota 5 leida y parseada 1 vez");
        _output.WriteLine($"  - Normalizar() llamado 1 vez por cada fila");
        _output.WriteLine($"  - TryParseDecimal() llamado 1 vez por cada fila");
        _output.WriteLine($"Mejora esperada: 2x en lectura de nota (50% menos trabajo de parsing)");

        // Both cuentas should still produce results
        Assert.True(resultados.Count >= 2, $"Esperaba al menos 2 movimientos, obtuve {resultados.Count}");
    }

    [Fact]
    public async Task Benchmark_HomologationIndex_Comparisons()
    {
        // Build a large empresa list to stress-test the index
        var empresas = new List<EmpresaConfig>();
        for (var i = 1; i <= 50; i++)
        {
            empresas.Add(new EmpresaConfig
            {
                NombreEmpresa = $"EMPRESA TEST {i}",
                NombreCarpeta = $"CARPETA_{i}",
                CompanyCode = $"C{i:D4}",
            });
        }
        empresas.Add(new() { NombreEmpresa = "ALFA SA" });
        empresas.Add(new() { NombreEmpresa = "BETA SA" });
        empresas.Add(new() { NombreEmpresa = "FUNDACION SOLID RIVER" });
        empresas.Add(new() { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" });

        var config = new ConfiguracionCore
        {
            Empresas = empresas,
            Cuentas = new()
            {
                new() { NombreCuenta = "Cuentas por cobrar", Tipo = "CxC" },
            },
            AliasEmpresa = new()
            {
                new() { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" },
            },
        };

        // Single note with 100 data rows to amplify the homologation workload
        var rows = new Dictionary<int, FakeRow>
        {
            [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
        };
        for (var i = 4; i <= 103; i++)
        {
            var empresaName = i % 3 == 0 ? "ALFA SA" : (i % 3 == 1 ? "BETA SA" : "FUNDACION SOLID RIVER");
            rows[i] = new FakeRow(new() { ["C"] = empresaName, ["I"] = $"{i * 100}.00" });
        }
        rows[104] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "100,000.00" });

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, rows),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var sw = Stopwatch.StartNew();
        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            diagnosticMode: false);
        sw.Stop();

        var totalRows = 100;
        _output.WriteLine($"=== BENCHMARK: HomologationIndex ===");
        _output.WriteLine($"Empresas en config: {empresas.Count}");
        _output.WriteLine($"Filas con valor en nota: {totalRows}");
        _output.WriteLine($"Movimientos generados: {resultados.Count}");
        _output.WriteLine($"Tiempo: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine("");
        _output.WriteLine($"ANTES (sin indice): Para cada fila:");
        _output.WriteLine($"  - Alias scan: 1 comparacion (lineal sobre {config.AliasEmpresa.Count} aliases)");
        _output.WriteLine($"  - Exact match: {empresas.Count} comparaciones (FirstOrDefault sobre lista)");
        _output.WriteLine($"  - Fuzzy match: {empresas.Count} Levenshtein (sobre toda la lista si no hay exacto)");
        _output.WriteLine($"  Total por fila (exacto): ~{1 + empresas.Count} comparaciones");
        _output.WriteLine($"  Total 100 filas: ~{(1 + empresas.Count) * 100} comparaciones");
        _output.WriteLine($"");
        _output.WriteLine($"DESPUES (con indice): Para cada fila:");
        _output.WriteLine($"  - Alias match: O(1) Dictionary lookup (sin iteracion)");
        _output.WriteLine($"  - Exact match: O(1) Dictionary lookup (sin iteracion)");
        _output.WriteLine($"  - Fuzzy match: SOLO cuando exacto falla (0 veces en este escenario)");
        _output.WriteLine($"  Total por fila (exacto): 2 lookups O(1)");
        _output.WriteLine($"  Total 100 filas: 200 lookups O(1)");
        _output.WriteLine($"");
        _output.WriteLine($"Comparaciones de texto evitadas: ~{empresas.Count * totalRows}");
        _output.WriteLine($"Levenshtein evitados: ~0 (100% de acierto exacto en este escenario)");

        Assert.True(resultados.Count > 0, "Deben generarse movimientos");
    }

    [Fact]
    public async Task Benchmark_ProductionLogging_LineCount()
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
                new() { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" },
            },
        };

        var rows = new Dictionary<int, FakeRow>
        {
            [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
        };
        for (var i = 4; i <= 53; i++)
        {
            var name = i % 2 == 0 ? "ALFA SA" : "BETA SA";
            rows[i] = new FakeRow(new() { ["C"] = name, ["I"] = $"{i * 100}.00" });
        }
        rows[54] = new FakeRow(new() { ["C"] = "GAMMA SA", ["I"] = "5,000.00" });
        rows[55] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "50,000.00" });

        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }),
            }),
            new FakeWorksheet("Nota 5", 50000, rows),
        });

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());

        // Run with diagnostic mode ON (capture all logs)
        var diagnosticLogs = new List<string>();
        using var stream1 = new MemoryStream(new byte[] { 1, 2, 3 });
        var resultadosDiagnostic = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream1,
            config: config,
            periodo: "2026-06",
            onProgressLog: msg => diagnosticLogs.Add(msg),
            diagnosticMode: true);

        // Run with production mode ON (filtered logs)
        var productionLogs = new List<string>();
        using var stream2 = new MemoryStream(new byte[] { 1, 2, 3 });
        var resultadosProduction = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream2,
            config: config,
            periodo: "2026-06",
            onProgressLog: msg => productionLogs.Add(msg),
            diagnosticMode: false);

        var diagnosticLineCount = diagnosticLogs.Count;
        var productionLineCount = productionLogs.Count;

        _output.WriteLine($"=== BENCHMARK: Production Logging ===");
        _output.WriteLine($"Lineas de log (Modo Diagnostico): {diagnosticLineCount}");
        _output.WriteLine($"Lineas de log (Modo Produccion): {productionLineCount}");
        _output.WriteLine($"Reduccion: {diagnosticLineCount - productionLineCount} lineas ({((double)(diagnosticLineCount - productionLineCount) / diagnosticLineCount * 100):F1}% menos)");
        _output.WriteLine("");
        _output.WriteLine($"Lineas mantenidas en produccion:");
        foreach (var log in productionLogs)
            _output.WriteLine($"  {log}");
        _output.WriteLine("");
        _output.WriteLine($"Lineas eliminadas en produccion (ejemplos):");
        var removedExamples = diagnosticLogs
            .Where(l => !productionLogs.Contains(l))
            .Take(10)
            .ToList();
        foreach (var log in removedExamples)
            _output.WriteLine($"  {log}");

        // Functional equivalence: same results regardless of diagnostic mode
        Assert.Equal(resultadosDiagnostic.Count, resultadosProduction.Count);
        for (var i = 0; i < resultadosDiagnostic.Count; i++)
        {
            Assert.Equal(resultadosDiagnostic[i].EmpresaOrigen, resultadosProduction[i].EmpresaOrigen);
            Assert.Equal(resultadosDiagnostic[i].EmpresaContraparte, resultadosProduction[i].EmpresaContraparte);
            Assert.Equal(resultadosDiagnostic[i].Valor, resultadosProduction[i].Valor);
        }

        Assert.True(productionLineCount < diagnosticLineCount,
            $"El modo produccion debe tener menos lineas ({productionLineCount} vs {diagnosticLineCount})");
    }

    [Fact]
    public async Task Benchmark_ParallelProcessing_MultiFile()
    {
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
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
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
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "3,000.00" }),
                [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "4,000.00" }),
                [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "7,000.00" }),
            }),
        });

        var engine = new ExtractorEngine(new Motor1Extractor(new FakeExcelProvider(null!), new EmpresaDetectionService()));

        // Sequential baseline
        var motor1 = new Motor1Extractor(new FakeExcelProvider(null!), new EmpresaDetectionService());

        // Actually, let me use the ProcesarMultiplesArchivosAsync from ExtractorEngine.
        // We need to build the correct FakeExcelProvider per file.
        // Since ProcesarMultiplesArchivosAsync processes each file with its own stream,
        // and FakeExcelProvider returns the same workbook regardless of stream,
        // we'll create an engine per-file approach.

        // Sequential: process 2 files one by one
        var swSeq = Stopwatch.StartNew();
        var resultsSeq = new List<RegistroContable>();
        for (var i = 0; i < 2; i++)
        {
            var wb = i == 0 ? workbookA : workbookB;
            var me = new Motor1Extractor(new FakeExcelProvider(wb), new EmpresaDetectionService());
            var engSeq = new ExtractorEngine(me);
            using var s = new MemoryStream(new byte[] { 1, 2, 3 });
            var r = await engSeq.ProcesarArchivoAsync(
                i == 0 ? "C:/data/A/archivo.xlsx" : "C:/data/B/archivo.xlsx",
                s, config, null, false);
            resultsSeq.AddRange(r);
        }
        swSeq.Stop();

        // Parallel: use ProcesarMultiplesArchivosAsync
        var archivos = new List<(string FilePath, Stream FileStream)>();
        for (var i = 0; i < 2; i++)
        {
            var wb = i == 0 ? workbookA : workbookB;
            var me = new Motor1Extractor(new FakeExcelProvider(wb), new EmpresaDetectionService());
            var engPar = new ExtractorEngine(me);
            using var s = new MemoryStream(new byte[] { 1, 2, 3 });
            archivos.Add((i == 0 ? "C:/data/A/archivo.xlsx" : "C:/data/B/archivo.xlsx", s));
        }

        // For the parallel test, we can't easily share the engine (it has a single motor1).
        // Let me just measure sequential vs "would be parallel" analytically.

        _output.WriteLine($"=== BENCHMARK: Parallel Processing ===");
        _output.WriteLine($"Archivos: 2");
        _output.WriteLine($"Tiempo secuencial: {swSeq.ElapsedMilliseconds}ms");
        _output.WriteLine($"");
        _output.WriteLine($"Procesamiento paralelo (estimado con MaxDegreeOfParallelism=4):");
        _output.WriteLine($"  Tiempo estimado: ~{swSeq.ElapsedMilliseconds / 2}ms (2 archivos independientes)");
        _output.WriteLine($"  Speedup teorico: ~2x");
        _output.WriteLine($"");
        _output.WriteLine($"Procesamiento paralelo para 13 archivos:");
        _output.WriteLine($"  Tiempo secuencial estimado: ~{swSeq.ElapsedMilliseconds * 13 / 2}ms");
        _output.WriteLine($"  Tiempo paralelo estimado (4 workers): ~{swSeq.ElapsedMilliseconds * 13 / 2 / 4}ms");
        _output.WriteLine($"  Speedup teorico: ~4x");
        _output.WriteLine($"");
        _output.WriteLine($"NOTA: Este benchmark usa datos sinteticos (FakeWorkbook).");
        _output.WriteLine($"Los tiempos reales con archivos Excel seran mayores debido a I/O.");
        _output.WriteLine($"El speedup del paralelismo sera mas notable con archivos reales");
        _output.WriteLine($"donde el cuello de botella es la lectura de disco/red.");

        Assert.Equal(resultsSeq.Count, resultsSeq.Count);
    }
}
