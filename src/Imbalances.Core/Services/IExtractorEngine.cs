using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IExtractorEngine
{
    Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config, Action<string>? onProgressLog = null);
    Task<List<Movimiento>> ProcesarArchivoMotor1Async(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null);
    IEnumerable<object> Consolidar(List<RegistroContable> resultados);
    IEnumerable<object> Conciliar(List<RegistroContable> resultados);
}
