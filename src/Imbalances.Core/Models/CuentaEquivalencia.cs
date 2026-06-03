using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Imbalances.Core.Models;

public class CuentaEquivalencia
{
    [JsonPropertyName("cuentaCanonica")]
    public string CuentaCanonica { get; set; } = string.Empty;

    [JsonPropertyName("categoria")]
    public string Categoria { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = string.Empty;

    [JsonPropertyName("aliasTexto")]
    public List<string> AliasTexto { get; set; } = new();

    [JsonPropertyName("notasPermitidas")]
    public List<string> NotasPermitidas { get; set; } = new();

    [JsonPropertyName("columnaValor")]
    public string ColumnaValor { get; set; } = string.Empty;
}
