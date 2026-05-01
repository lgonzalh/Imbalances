using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IConfigService
{
    Task<ConfiguracionCore> CargarConfiguracionAsync();
    Task GuardarConfiguracionAsync(ConfiguracionCore config);
}
