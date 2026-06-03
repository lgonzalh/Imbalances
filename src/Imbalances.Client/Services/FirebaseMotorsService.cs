using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Imbalances.Core.Models;

namespace Imbalances.Client.Services;

public class FirebaseMotorsService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FirebaseMotorsService(HttpClient http)
    {
        _http = http;
    }

    public async Task<GuardarMovimientosResponse> GuardarMovimientosAsync(
        string baseUrl,
        string empresa,
        string periodo,
        IReadOnlyList<Movimiento> movimientos,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "guardarMovimientos");

        var payload = new GuardarMovimientosRequest
        {
            Empresa = empresa,
            Periodo = periodo,
            Movimientos = movimientos.Select(m => new MovimientoDto
            {
                EmpresaContraparte = m.EmpresaContraparte,
                Tipo = m.Tipo,
                Cuenta = m.Cuenta,
                Valor = m.Valor,
                Nota = string.IsNullOrWhiteSpace(m.Nota) ? null : m.Nota,
            }).ToList()
        };

        var resp = await _http.PostAsJsonAsync(url, payload, cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        return (await resp.Content.ReadFromJsonAsync<GuardarMovimientosResponse>(cancellationToken: cancellationToken))
               ?? new GuardarMovimientosResponse();
    }

    public async Task<CruzarResponse> CruzarAsync(
        string baseUrl,
        string periodo,
        decimal tolerancia,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "cruzar");
        var payload = new CruzarRequest { Periodo = periodo, Tolerancia = tolerancia };

        var resp = await _http.PostAsJsonAsync(url, payload, cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        return (await resp.Content.ReadFromJsonAsync<CruzarResponse>(cancellationToken: cancellationToken))
               ?? new CruzarResponse();
    }

    public async Task GuardarConfiguracionAsync(
        string baseUrl,
        ConfiguracionCore configuracion,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "guardarConfiguracion");
        var resp = await _http.PostAsJsonAsync(url, configuracion, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
    }

    public async Task<ConfiguracionCore?> ObtenerConfiguracionAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "obtenerConfiguracion");
        var resp = await _http.GetAsync(url, cancellationToken);
        if (!resp.IsSuccessStatusCode) return null;

        return await resp.Content.ReadFromJsonAsync<ConfiguracionCore>(JsonOptions, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
    }

    private static string BuildUrl(string baseUrl, string functionName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"/{functionName}";
        }

        return $"{baseUrl.TrimEnd('/')}/{functionName}";
    }

    private sealed class GuardarMovimientosRequest
    {
        [JsonPropertyName("empresa")]
        public string Empresa { get; set; } = string.Empty;

        [JsonPropertyName("periodo")]
        public string Periodo { get; set; } = string.Empty;

        [JsonPropertyName("movimientos")]
        public List<MovimientoDto> Movimientos { get; set; } = new();
    }

    private sealed class MovimientoDto
    {
        [JsonPropertyName("empresa_contraparte")]
        public string EmpresaContraparte { get; set; } = string.Empty;

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; } = string.Empty;

        [JsonPropertyName("cuenta")]
        public string Cuenta { get; set; } = string.Empty;

        [JsonPropertyName("valor")]
        public decimal Valor { get; set; }

        [JsonPropertyName("nota")]
        public string? Nota { get; set; }
    }

    public class GuardarMovimientosResponse
    {
        [JsonPropertyName("insertados")]
        public int Insertados { get; set; }

        [JsonPropertyName("duplicados")]
        public int Duplicados { get; set; }

        [JsonPropertyName("errores")]
        public int Errores { get; set; }
    }

    private sealed class CruzarRequest
    {
        [JsonPropertyName("periodo")]
        public string Periodo { get; set; } = string.Empty;

        [JsonPropertyName("tolerancia")]
        public decimal Tolerancia { get; set; }
    }

    public class CruzarResponse
    {
        [JsonPropertyName("periodo")]
        public string Periodo { get; set; } = string.Empty;

        [JsonPropertyName("tolerancia")]
        public decimal Tolerancia { get; set; }

        [JsonPropertyName("movimientos_leidos")]
        public int MovimientosLeidos { get; set; }

        [JsonPropertyName("movimientos_invalidos")]
        public int MovimientosInvalidos { get; set; }

        [JsonPropertyName("pares_evaluados")]
        public int ParesEvaluados { get; set; }

        [JsonPropertyName("resultados_guardados")]
        public int ResultadosGuardados { get; set; }

        [JsonPropertyName("omitidos_ambos_cero")]
        public int OmitidosAmbosCero { get; set; }

        [JsonPropertyName("ok")]
        public int Ok { get; set; }

        [JsonPropertyName("descuadre")]
        public int Descuadre { get; set; }
    }
}
