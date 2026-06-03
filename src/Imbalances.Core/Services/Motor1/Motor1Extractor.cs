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

    private const string NotaEmpresaColumn = "C";
    private const string NotaValorColumn = "I";
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
            onProgressLog?.Invoke($"[Info] Empresa no detectada para {nombreArchivo} → SKIP");
            return resultados;
        }

        var empresaOrigen = string.IsNullOrWhiteSpace(empresa.NombreEmpresa) ? empresa.NombreCarpeta : empresa.NombreEmpresa;
        onProgressLog?.Invoke($"[Info] Empresa detectada: {empresaOrigen}");

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
            onProgressLog?.Invoke("[Info] No hay cuentas configuradas → SKIP");
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

            var movimientosCuenta = ExtraerDesdeNota(sheetNota, cuenta, nota, empresaOrigen, periodo, onProgressLog);
            resultados.AddRange(movimientosCuenta);
        }

        var dedup = new Dictionary<string, Movimiento>(StringComparer.Ordinal);
        foreach (var m in resultados)
        {
            var key = $"{m.EmpresaOrigen}|{m.EmpresaContraparte}|{m.Tipo}|{m.Cuenta}|{m.Nota}|{m.Periodo}|{m.Valor.ToString(CultureInfo.InvariantCulture)}";
            if (!dedup.ContainsKey(key))
                dedup[key] = m;
        }

        onProgressLog?.Invoke($"[Info] Archivo completado: {nombreArchivo} | Movimientos: {dedup.Count}");
        return dedup.Values.ToList();
    }

    private static int? BuscarFilaExacta(IExcelWorksheet sheet, string col, string texto, int maxRows)
    {
        var target = Normalizar(texto);
        if (string.IsNullOrWhiteSpace(target))
            return null;

        var limit = Math.Min(sheet.RowCount, maxRows);
        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null)
                return null;

            var value = Normalizar(row.GetCell(col));
            if (value == target)
                return fila;
        }

        return null;
    }

    private static List<Movimiento> ExtraerDesdeNota(
        IExcelWorksheet sheetNota,
        CuentaConfig cuenta,
        string nota,
        string empresaOrigen,
        string periodo,
        Action<string>? onProgressLog)
    {
        var resultados = new List<Movimiento>();
        var startRow = BuscarInicioTabla(sheetNota);
        var limit = Math.Min(sheetNota.RowCount, startRow + NotaDataScanMaxRows);

        for (var fila = startRow; fila <= limit; fila++)
        {
            var row = sheetNota.GetRow(fila);
            if (row == null)
                break;

            var nombre = row.GetCell(NotaEmpresaColumn);
            if (string.IsNullOrWhiteSpace(nombre))
                continue;

            if (EsTotal(nombre))
                break;

            var valorRaw = row.GetCell(NotaValorColumn);
            if (!TryParseDecimal(valorRaw, out var valor))
                continue;

            var contraparte = Normalizar(nombre);
            if (string.IsNullOrWhiteSpace(contraparte))
                continue;

            resultados.Add(new Movimiento
            {
                EmpresaOrigen = empresaOrigen,
                EmpresaContraparte = contraparte,
                Tipo = cuenta.Tipo,
                Cuenta = cuenta.NombreCuenta,
                Valor = valor,
                Nota = nota,
                Periodo = periodo
            });
        }

        onProgressLog?.Invoke($"[Info] Nota {nota}: filas extraídas {resultados.Count}");
        return resultados;
    }

    private static int BuscarInicioTabla(IExcelWorksheet sheet)
    {
        var limit = Math.Min(sheet.RowCount, NotaHeaderScanMaxRows);
        for (var fila = 1; fila <= limit; fila++)
        {
            var row = sheet.GetRow(fila);
            if (row == null)
                break;

            var cell = row.GetCell(NotaEmpresaColumn);
            if (cell.Contains("MOVIMIENTO", StringComparison.OrdinalIgnoreCase)
                || cell.Contains("CUENTAS", StringComparison.OrdinalIgnoreCase))
            {
                return fila + 1;
            }
        }

        return 10;
    }

    private static bool EsTotal(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        return texto.Contains("GRAN TOTAL", StringComparison.OrdinalIgnoreCase)
            || texto.Contains("TOTAL", StringComparison.OrdinalIgnoreCase);
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

        var trimmed = texto.Trim().ToUpperInvariant();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var prevSpace = false;

        foreach (var ch in decomposed)
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

            prevSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }
}

