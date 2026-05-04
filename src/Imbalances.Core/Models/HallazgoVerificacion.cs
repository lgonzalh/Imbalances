using System;

namespace Imbalances.Core.Models;

public class HallazgoVerificacion
{
    public string Id { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Severidad { get; set; } = string.Empty;
    public string Estado { get; set; } = "Pendiente";
    public string Empresa { get; set; } = string.Empty;
    public string Contraparte { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Reporte { get; set; } = string.Empty;
    public string ArchivoOrigen { get; set; } = string.Empty;
    public decimal ValorLocal { get; set; }
    public decimal ValorContraparte { get; set; }
    public decimal Diferencia { get; set; }
    public string Detalle { get; set; } = string.Empty;
    public DateTime FechaDeteccionUtc { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacionUtc { get; set; } = DateTime.UtcNow;
}
