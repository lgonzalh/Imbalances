using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public class ExtractorEngine : IExtractorEngine
{
    private readonly IExcelProvider _excelProvider;

    public ExtractorEngine(IExcelProvider excelProvider)
    {
        _excelProvider = excelProvider;
    }

    public async Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config, Action<string>? onProgressLog = null)
    {
        var resultados = new List<RegistroContable>();
        var nombreArchivo = Path.GetFileName(filePath);

        onProgressLog?.Invoke($"[Info] Procesando {nombreArchivo}");

        var empresa = config.Empresas.FirstOrDefault(e => filePath.Contains(e.NombreCarpeta, StringComparison.OrdinalIgnoreCase));
        if (empresa == null)
        {
            onProgressLog?.Invoke($"[Info] Empresa no detectada para {nombreArchivo} → SKIP");
            return resultados;
        }

        onProgressLog?.Invoke($"[Info] Empresa detectada: {empresa.NombreEmpresa}");

        var workbook = await _excelProvider.OpenAsync(fileStream);
        
        var balance = workbook.Worksheets.FirstOrDefault(w => w.Name.Contains("Balance", StringComparison.OrdinalIgnoreCase));
        if (balance == null)
        {
            onProgressLog?.Invoke($"[Error] No hay hoja 'Balance' en {nombreArchivo}");
            return resultados;
        }

        onProgressLog?.Invoke($"[Info] Leyendo Balance: Cuenta (Col C) → Nota (Col dinámicamente)");

        int totalFilas = 0;
        int cuentasConfiguradas = 0;
        int notasProcesadas = 0;
        int notasIgnoradas = 0;

        foreach (var row in balance.Rows)
        {
            totalFilas++;
            var nombreCuenta = row.GetCell("C").Trim(); 
            if (string.IsNullOrEmpty(nombreCuenta)) continue;

            var cuentaConfig = config.Cuentas.FirstOrDefault(c => c.NombreCuenta.Equals(nombreCuenta, StringComparison.OrdinalIgnoreCase));
            
            if (cuentaConfig == null) 
            {
                onProgressLog?.Invoke($"[Info] Fila {totalFilas}: Cuenta '{nombreCuenta}' no configurada → SKIP");
                continue;
            }

            var notaRaw = row.GetCell(cuentaConfig.ColumnaNota).Trim();
            if (string.IsNullOrEmpty(notaRaw)) 
            {
                onProgressLog?.Invoke($"[Info] Fila {totalFilas}: Sin nota en columna {cuentaConfig.ColumnaNota} → SKIP");
                notasIgnoradas++;
                continue;
            }

            // Validar que la nota sea numérica y esté en el rango permitido (1-20)
            if (!int.TryParse(notaRaw, out int numeroNota) || numeroNota < 1 || numeroNota > 20)
            {
                onProgressLog?.Invoke($"[Info] Fila {totalFilas}: Nota {notaRaw} no permitida para '{nombreCuenta}' → SKIP");
                notasIgnoradas++;
                continue;
            }

            var nombreHoja = $"Nota {notaRaw}".Trim();
            var hojaNota = workbook.GetWorksheet(nombreHoja);

            if (hojaNota == null) 
            {
                onProgressLog?.Invoke($"[Info] Fila {totalFilas}: Hoja 'Nota {notaRaw}' no encontrada → SKIP");
                notasIgnoradas++;
                continue;
            }

            int registrosLeidos = 0;
            notasProcesadas++;
            cuentasConfiguradas++;

            foreach (var rowNota in hojaNota.Rows)
            {
                var descripcionRaw = rowNota.GetCell("C").Trim();
                if (string.IsNullOrEmpty(descripcionRaw)) continue;

                bool coincide = descripcionRaw.Contains(cuentaConfig.NombreCuenta, StringComparison.OrdinalIgnoreCase);

                if (!coincide) continue;

                var valorStr = rowNota.GetCell(cuentaConfig.ColumnaValor);
                var valor = ParseDecimal(valorStr);
                
                if (valor != 0)
                {
                    registrosLeidos++;
                    resultados.Add(new RegistroContable
                    {
                        Empresa = empresa.NombreEmpresa,
                        Cuenta = cuentaConfig.NombreCuenta,
                        Categoria = cuentaConfig.Tipo,
                        Tipo = cuentaConfig.Tipo,
                        Nota = notaRaw,
                        Valor = valor,
                        ArchivoOrigen = nombreArchivo,
                        HojaOrigen = hojaNota.Name,
                        TextoOrigen = descripcionRaw
                    });
                }
            }

            if (registrosLeidos > 0)
            {
                onProgressLog?.Invoke($"[Info] Cuenta: {cuentaConfig.NombreCuenta} | Nota: {notaRaw} | Registros: {registrosLeidos}");
            }
        }

        // Generar resumen
        onProgressLog?.Invoke("[Info] --- RESUMEN ---");
        onProgressLog?.Invoke($"[Info] Filas leídas del Balance: {totalFilas}");
        onProgressLog?.Invoke($"[Info] Cuentas configuradas encontradas: {cuentasConfiguradas}");
        onProgressLog?.Invoke($"[Info] Notas procesadas: {notasProcesadas}");
        onProgressLog?.Invoke($"[Info] Notas ignoradas: {notasIgnoradas}");
        onProgressLog?.Invoke($"[Info] Total registros extraídos: {resultados.Count}");
        onProgressLog?.Invoke($"[Info] Extracción finalizada para {empresa.NombreEmpresa}");

        return resultados;
    }

    private decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Replace("$", "").Replace(" ", "");
        if (value.Contains(",") && value.Contains("."))
            value = value.Replace(".", "").Replace(",", ".");
        else if (value.Contains(","))
            value = value.Replace(",", ".");
        
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal res) ? res : 0;
    }

    public IEnumerable<object> Consolidar(List<RegistroContable> resultados)
    {
        return resultados.GroupBy(x => new { x.Empresa, x.Cuenta, x.Tipo, x.Nota })
            .Select(g => new { g.Key.Empresa, g.Key.Cuenta, g.Key.Tipo, g.Key.Nota, Valor = g.Sum(x => x.Valor) });
    }

    public IEnumerable<object> Conciliar(List<RegistroContable> resultados)
    {
        return resultados.GroupBy(x => new { x.Empresa, x.Tipo })
            .Select(g => new { g.Key.Empresa, g.Key.Tipo, Total = g.Sum(x => x.Valor) });
    }
}
