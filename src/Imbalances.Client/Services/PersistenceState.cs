using System.Collections.Generic;
using Imbalances.Client.Models;
using Imbalances.Core.Models;

namespace Imbalances.Client.Services;

public class PersistenceState
{
    public string FunctionsBaseUrl { get; set; } = string.Empty;
    public List<RegistroContable> Resultados { get; set; } = new();
    public List<HallazgoVerificacion> Hallazgos { get; set; } = new();
    public List<DocumentoVerificado> DocumentosVerificados { get; set; } = new();
    public List<ReciprocidadResultado> Reciprocidades { get; set; } = new();
    public Dictionary<string, string> EstadosHallazgos { get; set; } = new();
    public HashSet<string> ArchivosProcesados { get; set; } = new();
    public List<string> ArchivosSeleccionados { get; set; } = new();
    public List<FileTreeItem> TreeItems { get; set; } = new();
    public List<Movimiento> MovimientosMotor1 { get; set; } = new();
    public List<LogProceso> Logs { get; set; } = new();
    public bool SesionFinalizada { get; set; }
}