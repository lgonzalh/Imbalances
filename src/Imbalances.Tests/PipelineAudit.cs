using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Imbalances.Tests;

public record RowAudit(int Fila, string Texto, string Clasificacion, string? Motivo, string? Contraparte, decimal? Valor);

public record NotaAudit(string Nota, int InicioFila, int FinFila, int RowCount,
    List<RowAudit> Filas, int Rubros, int EmpresasHomologadas,
    int EmpresasDescartadas, int Estructurales, int SubEntradas, int Vacias, int Movimientos);

public record CuentaAudit(string Cuenta, string Tipo, string? NotaEncontrada, string? Resultado);

public record FsrAudit(int Apariciones, int Homologaciones, int Descartadas, string? MotivoDescarte);

public record ArchivoAudit(
    string Archivo,
    bool EmpresaDetectada,
    string? EmpresaNombre,
    bool BalanceEncontrado,
    List<CuentaAudit> Cuentas,
    List<NotaAudit> Notas,
    int FilasRecorridas,
    int MovimientosGenerados,
    FsrAudit? Fsr
);

public class PipelineAudit
{
    public List<ArchivoAudit> Archivos { get; } = new();
    public List<(string Archivo, string Detalle)> Errores { get; } = new();

    private ArchivoAudit? _current;
    private CuentaAudit? _currentCuenta;
    private List<RowAudit> _currentFilas = new();
    private int _currentRubros;
    private int _currentEmpresasHomologadas;
    private int _currentEmpresasDescartadas;
    private int _currentEstructurales;
    private int _currentSubEntradas;
    private int _currentVacias;
    private int _currentMovimientos;
    private int _currentFsrApariciones;
    private int _currentFsrHomologaciones;
    private int _currentFsrDescartadas;
    private string? _currentFsrMotivoDescarte;
    private string? _notaActual;
    private int _notaInicio;
    private int _notaFin;
    private int _notaRowCount;

    public Action<string> CreateLogger()
    {
        return msg =>
        {
            ParseLine(msg);
        };
    }

