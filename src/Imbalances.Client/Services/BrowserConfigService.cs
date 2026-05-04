using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Imbalances.Client.Services;

public class BrowserConfigService : IConfigService
{
    private const string ConfigKey = "config";

    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<BrowserConfigService> _logger;
    private ConfiguracionCore? _cached;

    public BrowserConfigService(IJSRuntime jsRuntime, ILogger<BrowserConfigService> logger)
    {
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task<ConfiguracionCore> CargarConfiguracionAsync()
    {
        if (_cached != null)
        {
            return _cached;
        }

        try
        {
            var config = await _jsRuntime.InvokeAsync<ConfiguracionCore?>("imbalancesPersistence.loadItem", ConfigKey);
            _cached = config ?? CreateDefaultConfig();
        }
        catch (JSException ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar la configuración desde el navegador; se usará la configuración por defecto.");
            _cached = CreateDefaultConfig();
        }

        return _cached;
    }

    public async Task GuardarConfiguracionAsync(ConfiguracionCore config)
    {
        _cached = config;
        try
        {
            await _jsRuntime.InvokeVoidAsync("imbalancesPersistence.saveItem", ConfigKey, config);
        }
        catch (JSException ex)
        {
            _logger.LogWarning(ex, "No se pudo guardar la configuración en el navegador.");
        }
    }

    private static ConfiguracionCore CreateDefaultConfig()
    {
        return new ConfiguracionCore
        {
            Empresas =
            {
                new EmpresaConfig { NombreEmpresa = "AGUILAS DE LA U", NombreCarpeta = "AGUILAS" },
                new EmpresaConfig { NombreEmpresa = "SASA", NombreCarpeta = "SASA" },
                new EmpresaConfig { NombreEmpresa = "SAN FRANCISCO FC", NombreCarpeta = "SAN FRANCISCO" }
            },
            Cuentas =
            {
                new CuentaConfig
                {
                    NombreCuenta = "Cuentas por cobrar - Partes relacionadas",
                    Tipo = "CxC",
                    ColumnaValor = "C",
                    ColumnaNota = "J"
                },
                new CuentaConfig
                {
                    NombreCuenta = "Cuentas por pagar - Partes relacionadas",
                    Tipo = "CxP",
                    ColumnaValor = "C",
                    ColumnaNota = "J"
                }
            }
        };
    }
}
