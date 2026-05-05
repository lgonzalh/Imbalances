using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Imbalances.Client.UI.Core;

public sealed class UiSnapshotService
{
    private readonly IJSRuntime _js;

    public UiSnapshotService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task SaveSnapshotAsync(UiSnapshot snapshot)
    {
        var key = GetKey(snapshot.Page);
        try
        {
            await _js.InvokeVoidAsync("imbalancesPersistence.saveItem", key, snapshot);
        }
        catch
        {
        }
    }

    public async Task<UiSnapshot?> LoadSnapshotAsync(string page)
    {
        var key = GetKey(page);
        try
        {
            return await _js.InvokeAsync<UiSnapshot?>("imbalancesPersistence.loadItem", key);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> ExportSnapshotJsonAsync(string page)
    {
        var snap = await LoadSnapshotAsync(page);
        return JsonSerializer.Serialize(snap ?? new UiSnapshot { Page = page }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string GetKey(string page) => $"ui:snapshot:{page}";
}

