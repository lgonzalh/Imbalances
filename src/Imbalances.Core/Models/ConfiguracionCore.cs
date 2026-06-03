using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Imbalances.Core.Models;

public class ConfiguracionCore
{
    [JsonPropertyName("empresas")]
    public List<EmpresaConfig> Empresas { get; set; } = new();

    [JsonPropertyName("cuentas")]
    public List<CuentaConfig> Cuentas { get; set; } = new();

    [JsonPropertyName("notas")]
    public List<NotaConfig> Notas { get; set; } = new();

    [JsonPropertyName("equivalencias")]
    public List<CuentaEquivalencia> Equivalencias { get; set; } = new();
}
