using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public class ExtractorEngine : IExtractorEngine
{
    private readonly IMotor1Extractor _motor1;

    public ExtractorEngine(IMotor1Extractor motor1)
    {
        _motor1 = motor1;
    }

    public async Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config, Action<string>? onProgressLog = null)
    {
        var nombreArchivo = Path.GetFileName(filePath);
        var movimientos = await ProcesarArchivoMotor1Async(filePath, fileStream, config, periodo: string.Empty, onProgressLog);
        var resultados = movimientos.Select(m => new RegistroContable
        {
            Empresa = m.EmpresaOrigen,
            EmpresaContraparte = m.EmpresaContraparte,
            Cuenta = m.Cuenta,
            Categoria = m.Tipo,
            Tipo = m.Tipo,
            Nota = m.Nota,
            Valor = m.Valor,
            ArchivoOrigen = nombreArchivo,
            HojaOrigen = string.IsNullOrWhiteSpace(m.Nota) ? string.Empty : $"Nota {m.Nota}",
            TextoOrigen = m.EmpresaContraparte
        }).ToList();

        return resultados;

    }

    public async Task<List<Movimiento>> ProcesarArchivoMotor1Async(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null)
    {
        return await _motor1.ExtraerAsync(filePath, fileStream, config, periodo, onProgressLog);
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
