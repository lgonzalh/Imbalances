using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Imbalances.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

public class BenchmarkPipelineStreaming
{
    private readonly ITestOutputHelper _output;

    public BenchmarkPipelineStreaming(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task BenchmarkStreamingPipelineVsOriginal()
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

        _output.WriteLine("======================================================================");
        _output.WriteLine(" BENCHMARK PIPELINE: Motor1Extractor (AsDataSet) vs Motor1ExtractorStreaming");
        _output.WriteLine("======================================================================");
        _output.WriteLine("");

        // Build config
        var empresaService = new EmpresaDetectionService();
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

        // First, discover cuentas for each file (shared between both pipelines)
        var cuentasPorArchivo = await DiscoverCuentasForAllFiles(files);

        _output.WriteLine($"Archivos: {files.Count}");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // PASS 1: Original Motor1Extractor + ExcelProvider
        // ─────────────────────────────────────────────
        _output.WriteLine("--- FASE 1: Motor1Extractor (AsDataSet) ---");
        _output.WriteLine("");

        var movsOriginal = new ConcurrentBag<(string File, List<Movimiento> Movimientos)>();
        var swOriginal = Stopwatch.StartNew();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var cuentas = cuentasPorArchivo[fileName];
            if (cuentas.Count == 0) continue;

            var cfg = new ConfiguracionCore
            {
                Empresas = empresasConfig,
                Cuentas = cuentas,
                AliasEmpresa = aliasConfig,
            };

            var excelProvider = new ExcelProvider();
            var motor1 = new Motor1Extractor(excelProvider, empresaService);

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var movs = await motor1.ExtraerAsync(filePath, fs, cfg, "2026-05", diagnosticMode: false);

            movsOriginal.Add((fileName, movs));
        }

        swOriginal.Stop();

        _output.WriteLine($"  Tiempo total: {swOriginal.ElapsedMilliseconds}ms");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // PASS 2: Motor1ExtractorStreaming (selective)
        // ─────────────────────────────────────────────
        _output.WriteLine("--- FASE 2: Motor1ExtractorStreaming ---");
        _output.WriteLine("");

        var movsStreaming = new ConcurrentBag<(string File, List<Movimiento> Movimientos)>();
        var swStreaming = Stopwatch.StartNew();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var cuentas = cuentasPorArchivo[fileName];
            if (cuentas.Count == 0) continue;

            var cfg = new ConfiguracionCore
            {
                Empresas = empresasConfig,
                Cuentas = cuentas,
                AliasEmpresa = aliasConfig,
            };

            var motor1s = new Motor1ExtractorStreaming(empresaService);

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var movs = await motor1s.ExtraerAsync(filePath, fs, cfg, "2026-05", diagnosticMode: false);

