using System.Text.Json.Serialization;

namespace Imbalances.Core.Models;

public class NotaConfig
{
    [JsonPropertyName("nota")]
    public string Nota { get; set; } = string.Empty;

    [JsonPropertyName("nombreHoja")]
    public string NombreHoja { get; set; } = string.Empty;
}
