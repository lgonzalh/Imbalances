namespace Imbalances.Core.Models;

public class RegistroContable
{
    public string Empresa { get; set; } = string.Empty;
    public string Cuenta { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Nota { get; set; } = string.Empty;
    public decimal Valor { get; set; }
}
