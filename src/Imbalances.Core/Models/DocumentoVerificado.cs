namespace Imbalances.Core.Models;

public class DocumentoVerificado
{
    public string Id { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Reporte { get; set; } = string.Empty;
    public string ArchivoRegex { get; set; } = string.Empty;
    public string CarpetaRegex { get; set; } = string.Empty;
    public bool Presente { get; set; }
    public string ArchivoEncontrado { get; set; } = string.Empty;
    public string Detalle { get; set; } = string.Empty;
}
