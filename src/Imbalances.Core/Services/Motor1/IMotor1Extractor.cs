using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IMotor1Extractor
{
    Task<List<Movimiento>> ExtraerAsync(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null, bool diagnosticMode = true, Action<PipelineProfile>? onProfile = null);
}
