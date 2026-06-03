using System.Collections.Generic;

namespace Imbalances.Core.Models;

public class CuentaConfig
{
    public string NombreCuenta { get; set; } = string.Empty;
    public string Tipo { get; set; } = "CxC"; // CxC | CxP
    public string ColumnaValor { get; set; } = "C";
    public string ColumnaNota { get; set; } = "J";
}
