using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Imbalances.Core.Models;

namespace Imbalances.Core.Services;

public class AuditoriaService : IAuditoriaService
{
    private const string EstadoPendiente = "Pendiente";

    public AuditoriaResultado Auditar(IEnumerable<RegistroContable> registros, IEnumerable<string> archivosDisponibles, ConfiguracionCore config)
    {
        var reciprocidades = AuditarReciprocidad(registros, config);
        var documentos = ValidarDisponibilidadDocumental(archivosDisponibles, config);
        var hallazgos = new List<HallazgoVerificacion>();

        hallazgos.AddRange(GenerarHallazgosReciprocidad(reciprocidades, registros, config));
        
        return new AuditoriaResultado
        {
            FechaEjecucionUtc = DateTime.UtcNow,
            Reciprocidades = reciprocidades,
            Documentos = documentos,
            Hallazgos = hallazgos
                .OrderByDescending(h => PesoSeveridad(h.Severidad))
                .ToList()
        };
    }

    public List<ReciprocidadResultado> AuditarReciprocidad(IEnumerable<RegistroContable> registros, ConfiguracionCore config)
    {
        var tolerancia = 1m;
        
        var normalizados = registros
            .Where(r => r.Categoria == "CxC" || r.Categoria == "CxP")
            .Select(r => new
            {
                Registro = r,
                Empresa = NormalizarClave(r.Empresa),
                Contraparte = NormalizarClave(r.EmpresaContraparte),
                Grupo = NormalizarClave(r.Cuenta)
            })
            .ToList();

        var cxc = normalizados
            .Where(x => x.Registro.Categoria == "CxC" && !string.IsNullOrWhiteSpace(x.Empresa))
            .GroupBy(x => new LlaveReciprocidad(x.Empresa, x.Contraparte, x.Grupo))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Registro.Valor));

        var cxp = normalizados
            .Where(x => x.Registro.Categoria == "CxP" && !string.IsNullOrWhiteSpace(x.Empresa))
            .GroupBy(x => new LlaveReciprocidad(x.Contraparte, x.Empresa, x.Grupo))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Registro.Valor));

        return cxc.Keys
            .Union(cxp.Keys)
            .Select(k =>
            {
                var valorCxc = cxc.TryGetValue(k, out var activo) ? activo : 0m;
                var valorCxp = cxp.TryGetValue(k, out var pasivo) ? pasivo : 0m;
                var diferencia = valorCxc - valorCxp;

                return new ReciprocidadResultado
                {
                    Id = $"REC-{k.EmpresaCxc}-{k.EmpresaCxp}-{k.Grupo}".GetHashCode().ToString("X"),
                    EmpresaCxc = k.EmpresaCxc,
                    EmpresaCxp = k.EmpresaCxp,
                    GrupoReciprocidad = k.Grupo,
                    ValorCxc = valorCxc,
                    ValorCxp = valorCxp,
                    Diferencia = diferencia,
                    DentroTolerancia = Math.Abs(diferencia) <= tolerancia
                };
            })
            .OrderByDescending(r => Math.Abs(r.Diferencia))
            .ToList();
    }

    public List<DocumentoVerificado> ValidarDisponibilidadDocumental(IEnumerable<string> archivosDisponibles, ConfiguracionCore config)
    {
        var archivos = archivosDisponibles.ToList();
        var resultados = new List<DocumentoVerificado>();

        foreach (var empresa in config.Empresas.Where(e => !string.IsNullOrWhiteSpace(e.NombreCarpeta)))
        {
            var archivoEncontrado = archivos.FirstOrDefault(a => a.Contains(empresa.NombreCarpeta, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
            var encontrado = !string.IsNullOrWhiteSpace(archivoEncontrado);
            var patron = $"(?i).*{Regex.Escape(empresa.NombreCarpeta)}.*";
            var empresaLabel = !string.IsNullOrWhiteSpace(empresa.NombreEmpresa) ? empresa.NombreEmpresa : empresa.NombreCarpeta;

            resultados.Add(new DocumentoVerificado
            {
                Id = $"DOC-{empresaLabel}-{empresa.NombreCarpeta}".GetHashCode().ToString("X"),
                Empresa = empresaLabel,
                Reporte = $"Carpeta base: {empresa.NombreCarpeta}",
                ArchivoRegex = patron,
                CarpetaRegex = patron,
                Presente = encontrado,
                ArchivoEncontrado = archivoEncontrado,
                Detalle = encontrado
                    ? $"Se detectó una ruta que coincide con la carpeta: {archivoEncontrado}"
                    : $"No se detectó ninguna ruta que contenga: {empresa.NombreCarpeta}"
            });
        }

        return resultados;
    }

    private static IEnumerable<HallazgoVerificacion> GenerarHallazgosReciprocidad(
        IEnumerable<ReciprocidadResultado> reciprocidades,
        IEnumerable<RegistroContable> registros,
        ConfiguracionCore config)
    {
        foreach (var r in reciprocidades.Where(r => !r.DentroTolerancia))
        {
            yield return new HallazgoVerificacion
            {
                Tipo = "Reciprocidad",
                Severidad = Math.Abs(r.Diferencia) > 1000 ? "Critica" : "Advertencia",
                Estado = EstadoPendiente,
                Empresa = r.EmpresaCxc,
                Contraparte = r.EmpresaCxp,
                Cuenta = r.GrupoReciprocidad,
                ValorLocal = r.ValorCxc,
                ValorContraparte = r.ValorCxp,
                Diferencia = r.Diferencia,
                Detalle = $"Diferencia de {r.Diferencia:N2} entre CxC y CxP."
            };
        }
    }

    private static string NormalizarClave(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static int PesoSeveridad(string severidad) => severidad switch
    {
        "Critica" => 3,
        "Advertencia" => 2,
        _ => 1
    };

    private readonly record struct LlaveReciprocidad(string EmpresaCxc, string EmpresaCxp, string Grupo);
}