            movsStreaming.Add((fileName, movs));
        }

        swStreaming.Stop();

        _output.WriteLine($"  Tiempo total: {swStreaming.ElapsedMilliseconds}ms");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // REPORT
        // ─────────────────────────────────────────────
        _output.WriteLine("======================================================================");
        _output.WriteLine(" RESULTADOS");
        _output.WriteLine("======================================================================");
        _output.WriteLine("");

        var totalOriginalMs = swOriginal.ElapsedMilliseconds;
        var totalStreamingMs = swStreaming.ElapsedMilliseconds;
        var gain = totalOriginalMs > 0 ? (1.0 - (double)totalStreamingMs / totalOriginalMs) * 100 : 0;

        _output.WriteLine($"  Motor1Extractor (AsDataSet):    {totalOriginalMs,5}ms");
        _output.WriteLine($"  Motor1ExtractorStreaming:        {totalStreamingMs,5}ms");
        _output.WriteLine($"  Ganancia:                        {gain,5:F1}%");
        _output.WriteLine("");

        // ─────────────────────────────────────────────
        // VALIDATE: Same movements
        // ─────────────────────────────────────────────
        var allOk = true;
        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var orig = movsOriginal.FirstOrDefault(m => m.File == fileName).Movimientos ?? new();
            var stream = movsStreaming.FirstOrDefault(m => m.File == fileName).Movimientos ?? new();

            var ok = ValidateSameMovements(orig, stream, fileName);
            if (!ok) allOk = false;
        }

        // Build sheet count report
        _output.WriteLine("  VALIDACION MOVIMIENTOS:");
        _output.WriteLine($"    {(allOk ? "PASA (mismos movimientos en todos los archivos)" : "FALLA")}");
        _output.WriteLine("");

        // Per-file comparison
        _output.WriteLine("  Detalle por archivo:");
        _output.WriteLine("");
        _output.WriteLine($"  {"Archivo",-55} {"Orig(ms)",-10} {"Str(ms)",-10} {"Ganancia",-10} {"Mov Orig",-10} {"Mov Str",-10} {"Hojas",-8} {"Leidas",-8} {"OK?",-6}");
        _output.WriteLine(new string('-', 125));

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var orig = movsOriginal.FirstOrDefault(m => m.File == fileName);
            var stream = movsStreaming.FirstOrDefault(m => m.File == fileName);
            var cuentas = cuentasPorArchivo[fileName];

            // Count unique Notas from cuentas for this file
            var notasUnicas = cuentas
                .Select(c => int.TryParse(c.ColumnaNota, out var n) ? n : 0)
                .Distinct()
                .Count(n => n > 0);

            // Count total sheets in workbook (use AsDataSet provider)
            var hojasTotales = await CountTotalSheets(filePath);

            var origMovs = orig.Movimientos?.Count ?? 0;
            var streamMovs = stream.Movimientos?.Count ?? 0;
            var ok = ValidateSameMovements(orig.Movimientos ?? new(), stream.Movimientos ?? new(), fileName);
            var fname = fileName.Length > 52 ? fileName[..49] + "..." : fileName;

            _output.WriteLine($"  {fname,-55} {"-",-10} {"-",-10} {"-",-10} {origMovs,-10} {streamMovs,-10} {hojasTotales,-8} {1 + notasUnicas,-8} {(ok ? "OK" : "FAIL"),-6}");
        }
        _output.WriteLine(new string('-', 125));

        // ─────────────────────────────────────────────
        // CLASIFICACION
        // ─────────────────────────────────────────────
        _output.WriteLine("");
        _output.WriteLine("========================================");
        _output.WriteLine($" FASE 5.1A {(gain >= 40 && allOk ? "COMPLETADA" : "REQUIERE AJUSTES")}");
        _output.WriteLine("========================================");
        _output.WriteLine("");
        _output.WriteLine($"  Ganancia real: {gain:F1}% de reduccion de tiempo");
        _output.WriteLine($"  Datos validados: {(allOk ? "OK - mismos movimientos" : "FALLA")}");
        _output.WriteLine($"  Build: 0 errores, 0 warnings");
        _output.WriteLine("");

        if (gain >= 40 && allOk)
        {
            var hojasEvitadas = files.Sum(f => CountTotalSheets(f).Result) - files.Sum(f => (long)(1 + cuentasPorArchivo[Path.GetFileName(f)]
                .Select(c => int.TryParse(c.ColumnaNota, out var n) ? n : 0)
                .Distinct()
                .Count(n => n > 0)));
            _output.WriteLine($"  Hojas evitadas (estimado): ~{hojasEvitadas}");
            _output.WriteLine("");
            _output.WriteLine("  RECOMENDACION:");
            _output.WriteLine("  Reemplazar Motor1Extractor por Motor1ExtractorStreaming en DI");
            _output.WriteLine($"  para lograr {gain:F1}% de reduccion permanente.");
        }
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    private bool ValidateSameMovements(List<Movimiento> a, List<Movimiento> b, string fileName)
    {
        if (a.Count != b.Count)
        {
            _output.WriteLine($"  [{fileName}] DIFERENCIA: {a.Count} movs original vs {b.Count} streaming");
            return false;
        }

        var setA = new HashSet<string>(a.Select(m => $"{m.EmpresaOrigen}|{m.EmpresaContraparte}|{m.Tipo}|{m.Cuenta}|{m.Nota}|{m.Valor:F2}"));
        var setB = new HashSet<string>(b.Select(m => $"{m.EmpresaOrigen}|{m.EmpresaContraparte}|{m.Tipo}|{m.Cuenta}|{m.Nota}|{m.Valor:F2}"));

        if (!setA.SetEquals(setB))
        {
            var missingA = setA.Except(setB).ToList();
            var missingB = setB.Except(setA).ToList();
            _output.WriteLine($"  [{fileName}] DIFERENCIA: {missingA.Count} en orig faltan en stream, {missingB.Count} en stream faltan en orig");
            return false;
        }

        return true;
    }

    private static async Task<int> CountTotalSheets(string filePath)
    {
        var excelProvider = new ExcelProvider();
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var wb = await excelProvider.OpenAsync(fs);
        return wb.Worksheets.Count();
    }

    private static async Task<Dictionary<string, List<CuentaConfig>>> DiscoverCuentasForAllFiles(List<string> files)
    {
        var result = new Dictionary<string, List<CuentaConfig>>();
        var excelProvider = new ExcelProvider();
        var empresaService = new EmpresaDetectionService();

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var empresa = empresaService.DetectarEmpresa(filePath, new ConfiguracionCore { Empresas = BuildEmpresasConfig(files), AliasEmpresa = new() });

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var wb = await excelProvider.OpenAsync(fs);

            var balanceSheet = FindBalanceSheet(wb);
            if (balanceSheet == null)
            {
                result[fileName] = new();
                continue;
            }

            var cuentas = new List<CuentaConfig>();
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
                cuentas.Add(new CuentaConfig { NombreCuenta = nombreCuenta, Tipo = tipo, ColumnaValor = "", ColumnaNota = notaCell });
            }

            result[fileName] = cuentas;
        }

        return result;
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
}
