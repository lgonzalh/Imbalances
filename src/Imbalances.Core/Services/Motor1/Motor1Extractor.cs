using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public class Motor1Extractor : IMotor1Extractor
{
    private readonly IExcelProvider _excelProvider;

    private const string DefaultHojaBalance = "Balance de situación";
    private const string BalanceCuentaColumn = "C";
    private const string BalanceNotaColumn = "J";
    private const int BalanceScanMaxRows = 200;

    private const string DefaultNotaEmpresaColumn = "C";
    private const string DefaultNotaValorColumn = "I";
    private const int NotaHeaderScanMaxRows = 50;
    private const int NotaDataScanMaxRows = 200;

    public Motor1Extractor(IExcelProvider excelProvider)
    {
        _excelProvider = excelProvider;
    }

    public async Task<List<Movimiento>> ExtraerAsync(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null)
    {
        var resultados = new List<Movimiento>();
        var nombreArchivo = Path.GetFileName(filePath);

        onProgressLog?.Invoke($"[Info] Procesando: {nombreArchivo}");

        var empresa = config.Empresas.FirstOrDefault(e => filePath.Contains(e.NombreCarpeta, StringComparison.OrdinalIgnoreCase));
        if (empresa == null)
        {
            onProgressLog?.Invoke($"[Info] Empresa no detectada para {nombreArchivo} -> SKIP");
            return resultados;
        }

        var empresaOrigen = string.IsNullOrWhiteSpace(empresa.NombreEmpresa) ? empresa.NombreCarpeta : empresa.NombreEmpresa;
        onProgressLog?.Invoke($"[Info] Empresa detectada: {empresaOrigen}");

        var empresasNormalizadas = config.Empresas
            .Select(e => (
                Config: e,
                NombreNormalizado: Normalizar(string.IsNullOrWhiteSpace(e.NombreEmpresa) ? e.NombreCarpeta : e.NombreEmpresa)
            ))
            .Where(e => !string.IsNullOrWhiteSpace(e.NombreNormalizado))
            .ToList();

        var workbook = await _excelProvider.OpenAsync(fileStream);

        var hojaBalanceNombre = string.IsNullOrWhiteSpace(empresa.HojaBalance) ? DefaultHojaBalance : empresa.HojaBalance.Trim();
        var sheetBalance = workbook.GetWorksheet(hojaBalanceNombre);
        if (sheetBalance == null)
        {
            onProgressLog?.Invoke($"[Error] Hoja balance no encontrada: '{hojaBalanceNombre}'");
            return resultados;
        }

        var cuentas = config.Cuentas.Where(c => !string.IsNullOrWhiteSpace(c.NombreCuenta)).ToList();
        if (cuentas.Count == 0)
        {
            onProgressLog?.Invoke("[Info] No hay cuentas configuradas -> SKIP");
            return resultados;
        }

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

            // Use config columns if provided, else fallback to defaults
            string colEmpresa = DefaultNotaEmpresaColumn; 
            string colValor = !string.IsNullOrWhiteSpace(cuenta.ColumnaValor) ? cuenta.ColumnaValor : DefaultNotaValorColumn;

            var movimientosCuenta = ExtraerDesdeNota(sheetNota, cuenta, nota, empresaOrigen, periodo, empresasNormalizadas, config.AliasEmpresa, nombreArchivo, nombreNota, onProgressLog, colEmpresa, colValor);
            resultados.AddRange(movimientosCuenta);
        }

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
            onProgressLog?.Invoke($"[Info] --- Métricas finales FSR {nombreArchivo} ---");
            onProgressLog?.Invoke($"[Info]   Movimientos FSR generados: {fsrFileMovements.Count}");
            foreach (var m in fsrFileMovements)
            {
                onProgressLog?.Invoke($"[Info]     • {m.EmpresaOrigen} -> {m.EmpresaContraparte} | {m.Tipo} | {m.Valor:N2}");
            }
            onProgressLog?.Invoke($"[Info]   Valor total FSR generado: {fsrFileTotal:N2}");
        }

        return dedupList;
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

    private static List<Movimiento> ExtraerDesdeNota(
          IExcelWorksheet sheetNota,
          CuentaConfig cuenta,
          string nota,
          string empresaOrigen,
          string periodo,
          List<(EmpresaConfig Config, string NombreNormalizado)> empresasNormalizadas,
          List<EquivalenciaTercero> aliasEmpresa,
          string nombreArchivo,
          string nombreHoja,
          Action<string>? onProgressLog,
          string colEmpresa,
          string colValor)
    {
        var resultados = new List<Movimiento>();

        var (detectedEmp, detectedVal) = TryDetectColumns(sheetNota);
        if (detectedVal != null && detectedVal != colValor)
        {
            onProgressLog?.Invoke($"[Info] {nombreHoja}: columna de valores auto-detectada: {detectedVal} (configurada: {colValor})");
            colValor = detectedVal;
        }
        if (detectedEmp != null && detectedEmp != colEmpresa)
        {
            onProgressLog?.Invoke($"[Info] {nombreHoja}: columna de empresas auto-detectada: {detectedEmp} (configurada: {colEmpresa})");
            colEmpresa = detectedEmp;
        }

        var startRow = BuscarInicioTabla(sheetNota, colEmpresa);
        var limit = Math.Min(sheetNota.RowCount, startRow + NotaDataScanMaxRows);

        string? rubroActual = null;
        for (var fila = startRow; fila <= limit; fila++)
        {
            var row = sheetNota.GetRow(fila);
            if (row == null) break;

            var nombre = row.GetCell(colEmpresa);
            if (string.IsNullOrWhiteSpace(nombre)) continue;
            if (EsGranTotal(nombre)) break;

            if (!TryParseDecimal(row.GetCell(colValor), out _))
            {
                var texto = Normalizar(nombre);
                if (!string.IsNullOrWhiteSpace(texto))
                {
                    rubroActual = texto;
                }
                break;
            }
        }

        var totalRowsEvaluated = 0;
        var totalRowsAccepted = 0;
        var totalRowsDiscarded = 0;
        var fsrRowsDetected = 0;
        var fsrMovements = new List<Movimiento>();

        for (var fila = startRow; fila <= limit; fila++)
        {
            var row = sheetNota.GetRow(fila);
            if (row == null)
                break;

            var nombre = row.GetCell(colEmpresa);
            if (string.IsNullOrWhiteSpace(nombre))
                continue;

            if (EsGranTotal(nombre))
                break;

            var tieneValor = TryParseDecimal(row.GetCell(colValor), out var valor);

            if (!tieneValor)
            {
                var texto = Normalizar(nombre);
                if (!string.IsNullOrWhiteSpace(texto))
                    rubroActual = texto;
                continue;
            }

            var textoNormalizado = Normalizar(nombre);
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
                onProgressLog?.Invoke($"[Fila {fila}] \"{nombre}\" -> DESCARTADA (estructural)");
                totalRowsDiscarded++;
                continue;
            }

            if (EsSubEntry(textoNormalizado, empresasNormalizadas))
            {
                onProgressLog?.Invoke($"[Fila {fila}] \"{nombre}\" -> DESCARTADA (subentrada)");
                totalRowsDiscarded++;
                continue;
            }

            if (EsRubro(textoNormalizado))
            {
                onProgressLog?.Invoke($"[Fila {fila}] \"{nombre}\" -> DESCARTADA (rubro)");
                totalRowsDiscarded++;
                continue;
            }

            var contraparte = HomologarEmpresa(textoNormalizado, empresasNormalizadas, aliasEmpresa);

            if (string.IsNullOrEmpty(contraparte))
            {
                onProgressLog?.Invoke($"[Fila {fila}] \"{nombre}\" -> DESCARTADA (no homologada)");
                totalRowsDiscarded++;
                continue;
            }

            var mov = new Movimiento
            {
                EmpresaOrigen = empresaOrigen,
                EmpresaContraparte = contraparte,
                Tipo = cuenta.Tipo,
                Cuenta = rubroActual ?? cuenta.NombreCuenta,
                Valor = valor,
                Nota = nota,
                Periodo = periodo
            };

            if (MovimientoValido(mov, onProgressLog, nombreHoja, nombreArchivo))
            {
                onProgressLog?.Invoke($"[Fila {fila}] \"{nombre}\" -> ACEPTADA (contraparte: {contraparte} | valor: {valor:N2})");
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
                onProgressLog?.Invoke($"[Fila {fila}] \"{nombre}\" -> DESCARTADA (movimiento invalido)");
                totalRowsDiscarded++;
            }
        }

        if (resultados.Count == 0 && totalRowsEvaluated == 0)
        {
            var (suggEmp, suggVal) = TryDetectColumns(sheetNota);
            if (suggVal != null && suggVal != colValor)
            {
                onProgressLog?.Invoke($"[Sugerencia] En {nombreHoja}, la columna configurada ({colValor}) no parece contener datos. Se detectó información en la columna {suggVal}. Por favor, actualice la configuración.");
            }
            else if (suggEmp != null && suggEmp != colEmpresa)
            {
                onProgressLog?.Invoke($"[Sugerencia] En {nombreHoja}, la columna de empresa configurada ({colEmpresa}) no parece correcta. Se detectó información en la columna {suggEmp}.");
            }
        }

        onProgressLog?.Invoke($"[Info] Nota {nota}: filas extraídas {resultados.Count}");

        var fsrTotal = fsrMovements.Sum(m => m.Valor);
        if (fsrRowsDetected > 0 || fsrMovements.Count > 0)
        {
            onProgressLog?.Invoke($"[Info] --- Diagnóstico FSR {nombreHoja} ---");
            onProgressLog?.Invoke($"[Info]   Filas evaluadas: {totalRowsEvaluated}");
            onProgressLog?.Invoke($"[Info]   Filas aceptadas: {totalRowsAccepted}");
            onProgressLog?.Invoke($"[Info]   Filas descartadas: {totalRowsDiscarded}");
            onProgressLog?.Invoke($"[Info]   Filas detectadas con (FSR): {fsrRowsDetected}");
            onProgressLog?.Invoke($"[Info]   Movimientos FSR generados: {fsrMovements.Count}");
            foreach (var m in fsrMovements)
            {
                onProgressLog?.Invoke($"[Info]     • {m.EmpresaOrigen} -> {m.EmpresaContraparte} | {m.Tipo} | {m.Valor:N2}");
            }
            onProgressLog?.Invoke($"[Info]   Valor total FSR: {fsrTotal:N2}");
        }

        return resultados;
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

    private static string HomologarEmpresa(
        string textoNormalizado,
        List<(EmpresaConfig Config, string NombreNormalizado)> empresas,
        List<EquivalenciaTercero> aliasEmpresa)
    {
        if (string.IsNullOrWhiteSpace(textoNormalizado))
            return string.Empty;

        // Level 1: Alias / Equivalencies
        foreach (var entry in aliasEmpresa)
        {
            var aliasNorm = Normalizar(entry.Alias);
            if (textoNormalizado == aliasNorm)
            {
                return Normalizar(entry.NombreEmpresaDestino);
            }
        }

        // Level 2: Exact match
        var exactMatch = empresas.FirstOrDefault(e => e.NombreNormalizado == textoNormalizado);
        if (exactMatch.Config != null)
            return exactMatch.Config.NombreEmpresa ?? exactMatch.Config.NombreCarpeta;

        // Level 3: Fuzzy match (85%+)
        var bestFuzzy = empresas
            .Select(e => (Config: e.Config, Similitud: LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado)))
            .Where(e => e.Similitud >= 0.85)
            .OrderByDescending(e => e.Similitud)
            .FirstOrDefault();

        if (bestFuzzy.Config != null)
            return bestFuzzy.Config.NombreEmpresa ?? bestFuzzy.Config.NombreCarpeta;

        // Level 4: Low-confidence match (70-84.99%)
        var posibles = new List<(EmpresaConfig Config, double Similitud, string Normalizado)>();
        foreach (var e in empresas)
        {
            var sim = LevenshteinSimilarity(textoNormalizado, e.NombreNormalizado);
            if (sim >= 0.70)
                posibles.Add((e.Config, sim, e.NombreNormalizado));
        }

        if (posibles.Count > 0)
        {
            // Warning is handled in the calling loop via onProgressLog, but we return null to avoid wrong insertion
            return string.Empty;
        }

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

        var result = texto.Trim().ToUpperInvariant();
        result = result.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(result.Length);
        var prevSpace = false;

        foreach (var ch in result)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                prevSpace = false;
                sb.Append(ch);
            }
            else
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
        }

        var normalized = sb.ToString().Trim().Normalize(NormalizationForm.FormC);
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