    private void ParseLine(string msg)
    {
        if (msg.StartsWith("[Info] Procesando: "))
        {
            FinalizeCurrentNota();
            FinalizeCurrentCuenta();
            FinalizeCurrentArchivo();

            var archivo = msg["[Info] Procesando: ".Length..].Trim();
            _current = new ArchivoAudit(archivo, false, null, false, new(), new(), 0, 0, null);
            return;
        }

        if (_current == null) return;

        if (msg.StartsWith("[Info] Empresa detectada: "))
        {
            var nombre = msg["[Info] Empresa detectada: ".Length..].Trim();
            _current = _current with { EmpresaDetectada = true, EmpresaNombre = nombre };
            return;
        }

        if (msg.StartsWith("[Error] Hoja balance no encontrada"))
        {
            _current = _current with { BalanceEncontrado = false };
            FinalizeCurrentArchivo();
            return;
        }

        if (msg.StartsWith("[Diagnostic] Buscando hoja balance:"))
        {
            _current = _current with { BalanceEncontrado = true };
            return;
        }

        if (msg.StartsWith("[Info] Cuenta: "))
        {
            FinalizeCurrentNota();
            var nombre = msg["[Info] Cuenta: ".Length..].Trim();
            _currentCuenta = new CuentaAudit(nombre, "", null, null);
            return;
        }

        if (msg.StartsWith("[Info] ") && msg.Contains(": InicioFila="))
        {
            FinalizeCurrentNota();

            var match = Regex.Match(msg, @"\[Info\] ([^:]+): InicioFila=(\d+), FinFila=(\d+), RowCount=(\d+)");
            if (match.Success)
            {
                _notaActual = match.Groups[1].Value;
                _notaInicio = int.Parse(match.Groups[2].Value);
                _notaFin = int.Parse(match.Groups[3].Value);
                _notaRowCount = int.Parse(match.Groups[4].Value);
                _currentFilas = new();
                _currentRubros = 0;
                _currentEmpresasHomologadas = 0;
                _currentEmpresasDescartadas = 0;
                _currentEstructurales = 0;
                _currentSubEntradas = 0;
                _currentMovimientos = 0;
            }

            if (_currentCuenta != null)
            {
                _currentCuenta = _currentCuenta with { NotaEncontrada = _notaActual };
            }
            return;
        }

        if (msg.StartsWith("[Fila ") && (msg.Contains("RUBRO detectado") || msg.Contains("DESCARTADA") || msg.Contains("CONTRAPARTE detectada") || msg.Contains(" MOVIMIENTO generado") || msg.Contains(" Vacio:")))
        {
            var filaMatch = Regex.Match(msg, @"\[Fila (\d+)\]");
            if (!filaMatch.Success) return;
            var fila = int.Parse(filaMatch.Groups[1].Value);

            string texto = "";
            string clasificacion;
            string? motivo = null;
            string? contraparte = null;
            decimal? valor = null;

            if (msg.Contains("RUBRO detectado:"))
            {
                var tmatch = Regex.Match(msg, @"RUBRO detectado: ""([^""]*)""");
                texto = tmatch.Success ? tmatch.Groups[1].Value : "";
                clasificacion = "Rubro";
                _currentRubros++;
            }
            else if (msg.Contains("DESCARTADA (estructural)"))
            {
                var tmatch = Regex.Match(msg, @"^\[Fila \d+\] ""([^""]*)""");
                texto = tmatch.Success ? tmatch.Groups[1].Value : "";
                clasificacion = "Estructural";
                motivo = "Total/Subtotal/Movimiento";
                _currentEstructurales++;
            }
            else if (msg.Contains("DESCARTADA (subentrada)"))
            {
                var tmatch = Regex.Match(msg, @"^\[Fila \d+\] ""([^""]*)""");
                texto = tmatch.Success ? tmatch.Groups[1].Value : "";
                clasificacion = "SubEntrada";
                _currentSubEntradas++;
            }
            else if (msg.Contains("DESCARTADA (no homologada)"))
            {
                var tmatch = Regex.Match(msg, @"^\[Fila \d+\] ""([^""]*)""");
                texto = tmatch.Success ? tmatch.Groups[1].Value : "";
                clasificacion = "NoHomologada";
                motivo = "Empresa no configurada";

                if (texto.Contains("FSR") || texto.Contains("FUNDACION") || texto.Contains("SOLID RIVER"))
                {
                    _currentFsrApariciones++;
                    _currentFsrDescartadas++;
                    _currentFsrMotivoDescarte = "No homologada (aunque coincide con FSR)";
                }

                _currentEmpresasDescartadas++;
            }
            else if (msg.Contains("DESCARTADA (movimiento invalido)"))
            {
                var tmatch = Regex.Match(msg, @"^\[Fila \d+\] ""([^""]*)""");
                texto = tmatch.Success ? tmatch.Groups[1].Value : "";
                clasificacion = "MovInvalido";
                _currentEmpresasDescartadas++;
            }
            else if (msg.Contains("CONTRAPARTE detectada:"))
            {
                var tmatch = Regex.Match(msg, @"""([^""]*)""");
                texto = tmatch.Success ? tmatch.Groups[1].Value : "";

                var cmatch = Regex.Match(msg, @"CONTRAPARTE detectada: ""([^""]*)""");
                contraparte = cmatch.Success ? cmatch.Groups[1].Value : "";
                clasificacion = "Contraparte";

                if (contraparte?.Contains("FUNDACION SOLID RIVER", StringComparison.OrdinalIgnoreCase) == true ||
                    contraparte == "FSR")
                {
                    _currentFsrApariciones++;
                    _currentFsrHomologaciones++;
                }
            }
            else if (msg.Contains("MOVIMIENTO generado:"))
            {
                clasificacion = "Movimiento";
                _currentMovimientos++;

                var tmatch = Regex.Match(msg, @"\[Fila \d+\] MOVIMIENTO generado: ([^|]+) \|-> ([^|]+)");
                if (tmatch.Success)
                {
                    texto = tmatch.Groups[1].Value.Trim();
                    contraparte = tmatch.Groups[2].Value.Trim();
                }
                else
                {
                    var cmatch = Regex.Match(msg, @"-> ([^|]+) \|");
                    if (cmatch.Success)
                        contraparte = cmatch.Groups[1].Value.Trim();
                }

                var vmatch = Regex.Match(msg, @"\| (-?\d+(?:[.,]\d+)?)");
                if (vmatch.Success && decimal.TryParse(vmatch.Groups[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var dv))
                    valor = dv;
            }
            else if (msg.Contains("Vacio:") || msg.Contains(" sin texto en columna empresa"))
            {
                clasificacion = "Vacia";
                _currentVacias++;
            }
            else
            {
                clasificacion = "Desconocida";
            }

            _currentFilas.Add(new RowAudit(fila, texto, clasificacion, motivo, contraparte, valor));
            return;
        }

        if (msg.StartsWith("[Info] ") && msg.Contains(": filas extra") && msg.Contains("0"))
        {
            // Finalize the nota
            FinalizeCurrentNota();
            return;
        }

        if (msg.StartsWith("[Info] Archivo completado:"))
        {
            FinalizeCurrentNota();
            FinalizeCurrentCuenta();

            var match = Regex.Match(msg, @"Archivo completado: ([^|]+) \| Movimientos: (\d+)");
            if (match.Success)
            {
                var archivo = match.Groups[1].Value.Trim();
                var movs = int.Parse(match.Groups[2].Value);
                if (_current != null)
                {
                    _current = _current with { MovimientosGenerados = movs };
                }
            }
            FinalizeCurrentArchivo();
        }
    }

    private void FinalizeCurrentNota()
    {
        if (_notaActual != null)
        {
            var nota = new NotaAudit(
                _notaActual, _notaInicio, _notaFin, _notaRowCount,
                new List<RowAudit>(_currentFilas),
                _currentRubros, _currentEmpresasHomologadas,
                _currentEmpresasDescartadas, _currentEstructurales,
                _currentSubEntradas, _currentVacias, _currentMovimientos
            );

            if (_current != null)
            {
                var notas = new List<NotaAudit>(_current.Notas) { nota };
                _current = _current with { Notas = notas };
                _current = _current with { FilasRecorridas = _current.FilasRecorridas + (_notaFin - _notaInicio + 1) };
            }

            _notaActual = null;
            _currentFilas = new();
        }
    }

    private void FinalizeCurrentCuenta()
    {
        if (_currentCuenta != null && _current != null)
        {
            var cuentas = new List<CuentaAudit>(_current.Cuentas) { _currentCuenta };
            _current = _current with { Cuentas = cuentas };
            _currentCuenta = null;
        }
    }

    private void FinalizeCurrentArchivo()
    {
        if (_current != null)
        {
            if (_current.Fsr == null && (_currentFsrApariciones > 0 || _currentFsrHomologaciones > 0 || _currentFsrDescartadas > 0))
            {
                _current = _current with
                {
                    Fsr = new FsrAudit(_currentFsrApariciones, _currentFsrHomologaciones, _currentFsrDescartadas, _currentFsrMotivoDescarte)
                };
            }

            var existing = Archivos.FirstOrDefault(a => a.Archivo == _current.Archivo);
            if (existing == null)
            {
                Archivos.Add(_current);
            }

            _current = null;
            _currentCuenta = null;
            _currentFsrApariciones = 0;
            _currentFsrHomologaciones = 0;
            _currentFsrDescartadas = 0;
            _currentFsrMotivoDescarte = null;
        }
    }

    public string GenerarReporte()
    {
        var sb = new System.Text.StringBuilder();

        var totalMovs = 0;
        var totalFilasRecorridas = 0;
        var totalEmpresasHomologadas = 0;
        var totalEmpresasDescartadas = 0;
        var totalRubros = 0;
        var totalEstructurales = 0;
        var totalSubEntradas = 0;
        var totalVacias = 0;
        var totalFsrApariciones = 0;
        var totalFsrHomologaciones = 0;
        var totalFsrDescartadas = 0;
        var archivosConMovs = 0;
        var archivosSinMovs = 0;
        var causasDescarte = new Dictionary<string, int>();

        foreach (var archivo in Archivos)
        {
            var noDetectada = !archivo.EmpresaDetectada ? "NO" : "SI";
            var balanceOk = archivo.BalanceEncontrado ? "SI" : "NO";
            sb.AppendLine($"\nARCHIVO: {archivo.Archivo}");
            sb.AppendLine($"  Empresa detectada: {noDetectada}" + (archivo.EmpresaNombre != null ? $" ({archivo.EmpresaNombre})" : ""));
            sb.AppendLine($"  Balance encontrado: {balanceOk}");
            sb.AppendLine($"  Cuentas encontradas: {archivo.Cuentas.Count}");
            foreach (var c in archivo.Cuentas)
            {
                sb.AppendLine($"    - {c.Cuenta}: Nota={c.NotaEncontrada ?? "NO ENCONTRADA"}");
            }
            sb.AppendLine($"  Notas encontradas: {archivo.Notas.Count}");

            var archivoFilas = 0;
            var archivoRubros = 0;
            var archivoEmpHom = 0;
            var archivoEmpDesc = 0;
            var archivoEstruct = 0;
            var archivoSub = 0;
            var archivoVacio = 0;
            var archivoMovs = 0;

            foreach (var nota in archivo.Notas)
            {
                archivoFilas += nota.Filas.Count;
                archivoRubros += nota.Rubros;
                archivoEmpHom += nota.EmpresasHomologadas;
                archivoEmpDesc += nota.EmpresasDescartadas;
                archivoEstruct += nota.Estructurales;
                archivoSub += nota.SubEntradas;
                archivoVacio += nota.Vacias;
                archivoMovs += nota.Movimientos;

                sb.AppendLine($"    Nota {nota.Nota}:");
                sb.AppendLine($"      Filas recorridas: {nota.Filas.Count}");
                sb.AppendLine($"      Rubros: {nota.Rubros}");
                sb.AppendLine($"      Empresas homologadas: {nota.EmpresasHomologadas}");
                sb.AppendLine($"      Empresas descartadas: {nota.EmpresasDescartadas}");
                sb.AppendLine($"      Totales/Subtotales: {nota.Estructurales}");
                sb.AppendLine($"      Sub-entradas: {nota.SubEntradas}");
                sb.AppendLine($"      Vacias: {nota.Vacias}");
                sb.AppendLine($"      Movimientos generados: {nota.Movimientos}");

                if (nota.Filas.Any(f => f.Clasificacion == "NoHomologada"))
                {
                    sb.AppendLine($"      Empresas NO homologadas:");
                    foreach (var f in nota.Filas.Where(f => f.Clasificacion == "NoHomologada"))
                        sb.AppendLine($"        - \"{f.Texto}\"");
                }
            }

            sb.AppendLine($"  Filas recorridas (total): {archivoFilas}");
            sb.AppendLine($"  Movimientos generados: {archivoMovs}");

            if (archivo.Fsr != null)
            {
                sb.AppendLine($"  FUNDACION SOLID RIVER:");
                sb.AppendLine($"    Apariciones: {archivo.Fsr.Apariciones}");
                sb.AppendLine($"    Homologaciones: {archivo.Fsr.Homologaciones}");
                sb.AppendLine($"    Descartadas: {archivo.Fsr.Descartadas}");
                if (archivo.Fsr.MotivoDescarte != null)
                    sb.AppendLine($"    Motivo descarte: {archivo.Fsr.MotivoDescarte}");
            }

            totalMovs += archivoMovs;
            totalFilasRecorridas += archivoFilas;
            totalEmpresasHomologadas += archivoEmpHom;
            totalEmpresasDescartadas += archivoEmpDesc;
            totalRubros += archivoRubros;
            totalEstructurales += archivoEstruct;
            totalSubEntradas += archivoSub;
            totalVacias += archivoVacio;

            if (archivo.Fsr != null)
            {
                totalFsrApariciones += archivo.Fsr.Apariciones;
                totalFsrHomologaciones += archivo.Fsr.Homologaciones;
                totalFsrDescartadas += archivo.Fsr.Descartadas;
            }

            if (archivoMovs > 0) archivosConMovs++; else archivosSinMovs++;
        }

        sb.AppendLine("\n========================================");
        sb.AppendLine("RESUMEN GLOBAL");
        sb.AppendLine("========================================");
        sb.AppendLine($"Archivos procesados: {Archivos.Count}");
        sb.AppendLine($"Archivos con movimientos: {archivosConMovs}");
        sb.AppendLine($"Archivos sin movimientos: {archivosSinMovs}");
        sb.AppendLine("");
        sb.AppendLine($"Empresas detectadas: {Archivos.Count(a => a.EmpresaDetectada)}");
        sb.AppendLine($"Balances encontrados: {Archivos.Count(a => a.BalanceEncontrado)}");
        sb.AppendLine($"Notas encontradas: {Archivos.Sum(a => a.Notas.Count)}");
        sb.AppendLine($"");
        sb.AppendLine($"Filas recorridas: {totalFilasRecorridas}");
        sb.AppendLine($"  Rubros: {totalRubros}");
        sb.AppendLine($"  Empresas: {totalEmpresasHomologadas + totalEmpresasDescartadas}");
        sb.AppendLine($"    Homologadas: {totalEmpresasHomologadas}");
        sb.AppendLine($"    Descartadas: {totalEmpresasDescartadas}");
        sb.AppendLine($"  Totales/Subtotales: {totalEstructurales}");
        sb.AppendLine($"  Sub-entradas: {totalSubEntradas}");
        sb.AppendLine($"  Vacias: {totalVacias}");
        sb.AppendLine($"");
        sb.AppendLine($"Movimientos generados: {totalMovs}");

        var totalEmpresas = totalEmpresasHomologadas + totalEmpresasDescartadas;
        var conversion = totalEmpresas > 0 ? (double)totalEmpresasHomologadas / totalEmpresas * 100 : 0;
        sb.AppendLine($"");
        sb.AppendLine($"Conversion Filas->Movimiento:");
        sb.AppendLine($"  {totalEmpresas} filas empresa -> {totalMovs} movimientos");
        sb.AppendLine($"  Tasa homologacion: {conversion:F2}%");
        var tasaMovFila = totalFilasRecorridas > 0 ? (double)totalMovs / totalFilasRecorridas * 100 : 0;
        sb.AppendLine($"  Tasa mov/fila: {tasaMovFila:F2}%");

        if (totalFsrApariciones > 0)
        {
            sb.AppendLine($"\nFUNDACION SOLID RIVER:");
            sb.AppendLine($"  Apariciones totales: {totalFsrApariciones}");
            sb.AppendLine($"  Homologaciones totales: {totalFsrHomologaciones}");
            sb.AppendLine($"  Descartadas totales: {totalFsrDescartadas}");
        }

        sb.AppendLine("\n========================================");
        sb.AppendLine("TOP CAUSAS DE DESCARTE");
        sb.AppendLine("========================================");

        var causas = new List<(string Motivo, int Conteo)>
        {
            ("Rubro", totalRubros),
            ("Total/Subtotal/Movimiento (estructural)", totalEstructurales),
            ("Sub-entrada", totalSubEntradas),
            ("Empresa no homologada", totalEmpresasDescartadas),
            ("Vacia", totalVacias),
        };
        causas = causas.Where(c => c.Conteo > 0).OrderByDescending(c => c.Conteo).ToList();

        foreach (var (motivo, count) in causas)
            sb.AppendLine($"  {count,-5} {motivo}");

        sb.AppendLine("\n========================================");
        sb.AppendLine("ANALISIS POR ARCHIVO SIN MOVIMIENTOS");
        sb.AppendLine("========================================");
        foreach (var archivo in Archivos.Where(a => a.MovimientosGenerados == 0))
        {
            sb.AppendLine($"\nARCHIVO: {archivo.Archivo}");
            if (!archivo.EmpresaDetectada)
                sb.AppendLine("  [PUNTO DE PERDIDA 1] Empresa NO detectada");
            else if (!archivo.BalanceEncontrado)
                sb.AppendLine("  [PUNTO DE PERDIDA 2] Balance NO encontrado");
            else if (archivo.Cuentas.Count == 0)
                sb.AppendLine("  [PUNTO DE PERDIDA 3] Cuentas NO encontradas en balance");
            else if (archivo.Notas.Count == 0)
                sb.AppendLine("  [PUNTO DE PERDIDA 4] Notas NO encontradas en workbook");
            else if (archivo.Notas.Sum(n => n.EmpresasHomologadas) == 0)
            {
                sb.AppendLine("  [PUNTO DE PERDIDA 5] Empresas NO homologadas en nota");
                var noHom = archivo.Notas.SelectMany(n => n.Filas.Where(f => f.Clasificacion == "NoHomologada")).ToList();
                foreach (var f in noHom)
                    sb.AppendLine($"    - \"{f.Texto}\" -> NO HOMOLOGADA");
                var soloRubro = archivo.Notas.SelectMany(n => n.Filas.Where(f => f.Clasificacion == "Rubro")).ToList();
                foreach (var f in soloRubro)
                    sb.AppendLine($"    - \"{f.Texto}\" -> RUBRO");
            }
            else
                sb.AppendLine("  [PUNTO DE PERDIDA 6] Movimientos no generados pese a tener empresas homologadas");
        }

        sb.AppendLine("\n========================================");
        sb.AppendLine("RECOMENDACIONES TECNICAS");
        sb.AppendLine("========================================");

        if (Archivos.Any(a => !a.EmpresaDetectada))
            sb.AppendLine("- Mejorar deteccion de empresas: expandir patrones de carpeta/archivo");
        if (Archivos.Any(a => !a.BalanceEncontrado))
            sb.AppendLine("- Mejorar deteccion de balance: verificar nombres de hojas en workbook");
        if (Archivos.Any(a => a.Cuentas.Count == 0))
            sb.AppendLine("- Verificar configuracion de cuentas: nombres deben coincidir exactamente");
        if (Archivos.Any(a => a.Notas.Count == 0))
            sb.AppendLine("- Verificar columna de nota en balance: columna por defecto es J");
        if (Archivos.Any(a => a.Notas.Sum(n => n.EmpresasHomologadas) == 0 && a.Notas.Sum(n => n.Rubros) > 0))
            sb.AppendLine("- Valores no encontrados en columna valor: verificar columna configurada vs datos");
        if (totalEmpresasDescartadas > 0)
            sb.AppendLine("- Ampliar diccionario de empresas o mejorar fuzzy matching");
        if (totalFsrApariciones > totalFsrHomologaciones)
            sb.AppendLine("- Verificar homologacion de FUNDACION SOLID RIVER: alias o nombre exacto faltante");
        if (totalMovs == 0 && Archivos.Count > 0)
            sb.AppendLine("- URGENTE: Ningun movimiento generado. Revisar pipeline completo.");

        return sb.ToString();
    }
}
