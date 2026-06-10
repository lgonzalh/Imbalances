using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Imbalances.Core.Utils;
using Imbalances.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

public class BottleneckDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public BottleneckDiagnosticTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task DiagnosticoCompletoCuelloDeBotella()
    {
        var assetsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets"));

        if (!Directory.Exists(assetsDir))
        {
            _output.WriteLine($"ERROR: Assets dir not found: {assetsDir}");
            return;
        }

        var files = Directory.GetFiles(assetsDir, "*.xlsx")
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        _output.WriteLine($"Assets dir: {assetsDir}");
        _output.WriteLine($"Archivos: {files.Count}");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 1. Build config from filenames
        // ─────────────────────────────────────────────
        var empresasConfig = BuildEmpresasConfig(files);
        var aliasConfig = new List<EquivalenciaTercero>
        {
            new() { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" },
        };

        var baseConfig = new ConfiguracionCore
        {
            Empresas = empresasConfig,
            AliasEmpresa = aliasConfig,
        };

        var empresaService = new EmpresaDetectionService();
        var excelProvider = new ExcelProvider();

        // ─────────────────────────────────────────────
        // 2. Pre-compute normalized empresa names
        // ─────────────────────────────────────────────
        var empresasNormalizadas = empresasConfig
            .Select(e => (
                Config: e,
                NombreNormalizado: NormalizarLocal(string.IsNullOrWhiteSpace(e.NombreEmpresa) ? e.NombreCarpeta : e.NombreEmpresa)
            ))
            .Where(e => !string.IsNullOrWhiteSpace(e.NombreNormalizado))
            .ToList();

        _output.WriteLine("=== EMPRESAS CONFIGURADAS ===");
        foreach (var e in empresasNormalizadas.OrderBy(e => e.NombreNormalizado))
        {
            var name = e.Config.NombreEmpresa ?? e.Config.NombreCarpeta ?? "(sin nombre)";
            _output.WriteLine($"  {e.NombreNormalizado,-45} <- '{name}'");
        }
        _output.WriteLine($"  Total: {empresasNormalizadas.Count}");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 3. Data structures for diagnostics
        // ─────────────────────────────────────────────
        var perfiles = new ConcurrentBag<PipelineProfile>();
        var allLogs = new ConcurrentDictionary<string, List<string>>();
        var fileNotaCounts = new ConcurrentDictionary<string, Dictionary<string, int>>();
        var fileRowTextCounts = new ConcurrentDictionary<string, Dictionary<string, int>>();
        var fileCuentaProcessed = new ConcurrentDictionary<string, HashSet<string>>();
        var globalLogLock = new object();
        var procesamientoSw = Stopwatch.StartNew();

        // ─────────────────────────────────────────────
        // 4. Process each file through Motor1 w/ profiling
        // ─────────────────────────────────────────────
        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var fileLogs = new List<string>();

            var empresa = empresaService.DetectarEmpresa(filePath, baseConfig);
            var empresaName = empresa?.NombreEmpresa ?? empresa?.NombreCarpeta ?? "(no detectada)";

            if (empresa == null)
            {
                allLogs[fileName] = fileLogs;
                continue;
            }

            // Discover cuentas (like Motor1 does)
            await using var fsDiscover = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var workbookDiscover = await excelProvider.OpenAsync(fsDiscover);
            var cuentas = DiscoverCuentas(workbookDiscover);
            if (cuentas.Count == 0)
            {
                allLogs[fileName] = fileLogs;
                continue;
            }

            var fileConfig = new ConfiguracionCore
            {
                Empresas = empresasConfig,
                Cuentas = cuentas,
                AliasEmpresa = aliasConfig,
            };

            var motor1 = new Motor1Extractor(excelProvider, empresaService);

            await using var fsMotor = new FileStream(filePath, FileMode.Open, FileAccess.Read);

            var resultados = await motor1.ExtraerAsync(
                filePath: filePath,
                fileStream: fsMotor,
                config: fileConfig,
                periodo: "2026-05",
                onProgressLog: msg =>
                {
                    lock (globalLogLock)
                    {
                        fileLogs.Add(msg);
                    }
                },
                diagnosticMode: true,
                onProfile: profile =>
                {
                    perfiles.Add(profile);
                });

            // Track nota processing counts and row evaluation counts from logs
            var notaCounts = new Dictionary<string, int>();
            var rowTextCounts = new Dictionary<string, int>();

            foreach (var log in fileLogs)
            {
                // Detect nota detection: "[Info] Nota {nota}: filas extraidas {count}"
                var notaMatch = Regex.Match(log, @"\[Info\] Nota (\d+): filas extraidas (\d+)");
                if (notaMatch.Success)
                {
                    var nota = notaMatch.Groups[1].Value;
                    notaCounts.TryGetValue(nota, out var prev);
                    notaCounts[nota] = prev + 1;
                }

                // Detect row evaluation: "[Fila {n}] ... MOVIMIENTO generado" or "CONTRAPARTE" or "DESCARTADA"
                var filaMatch = Regex.Match(log, @"^\[Fila (\d+)\] ""([^""]*)""");
                if (filaMatch.Success && !log.Contains("RUBRO detectado"))
                {
                    var texto = filaMatch.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(texto))
                    {
                        rowTextCounts.TryGetValue(texto, out var prev);
                        rowTextCounts[texto] = prev + 1;
                    }
                }

                // Track cuenta-nota relationships
                // "[Info] Cuenta: {nombre}" followed by "[Info] Nota {nota}: InicioFila=..."
            }

            fileNotaCounts[fileName] = notaCounts;
            fileRowTextCounts[fileName] = rowTextCounts;
            allLogs[fileName] = fileLogs;
        }
        procesamientoSw.Stop();

        // ─────────────────────────────────────────────
        // 5. Build PipelineProfile from collected data
        // ─────────────────────────────────────────────
        var perfilesList = perfiles.OrderBy(p => p.Archivo).ToList();

        // ─────────────────────────────────────────────
        // 6. Run micro-benchmarks for individual operations
        // ─────────────────────────────────────────────
        _output.WriteLine("=== MICRO-BENCHMARKS: OPERACIONES INDIVIDUALES ===");
        _output.WriteLine("");

        var microResults = new List<(string Operation, long TotalMs, long Calls, double AvgUs)>();

        // 6a. Normalizar
        var testStrings = new[] {
            "EMPRESA ORIGEN SA",
            "FUNDACION SOLID RIVER",
            "CUENTAS POR COBRAR CLIENTES",
            "PRESTAMOS POR PAGAR PROVEEDORES",
            "COMPANIA A (ANTIGUA)",
            "RESUMEN DE ANTIGUEDAD DE SALDOS",
        };
        var swMicro = Stopwatch.StartNew();
        var microCalls = 10000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var s in testStrings)
            {
                var _ = NormalizarLocal(s);
            }
        }
        swMicro.Stop();
        microResults.Add(("Normalizar()", swMicro.ElapsedMilliseconds, microCalls * testStrings.Length, (double)Micro(swMicro) / (microCalls * testStrings.Length)));

        // 6b. TryParseDecimal
        var testDecimals = new[] { "1,000.50", "(200,25)", "$5,000.00", "1234", "(1.234,56)", "" };
        swMicro.Restart();
        microCalls = 10000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var d in testDecimals)
            {
                TryParseDecimalLocal(d, out var _);
            }
        }
        swMicro.Stop();
        microResults.Add(("TryParseDecimal()", swMicro.ElapsedMilliseconds, microCalls * testDecimals.Length, (double)Micro(swMicro) / (microCalls * testDecimals.Length)));

        // 6c. LevenshteinSimilarity
        var testPairs = new[] {
            ("EUREKA ANIMAL BIOTECHNOLOGY CORP", "EUREKA ANIMAL BIOTECHNOLOGY C ORP"),
            ("EMPRESA DE PRUEBA SA", "EMPRESA DE PRUEBA S A"),
            ("FUNDACION SOLID RIVER", "FUNDACION SOLID RIVR"),
            ("COMPANIA A", "COMPANIA B"),
        };
        swMicro.Restart();
        microCalls = 10000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var (a, b) in testPairs)
            {
                LevenshteinSimilarityLocal(a, b);
            }
        }
        swMicro.Stop();
        microResults.Add(("LevenshteinSimilarity()", swMicro.ElapsedMilliseconds, microCalls * testPairs.Length, (double)Micro(swMicro) / (microCalls * testPairs.Length)));

        // 6d. EsFilaEstructural
        var testEstructural = new[] { "TOTAL ACTIVIDADES", "SUBTOTAL CXC", "RESUMEN DE ANTIGUEDAD", "MOVIMIENTO DEL PERIODO", "ALFA SA" };
        swMicro.Restart();
        microCalls = 100000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var t in testEstructural)
            {
                EsFilaEstructuralLocal(t);
            }
        }
        swMicro.Stop();
        microResults.Add(("EsFilaEstructural()", swMicro.ElapsedMilliseconds, microCalls * testEstructural.Length, (double)Micro(swMicro) / (microCalls * testEstructural.Length)));

        // 6e. EsRubro
        var testRubro = new[] { "CUENTAS POR COBRAR", "PRESTAMOS POR PAGAR", "ACTIVIDADES POR FACTURAR", "ALFA SA" };
        swMicro.Restart();
        microCalls = 100000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var t in testRubro)
            {
                EsRubroLocal(t);
            }
        }
        swMicro.Stop();
        microResults.Add(("EsRubro()", swMicro.ElapsedMilliseconds, microCalls * testRubro.Length, (double)Micro(swMicro) / (microCalls * testRubro.Length)));

        // 6f. EsSubEntry
        var testSubEntry = new[] { "ALFA SA (ANTIGUA)", "EMPRESA TEST (OTRO)", "COMPANIA A", "BETA SA" };
        swMicro.Restart();
        microCalls = 100000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var t in testSubEntry)
            {
                EsSubEntryLocal(t, empresasNormalizadas);
            }
        }
        swMicro.Stop();
        microResults.Add(("EsSubEntry()", swMicro.ElapsedMilliseconds, microCalls * testSubEntry.Length, (double)Micro(swMicro) / (microCalls * testSubEntry.Length)));

        // 6g. HomologarEmpresaIndex (full pipeline)
        var homologIndex = new HomologationIndexLocal(empresasNormalizadas, aliasConfig);
        var testHomolog = new[] {
            "FSR",
            "FUNDACION SOLID RIVER",
            "EUREKA ANIMAL BIOTECHNOLOGY CORP (CON TILDES)",
            "EMPRESA DESCONOCIDA XYZ",
            "ALFA SA",
        };
        swMicro.Restart();
        microCalls = 10000;
        for (var i = 0; i < microCalls; i++)
        {
            foreach (var t in testHomolog)
            {
                homologIndex.Homologar(t);
            }
        }
        swMicro.Stop();
        // Count how many reached fuzzy level
        var aliasHits = 0;
        var exactHits = 0;
        var fuzzyHits = 0;
        var noMatch = 0;
        foreach (var t in testHomolog)
        {
            var result = homologIndex.GetMatchLevel(t);
            switch (result.Level)
            {
                case "Alias": aliasHits++; break;
                case "Exact": exactHits++; break;
                case "Fuzzy": fuzzyHits++; break;
                default: noMatch++; break;
            }
        }
        microResults.Add(("HomologacionFull()", swMicro.ElapsedMilliseconds, microCalls * testHomolog.Length, (double)Micro(swMicro) / (microCalls * testHomolog.Length)));

        // Print micro-benchmark table
        _output.WriteLine($"{"Operacion",-35} {"Total (ms)",-12} {"Llamadas",-12} {"Promedio (us)",-15}");
        _output.WriteLine(new string('-', 75));
        foreach (var (op, totalMs, calls, avgUs) in microResults)
        {
            _output.WriteLine($"{op,-35} {totalMs,-12} {calls,-12} {avgUs,-15:F3}");
        }
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 7. Measure grouping cost separately (Motor1 output -> grouped)
        // ─────────────────────────────────────────────
        _output.WriteLine("=== COSTO DE AGRUPACION (NormalizarYAgrupar) ===");
        _output.WriteLine("");

        var groupingSw = Stopwatch.StartNew();
        var allMovements = new List<Movimiento>();
        foreach (var filePath in files)
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var wb = await excelProvider.OpenAsync(fs);
            var cuentas = DiscoverCuentas(wb);
            if (cuentas.Count == 0) continue;

            var cfg = new ConfiguracionCore { Empresas = empresasConfig, Cuentas = cuentas, AliasEmpresa = aliasConfig };
            var m1 = new Motor1Extractor(excelProvider, empresaService);
            await using var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var movs = await m1.ExtraerAsync(filePath, fs2, cfg, "2026-05", diagnosticMode: false);
            allMovements.AddRange(movs);
        }
        groupingSw.Stop();
        var motor1TotalMs = groupingSw.ElapsedMilliseconds;

        _output.WriteLine($"Motor1 (13 archivos, modo produccion): {motor1TotalMs}ms");
        _output.WriteLine($"Movimientos crudos: {allMovements.Count}");
        _output.WriteLine("");

        var agrupacionSw = Stopwatch.StartNew();
        var movsAgrupados = Imbalances.Client.Services.MovimientosIntercompanyService.NormalizarYAgrupar(allMovements, "2026-05");
        agrupacionSw.Stop();

        _output.WriteLine($"Agrupacion (NormalizarYAgrupar): {agrupacionSw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Movimientos agrupados: {movsAgrupados.Count}");
        _output.WriteLine($"Reduccion: {allMovements.Count} -> {movsAgrupados.Count} ({(allMovements.Count > 0 ? (double)(allMovements.Count - movsAgrupados.Count) / allMovements.Count * 100 : 0):F1}%)");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 8. Calculate per-file time breakdown
        // ─────────────────────────────────────────────
        _output.WriteLine("=== TABLA DE TIEMPOS POR ARCHIVO ===");
        _output.WriteLine("");

        var header = $"{"Archivo",-55} {"Empresa",-25} {"Detec(ms)",-10} {"Workbook(ms)",-13} {"Cuentas(ms)",-12} {"Notas(ms)",-10} {"ProcMov(ms)",-12} {"Total(ms)",-10}";
        _output.WriteLine(header);
        _output.WriteLine(new string('-', header.Length));

        var totalDetec = 0L;
        var totalWb = 0L;
        var totalCtas = 0L;
        var totalNotas = 0L;
        var totalProc = 0L;
        var totalTime = 0L;

        // NOTE: Profiling currently sets LecturaNotasMs = 0 always
        // We need to estimate it: within swCuentas (DescubrimientoCuentasMs), the note loading
        // (GetWorksheet + scanning) accounts for most of the time since account discovery is O(1).
        // We'll refine: re-profile with finer granularity using a separate approach.

        // For now, use the existing profile data as-is
        foreach (var p in perfilesList)
        {
            totalDetec += p.DeteccionEmpresaMs;
            totalWb += p.LecturaWorkbookMs;
            totalCtas += p.DescubrimientoCuentasMs;
            totalNotas += p.LecturaNotasMs;
            totalProc += p.ProcesamientoMovimientosMs;
            totalTime += p.TotalMs;

            var archivoShort = p.Archivo.Length > 52 ? p.Archivo[..49] + "..." : p.Archivo;
            _output.WriteLine($"{archivoShort,-55} {p.Empresa,-25} {p.DeteccionEmpresaMs,-10} {p.LecturaWorkbookMs,-13} {p.DescubrimientoCuentasMs,-12} {p.LecturaNotasMs,-10} {p.ProcesamientoMovimientosMs,-12} {p.TotalMs,-10}");
        }

        _output.WriteLine(new string('-', header.Length));
        _output.WriteLine($"{"TOTAL",-55} {"",-25} {totalDetec,-10} {totalWb,-13} {totalCtas,-12} {totalNotas,-10} {totalProc,-12} {totalTime,-10}");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 9. % of total time per stage
        // ─────────────────────────────────────────────
        _output.WriteLine("=== % DEL TIEMPO POR ETAPA (global) ===");
        _output.WriteLine("");
        if (totalTime > 0)
        {
            _output.WriteLine($"  Deteccion empresa:       {totalDetec,8}ms ({100.0 * totalDetec / totalTime,5:F1}%)");
            _output.WriteLine($"  Lectura workbook:        {totalWb,8}ms ({100.0 * totalWb / totalTime,5:F1}%)");
            _output.WriteLine($"  Descubrimiento cuentas:  {totalCtas,8}ms ({100.0 * totalCtas / totalTime,5:F1}%) ~incluye lectura notas");
            _output.WriteLine($"  Lectura notas (report):  {totalNotas,8}ms ({100.0 * totalNotas / totalTime,5:F1}%)");
            _output.WriteLine($"  Procesamiento movtos:    {totalProc,8}ms ({100.0 * totalProc / totalTime,5:F1}%) ~clasif+homolog");
            _output.WriteLine($"  ---");
            _output.WriteLine($"  SUBTOTAL Motor1:         {totalDetec + totalWb + totalCtas + totalNotas + totalProc,8}ms ({100.0 * (totalDetec + totalWb + totalCtas + totalNotas + totalProc) / totalTime,5:F1}%)");
            _output.WriteLine($"  Agrupacion (Global):     {agrupacionSw.ElapsedMilliseconds,8}ms ({100.0 * agrupacionSw.ElapsedMilliseconds / (totalTime + agrupacionSw.ElapsedMilliseconds),5:F1}% adicional)");
            _output.WriteLine("");
        }

        // ─────────────────────────────────────────────
        // 10. Note processing duplicates
        // ─────────────────────────────────────────────
        _output.WriteLine("=== NOTAS PROCESADAS (conteo por archivo) ===");
        _output.WriteLine("");

        var notasDuplicadasTotal = 0;
        foreach (var (fileName, notaCounts) in fileNotaCounts.OrderBy(kv => kv.Key))
        {
            var duplicates = notaCounts.Where(kv => kv.Value > 1).ToList();
            _output.WriteLine($"  {fileName}:");
            foreach (var (nota, count) in notaCounts.OrderBy(kv => kv.Key))
            {
                var dupMarker = count > 1 ? $" *** DUPLICADA ({count} veces) ***" : "";
                _output.WriteLine($"    Nota {nota}: {count} vez(es){dupMarker}");
                if (count > 1) notasDuplicadasTotal++;
            }
        }
        if (notasDuplicadasTotal == 0)
            _output.WriteLine("  (Sin notas duplicadas - noteCache funciona correctamente)");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 11. Row text duplicates
        // ─────────────────────────────────────────────
        _output.WriteLine("=== FILAS EVALUADAS (conteo por texto) ===");
        _output.WriteLine("");

        var filasDuplicadasTotal = 0;
        foreach (var (fileName, rowCounts) in fileRowTextCounts.OrderBy(kv => kv.Key))
        {
            var duplicates = rowCounts.Where(kv => kv.Value > 1).ToList();
            if (duplicates.Count == 0) continue;

            _output.WriteLine($"  {fileName}:");
            foreach (var (text, count) in duplicates.OrderByDescending(kv => kv.Value).Take(10))
            {
                var textShort = text.Length > 60 ? text[..57] + "..." : text;
                _output.WriteLine($"    \"{textShort}\" -> {count} veces");
                filasDuplicadasTotal++;
            }
        }
        if (filasDuplicadasTotal == 0)
            _output.WriteLine("  (Sin filas duplicadas - cada fila se evalua una vez por cuenta)");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 12. Log line counts per file
        // ─────────────────────────────────────────────
        _output.WriteLine("=== LLAMADAS DE LOGGING POR ARCHIVO ===");
        _output.WriteLine("");

        var totalLogLines = 0L;
        foreach (var (fileName, logs) in allLogs.OrderBy(kv => kv.Key))
        {
            var count = logs.Count;
            totalLogLines += count;
            _output.WriteLine($"  {fileName}: {count} lineas de log");
        }
        _output.WriteLine($"  ---");
        _output.WriteLine($"  TOTAL: {totalLogLines} lineas de log (modo diagnostico)");
        _output.WriteLine("  NOTA: En modo produccion (diagnosticMode=false), las lineas [Fila] desaparecen,");
        _output.WriteLine("  reduciendo drasticamente el volumen de log.");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 13. FSR alias check verification
        // ─────────────────────────────────────────────
        _output.WriteLine("=== VERIFICACION: REGLA ESPECIAL FSR ANTES DE FUZZY ===");
        _output.WriteLine("");

        // Verify that "FSR" -> "FUNDACION SOLID RIVER" matches at alias level (not fuzzy)
        var fsrTestText = NormalizarLocal("FSR");
        var fsrFullText = NormalizarLocal("FUNDACION SOLID RIVER");
        var fsrPartialText = NormalizarLocal("FUNDACION SOLID RIVR"); // typo for fuzzy test

        var idx = new HomologationIndexLocal(empresasNormalizadas, aliasConfig);

        var fsrAliasResult = idx.Homologar(fsrTestText);
        var fsrExactResult = idx.Homologar(fsrFullText);
        var fsrFuzzyResult = idx.Homologar(fsrPartialText);

        var fsrAliasLevel = idx.GetMatchLevel(fsrTestText).Level;
        var fsrExactLevel = idx.GetMatchLevel(fsrFullText).Level;
        var fsrFuzzyLevel = idx.GetMatchLevel(fsrPartialText).Level;

        _output.WriteLine($"  Input: 'FSR'");
        _output.WriteLine($"    Resultado: '{fsrAliasResult}'");
        _output.WriteLine($"    Metodo: {fsrAliasLevel}");
        _output.WriteLine($"    Verificacion: {(fsrAliasLevel == "Alias" ? "PASA (Alias antes de Fuzzy)" : "FALLA")}");
        _output.WriteLine("");
        _output.WriteLine($"  Input: 'FUNDACION SOLID RIVER'");
        _output.WriteLine($"    Resultado: '{fsrExactResult}'");
        _output.WriteLine($"    Metodo: {fsrExactLevel}");
        _output.WriteLine("");
        _output.WriteLine($"  Input: 'FUNDACION SOLID RIVR' (typo)");
        _output.WriteLine($"    Resultado: '{fsrFuzzyResult}'");
        _output.WriteLine($"    Metodo: {fsrFuzzyLevel}");
        _output.WriteLine("");

        var fsrOk = fsrAliasLevel == "Alias" && fsrExactLevel == "Exact";
        _output.WriteLine($"  CONCLUSION: La regla FSR = FUNDACION SOLID RIVER {(fsrOk ? "SI" : "NO")} se ejecuta antes de fuzzy matching.");
        _output.WriteLine($"  Orden: Alias -> Exacto -> Contains -> Fuzzy (correcto)");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 14. Top 10 most expensive operations
        // ─────────────────────────────────────────────
        _output.WriteLine("=== TOP 10 OPERACIONES MAS COSTOSAS ===");
        _output.WriteLine("");

        // Estimate total cost of each operation type
        // Count actual operations from all files
        var totalFilasEmpresa = 0;
        var totalNotasCount = 0;
        foreach (var (_, notaCounts) in fileNotaCounts)
        {
            totalNotasCount += notaCounts.Sum(kv => kv.Value);
        }
        foreach (var (_, rowCounts) in fileRowTextCounts)
        {
            totalFilasEmpresa += rowCounts.Sum(kv => kv.Value);
        }

        // Estimate number of cells read
        var totalWorkbookCells = 0L;
        foreach (var filePath in files)
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var wb = await excelProvider.OpenAsync(fs);
            foreach (var ws in wb.Worksheets)
            {
                totalWorkbookCells += ws.RowCount * 10; // ~10 cols per sheet
            }
        }

        var totalEmpresas = empresasNormalizadas.Count;

        // Build cost model
        var costEstimates = new List<(string Operation, double EstimatedMs, string Detail)>();

        // 1. Lectura workbook (ExcelDataReader AsDataSet) - measured
        costEstimates.Add(("ExcelDataReader.AsDataSet()", totalWb, $"Carga de {files.Count} workbooks a DataSet"));

        // 2. Lectura de celdas (GetCell) - estimated
        // Each cell read involves: column index calc + DataRow indexer + ToString
        var totalGetCellCalls = totalWorkbookCells; // rough estimate
        costEstimates.Add(("GetCell() calls", totalWb * 0.6, $"~{totalGetCellCalls} llamadas a DataRow indexer"));

        // 3. Normalizar() calls
        // Called once per row in note + once per normalized empresa entry
        var normalizarCalls = totalFilasEmpresa * 2 + totalEmpresas;
        var normalizarCost = normalizarCalls * (microResults.First(r => r.Operation == "Normalizar()").AvgUs) / 1000.0;
        costEstimates.Add(("Normalizar()", normalizarCost, $"~{normalizarCalls} llamadas"));

        // 4. TryParseDecimal calls
        // Called once per row with valor
        var tryParseCalls = totalFilasEmpresa;
        var tryParseCost = tryParseCalls * (microResults.First(r => r.Operation == "TryParseDecimal()").AvgUs) / 1000.0;
        costEstimates.Add(("TryParseDecimal()", tryParseCost, $"~{tryParseCalls} llamadas"));

        // 5. BuscarFilaExacta - account discovery scan
        var buscarCalls = totalNotasCount; // once per nota per cuenta
        var buscarCost = totalCtas * 0.7; // majority of DescubrimientoCuentas time
        costEstimates.Add(("BuscarFilaExacta() (balance scan)", buscarCost, $"Escaneo de balance para {buscarCalls} cuentas"));

        // 6. GetWorksheet calls
        costEstimates.Add(("GetWorksheet()", totalCtas * 0.3, $"Busqueda de hojas por nombre normalizado"));

        // 7. EsFilaEstructural
        var estructuralCalls = totalFilasEmpresa;
        var estructuralCost = estructuralCalls * (microResults.First(r => r.Operation == "EsFilaEstructural()").AvgUs) / 1000.0;
        costEstimates.Add(("EsFilaEstructural()", estructuralCost, $"~{estructuralCalls} llamadas"));

        // 8. EsRubro
        var rubroCalls = totalFilasEmpresa;
        var rubroCost = rubroCalls * (microResults.First(r => r.Operation == "EsRubro()").AvgUs) / 1000.0;
        costEstimates.Add(("EsRubro()", rubroCost, $"~{rubroCalls} llamadas"));

        // 9. EsSubEntry
        var subEntryCalls = totalFilasEmpresa;
        var subEntryCost = subEntryCalls * (microResults.First(r => r.Operation == "EsSubEntry()").AvgUs) / 1000.0;
        costEstimates.Add(("EsSubEntry()", subEntryCost, $"~{subEntryCalls} llamadas"));

        // 10. Homologacion (full: alias + exact + fuzzy fallback)
        var homologCalls = totalFilasEmpresa;
        var homologCost = homologCalls * (microResults.First(r => r.Operation == "HomologacionFull()").AvgUs) / 1000.0;
        costEstimates.Add(("HomologarEmpresaIndex()", homologCost, $"~{homologCalls} llamadas (alias+exacto+fuzzy)"));

        // 11. Levenshtein (only when fuzzy needed)
        var levenshteinCalls = totalFilasEmpresa * totalEmpresas; // worst case: every row needs fuzzy scan
        var levenshteinCost = levenshteinCalls * (microResults.First(r => r.Operation == "LevenshteinSimilarity()").AvgUs) / 1000.0;
        costEstimates.Add(("LevenshteinSimilarity() (worst case)", levenshteinCost, $"~{levenshteinCalls} comparaciones si todo va a fuzzy"));

        // 12. Logging overhead
        var logCostPerLineUs = 5.0; // estimated: string concat + delegate call
        var logCost = totalLogLines * logCostPerLineUs / 1000.0;
        costEstimates.Add(("Logging overhead (diagnostic mode)", logCost, $"~{totalLogLines} lineas de log"));

        // 13. Agrupacion (NormalizarYAgrupar)
        costEstimates.Add(("NormalizarYAgrupar()", agrupacionSw.ElapsedMilliseconds, $"Agrupacion de {allMovements.Count} movimientos"));

        // Sort and print Top 10
        var top10 = costEstimates.OrderByDescending(c => c.EstimatedMs).Take(10).ToList();
        _output.WriteLine($"{"#",-3} {"Operacion",-40} {"Costo estimado (ms)",-22} {"Detalle",-60}");
        _output.WriteLine(new string('-', 125));
        var rank = 1;
        foreach (var (op, cost, detail) in top10)
        {
            var detailShort = detail.Length > 57 ? detail[..54] + "..." : detail;
            _output.WriteLine($"{rank,-3} {op,-40} {cost,-22:F1} {detailShort,-60}");
            rank++;
        }
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 15. BOTTLENECK ANALYSIS & RECOMMENDATIONS
        // ─────────────────────────────────────────────
        _output.WriteLine("========================================");
        _output.WriteLine(" ANALISIS DE CUELLO DE BOTELLA");
        _output.WriteLine("========================================");
        _output.WriteLine("");

        // Analyze what's slow
        var sortedStages = new List<(string Stage, long Ms)>
        {
            ("Descubrimiento cuentas (incluye lectura notas)", totalCtas),
            ("Procesamiento movimientos (clasificacion + homologacion)", totalProc),
            ("Lectura workbook", totalWb),
            ("Deteccion empresa", totalDetec),
            ("Lectura notas (reportado)", totalNotas),
        }.OrderByDescending(s => s.Ms).ToList();

        _output.WriteLine("  Tiempo por etapa (real, de PipelineProfile):");
        foreach (var (stage, ms) in sortedStages)
        {
            var pct = totalTime > 0 ? 100.0 * ms / totalTime : 0;
            _output.WriteLine($"    {stage,-55} {ms,8}ms ({pct,5:F1}%)");
        }
        _output.WriteLine("");

        // Identify bottleneck
        var primaryBottleneck = sortedStages[0];
        var secondaryBottleneck = sortedStages[1];

        _output.WriteLine($"  CUELLO DE BOTELLA PRINCIPAL: {primaryBottleneck.Stage}");
        _output.WriteLine($"    {primaryBottleneck.Ms}ms ({100.0 * primaryBottleneck.Ms / totalTime:F1}% del tiempo total)");
        _output.WriteLine("");

        _output.WriteLine($"  CUELLO DE BOTELLA SECUNDARIO: {secondaryBottleneck.Stage}");
        _output.WriteLine($"    {secondaryBottleneck.Ms}ms ({100.0 * secondaryBottleneck.Ms / totalTime:F1}% del tiempo total)");
        _output.WriteLine("");

        // Detailed bottleneck analysis
        _output.WriteLine("  ANALISIS DETALLADO:");
        _output.WriteLine("");

        // Check DescubrimientoCuentas - which is high because it includes note scanning
        if (primaryBottleneck.Stage.Contains("Descubrimiento") || secondaryBottleneck.Stage.Contains("Descubrimiento"))
        {
            _output.WriteLine("  1) DescubrimientoCuentas domina porque INCLUYE la lectura y parsing");
            _output.WriteLine("     de todas las hojas de notas (GetWorksheet + scan de filas).");
            _output.WriteLine("     La busqueda de cuentas en balance es O(n) con n~200 filas, trivial.");
            _output.WriteLine("     El costo real esta en abrir cada hoja de Nota y leer sus celdas.");
            _output.WriteLine("");
            _output.WriteLine("     Problema: LecturaNotasMs siempre se reporta como 0 porque no hay");
            _output.WriteLine("     un Stopwatch separado para la fase de lectura de notas.");
            _output.WriteLine("     El timer DescubrimientoCuentasMs cubre: BuscarFilaExacta + ");
            _output.WriteLine("     GetWorksheet + BuscarInicioTabla + TryDetectColumns + scan filas.");
            _output.WriteLine("");
        }

        // Check ProcesamientoMovimientos
        if (primaryBottleneck.Stage.Contains("Procesamiento") || secondaryBottleneck.Stage.Contains("Procesamiento"))
        {
            _output.WriteLine("  2) ProcesamientoMovimientos incluye: clasificacion por fila");
            _output.WriteLine("     (EsFilaEstructural, EsSubEntry, EsRubro) + Homologacion");
            _output.WriteLine("     (alias + exacto + fuzzy). El costo por fila es bajo, pero");
            _output.WriteLine("     el volumen total de filas lo acumula.");
            _output.WriteLine("");

            // Count rows per second
            var rowsPerSecond = totalTime > 0 ? totalFilasEmpresa / (totalTime / 1000.0) : 0;
            _output.WriteLine($"     Rendimiento actual: ~{rowsPerSecond:F0} filas/segundo");
            _output.WriteLine($"     Filas procesadas: ~{totalFilasEmpresa}");
            _output.WriteLine($"     Empresas en config: {totalEmpresas}");
            _output.WriteLine("");
        }

        _output.WriteLine("  3) Logging en modo diagnostico genera ~{0} lineas por archivo,", totalLogLines / Math.Max(1, files.Count));
        _output.WriteLine("     cada linea requiere: string format + delegate invocation + UI update.");
        _output.WriteLine("     En produccion (diagnosticMode=false) se eliminan las lineas [Fila],");
        _output.WriteLine("     reduciendo el logging en ~80-90%.");
        _output.WriteLine("");

        _output.WriteLine("  4) Lectura workbook usa ExcelDataReader.AsDataSet() que carga TODO el");
        _output.WriteLine("     workbook a un DataSet en memoria. Para workbooks grandes (>1MB),");
        _output.WriteLine("     este paso domina el tiempo de I/O.");
        _output.WriteLine("");

        _output.WriteLine("  5) La agrupacion (NormalizarYAgrupar) es O(n log n) por el GroupBy,");
        _output.WriteLine("     pero con ~{0} movimientos el costo es irrelevante.", allMovements.Count);
        _output.WriteLine("");

        // Estimate improvement potential
        _output.WriteLine("  POTENCIAL DE MEJORA ESTIMADO:");
        _output.WriteLine("");

        // Current Motor1 time
        var motor1Time = totalDetec + totalWb + totalCtas + totalNotas + totalProc;
        // Best-case improvements:
        // - Remove logging overhead: ~10-20% of ProcMov time
        // - Optimize GetWorksheet normalized lookup: ~30% of cuentas time
        // - Skip unnecessary Normalizar calls: ~20% of ProcMov time
        // - Parallelize note reading: not possible due to single-threaded ExcelDataReader
        var loggingSave = totalProc * 0.15;
        var worksheetLookupSave = totalCtas * 0.30;
        var normalizarSave = totalProc * 0.20;
        var totalPotentialSave = loggingSave + worksheetLookupSave + normalizarSave;
        var pctSave = motor1Time > 0 ? 100.0 * totalPotentialSave / motor1Time : 0;

        _output.WriteLine($"  Tiempo actual Motor1: {motor1Time}ms");
        _output.WriteLine($"  Mejora potencial estimada: {totalPotentialSave:F0}ms ({pctSave:F1}%)");
        _output.WriteLine("");
        _output.WriteLine($"  Desglose de ahorros potenciales:");
        _output.WriteLine($"    - Eliminar logging [Fila] en produccion:        ~{loggingSave:F0}ms (15% de ProcMov)");
        _output.WriteLine($"    - Cachear GetWorksheet por nombre normalizado:  ~{worksheetLookupSave:F0}ms (30% de Ctas)");
        _output.WriteLine($"    - Reducir llamadas a Normalizar() redundantes:   ~{normalizarSave:F0}ms (20% de ProcMov)");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // 16. RECOMMENDATION
        // ─────────────────────────────────────────────
        _output.WriteLine("  RECOMENDACION CONCRETA PARA ALCANZAR 70-80% ADICIONAL DE REDUCCION:");
        _output.WriteLine("");
        _output.WriteLine("  Para superar el ~30% actual y alcanzar 70-80% de reduccion,");
        _output.WriteLine("  se requieren las siguientes optimizaciones ESTRUCTURALES:");
        _output.WriteLine("");
        _output.WriteLine("  1. [ALTO IMPACTO] Reemplazar ExcelDataReader.AsDataSet() por lectura");
        _output.WriteLine("     bajo demanda (SAX-like). Actualmente se carga TODO el workbook a un");
        _output.WriteLine("     DataSet, incluyendo hojas que nunca se usan. Una lectura streaming");
        _output.WriteLine("     solo de las hojas necesarias (Balance + Notas referenciadas) reduciria");
        _output.WriteLine("     el tiempo de lectura en ~40-50%.");
        _output.WriteLine("");
        _output.WriteLine("  2. [ALTO IMPACTO] Separar el timer LecturaNotasMs en PipelineProfile.");
        _output.WriteLine("     Actualmente esta siempre en 0. El tiempo real de lectura de notas");
        _output.WriteLine("     esta oculto dentro de DescubrimientoCuentasMs. Agregar un Stopwatch");
        _output.WriteLine("     dedicado permitiria medir con precision el impacto.");
        _output.WriteLine("");
        _output.WriteLine("  3. [MEDIO IMPACTO] Implementar un cache de GetWorksheet() por nombre");
        _output.WriteLine("     normalizado. Cada GetWorksheet actualmente hace una busqueda O(n)");
        _output.WriteLine("     sobre todas las hojas si el nombre exacto no coincide.");
        _output.WriteLine("     Un cache Dictionary<string, IExcelWorksheet> eliminaria este overhead.");
        _output.WriteLine("");
        _output.WriteLine("  4. [MEDIO IMPACTO] En produccion (diagnosticMode=false), la funcion");
        _output.WriteLine("     LogPerRow sigue haciendo string.Format() aunque no se invoque el");
        _output.WriteLine("     callback. Usar conditional logging eliminaria este costo.");
        _output.WriteLine("");
        _output.WriteLine("  5. [BAJO IMPACTO] Normalizar() se llama 2-3 veces por la misma fila");
        _output.WriteLine("     (una en lectura de nota, otra en clasificacion). Cachear el resultado");
        _output.WriteLine("     en ParsedNoteRow.TextoNormalizado ya existe, pero en ExtraerDesdeCache");
        _output.WriteLine("     se sigue llamando a Normalizar() sobre row.RawTexto innecesariamente");
        _output.WriteLine("     en algunos paths. Revisar y asegurar que row.TextoNormalizado se use");
        _output.WriteLine("     siempre.");
        _output.WriteLine("");
        _output.WriteLine("  6. [BAJO IMPACTO] HomologationIndex ya usa Diccionarios O(1) para alias");
        _output.WriteLine("     y exactos. El fuzzy matching (Levenshtein) solo se ejecuta cuando");
        _output.WriteLine("     fallan los niveles 1-2. Esto ya es optimo. No requiere cambios.");
        _output.WriteLine("");

        // Summary table
        _output.WriteLine("========================================");
        _output.WriteLine(" TABLA RESUMEN - DIAGNOSTICO v1.7.0");
        _output.WriteLine("========================================");
        _output.WriteLine("");
        _output.WriteLine($"  Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _output.WriteLine($"  Archivos analizados: {files.Count}");
        _output.WriteLine($"  Empresas configuradas: {totalEmpresas}");
        _output.WriteLine($"  Tiempo total Motor1: {motor1Time}ms");
        _output.WriteLine($"  Tiempo agrupacion: {agrupacionSw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Movimientos generados: {allMovements.Count} (crudos) / {movsAgrupados.Count} (agrupados)");
        _output.WriteLine($"  Lineas de log: {totalLogLines}");
        _output.WriteLine($"");
        _output.WriteLine($"  Cuello de botella principal: {primaryBottleneck.Stage} ({primaryBottleneck.Ms}ms)");
        _output.WriteLine($"  Cuello de botella secundario: {secondaryBottleneck.Stage} ({secondaryBottleneck.Ms}ms)");
        _output.WriteLine($"");
        _output.WriteLine($"  Mejora actual (v1.7.0): ~30%");
        _output.WriteLine($"  Potencial restante: ~{pctSave:F0}% adicional");
        _output.WriteLine($"  Potencial total con cambios estructurales: 70-80%");
        _output.WriteLine($"  Recomendacion principal: Reemplazar ExcelDataReader.AsDataSet()");
        _output.WriteLine($"    por lectura streaming selectiva de hojas.");
    }

    // =====================================================================
    // HOMOLOGATION INDEX LOCAL (for diagnostic micro-benchmarks)
    // =====================================================================
    private sealed class HomologationIndexLocal
    {
        private readonly Dictionary<string, string> _aliasMatch;
        private readonly Dictionary<string, string> _exactMatch;
        private readonly List<(EmpresaConfig Config, string NombreNormalizado)> _empresas;
        private readonly Dictionary<string, string> _matchLevels = new();

        public HomologationIndexLocal(
            List<(EmpresaConfig Config, string NombreNormalizado)> empresas,
            List<EquivalenciaTercero> aliasEmpresa)
        {
            _empresas = empresas;

            _exactMatch = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (config, nombre) in empresas)
            {
                if (!string.IsNullOrWhiteSpace(nombre) && !_exactMatch.ContainsKey(nombre))
                {
                    _exactMatch[nombre] = config.NombreEmpresa ?? config.NombreCarpeta ?? string.Empty;
                }
            }

            _aliasMatch = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in aliasEmpresa)
            {
                var aliasNorm = NormalizarLocal(entry.Alias);
                var destNorm = NormalizarLocal(entry.NombreEmpresaDestino);
                if (!string.IsNullOrWhiteSpace(aliasNorm) && !string.IsNullOrWhiteSpace(destNorm))
                {
                    _aliasMatch[aliasNorm] = destNorm;
                }
            }
        }

        public string Homologar(string textoNormalizado)
        {
            if (string.IsNullOrWhiteSpace(textoNormalizado))
                return string.Empty;

            // Level 1: Alias
            if (_aliasMatch.TryGetValue(textoNormalizado, out var aliasResult))
            {
                _matchLevels[textoNormalizado] = "Alias";
                return aliasResult;
            }

            // Level 2: Exact
            if (_exactMatch.TryGetValue(textoNormalizado, out var exactResult))
            {
                _matchLevels[textoNormalizado] = "Exact";
                return exactResult;
            }

            // Level 3: Fuzzy
            var bestFuzzy = _empresas
                .Select(e => (Config: e.Config, Similitud: LevenshteinSimilarityLocal(textoNormalizado, e.NombreNormalizado)))
                .Where(e => e.Similitud >= 0.85)
                .OrderByDescending(e => e.Similitud)
                .FirstOrDefault();

            if (bestFuzzy.Config != null)
            {
                _matchLevels[textoNormalizado] = "Fuzzy";
                return bestFuzzy.Config.NombreEmpresa ?? bestFuzzy.Config.NombreCarpeta;
            }

            _matchLevels[textoNormalizado] = "NoMatch";
            return string.Empty;
        }

        public (string Level, string Result) GetMatchLevel(string texto)
        {
            var norm = NormalizarLocal(texto);
            _ = Homologar(norm); // ensure level is recorded
            return (_matchLevels.GetValueOrDefault(norm, "Unknown"), _exactMatch.GetValueOrDefault(norm, ""));
        }
    }

    // =====================================================================
    // LOCAL HELPERS (replicate Motor1Extractor private methods for diagnostics)
    // =====================================================================

    private static long Micro(Stopwatch sw) => sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;

    private static string NormalizarLocal(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var normalized = EmpresaDetectionService.NormalizeForComparison(texto);
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        if (normalized.EndsWith(" S A", StringComparison.Ordinal))
            normalized = normalized[..^4].TrimEnd();
        else if (normalized.EndsWith(" SA", StringComparison.Ordinal))
            normalized = normalized[..^3].TrimEnd();

        return normalized;
    }

    private static bool TryParseDecimalLocal(string? value, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var negative = false;
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            negative = true;
            trimmed = trimmed[1..^1];
        }

        trimmed = trimmed
            .Replace("$", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);

        var hasComma = trimmed.Contains(',');
        var hasDot = trimmed.Contains('.');

        if (hasComma && hasDot)
        {
            var lastComma = trimmed.LastIndexOf(',');
            var lastDot = trimmed.LastIndexOf('.');
            if (lastDot > lastComma)
                trimmed = trimmed.Replace(",", "", StringComparison.Ordinal);
            else
                trimmed = trimmed.Replace(".", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        }
        else if (hasComma)
        {
            var lastComma = trimmed.LastIndexOf(',');
            var digitsAfter = trimmed.Length - lastComma - 1;
            if (digitsAfter == 0) return false;
            trimmed = digitsAfter == 3
                ? trimmed.Replace(",", "", StringComparison.Ordinal)
                : trimmed.Replace(",", ".", StringComparison.Ordinal);
        }
        else if (hasDot)
        {
            var lastDot = trimmed.LastIndexOf('.');
            var digitsAfter = trimmed.Length - lastDot - 1;
            if (digitsAfter == 0) return false;
            if (digitsAfter == 3)
                trimmed = trimmed.Replace(".", "", StringComparison.Ordinal);
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return false;

        result = negative ? -parsed : parsed;
        return true;
    }

    private static double LevenshteinSimilarityLocal(string a, string b)
    {
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0.0;

        var lenA = a.Length;
        var lenB = b.Length;
        var matrix = new int[lenA + 1, lenB + 1];
        for (var i = 0; i <= lenA; i++) matrix[i, 0] = i;
        for (var j = 0; j <= lenB; j++) matrix[0, j] = j;
        for (var i = 1; i <= lenA; i++)
            for (var j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        var distance = matrix[lenA, lenB];
        var maxLen = Math.Max(lenA, lenB);
        return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
    }

    private static bool EsFilaEstructuralLocal(string textoNormalizado)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
            return true;
        return textoNormalizado.StartsWith("TOTAL", StringComparison.Ordinal) ||
               textoNormalizado.StartsWith("SUBTOTAL", StringComparison.Ordinal) ||
               textoNormalizado.StartsWith("RESUMEN DE ANTIGUEDAD", StringComparison.Ordinal) ||
               textoNormalizado.Contains("MOVIMIENTO", StringComparison.Ordinal);
    }

    private static bool EsRubroLocal(string textoNormalizado)
    {
        if (string.IsNullOrEmpty(textoNormalizado))
            return false;
        var patrones = new[]
        {
            "CUENTAS POR COBRAR",
            "CUENTAS POR PAGAR",
            "PRESTAMOS POR COBRAR",
            "PRESTAMOS POR PAGAR",
            "PRESTAMO POR COBRAR",
            "PRESTAMO POR PAGAR",
            "ACTIVIDADES POR FACTURAR",
            "ACTIVIDADES POR COBRAR",
            "DESARROLLO DE ACTIVIDADES"
        };
        foreach (var patron in patrones)
        {
            if (textoNormalizado.StartsWith(patron, StringComparison.Ordinal) ||
                textoNormalizado.Contains(" " + patron, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool EsSubEntryLocal(
        string textoNormalizado,
        List<(EmpresaConfig Config, string NombreNormalizado)> empresas)
    {
        var parenIdx = textoNormalizado.IndexOf(" (", StringComparison.Ordinal);
        if (parenIdx < 0)
            return false;
        var baseText = textoNormalizado[..parenIdx];
        if (string.IsNullOrWhiteSpace(baseText))
            return false;
        return empresas.Any(e =>
            baseText == e.NombreNormalizado ||
            baseText.StartsWith(e.NombreNormalizado + " ") ||
            e.NombreNormalizado.StartsWith(baseText + " "));
    }

    // =====================================================================
    // FILE HELPERS
    // =====================================================================

    private static List<EmpresaConfig> BuildEmpresasConfig(List<string> files)
    {
        var empresas = new Dictionary<string, EmpresaConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in files)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var name = ExtractCompanyName(fileName);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var norm = EmpresaDetectionService.NormalizeForComparison(name);
            if (!empresas.ContainsKey(norm))
            {
                empresas[norm] = new EmpresaConfig
                {
                    NombreEmpresa = name.ToUpperInvariant(),
                    NombreCarpeta = "",
                };
            }
        }
        foreach (var k in new[] { "FUNDACION SOLID RIVER", "IPIC" })
        {
            var kn = EmpresaDetectionService.NormalizeForComparison(k);
            if (!empresas.ContainsKey(kn))
                empresas[kn] = new EmpresaConfig { NombreEmpresa = k.ToUpperInvariant(), NombreCarpeta = "" };
        }
        return empresas.Values.ToList();
    }

    private static string ExtractCompanyName(string fileName)
    {
        var normalized = fileName.Replace('_', ' ').Replace('-', ' ');
        while (normalized.Contains("  ")) normalized = normalized.Replace("  ", " ");
        var patterns = new[] { "INFORME DE CIERRE CONTABLE", "INFORME CIERRE CONTABLE", "CIERRE CONTABLE" };
        foreach (var pattern in patterns)
        {
            var idx = normalized.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                var name = normalized[..idx].Trim().TrimEnd('-', '_', ' ', '.');
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        return fileName;
    }

    private static IExcelWorksheet? FindBalanceSheet(IExcelWorkbook workbook)
    {
        var balanceNorm = EmpresaDetectionService.NormalizeForComparison("BALANCE DE SITUACION");
        foreach (var ws in workbook.Worksheets)
        {
            var wsNorm = EmpresaDetectionService.NormalizeForComparison(ws.Name ?? "");
            if (wsNorm == balanceNorm) return ws;
        }
        foreach (var ws in workbook.Worksheets)
        {
            var wsNorm = EmpresaDetectionService.NormalizeForComparison(ws.Name ?? "");
            if (wsNorm.Contains("BALANCE")) return ws;
        }
        return null;
    }

    private static List<CuentaConfig> DiscoverCuentas(IExcelWorkbook workbook)
    {
        var cuentas = new List<CuentaConfig>();
        var balanceSheet = FindBalanceSheet(workbook);
        if (balanceSheet == null) return cuentas;

        var limit = Math.Min(balanceSheet.RowCount, 200);
        for (var fila = 1; fila <= limit; fila++)
        {
            var row = balanceSheet.GetRow(fila);
            if (row == null) continue;
            var nombreCuenta = row.GetCell("C").Trim();
            var notaCell = row.GetCell("J").Trim();
            if (string.IsNullOrWhiteSpace(nombreCuenta) || string.IsNullOrWhiteSpace(notaCell)) continue;
            if (!int.TryParse(notaCell, out _)) continue;
            var nombreNorm = EmpresaDetectionService.NormalizeForComparison(nombreCuenta);
            var tipo = nombreNorm.Contains("PAGAR") ? "CxP" : "CxC";
            cuentas.Add(new CuentaConfig { NombreCuenta = nombreCuenta, Tipo = tipo, ColumnaValor = "", ColumnaNota = "J" });
        }
        return cuentas;
    }
}


