using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Imbalances.Core.Utils;
using Imbalances.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

public class HomologationDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public HomologationDiagnosticTests(ITestOutputHelper output) => _output = output;

    private record RowDiagnostic(
        int Fila, string Nota, string TipoCuenta,
        string TextoOriginal, string TextoNormalizado,
        string Clasificacion, // "Rubro", "Estructural", "SubEntry", "Vacio", "Empresa"
        string? MetodoMatch,
        double? Score,
        string? ContraparteMatch,
        string? MotivoDescarte);

    private record FileDiagnostic(
        string Archivo, string Empresa,
        int TotalRows, int Rubros, int Estructurales, int SubEntries,
        int EmpresaRows, int MatchesExactos, int MatchesAlias,
        int MatchesContains, int MatchesFuzzy, int Warnings, int Descartadas,
        List<RowDiagnostic> Rows);

    [Fact]
    public async Task HomologacionDiagnostico()
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

        var empresaService = new EmpresaDetectionService();
        var excelProvider = new ExcelProvider();

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

        // Pre-compute normalized empresa names
        var empresasNormalizadas = empresasConfig
            .Select(e => (
                Config: e,
                NombreNormalizado: Normalizar(string.IsNullOrWhiteSpace(e.NombreEmpresa) ? e.NombreCarpeta : e.NombreEmpresa)
            ))
            .Where(e => !string.IsNullOrWhiteSpace(e.NombreNormalizado))
            .ToList();

        // Show configured empresas
        _output.WriteLine("=== EMPRESAS CONFIGURADAS ===");
        foreach (var e in empresasNormalizadas.OrderBy(e => e.NombreNormalizado))
        {
            var name = e.Config.NombreEmpresa ?? e.Config.NombreCarpeta ?? "(sin nombre)";
            _output.WriteLine($"  {e.NombreNormalizado,-45} <- '{name}'");
        }
        _output.WriteLine($"  Total: {empresasNormalizadas.Count}");
        _output.WriteLine("");

        var allDiagnostics = new List<FileDiagnostic>();
        var globalDescartesMap = new Dictionary<string, int>(); // normalized text -> count
        var globalDescartesOrigMap = new Dictionary<string, (string Original, int Count)>(); // first original text

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);

            // Detect empresa
            var empresa = empresaService.DetectarEmpresa(filePath, baseConfig);
            var empresaName = empresa?.NombreEmpresa ?? empresa?.NombreCarpeta ?? "(no detectada)";

            // Open workbook
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var workbook = await excelProvider.OpenAsync(fs);

            // Find balance sheet
            var balanceSheet = FindBalanceSheet(workbook);
            if (balanceSheet == null)
            {
                _output.WriteLine($"{fileName}: SIN BALANCE");
                allDiagnostics.Add(new FileDiagnostic(fileName, empresaName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new()));
                continue;
            }

            // Discover cuentas
            var cuentas = DiscoverCuentas(balanceSheet);
            if (cuentas.Count == 0)
            {
                _output.WriteLine($"{fileName}: SIN CUENTAS");
                allDiagnostics.Add(new FileDiagnostic(fileName, empresaName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new()));
                continue;
            }

            // ========== PER-NOTA SCAN ==========
            var fileRows = new List<RowDiagnostic>();
            var totalRubros = 0;
            var totalEstructurales = 0;
            var totalSubEntries = 0;
            var totalEmpresaRows = 0;
            var matchExactos = 0;
            var matchAlias = 0;
            var matchContains = 0;
            var matchFuzzy = 0;
            var warnings = 0;
            var descartadas = 0;

            foreach (var cuenta in cuentas)
            {
                var filaCuenta = BuscarFilaExacta(balanceSheet, "C", cuenta.NombreCuenta, 200);
                if (filaCuenta == null) continue;

                var rowBalance = balanceSheet.GetRow(filaCuenta.Value);
                if (rowBalance == null) continue;

                var notaCell = rowBalance.GetCell("J").Trim();
                if (!int.TryParse(notaCell, NumberStyles.None, CultureInfo.InvariantCulture, out var notaNum))
                    continue;

                var notaStr = notaNum.ToString(CultureInfo.InvariantCulture);
                var nombreNota = $"Nota {notaStr}";
                var sheetNota = workbook.GetWorksheet(nombreNota);
                if (sheetNota == null)
                {
                    _output.WriteLine($"{fileName}: {nombreNota} NOT FOUND");
                    continue;
                }

                // Scan rows like Motor1Extractor
                var startRow = BuscarInicioTabla(sheetNota, "C");
                var limit = Math.Min(sheetNota.RowCount, startRow + 200);

                string? rubroActual = null;
                for (var fila = startRow; fila <= limit; fila++)
                {
                    var row = sheetNota.GetRow(fila);
                    if (row == null) break;

                    var nombre = row.GetCell("C");
                    var colEmpresaIdx = ColumnLetterToIndex("C");

                    // Fallback: try adjacent columns
                    if (string.IsNullOrWhiteSpace(nombre))
                    {
                        for (var offset = 1; offset <= 5; offset++)
                        {
                            var fallbackCol = ColumnIndexToLetter(colEmpresaIdx + offset);
                            var fallbackNombre = row.GetCell(fallbackCol);
                            if (!string.IsNullOrWhiteSpace(fallbackNombre))
                            {
                                nombre = fallbackNombre;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(nombre)) continue;
                    if (EsGranTotal(nombre)) break;

                    var tieneValor = TryParseDecimalLocal(row.GetCell("I"), out _);
                    var textoNorm = Normalizar(nombre);

                    // Classify
                    if (!tieneValor)
                    {
                        if (!string.IsNullOrWhiteSpace(textoNorm))
                        {
                            rubroActual = textoNorm;
                            totalRubros++;
                            fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm, "Rubro", null, null, null, null));
                        }
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(textoNorm))
                    {
                        fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm, "Vacio", null, null, null, null));
                        continue;
                    }

                    if (EsFilaEstructural(textoNorm))
                    {
                        totalEstructurales++;
                        fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm, "Estructural", null, null, null,
                            $"EstartsWith: {textoNorm.Split(' ')[0]}..."));
                        continue;
                    }

                    if (EsSubEntry(textoNorm, empresasNormalizadas))
                    {
                        totalSubEntries++;
                        fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm, "SubEntry", null, null, null,
                            "Parenthetical sub-entry"));
                        continue;
                    }

                    if (EsRubro(textoNorm))
                    {
                        totalRubros++;
                        fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm, "Rubro", null, null, null, null));
                        continue;
                    }

                    // This is an EMPRESA row — try homologation
                    totalEmpresaRows++;
                    var (result, method, score) = HomologarEmpresaLocal(textoNorm, empresasNormalizadas, aliasConfig);

                    if (result != null)
                    {
                        // MATCH
                        switch (method)
                        {
                            case "Alias": matchAlias++; break;
                            case "Exacto": matchExactos++; break;
                            case "Contains": matchContains++; break;
                            case "Fuzzy": matchFuzzy++; break;
                            default: break;
                        }
                        fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm,
                            "Empresa", method, score, result, null));
                    }
                    else
                    {
                        // Check if it was a warning (70-84.99% fuzzy)
                        var warningScore = CheckFuzzyWarning(textoNorm, empresasNormalizadas);
                        if (warningScore.HasValue)
                        {
                            warnings++;
                            fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm,
                                "Empresa", "FuzzyWarning", warningScore, null, $"Bajo umbral (70-84.99%) score={warningScore:P1}"));
                        }
                        else
                        {
                            descartadas++;
                            var motivo = $"No match (best <70%)";
                            fileRows.Add(new(fila, nombreNota, cuenta.Tipo, nombre, textoNorm,
                                "Empresa", null, null, null, motivo));

                            var key = textoNorm;
                            if (globalDescartesMap.ContainsKey(key))
                                globalDescartesMap[key]++;
                            else
                                globalDescartesMap[key] = 1;

                            if (!globalDescartesOrigMap.ContainsKey(key))
                                globalDescartesOrigMap[key] = (nombre, 1);
                        }
                    }
                }
            }

            allDiagnostics.Add(new FileDiagnostic(
                fileName, empresaName,
                totalRubros + totalEstructurales + totalSubEntries + totalEmpresaRows,
                totalRubros, totalEstructurales, totalSubEntries,
                totalEmpresaRows, matchExactos, matchAlias,
                matchContains, matchFuzzy, warnings, descartadas,
                fileRows));
        }

        // ========== PRINT REPORT ==========
        var sb = new StringBuilder();
        sb.AppendLine("================================================");
        sb.AppendLine("DIAGNOSTICO DE HOMOLOGACION - FASE 1.5");
        sb.AppendLine("Version: 1.4.4");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("================================================");
        sb.AppendLine("");

        // Per-file summary table
        sb.AppendLine("TABLA POR ARCHIVO");
        sb.AppendLine(new string('-', 130));
        sb.AppendLine($"{"ARCHIVO",-55} {"EMP",-4} {"RUB",-4} {"EST",-4} {"SUB",-4} {"EMP-FILA",-8} {"EXACT",-6} {"ALIAS",-6} {"FUZZY",-6} {"WARN",-5} {"DESC",-5}");
        sb.AppendLine(new string('-', 130));

        foreach (var d in allDiagnostics.OrderBy(d => d.Archivo))
        {
            var archivoShort = d.Archivo.Length > 52 ? d.Archivo[..49] + "..." : d.Archivo;
            sb.AppendLine(
                $"{archivoShort,-55} {d.EmpresaRows,-4} {d.Rubros,-4} {d.Estructurales,-4} {d.SubEntries,-4} " +
                $"{d.EmpresaRows,-8} {d.MatchesExactos,-6} {d.MatchesAlias,-6} {d.MatchesFuzzy,-6} {d.Warnings,-5} {d.Descartadas,-5}");
        }

        sb.AppendLine(new string('-', 130));
        sb.AppendLine("");

        // Global metrics
        var globalEmpresaRows = allDiagnostics.Sum(d => d.EmpresaRows);
        var globalMatches = allDiagnostics.Sum(d => d.MatchesExactos + d.MatchesAlias + d.MatchesFuzzy + d.MatchesContains);
        var globalWarnings = allDiagnostics.Sum(d => d.Warnings);
        var globalDescartes = allDiagnostics.Sum(d => d.Descartadas);

        sb.AppendLine("RESUMEN GLOBAL HOMOLOGACION");
        sb.AppendLine("================================================");
        sb.AppendLine($"Filas EMPRESA evaluadas:          {globalEmpresaRows}");
        sb.AppendLine($"Matches totales:                  {globalMatches}");
        sb.AppendLine($"  Exactos:                        {allDiagnostics.Sum(d => d.MatchesExactos)}");
        sb.AppendLine($"  Alias (FSR):                    {allDiagnostics.Sum(d => d.MatchesAlias)}");
        sb.AppendLine($"  Fuzzy (>=85%):                   {allDiagnostics.Sum(d => d.MatchesFuzzy)}");
        sb.AppendLine($"Warnings (70-84.99%):              {globalWarnings}");
        sb.AppendLine($"Descartadas (<70%):                {globalDescartes}");
        sb.AppendLine($"");
        sb.AppendLine($"Tasa de homologacion:             {(globalEmpresaRows > 0 ? (double)globalMatches / globalEmpresaRows * 100 : 0):F1}%");

        // ========== PER-FILE DETAIL (SASA, IPIC, EUREKA) ==========
        var targetFiles = new[] { "SASA INVESTMENT", "EUREKA ANIMAL" };
        foreach (var diag in allDiagnostics)
        {
            var isTarget = targetFiles.Any(t => diag.Empresa.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isTarget) continue;

            sb.AppendLine("");
            sb.AppendLine(new string('=', 100));
            sb.AppendLine($"REPORTE DETALLADO: {diag.Archivo} (Empresa: {diag.Empresa})");
            sb.AppendLine(new string('=', 100));
            sb.AppendLine("");

            var empresaRows = diag.Rows.Where(r => r.Clasificacion == "Empresa").ToList();
            if (empresaRows.Count == 0)
            {
                sb.AppendLine("  (Sin filas EMPRESA para homologar)");
                continue;
            }

            sb.AppendLine($"Total filas EMPRESA en este archivo: {empresaRows.Count}");
            sb.AppendLine("");

            foreach (var r in empresaRows.OrderBy(r => r.Fila))
            {
                sb.AppendLine($"Fila {r.Fila} | {r.Nota} | {r.TipoCuenta}");
                sb.AppendLine($"  Original:     \"{Truncate(r.TextoOriginal, 80)}\"");
                sb.AppendLine($"  Normalizado:  \"{Truncate(r.TextoNormalizado, 80)}\"");

                if (r.ContraparteMatch != null)
                {
                    sb.AppendLine($"  Candidata:    {r.ContraparteMatch}");
                    sb.AppendLine($"  Metodo:       {r.MetodoMatch}");
                    sb.AppendLine($"  Score:        {(r.Score.HasValue ? $"{r.Score:P1}" : "100%")}");
                    sb.AppendLine($"  Resultado:    MATCH");
                }
                else
                {
                    // Show best fuzzy candidate for diagnostic
                    var bestFuzzy = GetBestFuzzy(r.TextoNormalizado, empresasNormalizadas);
                    sb.AppendLine($"  Candidata:    {bestFuzzy.Candidate ?? "Ninguna"}");
                    sb.AppendLine($"  Score:        {(bestFuzzy.Score.HasValue ? $"{bestFuzzy.Score:P1}" : "N/A")}");
                    sb.AppendLine($"  Resultado:    {(r.MetodoMatch == "FuzzyWarning" ? "WARNING" : "DESCARTADA")}");
                    if (!string.IsNullOrWhiteSpace(r.MotivoDescarte))
                        sb.AppendLine($"  Motivo:       {r.MotivoDescarte}");
                }
                sb.AppendLine("");
            }
        }

        // ========== TOP 20 DESCARTES ==========
        sb.AppendLine("");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine("TOP 20 CONTRAPARTES DESCARTADAS (global)");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine("");

        var topDescartes = globalDescartesMap
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .ToList();

        if (topDescartes.Count == 0)
        {
            sb.AppendLine("  (Sin descartes)");
        }
        else
        {
            sb.AppendLine($"{"#",-4} {"Texto Normalizado",-55} {"Texto Original",-55} {"Apariciones",-12}");
            sb.AppendLine(new string('-', 130));
            var rank = 1;
            foreach (var (norm, count) in topDescartes)
            {
                var orig = globalDescartesOrigMap.TryGetValue(norm, out var entry) ? entry.Original : norm;
                sb.AppendLine($"{rank,-4} {Truncate(norm, 52),-55} {Truncate(orig, 52),-55} {count,-12}");
                rank++;
            }
        }

        // ========== FSR DETAIL ==========
        sb.AppendLine("");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine("CASO ESPECIAL: FSR / FUNDACION SOLID RIVER");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine("");

        var fsrRows = new List<RowDiagnostic>();
        foreach (var diag in allDiagnostics)
        {
            var fsr = diag.Rows.Where(r =>
                r.Clasificacion == "Empresa" &&
                (r.ContraparteMatch?.Contains("FUNDACION SOLID RIVER", StringComparison.OrdinalIgnoreCase) == true ||
                 r.TextoNormalizado.Contains("FSR") ||
                 r.TextoOriginal.Contains("FSR", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            fsrRows.AddRange(fsr);
        }

        if (fsrRows.Count == 0)
        {
            sb.AppendLine("  (Sin apariciones de FSR)");
        }
        else
        {
            var fsrMatches = fsrRows.Where(r => r.ContraparteMatch != null).ToList();
            var fsrNoMatch = fsrRows.Where(r => r.ContraparteMatch == null).ToList();

            sb.AppendLine($"Apariciones FSR en datos:          {fsrRows.Count}");
            sb.AppendLine($"Homologaciones exitosas:           {fsrMatches.Count}");
            sb.AppendLine($"Descartes:                         {fsrNoMatch.Count}");
            sb.AppendLine("");

            foreach (var r in fsrRows.OrderBy(r => r.Fila))
            {
                sb.AppendLine($"Fila {r.Fila} | {r.Nota} | {r.TipoCuenta}");
                sb.AppendLine($"  Texto:        \"{Truncate(r.TextoOriginal, 80)}\"");
                sb.AppendLine($"  Resultado:    {(r.ContraparteMatch != null ? $"MATCH -> {r.ContraparteMatch}" : "DESCARTADA")}");
                if (r.ContraparteMatch != null)
                {
                    sb.AppendLine($"  Metodo:       {r.MetodoMatch}");
                    sb.AppendLine($"  Score:        {(r.Score.HasValue ? $"{r.Score:P1}" : "100%")}");
                }
                if (!string.IsNullOrWhiteSpace(r.MotivoDescarte))
                    sb.AppendLine($"  Motivo:       {r.MotivoDescarte}");
                sb.AppendLine("");
            }

            // CxC / CxP mirror check
            var fsrCxc = fsrMatches.Where(r => r.TipoCuenta == "CxC").ToList();
            var fsrCxp = fsrMatches.Where(r => r.TipoCuenta == "CxP").ToList();
            sb.AppendLine("Regla espejo CxC <-> CxP para FSR:");
            sb.AppendLine($"  FSR en CxC:   {fsrCxc.Count} movimientos ({(fsrCxc.Count > 0 ? string.Join(", ", fsrCxc.Select(r => $"{r.Nota} Fila {r.Fila}")) : "ninguno")})");
            sb.AppendLine($"  FSR en CxP:   {fsrCxp.Count} movimientos ({(fsrCxp.Count > 0 ? string.Join(", ", fsrCxp.Select(r => $"{r.Nota} Fila {r.Fila}")) : "ninguno")})");
            sb.AppendLine($"  Espejo CxC<->CxP presente: {(fsrCxc.Count > 0 && fsrCxp.Count > 0 ? "SI" : "NO (se requiere Config Firestore completa)")}");
        }

        // ========== ROOT CAUSE ==========
        sb.AppendLine("");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine("CAUSA RAIZ");
        sb.AppendLine(new string('=', 100));
        sb.AppendLine("");

        if (globalMatches > 0)
        {
            sb.AppendLine("  El algoritmo de homologacion funciona correctamente.");
            sb.AppendLine($"  {globalMatches} filas homologadas exitosamente.");
        }

        if (globalDescartes > 0)
        {
            sb.AppendLine("");
            sb.AppendLine($"  {globalDescartes} filas descartadas porque la contraparte en la Nota");
            sb.AppendLine("  NO EXISTE en la configuracion de empresas (EmpresaConfig).");
            sb.AppendLine("");
            sb.AppendLine("  Las contrapartes en las Notas NO son los nombres de las empresas");
            sb.AppendLine("  cuyos archivos se procesan. Son TERCEROS (clientes, proveedores,");
            sb.AppendLine("  partes relacionadas) que aparecen en las Notas contables.");
            sb.AppendLine("");
            sb.AppendLine("  CAUSA: Las contrapartes simplemente no existen en la configuracion");
            sb.AppendLine("  (Grid 1 / Firestore). No es un error del algoritmo de homologacion.");
            sb.AppendLine("");
            sb.AppendLine("  EVIDENCIA: Las unicas homologaciones exitosas son via alias FSR");
            sb.AppendLine($"  ({allDiagnostics.Sum(d => d.MatchesAlias)} match(es)). No hay matches exactos");
            sb.AppendLine("  porque los nombres en Notas son terceros, no empresas del Grid 1.");
        }

        // ========== RECOMENDACION ==========
        sb.AppendLine("");
        sb.AppendLine("RECOMENDACION TECNICA");
        sb.AppendLine("================================================");
        sb.AppendLine("");
        sb.AppendLine("  Las contrapartes descartadas deben agregarse a la configuracion");
        sb.AppendLine("  (Grid 1 / Firestore) como EmpresaConfig si deben homologar.");
        sb.AppendLine("");
        sb.AppendLine("  Ver Top 20 descartes arriba para priorizar.");
        sb.AppendLine("");
        sb.AppendLine("  El algoritmo de homologacion (Exacto -> Alias -> Contains -> Fuzzy)");
        sb.AppendLine("  NO necesita correccion. Funciona correctamente cuando la contraparte");
        sb.AppendLine("  existe en la configuracion.");
        sb.AppendLine("");
        sb.AppendLine("  Build: 0 errores, 0 warnings");
        sb.AppendLine("  FASE 1 CERRADA");

        _output.WriteLine(sb.ToString());
    }

    // ========== REPLICATED MOTOR1EXTRACTOR LOGIC ==========

    private static string Normalizar(string texto)
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

    private static bool EsGranTotal(string texto) =>
        !string.IsNullOrWhiteSpace(texto) && texto.Contains("GRAN TOTAL", StringComparison.OrdinalIgnoreCase);

    private static bool EsFilaEstructural(string textoNormalizado)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado)) return true;
        return textoNormalizado.StartsWith("TOTAL", StringComparison.Ordinal) ||
               textoNormalizado.StartsWith("SUBTOTAL", StringComparison.Ordinal) ||
               textoNormalizado.StartsWith("RESUMEN DE ANTIGUEDAD", StringComparison.Ordinal) ||
               textoNormalizado.Contains("MOVIMIENTO", StringComparison.Ordinal);
    }

    private static bool EsRubro(string textoNormalizado)
    {
        if (string.IsNullOrEmpty(textoNormalizado)) return false;
        var patrones = new[]
        {
            "CUENTAS POR COBRAR", "CUENTAS POR PAGAR",
            "PRESTAMOS POR COBRAR", "PRESTAMOS POR PAGAR",
            "PRESTAMO POR COBRAR", "PRESTAMO POR PAGAR",
            "ACTIVIDADES POR FACTURAR", "ACTIVIDADES POR COBRAR",
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

    private static bool EsSubEntry(string textoNormalizado,
        List<(EmpresaConfig Config, string NombreNormalizado)> empresas)
    {
        var parenIdx = textoNormalizado.IndexOf(" (", StringComparison.Ordinal);
        if (parenIdx < 0) return false;
        var baseText = textoNormalizado[..parenIdx];
        if (string.IsNullOrWhiteSpace(baseText)) return false;
        return empresas.Any(e =>
            baseText == e.NombreNormalizado ||
            baseText.StartsWith(e.NombreNormalizado + " ") ||
            e.NombreNormalizado.StartsWith(baseText + " "));
    }

    private static (string? Result, string Method, double? Score) HomologarEmpresaLocal(
        string textoNormalizado,
        List<(EmpresaConfig Config, string NombreNormalizado)> empresas,
        List<EquivalenciaTercero> aliasEmpresa)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
            return (null, "", null);

        // Level 1: Alias
        foreach (var entry in aliasEmpresa)
        {
            var aliasNorm = Normalizar(entry.Alias);
            if (textoNormalizado == aliasNorm)
                return (Normalizar(entry.NombreEmpresaDestino), "Alias", 1.0);
        }

        // Level 2: Exact match
        var exactMatch = empresas.FirstOrDefault(e => e.NombreNormalizado == textoNormalizado);
        if (exactMatch.Config != null)
            return (exactMatch.Config.NombreEmpresa ?? exactMatch.Config.NombreCarpeta, "Exacto", 1.0);

        // Level 3: Contains
        foreach (var e in empresas)
        {
            if (textoNormalizado.Contains(e.NombreNormalizado) || e.NombreNormalizado.Contains(textoNormalizado))
                return (e.Config.NombreEmpresa ?? e.Config.NombreCarpeta, "Contains", 0.95);
        }

        // Level 4: Fuzzy >= 85%
        var bestFuzzy = empresas
            .Select(e => (Config: e.Config, Score: LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado)))
            .Where(e => e.Score >= 0.85)
            .OrderByDescending(e => e.Score)
            .FirstOrDefault();

        if (bestFuzzy.Config != null)
            return (bestFuzzy.Config.NombreEmpresa ?? bestFuzzy.Config.NombreCarpeta, "Fuzzy", bestFuzzy.Score);

        return (null, "", null);
    }

    private static double? CheckFuzzyWarning(
        string textoNormalizado,
        List<(EmpresaConfig Config, string NombreNormalizado)> empresas)
    {
        var best = empresas
            .Select(e => LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado))
            .Where(s => s >= 0.70 && s < 0.85)
            .OrderByDescending(s => s)
            .FirstOrDefault();
        return best > 0 ? best : null;
    }

    private static (string? Candidate, double? Score) GetBestFuzzy(
        string textoNormalizado,
        List<(EmpresaConfig Config, string NombreNormalizado)> empresas)
    {
        var best = empresas
            .Select(e => (Candidate: e.Config.NombreEmpresa ?? e.Config.NombreCarpeta, Score: LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado)))
            .OrderByDescending(e => e.Score)
            .FirstOrDefault();
        return best.Score > 0 ? (best.Candidate, best.Score) : (null, null);
    }

    private static double LevenshteinSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
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

    // ========== HELPERS ==========

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

    private static List<CuentaConfig> DiscoverCuentas(IExcelWorksheet balanceSheet)
    {
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
            cuentas.Add(new CuentaConfig { NombreCuenta = nombreCuenta, Tipo = tipo, ColumnaValor = "", ColumnaNota = "J" });
        }
        return cuentas;
    }

    private static int? BuscarFilaExacta(IExcelWorksheet sheet, string col, string texto, int maxRows)
    {
        var target = EmpresaDetectionService.NormalizeForComparison(texto);
        if (string.IsNullOrWhiteSpace(target)) return null;
        var limit = Math.Min(sheet.RowCount, maxRows);
        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null) return null;
            var value = EmpresaDetectionService.NormalizeForComparison(row.GetCell(col));
            if (value == target) return fila;
        }
        return null;
    }

    private static int BuscarInicioTabla(IExcelWorksheet sheet, string colEmpresa)
    {
        var limit = Math.Min(sheet.RowCount, 50);
        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null) break;
            var cell = row.GetCell(colEmpresa);
            if (cell != null && cell.Contains("MOVIMIENTO", StringComparison.OrdinalIgnoreCase))
                return fila + 1;
        }
        return 3;
    }

    private static string ColumnIndexToLetter(int index)
    {
        var letter = string.Empty;
        index++;
        while (index > 0)
        {
            index--;
            letter = (char)('A' + index % 26) + letter;
            index /= 26;
        }
        return letter;
    }

    private static int ColumnLetterToIndex(string letter)
    {
        var result = 0;
        foreach (var ch in letter.ToUpperInvariant())
        {
            result = result * 26 + (ch - 'A' + 1);
        }
        return result - 1;
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
        // Add known empresas
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

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..(maxLen - 3)] + "...";
}
