namespace Imbalances.Core.Models;

public class Movimiento
{
    public string EmpresaOrigen { get; set; } = string.Empty;
    public string EmpresaContraparte { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string Nota { get; set; } = string.Empty;
    public string Periodo { get; set; } = string.Empty;
}

