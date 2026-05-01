using System.Collections.Generic;

namespace Imbalances.Core.Models;

public class ConfiguracionCore
{
    public List<EmpresaConfig> Empresas { get; set; } = new();
    public List<NotaConfig> Notas { get; set; } = new();
    public List<CuentaEquivalencia> Equivalencias { get; set; } = new();
}
