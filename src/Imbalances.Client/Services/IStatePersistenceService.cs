﻿﻿﻿﻿﻿﻿﻿using System.Threading.Tasks;

namespace Imbalances.Client.Services;

public interface IStatePersistenceService
{
    Task GuardarEstadoAsync(PersistenceState state);
    Task<PersistenceState?> CargarEstadoAsync();
    Task LimpiarEstadoAsync();
}
