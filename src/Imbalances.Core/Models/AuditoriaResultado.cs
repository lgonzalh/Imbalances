using System;
using System.Collections.Generic;

namespace Imbalances.Core.Models;

public class AuditoriaResultado
{
    public DateTime FechaEjecucionUtc { get; set; } = DateTime.UtcNow;
    public List<ReciprocidadResultado> Reciprocidades { get; set; } = new();
    public List<DocumentoVerificado> Documentos { get; set; } = new();
    public List<HallazgoVerificacion> Hallazgos { get; set; } = new();
}
