using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Imbalances.Core.Models;

namespace Imbalances.Client.Services;

public static class MovimientosIntercompanyService
{
    public static List<Movimiento> NormalizarYAgrupar(IEnumerable<Movimiento> movimientosCrudos, string periodo)
    {
        var normalizados = movimientosCrudos
            .Select(m => new Movimiento
            {
                EmpresaOrigen = NormalizeId(m.EmpresaOrigen),
                EmpresaContraparte = NormalizeId(m.EmpresaContraparte),
                Tipo = NormalizeTipo(m.Tipo),
                Cuenta = NormalizeId(m.Cuenta),
                Nota = string.Empty,
                Valor = m.Valor,
                Periodo = periodo,
            })
            .Where(m => !string.IsNullOrWhiteSpace(m.EmpresaOrigen)
                        && !string.IsNullOrWhiteSpace(m.EmpresaContraparte)
                        && (m.Tipo == "CxC" || m.Tipo == "CxP"))
            .ToList();

        return normalizados
            .GroupBy(m => new { m.EmpresaOrigen, m.EmpresaContraparte, m.Tipo, m.Cuenta, m.Periodo })
            .Select(g => new Movimiento
            {
                EmpresaOrigen = g.Key.EmpresaOrigen,
                EmpresaContraparte = g.Key.EmpresaContraparte,
                Tipo = g.Key.Tipo,
                Cuenta = g.Key.Cuenta,
                Nota = string.Empty,
                Valor = g.Sum(x => x.Valor),
                Periodo = g.Key.Periodo,
            })
            .Where(m => m.Valor != 0)
            .OrderBy(m => m.EmpresaOrigen)
            .ThenBy(m => m.EmpresaContraparte)
            .ThenBy(m => m.Cuenta)
            .ThenBy(m => m.Tipo)
            .ToList();
    }

    public static bool EsPeriodoValido(string periodo)
    {
        if (string.IsNullOrWhiteSpace(periodo)) return false;
        if (periodo.Length != 7) return false;
        if (periodo[4] != '-') return false;

        return DateTime.TryParseExact(periodo, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static string NormalizeTipo(string input)
    {
        var s = (input ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", "");
        return s switch
        {
            "CXC" => "CxC",
            "CXP" => "CxP",
            _ => string.Empty,
        };
    }

    private static string NormalizeId(string input)
    {
        var upper = (input ?? string.Empty).Trim().ToUpperInvariant();
        var noDiacritics = upper.Normalize(NormalizationForm.FormD);
        var filtered = new string(noDiacritics.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var kept = new string(filtered.Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ').ToArray());
        return string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
