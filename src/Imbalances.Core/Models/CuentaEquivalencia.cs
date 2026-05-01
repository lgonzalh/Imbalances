using System.Collections.Generic;

namespace Imbalances.Core.Models;

public class CuentaEquivalencia
{
    public string CuentaCanonica { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public List<string> AliasTexto { get; set; } = new();
    public List<string> NotasPermitidas { get; set; } = new();
    public string ColumnaValor { get; set; } = string.Empty;
}
