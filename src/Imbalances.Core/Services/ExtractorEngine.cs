using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    public async Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config)
    {
        var resultados = new List<RegistroContable>();

        var empresa = config.Empresas.FirstOrDefault(e => Regex.IsMatch(filePath, e.CarpetaRegex, RegexOptions.IgnoreCase));
        if (empresa == null) return resultados;

        var workbook = await _excelProvider.OpenAsync(fileStream);
        var balance = workbook.GetWorksheet(empresa.HojaBalance);
        if (balance == null) return resultados;

        var listaNotasDetectadas = new HashSet<string>();

        foreach (var row in balance.Rows)
        {
            var nota = row.GetCell("J");
            if (!string.IsNullOrWhiteSpace(nota))
            {
                listaNotasDetectadas.Add(nota.Trim());
            }
        }

        foreach (var notaDetectada in listaNotasDetectadas)
        {
            var notaConfig = config.Notas.FirstOrDefault(n => n.Nota == notaDetectada);
            if (notaConfig == null) continue;

            var hojaNota = workbook.GetWorksheet(notaConfig.NombreHoja);
            if (hojaNota == null) continue;

            foreach (var eq in config.Equivalencias)
            {
                if (!eq.NotasPermitidas.Contains(notaDetectada)) continue;

                var fila = hojaNota.Rows.FirstOrDefault(r =>
                    eq.AliasTexto.Any(alias =>
                        r.Texto.Contains(alias, StringComparison.OrdinalIgnoreCase)));

                if (fila != null)
                {
                    var valorStr = fila.GetCell(eq.ColumnaValor);
                    var valor = ParseDecimal(valorStr);

                    resultados.Add(new RegistroContable
                    {
                        Empresa = empresa.Nombre,
                        Cuenta = eq.CuentaCanonica,
                        Categoria = eq.Categoria,
                        Tipo = eq.Tipo,
                        Nota = notaDetectada,
                        Valor = valor
                    });
                }
            }
        }

        return resultados;
    }

    private decimal ParseDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Replace(".", "").Replace(",", ".");
        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal result))
        {
            return result;
        }
        return 0;
    }

    public IEnumerable<object> Consolidar(List<RegistroContable> resultados)
    {
        return resultados
            .GroupBy(x => new { x.Empresa, x.Cuenta })
            .Select(g => new {
                g.Key.Empresa,
                g.Key.Cuenta,
                Valor = g.Sum(x => x.Valor)
            });
    }

    public IEnumerable<object> Conciliar(List<RegistroContable> resultados)
    {
        var cxc = resultados.Where(x => x.Categoria == "CxC");
        var cxp = resultados.Where(x => x.Categoria == "CxP");

        return from a in cxc
               join b in cxp on a.Empresa equals b.Empresa
               select new {
                   a.Empresa,
                   Diferencia = a.Valor - b.Valor
               };
    }
}
