using System.Collections.Generic;
using Microsoft.AspNetCore.Components.Forms;
using Imbalances.Client.Models;
using Imbalances.Core.Models;

namespace Imbalances.Client.Services;

public class ExplorerStateService
{
    public string FunctionsBaseUrl { get; set; } = string.Empty;
    public List<IBrowserFile> BrowserFiles { get; set; } = new();
    public List<FileTreeItem> TreeItems { get; set; } = new();
    public List<RegistroContable> Resultados { get; set; } = new();
    public List<Movimiento> MovimientosMotor1 { get; set; } = new();
    public List<HallazgoVerificacion> Hallazgos { get; set; } = new();
    public List<DocumentoVerificado> DocumentosVerificados { get; set; } = new();
    public List<ReciprocidadResultado> Reciprocidades { get; set; } = new();
    public Dictionary<string, string> EstadosHallazgos { get; set; } = new();
    public HashSet<string> ArchivosProcesados { get; set; } = new();
    public List<string> ArchivosSeleccionados { get; set; } = new();
    public bool HaySesionPendiente { get; set; } = false;
    public bool PasswordUnlocked { get; set; } = false;
    private bool _isProcessing;

    public bool IsProcessing
    {
        get => _isProcessing;
        set
        {
            if (_isProcessing == value) return;
            _isProcessing = value;
            OnIsProcessingChanged?.Invoke();
        }
    }

    public event Action? OnIsProcessingChanged;
    public event Action<InputFileChangeEventArgs>? OnInputFileChange;
    public void NotifyInputFileChanged(InputFileChangeEventArgs e) => OnInputFileChange?.Invoke(e);
    
    public void Clear()
    {
        FunctionsBaseUrl = string.Empty;
        BrowserFiles.Clear();
        TreeItems.Clear();
        Resultados.Clear();
        MovimientosMotor1.Clear();
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
