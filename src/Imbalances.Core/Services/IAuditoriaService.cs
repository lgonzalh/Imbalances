using System.Collections.Generic;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public interface IAuditoriaService
{
    AuditoriaResultado Auditar(IEnumerable<RegistroContable> registros, IEnumerable<string> archivosDisponibles, ConfiguracionCore config);
    List<DocumentoVerificado> ValidarDisponibilidadDocumental(IEnumerable<string> archivosDisponibles, ConfiguracionCore config);
    List<ReciprocidadResultado> AuditarReciprocidad(IEnumerable<RegistroContable> registros, ConfiguracionCore config);
}
