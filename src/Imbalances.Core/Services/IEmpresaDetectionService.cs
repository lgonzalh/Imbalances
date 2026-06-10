using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IEmpresaDetectionService
{
    EmpresaConfig? DetectarEmpresa(string filePath, ConfiguracionCore config, Action<string>? onProgressLog = null);
    EmpresaConfig? DetectarEmpresaEnArchivos(List<string> archivos, EmpresaConfig empresa, Action<string>? onProgressLog = null);
}
