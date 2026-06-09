using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IExtractorEngine
{
    Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config, Action<string>? onProgressLog = null, bool diagnosticMode = true);
    Task<List<Movimiento>> ProcesarArchivoMotor1Async(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null, bool diagnosticMode = true, Action<PipelineProfile>? onProfile = null);
    Task<(List<RegistroContable> Resultados, PipelineProfileSummary Perfil)> ProcesarMultiplesArchivosAsync(
        List<(string FilePath, Stream FileStream)> archivos,
        ConfiguracionCore config,
        Action<string>? onProgressLog = null,
        bool diagnosticMode = true,
        int maxParallelism = 4);
    IEnumerable<object> Consolidar(List<RegistroContable> resultados);
    IEnumerable<object> Conciliar(List<RegistroContable> resultados);
}
