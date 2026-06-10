using System;
using System.Collections.Generic;
using System.Linq;

namespace Imbalances.Core.Models;

public class NoteReuseStats
{
    public Dictionary<string, NoteStat> Notas { get; set; } = new(StringComparer.Ordinal);
    public int NotasUnicas => Notas.Count;
    public int NotasReutilizadas => Notas.Values.Count(n => n.ProcesamientosAntes > 1);
    public int TotalLecturas => Notas.Values.Sum(n => n.VecesLeida);
    public int TotalProcesamientos => Notas.Values.Sum(n => n.VecesProcesada);
    public int TotalHomologaciones => Notas.Values.Sum(n => n.VecesHomologada);
    public long TiempoAhorradoMs { get; set; }
    public double GananciaPorcentual => TiempoAhorradoMs > 0 ? Math.Round((double)TiempoAhorradoMs / (TiempoAhorradoMs + Notas.Values.Sum(n => n.TiempoProcesamientoMs)) * 100, 1) : 0;
}

public class NoteStat
{
    public string Nota { get; set; } = string.Empty;
    public int VecesLeida { get; set; }
    public int VecesProcesada { get; set; }
    public int VecesHomologada { get; set; }
    public long TiempoLecturaMs { get; set; }
    public long TiempoProcesamientoMs { get; set; }
    public int ProcesamientosAntes { get; set; }
}
