using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Imbalances.Core.Models;

public class CuentaConfig
{
    [JsonPropertyName("nombreCuenta")]
    public string NombreCuenta { get; set; } = string.Empty;

    [JsonPropertyName("tipo")]
    public string Tipo { get; set; } = "CxC"; // CxC | CxP

    [JsonPropertyName("columnaValor")]
    public string ColumnaValor { get; set; } = "C";

    [JsonPropertyName("columnaNota")]
    public string ColumnaNota { get; set; } = "J";
}
