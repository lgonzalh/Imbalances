using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Utils;

namespace Imbalances.Core.Services;

public class Motor1Extractor : IMotor1Extractor
{
    private readonly IExcelProvider _excelProvider;
    private readonly IEmpresaDetectionService _empresaDetectionService;

    private const string DefaultHojaBalance = "BALANCE DE SITUACION";
    private const string BalanceCuentaColumn = "C";
    private const string BalanceNotaColumn = "J";
    private const int BalanceScanMaxRows = 200;

    private const string DefaultNotaEmpresaColumn = "C";
    private const string DefaultNotaValorColumn = "I";
    private const int NotaHeaderScanMaxRows = 50;
    private const int NotaDataScanMaxRows = 200;

    private sealed class ParsedNoteRow
    {
        public int Fila { get; init; }
        public string RawTexto { get; init; } = string.Empty;
        public string TextoNormalizado { get; init; } = string.Empty;
        public decimal? Valor { get; init; }
    }

    private sealed class NoteCache
    {
        public string ColEmpresa { get; init; } = string.Empty;
        public string ColValor { get; init; } = string.Empty;
        public int StartRow { get; init; }
        public int EndRow { get; init; }
        public List<ParsedNoteRow> Rows { get; init; } = new();
    }

    private sealed class HomologationIndex
    {
        public Dictionary<string, string> ExactMatch { get; }
        public Dictionary<string, string> AliasMatch { get; }
        public List<(EmpresaConfig Config, string NombreNormalizado)> Empresas { get; }
        public int CacheHits { get; set; }

        public HomologationIndex(
            List<(EmpresaConfig Config, string NombreNormalizado)> empresas,
            List<EquivalenciaTercero> aliasEmpresa)
        {
            Empresas = empresas;
            ExactMatch = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (config, nombre) in empresas)
            {
                if (!string.IsNullOrWhiteSpace(nombre) && !ExactMatch.ContainsKey(nombre))
                {
                    ExactMatch[nombre] = config.NombreEmpresa ?? config.NombreCarpeta ?? string.Empty;
                }
            }

