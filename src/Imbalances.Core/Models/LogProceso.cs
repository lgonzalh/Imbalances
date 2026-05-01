using System;

namespace Imbalances.Core.Models;

public class LogProceso
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Mensaje { get; set; } = string.Empty;
    public string Estado { get; set; } = "Info"; // Success, Error, Info, Warning
}
