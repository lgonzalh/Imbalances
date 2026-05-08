using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Imbalances.Client.UI.Core;

public sealed class UiConfigService
{
    private readonly HttpClient _http;
    private UiConfigRoot? _cache;

    public UiConfigService(HttpClient http)
    {
        _http = http;
    }

    public async Task<UiConfigRoot> GetAsync()
    {
        if (_cache != null) return _cache;

        try
        {
            var cfg = await _http.GetFromJsonAsync<UiConfigRoot>("/UI/Config/ui-config.json");
            _cache = cfg ?? CreateDefault();
        }
        catch
        {
            _cache = CreateDefault();
        }

        return _cache;
    }

    public async Task<UiPageConfig> GetPageAsync(string pageKey)
    {
        var cfg = await GetAsync();
        if (cfg.Pages.TryGetValue(pageKey, out var page)) return page;
        return new UiPageConfig();
    }

    private static UiConfigRoot CreateDefault()
    {
        return new UiConfigRoot
        {
            Layout = new UiLayoutConfig { ToolbarPosition = "top", ToolbarSticky = false, ToolbarBottomOffset = 16 },
            Grid = new UiGridConfig { ScrollHorizontal = true, ResizableColumns = true, MinColumnWidth = 120 },
            Card = new UiCardConfig { Collapsible = true, DefaultExpanded = true },
            Toolbar = new UiToolbarConfig { ShowAdd = true, ShowDelete = true, ShowSave = true, ShowImport = true, ShowExport = true, ShowLog = true }
        };
    }
}
