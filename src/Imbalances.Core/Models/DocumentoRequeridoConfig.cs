namespace Imbalances.Core.Models;

public class DocumentoRequeridoConfig
{
    public string Nombre { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string CarpetaRegex { get; set; } = string.Empty;
    public string ArchivoRegex { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
}
