using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public class ExtractorEngine : IExtractorEngine
{
    private readonly IMotor1Extractor _motor1;

    public ExtractorEngine(IMotor1Extractor motor1)
    {
        _motor1 = motor1;
    }

    public async Task<List<RegistroContable>> ProcesarArchivoAsync(string filePath, Stream fileStream, ConfiguracionCore config, Action<string>? onProgressLog = null, bool diagnosticMode = true)
    {
        var nombreArchivo = Path.GetFileName(filePath);
        var movimientos = await ProcesarArchivoMotor1Async(filePath, fileStream, config, periodo: string.Empty, onProgressLog, diagnosticMode);

        var empresaLookup = BuildEmpresaLookup(config);
        var resultados = movimientos.Select(m => MapToRegistroContable(m, nombreArchivo, empresaLookup)).ToList();

        return resultados;

    }

    public async Task<List<Movimiento>> ProcesarArchivoMotor1Async(string filePath, Stream fileStream, ConfiguracionCore config, string periodo = "", Action<string>? onProgressLog = null, bool diagnosticMode = true, Action<PipelineProfile>? onProfile = null)
    {
        return await _motor1.ExtraerAsync(filePath, fileStream, config, periodo, onProgressLog, diagnosticMode, onProfile);
    }

    public async Task<(List<RegistroContable> Resultados, PipelineProfileSummary Perfil)> ProcesarMultiplesArchivosAsync(
        List<(string FilePath, Stream FileStream)> archivos,
        ConfiguracionCore config,
        Action<string>? onProgressLog = null,
        bool diagnosticMode = true,
        int maxParallelism = 4)
    {
        var swTotal = Stopwatch.StartNew();
        var resultados = new ConcurrentBag<RegistroContable>();
        var perfiles = new ConcurrentBag<PipelineProfile>();
        var empresaLookup = BuildEmpresaLookup(config);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism
        };

        var archivosList = archivos.Select((a, i) => (a.FilePath, a.FileStream, Index: i)).ToList();
        var syncLock = new object();

        await Parallel.ForEachAsync(archivosList, options, async (archivo, ct) =>
        {
            var (filePath, fileStream, idx) = archivo;
            var nombreArchivo = Path.GetFileName(filePath);

            try
            {
                var movimientos = await _motor1.ExtraerAsync(
                    filePath, fileStream, config,
                    periodo: string.Empty,
                    onProgressLog: msg =>
                    {
                        lock (syncLock)
                        {
                            onProgressLog?.Invoke($"[{idx + 1}/{archivos.Count}] {msg}");
                        }
                    },
                    diagnosticMode,
                    onProfile: profile =>
                    {
                        perfiles.Add(profile);
                    });

                foreach (var m in movimientos)
                {
                    var rc = MapToRegistroContable(m, nombreArchivo, empresaLookup);
                    resultados.Add(rc);
                }
            }
            catch (Exception ex)
            {
                lock (syncLock)
                {
                    onProgressLog?.Invoke($"[Error] {nombreArchivo}: {ex.Message}");
                }
            }
        });

        swTotal.Stop();

        var summary = new PipelineProfileSummary
        {
            Archivos = perfiles.OrderBy(p => p.Archivo).ToList()
        };

        onProgressLog?.Invoke($"[Perfil] Total {archivos.Count} archivos: {swTotal.ElapsedMilliseconds}ms (paralelismo={maxParallelism})");

        return (resultados.ToList(), summary);
    }

    public IEnumerable<object> Consolidar(List<RegistroContable> resultados)
    {
        return resultados.GroupBy(x => new { x.Empresa, x.Cuenta, x.Tipo, x.Nota })
            .Select(g => new { g.Key.Empresa, g.Key.Cuenta, g.Key.Tipo, g.Key.Nota, Valor = g.Sum(x => x.Valor) });
    }

    public IEnumerable<object> Conciliar(List<RegistroContable> resultados)
    {
        return resultados.GroupBy(x => new { x.Empresa, x.Tipo })
            .Select(g => new { g.Key.Empresa, g.Key.Tipo, Total = g.Sum(x => x.Valor) });
    }

    private static Dictionary<string, EmpresaConfig> BuildEmpresaLookup(ConfiguracionCore config)
    {
        var lookup = new Dictionary<string, EmpresaConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in config.Empresas)
        {
            if (!string.IsNullOrWhiteSpace(e.NombreEmpresa) && !lookup.ContainsKey(e.NombreEmpresa))
                lookup[e.NombreEmpresa] = e;
            if (!string.IsNullOrWhiteSpace(e.NombreCarpeta) && !lookup.ContainsKey(e.NombreCarpeta))
                lookup[e.NombreCarpeta] = e;
            if (!string.IsNullOrWhiteSpace(e.Alias) && !lookup.ContainsKey(e.Alias))
                lookup[e.Alias] = e;
        }
        return lookup;
    }

    private static RegistroContable MapToRegistroContable(Movimiento m, string nombreArchivo, Dictionary<string, EmpresaConfig> empresaLookup)
    {
        return new RegistroContable
        {
            Empresa = m.EmpresaOrigen,
            EmpresaContraparte = m.EmpresaContraparte,
            Cuenta = m.Cuenta,
            Categoria = m.Tipo,
            Tipo = m.Tipo,
            Nota = m.Nota,
            Valor = m.Valor,
            ArchivoOrigen = nombreArchivo,
            HojaOrigen = string.IsNullOrWhiteSpace(m.Nota) ? string.Empty : $"Nota {m.Nota}",
            TextoOrigen = m.EmpresaContraparte,
            CompanyCode = empresaLookup.TryGetValue(m.EmpresaOrigen, out var src) ? src.CompanyCode : string.Empty,
            TradePartnerCode = empresaLookup.TryGetValue(m.EmpresaContraparte, out var dst) ? dst.CompanyCode : string.Empty,
            ConcOp = empresaLookup.TryGetValue(m.EmpresaContraparte, out var cp) ? cp.ConcOp : string.Empty,
            Periodo = m.Periodo
        };
    }
}
