using System.Collections.Generic;
using Microsoft.AspNetCore.Components.Forms;
using Imbalances.Core.Models;

namespace Imbalances.Client.Services;

public class ExplorerStateService
{
    public List<IBrowserFile> BrowserFiles { get; set; } = new();
    public List<Pages.Home.TreeItemData> TreeItems { get; set; } = new();
    public List<RegistroContable> Resultados { get; set; } = new();
    public List<HallazgoVerificacion> Hallazgos { get; set; } = new();
    public List<DocumentoVerificado> DocumentosVerificados { get; set; } = new();
    public List<ReciprocidadResultado> Reciprocidades { get; set; } = new();
    public Dictionary<string, string> EstadosHallazgos { get; set; } = new();
    public HashSet<string> ArchivosProcesados { get; set; } = new();
    public List<string> ArchivosSeleccionados { get; set; } = new();
    public bool HaySesionPendiente { get; set; } = false;
    public bool IsProcessing { get; set; } = false;

    public event Action<InputFileChangeEventArgs>? OnInputFileChange;
    public void NotifyInputFileChanged(InputFileChangeEventArgs e) => OnInputFileChange?.Invoke(e);
    
    public void Clear()
    {
        BrowserFiles.Clear();
        TreeItems.Clear();
        Resultados.Clear();
        Hallazgos.Clear();
        DocumentosVerificados.Clear();
        Reciprocidades.Clear();
        EstadosHallazgos.Clear();
        ArchivosProcesados.Clear();
        ArchivosSeleccionados.Clear();
        HaySesionPendiente = false;
        IsProcessing = false;
    }
}
