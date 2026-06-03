using System;
using System.Collections.Generic;

namespace Imbalances.Client.UI.Core;

public sealed class UiSnapshot
{
    public string Page { get; set; } = string.Empty;
    public List<string> Components { get; set; } = new();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public UiConfigRoot? UiConfig { get; set; }
}

