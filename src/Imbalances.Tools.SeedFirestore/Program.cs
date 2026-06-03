using System.Globalization;
using System.Text;
using Google.Cloud.Firestore;

var argsMap = ParseArgs(args);

var projectId = GetRequired(argsMap, "--projectId");
var credentialsPath = GetOptional(argsMap, "--credentials");
var dryRun = GetOptional(argsMap, "--dryRun") is not null;

var companies = new List<string>
{
    "AGUILAS DE LA U",
    "AVANZAR",
    "CD SPORTS & FOOD",
    "CECLISA",
    "CEIP",
    "CEVAXIN",
    "CO2CERO",
    "COMPAÑIA GANADERA DEL OESTE",
    "ELON ESTATES",
    "ENDPOINTS",
    "EPIDRIVER",
    "EUREKA (COL)",
    "EUREKA (PAN)",
    "FAIR PLAY (COL)",
    "FAIR PLAY (PAN)",
    "FUNDACION EPIDRIVER",
    "GABMAR",
    "GAMALAB",
    "INTEGRA IT (COL)",
    "INTEGRA IT (PAN)",
    "IPIC",
    "MEDCARE",
    "POLICLINICO (COL)",
    "POLICLINICO (PAN)",
    "SAN FRANCISCO FC",
    "SASA",
};

var normalizedToOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var name in companies)
{
    var normalized = NormalizeEmpresaId(name);
    if (!normalizedToOriginal.TryAdd(normalized, name))
    {
        Console.Error.WriteLine($"Colisión de normalización: '{normalized}' ya existe para '{normalizedToOriginal[normalized]}' y también para '{name}'.");
        return 2;
    }
}

if (credentialsPath is not null)
{
    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
}

var db = FirestoreDb.Create(projectId);

var empresasCollection = db.Collection("empresas");

Console.WriteLine($"Proyecto: {projectId}");
Console.WriteLine($"Dry-run: {dryRun}");
Console.WriteLine($"Empresas a procesar: {companies.Count}");

var created = 0;
var skipped = 0;

foreach (var originalName in companies)
{
    var empresaId = NormalizeEmpresaId(originalName);
    var docRef = empresasCollection.Document(empresaId);
    var data = new Dictionary<string, object>
    {
        ["nombre"] = originalName.Trim(),
        ["nombre_normalizado"] = empresaId,
        ["fecha_creacion"] = FieldValue.ServerTimestamp,
    };

    if (dryRun)
    {
        Console.WriteLine($"[DRY] Crear empresas/{empresaId} (nombre='{originalName.Trim()}')");
        continue;
    }

    try
    {
        await docRef.CreateAsync(data);
        created++;
        Console.WriteLine($"[OK] Creado empresas/{empresaId}");
    }
    catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
    {
        skipped++;
        Console.WriteLine($"[SKIP] Ya existe empresas/{empresaId}");
    }
}

Console.WriteLine($"Resultado: creados={created}, existentes={skipped}");
return 0;

static Dictionary<string, string?> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (!key.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var value = (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            ? args[++i]
            : null;

        map[key] = value;
    }

    return map;
}

static string GetRequired(Dictionary<string, string?> argsMap, string key)
{
    if (!argsMap.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
    {
        Console.Error.WriteLine($"Falta argumento requerido: {key}");
        Console.Error.WriteLine("Uso: dotnet run --project src/Imbalances.Tools.SeedFirestore -- --projectId <id> [--credentials <path.json>] [--dryRun]");
        Environment.Exit(2);
    }

    return value!;
}

static string? GetOptional(Dictionary<string, string?> argsMap, string key)
    => argsMap.TryGetValue(key, out var value) ? value : null;

static string NormalizeEmpresaId(string input)
{
    var upper = input.Trim().ToUpperInvariant();

    var decomposed = upper.Normalize(NormalizationForm.FormD);
    var sb = new StringBuilder(decomposed.Length);
    foreach (var ch in decomposed)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (category == UnicodeCategory.NonSpacingMark)
        {
            continue;
        }

        if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
        {
            sb.Append(ch);
        }
    }

    var cleaned = sb.ToString().Normalize(NormalizationForm.FormC);
    var collapsedSpaces = CollapseSpaces(cleaned);
    return collapsedSpaces;
}

static string CollapseSpaces(string input)
{
    var sb = new StringBuilder(input.Length);
    var prevIsSpace = false;
    foreach (var ch in input)
    {
        var isSpace = char.IsWhiteSpace(ch);
        if (isSpace)
        {
            if (!prevIsSpace)
            {
                sb.Append(' ');
            }
            prevIsSpace = true;
            continue;
        }

        prevIsSpace = false;
        sb.Append(ch);
    }

    return sb.ToString().Trim();
}
