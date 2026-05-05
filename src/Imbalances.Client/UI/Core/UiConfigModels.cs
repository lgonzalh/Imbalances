using System.Collections.Generic;

namespace Imbalances.Client.UI.Core;

public sealed class UiConfigRoot
{
    public UiLayoutConfig Layout { get; set; } = new();
    public UiGridConfig Grid { get; set; } = new();
    public UiCardConfig Card { get; set; } = new();
    public UiToolbarConfig Toolbar { get; set; } = new();
    public Dictionary<string, UiPageConfig> Pages { get; set; } = new();
}

public sealed class UiPageConfig
{
    public UiLayoutConfig? Layout { get; set; }
    public UiGridConfig? Grid { get; set; }
    public UiCardConfig? Card { get; set; }
    public UiToolbarConfig? Toolbar { get; set; }
}

public sealed class UiLayoutConfig
{
    public string ToolbarPosition { get; set; } = "bottom";
    public bool ToolbarSticky { get; set; } = true;
    public int ToolbarBottomOffset { get; set; } = 16;
}

public sealed class UiGridConfig
{
    public bool ScrollHorizontal { get; set; } = true;
    public bool ResizableColumns { get; set; } = true;
    public int MinColumnWidth { get; set; } = 120;
}

public sealed class UiCardConfig
{
    public bool Collapsible { get; set; } = true;
    public bool DefaultExpanded { get; set; } = true;
}

public sealed class UiToolbarConfig
{
    public bool ShowAdd { get; set; } = true;
    public bool ShowDelete { get; set; } = true;
    public bool ShowSave { get; set; } = true;
    public bool ShowImport { get; set; } = true;
    public bool ShowExport { get; set; } = true;
    public bool ShowLog { get; set; } = true;
    public List<UiToolbarAction> Actions { get; set; } = new();
}

public sealed class UiToolbarAction
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Variant { get; set; } = "secondary";
    public bool FilePicker { get; set; }
    public string? Accept { get; set; }
    public bool Multiple { get; set; }
    public bool Directory { get; set; }
}
