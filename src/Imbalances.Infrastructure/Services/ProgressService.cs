using System;
using Imbalances.Core.Models;
using Imbalances.Core.Services;

namespace Imbalances.Infrastructure.Services;

public class ProgressService : IProgressService
{
    private readonly ProgresoProceso _estado;

    public ProgressService()
    {
        _estado = new ProgresoProceso();
    }

    public event Action? OnChange;

    public void Inicializar(int totalSteps)
    {
        _estado.TotalSteps = totalSteps;
        _estado.CurrentStep = 0;
        _estado.EtapaActual = "Inicialización";
        _estado.Logs.Clear();
        Log("Proceso inicializado", "Info");
        NotificarEstadoCambiado();
    }

    public void Avanzar(string etapa)
    {
        _estado.CurrentStep++;
        _estado.EtapaActual = etapa;
        Log($"Avanzando a etapa: {etapa}", "Info");
        NotificarEstadoCambiado();
    }

    public void Log(string mensaje, string estado = "Info")
    {
        _estado.Logs.Add(new LogProceso
        {
            Mensaje = mensaje,
            Estado = estado,
            Timestamp = DateTime.Now
        });
        NotificarEstadoCambiado();
    }

    public ProgresoProceso ObtenerEstado()
    {
        return _estado;
    }

    private void NotificarEstadoCambiado() => OnChange?.Invoke();
}
