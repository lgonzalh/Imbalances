using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Imbalances.Client.Services;

public class FirebaseAdminService
{
    private readonly HttpClient _http;

    public FirebaseAdminService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Obtiene la lista de colecciones disponibles en Firestore
    /// </summary>
    public async Task<List<string>> ObtenerColeccionesAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "listarColecciones");
        
        try
        {
            var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var error = await resp.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Error al obtener colecciones: {resp.StatusCode} - {error}");
            }

            var result = await resp.Content.ReadFromJsonAsync<ListarColeccionesResponse>(cancellationToken: cancellationToken);
            return result?.Colecciones ?? new List<string>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al conectar con Firebase: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Obtiene los documentos de una colección específica
    /// </summary>
    public async Task<List<DocumentoFirestore>> ObtenerDocumentosAsync(
        string baseUrl,
        string coleccion,
        int limite = 100,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, $"listarDocumentos?coleccion={Uri.EscapeDataString(coleccion)}&limite={limite}");
        
        try
        {
            var resp = await _http.GetAsync(url, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var error = await resp.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Error al obtener documentos: {resp.StatusCode} - {error}");
            }

            var result = await resp.Content.ReadFromJsonAsync<ListarDocumentosResponse>(cancellationToken: cancellationToken);
            return result?.Documentos ?? new List<DocumentoFirestore>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al obtener documentos: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Elimina documentos específicos de una colección
    /// </summary>
    public async Task<EliminarDocumentosResponse> EliminarDocumentosAsync(
        string baseUrl,
        string coleccion,
        List<string> documentIds,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "eliminarDocumentos");
        
        var payload = new EliminarDocumentosRequest
        {
            Coleccion = coleccion,
            DocumentIds = documentIds
        };

        var resp = await _http.PostAsJsonAsync(url, payload, cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        
        return (await resp.Content.ReadFromJsonAsync<EliminarDocumentosResponse>(cancellationToken: cancellationToken))
               ?? new EliminarDocumentosResponse();
    }

    /// <summary>
    /// Elimina todos los documentos de una colección
    /// </summary>
    public async Task<EliminarDocumentosResponse> LimpiarColeccionAsync(
        string baseUrl,
        string coleccion,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(baseUrl, "limpiarColeccion");
        
        var payload = new LimpiarColeccionRequest
        {
            Coleccion = coleccion
        };

        var resp = await _http.PostAsJsonAsync(url, payload, cancellationToken);
        await EnsureSuccessAsync(resp, cancellationToken);
        
        return (await resp.Content.ReadFromJsonAsync<EliminarDocumentosResponse>(cancellationToken: cancellationToken))
               ?? new EliminarDocumentosResponse();
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

    private static string BuildUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"/{path}";
        }

        return $"{baseUrl.TrimEnd('/')}/{path}";
    }

    // DTOs
    private sealed class ListarColeccionesResponse
    {
        [JsonPropertyName("colecciones")]
        public List<string> Colecciones { get; set; } = new();
    }

    private sealed class ListarDocumentosResponse
    {
        [JsonPropertyName("documentos")]
        public List<DocumentoFirestore> Documentos { get; set; } = new();
    }

    public class DocumentoFirestore
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public JsonElement Data { get; set; }

        [JsonPropertyName("preview")]
        public string Preview { get; set; } = string.Empty;
    }

    private sealed class EliminarDocumentosRequest
    {
        [JsonPropertyName("coleccion")]
        public string Coleccion { get; set; } = string.Empty;

        [JsonPropertyName("documentIds")]
        public List<string> DocumentIds { get; set; } = new();
    }

    private sealed class LimpiarColeccionRequest
    {
        [JsonPropertyName("coleccion")]
        public string Coleccion { get; set; } = string.Empty;
    }

    public class EliminarDocumentosResponse
    {
        [JsonPropertyName("eliminados")]
        public int Eliminados { get; set; }

        [JsonPropertyName("errores")]
        public int Errores { get; set; }
    }
}
