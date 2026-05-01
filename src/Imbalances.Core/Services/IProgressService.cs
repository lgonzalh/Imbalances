using System;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IProgressService
{
    void Inicializar(int totalSteps);
    void Avanzar(string etapa);
    void Log(string mensaje, string estado = "Info");
    ProgresoProceso ObtenerEstado();
    event Action OnChange;
}
