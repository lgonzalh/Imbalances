using System.Collections.Generic;

namespace Imbalances.Core.Models;

public class EquivalenciaTercero
{
    public string Alias { get; set; } = string.Empty;
    public List<string> Equivalentes { get; set; } = new();
}
