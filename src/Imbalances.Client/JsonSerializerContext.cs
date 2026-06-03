using System.Text.Json;
using System.Text.Json.Serialization;
using Imbalances.Core.Models;

namespace Imbalances.Client;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
[JsonSerializable(typeof(ConfiguracionCore))]
[JsonSerializable(typeof(EmpresaConfig))]
[JsonSerializable(typeof(CuentaConfig))]
[JsonSerializable(typeof(NotaConfig))]
[JsonSerializable(typeof(CuentaEquivalencia))]
[JsonSerializable(typeof(List<EmpresaConfig>))]
[JsonSerializable(typeof(List<CuentaConfig>))]
[JsonSerializable(typeof(List<NotaConfig>))]
[JsonSerializable(typeof(List<CuentaEquivalencia>))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}
