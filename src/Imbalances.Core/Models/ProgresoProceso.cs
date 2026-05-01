using System.Collections.Generic;

namespace Imbalances.Core.Models;

public class ProgresoProceso
{
    public int TotalSteps { get; set; }
    public int CurrentStep { get; set; }
    public string EtapaActual { get; set; } = string.Empty;
    public List<LogProceso> Logs { get; set; } = new();
}
