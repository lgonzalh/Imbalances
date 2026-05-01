using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IExtractorEngine
{
    Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config);
    IEnumerable<object> Consolidar(List<RegistroContable> resultados);
    IEnumerable<object> Conciliar(List<RegistroContable> resultados);
}
