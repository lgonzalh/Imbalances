using System.Text.Json.Serialization;

namespace Imbalances.Core.Models;

public class EmpresaConfig
{
    [JsonPropertyName("nombreEmpresa")]
    public string Nombre { get; set; } = string.Empty;

    [JsonIgnore]
    public string NombreEmpresa
    {
        get => Nombre;
        set => Nombre = value;
    }

    [JsonPropertyName("nombreCarpeta")]
    public string NombreCarpeta { get; set; } = string.Empty;

    [JsonPropertyName("carpetaRegex")]
    public string CarpetaRegex { get; set; } = string.Empty;

    [JsonPropertyName("archivoRegex")]
    public string ArchivoRegex { get; set; } = string.Empty;

    [JsonPropertyName("hojaBalance")]
    public string HojaBalance { get; set; } = string.Empty;
}