            AliasMatch = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in aliasEmpresa)
            {
                var aliasNorm = Normalizar(entry.Alias);
                var destNorm = Normalizar(entry.NombreEmpresaDestino);
                if (!string.IsNullOrWhiteSpace(aliasNorm) && !string.IsNullOrWhiteSpace(destNorm))
                {
                    AliasMatch[aliasNorm] = destNorm;
                }
            }
        }
    }

    public Motor1Extractor(IExcelProvider excelProvider, IEmpresaDetectionService empresaDetectionService)
    {
        _excelProvider = excelProvider;
        _empresaDetectionService = empresaDetectionService;
    }

    public async Task<List<Movimiento>> ExtraerAsync(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null, bool diagnosticMode = true, Action<PipelineProfile>? onProfile = null)
    {
        var sw = Stopwatch.StartNew();
        var resultados = new List<Movimiento>();
        var nombreArchivo = Path.GetFileName(filePath);

        onProgressLog?.Invoke($"[Info] Procesando: {nombreArchivo}");

        var swEmpresa = Stopwatch.StartNew();
        var empresa = _empresaDetectionService.DetectarEmpresa(filePath, config, onProgressLog);
        if (empresa == null)
        {
            onProgressLog?.Invoke($"[Info] Empresa no detectada para {nombreArchivo} -> SKIP");
            return resultados;
        }
        swEmpresa.Stop();

        var empresaOrigen = string.IsNullOrWhiteSpace(empresa.NombreEmpresa) ? empresa.NombreCarpeta : empresa.NombreEmpresa;
        onProgressLog?.Invoke($"[Info] Empresa detectada: {empresaOrigen}");

        var empresasNormalizadas = config.Empresas
            .Select(e => (
                Config: e,
                NombreNormalizado: Normalizar(string.IsNullOrWhiteSpace(e.NombreEmpresa) ? e.NombreCarpeta : e.NombreEmpresa)
            ))
            .Where(e => !string.IsNullOrWhiteSpace(e.NombreNormalizado))
            .ToList();

        var homologIndex = new HomologationIndex(empresasNormalizadas, config.AliasEmpresa);

        var swWorkbook = Stopwatch.StartNew();
        var workbook = await _excelProvider.OpenAsync(fileStream);
        swWorkbook.Stop();

        onProgressLog?.Invoke($"[Diagnostic] Hojas disponibles en el workbook ({workbook.Worksheets.Count()}):");
        foreach (var ws in workbook.Worksheets)
        {
            var wsName = ws.Name ?? "(sin nombre)";
            var wsNorm = Normalizar(wsName);
            onProgressLog?.Invoke($"[Diagnostic]   Hoja: '{wsName}' -> Normalizada: '{wsNorm}'");
        }

        var hojaBalanceNombre = string.IsNullOrWhiteSpace(empresa.HojaBalance) ? DefaultHojaBalance : empresa.HojaBalance.Trim();
        var hojaBalanceNorm = Normalizar(hojaBalanceNombre);
        onProgressLog?.Invoke($"[Diagnostic] Buscando hoja balance: '{hojaBalanceNombre}' -> Normalizada: '{hojaBalanceNorm}'");

        var sheetBalance = workbook.GetWorksheet(hojaBalanceNombre);
        if (sheetBalance == null)
        {
            onProgressLog?.Invoke($"[Error] Hoja balance no encontrada: '{hojaBalanceNombre}' (normalizada: '{hojaBalanceNorm}')");
            return resultados;
        }

        var cuentas = config.Cuentas.Where(c => !string.IsNullOrWhiteSpace(c.NombreCuenta)).ToList();
        if (cuentas.Count == 0)
        {
            onProgressLog?.Invoke("[Info] No hay cuentas configuradas -> SKIP");
            return resultados;
        }

        var swCuentas = Stopwatch.StartNew();
        var noteCache = new Dictionary<string, NoteCache>(StringComparer.Ordinal);
        var cuentaNotaMap = new List<(CuentaConfig Cuenta, string Nota, string NombreNota)>();

        foreach (var cuenta in cuentas)
        {
            onProgressLog?.Invoke($"[Info] Cuenta: {cuenta.NombreCuenta}");

            var filaCuenta = BuscarFilaExacta(sheetBalance, BalanceCuentaColumn, cuenta.NombreCuenta, BalanceScanMaxRows);
            if (filaCuenta == null)
                continue;

            var rowBalance = sheetBalance.GetRow(filaCuenta.Value);
            if (rowBalance == null)
                continue;

            var notaCell = rowBalance.GetCell(BalanceNotaColumn).Trim();
            if (!int.TryParse(notaCell, NumberStyles.None, CultureInfo.InvariantCulture, out var notaNum))
                continue;

            var nota = notaNum.ToString(CultureInfo.InvariantCulture);
            var nombreNota = $"Nota {nota}";

            var sheetNota = workbook.GetWorksheet(nombreNota);
            if (sheetNota == null)
                continue;

            cuentaNotaMap.Add((cuenta, nota, nombreNota));

            if (noteCache.ContainsKey(nota))
                continue;

            string colEmpresa = DefaultNotaEmpresaColumn;
            string colValor = !string.IsNullOrWhiteSpace(cuenta.ColumnaValor) ? cuenta.ColumnaValor : DefaultNotaValorColumn;

            var (detectedEmp, detectedVal) = TryDetectColumns(sheetNota);
            if (detectedVal != null && detectedVal != colValor)
            {
                onProgressLog?.Invoke($"[Info] {nombreNota}: columna de valores auto-detectada: {detectedVal} (configurada: {colValor})");
                colValor = detectedVal;
            }
            if (detectedEmp != null && detectedEmp != colEmpresa)
            {
                onProgressLog?.Invoke($"[Info] {nombreNota}: columna de empresas auto-detectada: {detectedEmp} (configurada: {colEmpresa})");
                colEmpresa = detectedEmp;
            }

            var startRow = BuscarInicioTabla(sheetNota, colEmpresa);
            var limit = Math.Min(sheetNota.RowCount, startRow + NotaDataScanMaxRows);
            onProgressLog?.Invoke($"[Info] {nombreNota}: InicioFila={startRow}, FinFila={limit}, RowCount={sheetNota.RowCount}");

            var rows = new List<ParsedNoteRow>();
            for (var fila = startRow; fila <= limit; fila++)
            {
                var row = sheetNota.GetRow(fila);
                if (row == null)
                    break;

                var nombre = row.GetCell(colEmpresa);
                var colEmpresaIdx = ColumnLetterToIndex(colEmpresa);

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

                if (string.IsNullOrWhiteSpace(nombre))
                {
                    rows.Add(new ParsedNoteRow { Fila = fila, RawTexto = string.Empty, TextoNormalizado = string.Empty, Valor = null });
                    continue;
                }

                if (EsGranTotal(nombre))
                {
                    rows.Add(new ParsedNoteRow { Fila = fila, RawTexto = nombre, TextoNormalizado = Normalizar(nombre), Valor = null });
                    break;
                }

                var textoNormalizado = Normalizar(nombre);
                var tieneValor = TryParseDecimal(row.GetCell(colValor), out var valor);

                rows.Add(new ParsedNoteRow
                {
                    Fila = fila,
                    RawTexto = nombre,
                    TextoNormalizado = textoNormalizado,
                    Valor = tieneValor ? valor : null
                });
            }

            noteCache[nota] = new NoteCache
            {
                ColEmpresa = colEmpresa,
                ColValor = colValor,
                StartRow = startRow,
                EndRow = limit,
                Rows = rows
            };
        }
        swCuentas.Stop();

        var swProcesamiento = Stopwatch.StartNew();
        foreach (var (cuenta, nota, nombreNota) in cuentaNotaMap)
        {
            var cache = noteCache[nota];
            var movimientosCuenta = ExtraerDesdeCache(cache, cuenta, nota, empresaOrigen, periodo, homologIndex, nombreArchivo, nombreNota, onProgressLog, diagnosticMode);
            resultados.AddRange(movimientosCuenta);
        }
        swProcesamiento.Stop();

        var dedup = new Dictionary<string, Movimiento>(StringComparer.Ordinal);
        foreach (var m in resultados)
        {
            var key = $"{m.EmpresaOrigen}|{m.EmpresaContraparte}|{m.Tipo}|{m.Cuenta}|{m.Nota}|{m.Periodo}|{m.Valor.ToString(CultureInfo.InvariantCulture)}";
            if (!dedup.ContainsKey(key))
                dedup[key] = m;
        }

        var dedupList = dedup.Values.ToList();

        var fsrFileMovements = dedupList.Where(m => m.EmpresaContraparte.Contains("FUNDACION SOLID RIVER", StringComparison.OrdinalIgnoreCase)).ToList();
        onProgressLog?.Invoke($"[Info] Archivo completado: {nombreArchivo} | Movimientos: {dedupList.Count}");
        if (fsrFileMovements.Count > 0)
        {
            var fsrFileTotal = fsrFileMovements.Sum(m => m.Valor);
            onProgressLog?.Invoke($"[Info] --- Metrics finales FSR {nombreArchivo} ---");
            onProgressLog?.Invoke($"[Info]   Movimientos FSR generados: {fsrFileMovements.Count}");
            foreach (var m in fsrFileMovements)
            {
                onProgressLog?.Invoke($"[Info]     * {m.EmpresaOrigen} -> {m.EmpresaContraparte} | {m.Tipo} | {m.Valor:N2}");
            }
            onProgressLog?.Invoke($"[Info]   Valor total FSR generado: {fsrFileTotal:N2}");
        }

        sw.Stop();

        if (onProfile != null)
        {
            var profile = new PipelineProfile
            {
                Archivo = nombreArchivo,
                Empresa = empresaOrigen,
                DeteccionEmpresaMs = swEmpresa.ElapsedMilliseconds,
                LecturaWorkbookMs = swWorkbook.ElapsedMilliseconds,
                DescubrimientoCuentasMs = swCuentas.ElapsedMilliseconds,
                LecturaNotasMs = 0,
                ProcesamientoMovimientosMs = swProcesamiento.ElapsedMilliseconds,
                TotalMs = sw.ElapsedMilliseconds,
                MovimientosGenerados = dedupList.Count,
                NotasProcesadas = noteCache.Count
            };
            onProgressLog?.Invoke($"[Perfil] {nombreArchivo}: empresa={swEmpresa.ElapsedMilliseconds}ms, workbook={swWorkbook.ElapsedMilliseconds}ms, cuentas={swCuentas.ElapsedMilliseconds}ms, procesamiento={swProcesamiento.ElapsedMilliseconds}ms, total={sw.ElapsedMilliseconds}ms");
            onProfile(profile);
        }

        return dedupList;
    }

    private static List<Movimiento> ExtraerDesdeCache(
        NoteCache cache,
        CuentaConfig cuenta,
        string nota,
        string empresaOrigen,
        string periodo,
        HomologationIndex homologIndex,
        string nombreArchivo,
        string nombreHoja,
        Action<string>? onProgressLog,
        bool diagnosticMode = true)
    {
        var resultados = new List<Movimiento>();
        string? rubroActual = null;
        var totalRowsEvaluated = 0;
        var totalRowsAccepted = 0;
        var totalRowsDiscarded = 0;
        var fsrRowsDetected = 0;
        var fsrMovements = new List<Movimiento>();

        void LogPerRow(string message)
        {
            if (diagnosticMode)
                onProgressLog?.Invoke(message);
        }

        foreach (var row in cache.Rows)
        {
            var nombre = row.RawTexto;

            if (string.IsNullOrWhiteSpace(nombre))
            {
                LogPerRow($"[Fila {row.Fila}] Vacio: sin texto en columna empresa");
                continue;
            }

            LogPerRow($@"[Fila {row.Fila}] Texto=""{nombre}""");

            if (EsGranTotal(nombre))
            {
                LogPerRow($"[Fila {row.Fila}] -> GRAN TOTAL: fin de nota");
                break;
            }

            if (row.Valor == null)
            {
                if (!string.IsNullOrWhiteSpace(row.TextoNormalizado))
                {
                    rubroActual = row.TextoNormalizado;
                    LogPerRow($"[Fila {row.Fila}] RUBRO detectado: \"{row.TextoNormalizado}\"");
                }
                continue;
            }

            var textoNormalizado = row.TextoNormalizado;
            if (string.IsNullOrWhiteSpace(textoNormalizado))
                continue;

            totalRowsEvaluated++;

            if (nombre.Contains("(FSR)", StringComparison.OrdinalIgnoreCase) ||
                nombre.Trim().Equals("FSR", StringComparison.OrdinalIgnoreCase))
            {
                fsrRowsDetected++;
            }

            if (EsFilaEstructural(textoNormalizado))
            {
                LogPerRow($"[Fila {row.Fila}] \"{nombre}\" -> DESCARTADA (estructural)");
                totalRowsDiscarded++;
                continue;
            }

            if (EsSubEntry(textoNormalizado, homologIndex.Empresas))
            {
                LogPerRow($"[Fila {row.Fila}] \"{nombre}\" -> DESCARTADA (subentrada)");
                totalRowsDiscarded++;
                continue;
            }

            if (EsRubro(textoNormalizado))
            {
                rubroActual = textoNormalizado;
                LogPerRow($"[Fila {row.Fila}] RUBRO detectado: \"{textoNormalizado}\"");
                continue;
            }

            var contraparte = HomologarEmpresaIndex(textoNormalizado, homologIndex);

            if (string.IsNullOrEmpty(contraparte))
            {
                LogPerRow($"[Fila {row.Fila}] \"{nombre}\" -> DESCARTADA (no homologada)");
                totalRowsDiscarded++;
                continue;
            }

            LogPerRow($"[Fila {row.Fila}] CONTRAPARTE detectada: \"{contraparte}\"");

            var mov = new Movimiento
            {
                EmpresaOrigen = empresaOrigen,
                EmpresaContraparte = contraparte,
                Tipo = cuenta.Tipo,
                Cuenta = rubroActual ?? cuenta.NombreCuenta,
                Valor = row.Valor.Value,
                Nota = nota,
                Periodo = periodo
            };

            if (MovimientoValido(mov, onProgressLog, nombreHoja, nombreArchivo))
            {
                LogPerRow($"[Fila {row.Fila}] MOVIMIENTO generado: {empresaOrigen} -> {contraparte} | {cuenta.Tipo} | {rubroActual ?? cuenta.NombreCuenta} | {row.Valor.Value:N2}");
                resultados.Add(mov);
                totalRowsAccepted++;

                if (contraparte.Contains("FUNDACION SOLID RIVER", StringComparison.OrdinalIgnoreCase))
                {
                    fsrMovements.Add(mov);
                    onProgressLog?.Invoke($"[Info] Contraparte especial FSR homologada correctamente: {empresaOrigen} -> {contraparte} | {mov.Tipo} | {mov.Valor:N2}");
                }
            }
            else
            {
                LogPerRow($"[Fila {row.Fila}] \"{nombre}\" -> DESCARTADA (movimiento invalido)");
                totalRowsDiscarded++;
            }
        }

        if (resultados.Count == 0 && totalRowsEvaluated == 0)
        {
            onProgressLog?.Invoke($"[Sugerencia] En {nombreHoja}, no se encontraron filas con datos procesables. Revise la configuracion de columnas.");
        }

        onProgressLog?.Invoke($"[Info] {nombreHoja}: resumen: filasExtraidas={resultados.Count}, evaluadas={totalRowsEvaluated}, aceptadas={totalRowsAccepted}, descartadas={totalRowsDiscarded}");
        onProgressLog?.Invoke($"[Info] Nota {nota}: filas extraidas {resultados.Count}");

        var fsrTotal = fsrMovements.Sum(m => m.Valor);
        if (fsrRowsDetected > 0 || fsrMovements.Count > 0)
        {
            onProgressLog?.Invoke($"[Info] --- Diagnostico FSR {nombreHoja} ---");
            onProgressLog?.Invoke($"[Info]   Filas evaluadas: {totalRowsEvaluated}");
            onProgressLog?.Invoke($"[Info]   Filas aceptadas: {totalRowsAccepted}");
            onProgressLog?.Invoke($"[Info]   Filas descartadas: {totalRowsDiscarded}");
            onProgressLog?.Invoke($"[Info]   Filas detectadas con (FSR): {fsrRowsDetected}");
            onProgressLog?.Invoke($"[Info]   Movimientos FSR generados: {fsrMovements.Count}");
            foreach (var m in fsrMovements)
            {
                onProgressLog?.Invoke($"[Info]     * {m.EmpresaOrigen} -> {m.EmpresaContraparte} | {m.Tipo} | {m.Valor:N2}");
            }
            onProgressLog?.Invoke($"[Info]   Valor total FSR: {fsrTotal:N2}");
        }

        return resultados;
    }

    private static int? BuscarFilaExacta(IExcelWorksheet sheet, string col, string texto, int maxRows)
    {
        var target = Normalizar(texto);
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var limit = Math.Min(sheet.RowCount, maxRows);
        double bestSim = 0;
        var bestRow = (int?)null;

        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null)
                return null;

            var value = Normalizar(row.GetCell(col));

            if (value == target)
                return fila;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            var sim = LevenshteinSimilarity(value, target);
            if (sim > bestSim)
            {
                bestSim = sim;
                bestRow = fila;
            }
        }

        if (bestSim > 0.90 && bestRow.HasValue)
            return bestRow;

        return null;
    }

    private static int BuscarInicioTabla(IExcelWorksheet sheet, string colEmpresa)
    {
        var limit = Math.Min(sheet.RowCount, NotaHeaderScanMaxRows);
        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null)
                break;

            var cell = row.GetCell(colEmpresa);
            if (cell != null && cell.Contains("MOVIMIENTO", StringComparison.OrdinalIgnoreCase))
                return fila + 1;
        }

        return 3;
    }

    private static (string? EmpresaCol, string? ValorCol) TryDetectColumns(IExcelWorksheet sheet)
    {
        var limit = Math.Min(sheet.RowCount, NotaHeaderScanMaxRows);

        string? detectedValorCol = null;
        string? detectedEmpresaCol = null;

        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null) continue;

            for (var colIdx = 0; colIdx < 20; colIdx++)
            {
                var colLetter = ColumnIndexToLetter(colIdx);
                var cellValue = row.GetCell(colLetter);
                if (string.IsNullOrWhiteSpace(cellValue)) continue;

                if (detectedValorCol == null &&
                    cellValue.Contains("Saldo final", StringComparison.OrdinalIgnoreCase))
                {
                    detectedValorCol = colLetter;
                }

                if (detectedEmpresaCol == null &&
                    (cellValue.Contains("MOVIMIENTO", StringComparison.OrdinalIgnoreCase) ||
                     cellValue.Contains("EMPRESA", StringComparison.OrdinalIgnoreCase) ||
                     cellValue.Contains("TERCERO", StringComparison.OrdinalIgnoreCase) ||
                     cellValue.Contains("CONTRAPARTE", StringComparison.OrdinalIgnoreCase) ||
                     cellValue.Contains("NOMBRE", StringComparison.OrdinalIgnoreCase)))
                {
                    detectedEmpresaCol = colLetter;
                }
            }

            if (detectedValorCol != null && detectedEmpresaCol != null)
                break;
        }

        return (detectedEmpresaCol, detectedValorCol);
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

    private static bool EsGranTotal(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        return texto.Contains("GRAN TOTAL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsFilaEstructural(string textoNormalizado)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
            return true;

        return textoNormalizado.StartsWith("TOTAL", StringComparison.Ordinal) ||
               textoNormalizado.StartsWith("SUBTOTAL", StringComparison.Ordinal) ||
               textoNormalizado.StartsWith("RESUMEN DE ANTIGUEDAD", StringComparison.Ordinal) ||
               textoNormalizado.Contains("MOVIMIENTO", StringComparison.Ordinal);
    }

    private static bool EsRubro(string textoNormalizado)
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

    private static bool EsSubEntry(
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

    private static string HomologarEmpresaIndex(
        string textoNormalizado,
        HomologationIndex index)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
            return string.Empty;

        // Level 1: Alias match O(1)
        if (index.AliasMatch.TryGetValue(textoNormalizado, out var aliasResult))
        {
            index.CacheHits++;
            return aliasResult;
        }

        // Level 2: Exact match O(1)
        if (index.ExactMatch.TryGetValue(textoNormalizado, out var exactResult))
        {
            return exactResult;
        }

        // Level 3: Fuzzy match (85%+) - only when exact fails
        var bestFuzzy = index.Empresas
            .Select(e => (Config: e.Config, Similitud: LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado)))
            .Where(e => e.Similitud >= 0.85)
            .OrderByDescending(e => e.Similitud)
            .FirstOrDefault();

        if (bestFuzzy.Config != null)
            return bestFuzzy.Config.NombreEmpresa ?? bestFuzzy.Config.NombreCarpeta;

        // Level 4: Low-confidence match (70-84.99%)
        var posibles = new List<(EmpresaConfig Config, double Similitud, string Normalizado)>();
        foreach (var e in index.Empresas)
        {
            var sim = LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado);
            if (sim >= 0.70)
                posibles.Add((e.Config, sim, e.NombreNormalizado));
        }

        if (posibles.Count > 0)
            return string.Empty;

        return string.Empty;
    }

    private static bool MovimientoValido(
        Movimiento mov,
        Action<string>? onProgressLog,
        string nombreHoja,
        string nombreArchivo)
    {
        if (string.IsNullOrWhiteSpace(mov.EmpresaOrigen)) return false;
        if (string.IsNullOrWhiteSpace(mov.EmpresaContraparte)) return false;
        if (string.IsNullOrWhiteSpace(mov.Cuenta)) return false;
        if (string.IsNullOrWhiteSpace(mov.Tipo)) return false;

        return true;
    }

    private static bool TryParseDecimal(string value, out decimal result)
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
            {
                trimmed = trimmed.Replace(",", "", StringComparison.Ordinal);
            }
            else
            {
                trimmed = trimmed.Replace(".", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
            }
        }
        else if (hasComma)
        {
            var lastComma = trimmed.LastIndexOf(',');
            var digitsAfter = trimmed.Length - lastComma - 1;

            if (digitsAfter == 0)
                return false;

            trimmed = digitsAfter == 3
                ? trimmed.Replace(",", "", StringComparison.Ordinal)
                : trimmed.Replace(",", ".", StringComparison.Ordinal);
        }
        else if (hasDot)
        {
            var lastDot = trimmed.LastIndexOf('.');
            var digitsAfter = trimmed.Length - lastDot - 1;

            if (digitsAfter == 0)
                return false;

            if (digitsAfter == 3)
                trimmed = trimmed.Replace(".", "", StringComparison.Ordinal);
        }

        if (!decimal.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return false;

        result = negative ? -parsed : parsed;
        return true;
    }

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

    private static double LevenshteinSimilarity(string a, string b)
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
        {
            for (var j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        var distance = matrix[lenA, lenB];
        var maxLen = Math.Max(lenA, lenB);
        return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
    }
}
