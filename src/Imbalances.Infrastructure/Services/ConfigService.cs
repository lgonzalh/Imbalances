using System.Collections.Generic;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;

namespace Imbalances.Infrastructure.Services;

public class ConfigService : IConfigService
{
    private ConfiguracionCore _currentConfig;

    public ConfigService()
    {
        _currentConfig = new ConfiguracionCore
        {
            Empresas = new List<EmpresaConfig>
            {
                new EmpresaConfig
                {
                    Nombre = "AGUILAS DE LA U",
                    CarpetaRegex = "AGUILAS.*",
                    ArchivoRegex = "CIERRE.*",
                    HojaBalance = "Balance de situación"
                }
            },
            AliasEmpresa = new List<EquivalenciaTercero>
            {
                new EquivalenciaTercero
                {
                    Alias = "FSR",
                    Equivalentes = new List<string> { "FUNDACION SOLID RIVER" }
                }
            },
            Notas = new List<NotaConfig>
            {
                new NotaConfig { Nota = "3", NombreHoja = "Nota 3" },
                new NotaConfig { Nota = "4", NombreHoja = "Nota 4" }
            },
            Equivalencias = new List<CuentaEquivalencia>
            {
                new CuentaEquivalencia
                {
                    CuentaCanonica = "ANTICIPOS",
                    Categoria = "CxC",
                    Tipo = "Activo",
                    AliasTexto = new List<string> { "ANTICIP", "PAGO ANTICIP" },
                    NotasPermitidas = new List<string> { "3" },
                    ColumnaValor = "H"
                },
                new CuentaEquivalencia
                {
                    CuentaCanonica = "APORTES POR PAGAR",
                    Categoria = "CxP",
                    Tipo = "Pasivo",
                    AliasTexto = new List<string> { "APORTE" },
                    NotasPermitidas = new List<string> { "4" },
                    ColumnaValor = "H"
                }
            }
        };
    }

    public Task<ConfiguracionCore> CargarConfiguracionAsync()
    {
        return Task.FromResult(_currentConfig);
    }

    public Task GuardarConfiguracionAsync(ConfiguracionCore config)
    {
        _currentConfig = config;
        return Task.CompletedTask;
    }
}
