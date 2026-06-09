using System;
using System.Collections.Generic;
using System.Linq;

namespace Imbalances.Core.Models;

public class PipelineProfile
{
    public string Archivo { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public long DeteccionEmpresaMs { get; set; }
    public long LecturaWorkbookMs { get; set; }
    public long DescubrimientoCuentasMs { get; set; }
    public long LecturaNotasMs { get; set; }
    public long ProcesamientoMovimientosMs { get; set; }
    public long TotalMs { get; set; }
    public int MovimientosGenerados { get; set; }
    public int NotasProcesadas { get; set; }
}

public class PipelineProfileSummary
{
    public List<PipelineProfile> Archivos { get; set; } = new();
    public int TotalArchivos => Archivos.Count;
    public long TotalTimeMs => Archivos.Sum(a => a.TotalMs);
    public int TotalMovimientos => Archivos.Sum(a => a.MovimientosGenerados);
    public double PromedioPorArchivoMs => Archivos.Count > 0 ? Archivos.Average(a => a.TotalMs) : 0;
}
