﻿﻿﻿﻿﻿﻿﻿using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Imbalances.Client.Services;

public class StatePersistenceService : IStatePersistenceService
{
    private readonly IJSRuntime _jsRuntime;

    public StatePersistenceService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task GuardarEstadoAsync(PersistenceState state)
    {
        await _jsRuntime.InvokeVoidAsync("imbalancesPersistence.saveState", state);
    }

    public async Task<PersistenceState?> CargarEstadoAsync()
    {
        return await _jsRuntime.InvokeAsync<PersistenceState?>("imbalancesPersistence.loadState");
    }

    public async Task LimpiarEstadoAsync()
    {
        await _jsRuntime.InvokeVoidAsync("imbalancesPersistence.clearState");
    }
}
