using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Imbalances.Core.Models;

public class EquivalenciaTercero
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("equivalentes")]
    public List<string> Equivalentes { get; set; } = new();

    [JsonPropertyName("nombreEmpresaDestino")]
    public string NombreEmpresaDestino { get; set; } = string.Empty;
}