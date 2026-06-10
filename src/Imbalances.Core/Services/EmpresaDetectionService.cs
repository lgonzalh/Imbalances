using System.Text;
using System.Text.RegularExpressions;
using Imbalances.Core.Models;
using Imbalances.Core.Utils;

namespace Imbalances.Core.Services;

public class EmpresaDetectionService : IEmpresaDetectionService
{
    public EmpresaConfig? DetectarEmpresa(string filePath, ConfiguracionCore config, Action<string>? onProgressLog = null)
    {
        var nombreArchivo = Path.GetFileName(filePath);
        onProgressLog?.Invoke($"[Detection] Archivo original: {nombreArchivo}");

        var archivoSinExtension = Path.GetFileNameWithoutExtension(filePath);
        var archivoNorm = NormalizeForComparison(archivoSinExtension);
        onProgressLog?.Invoke($"[Detection] Archivo normalizado: '{archivoNorm}'");

        var directorio = Path.GetDirectoryName(filePath);
        var directorioNorm = NormalizeForComparison(directorio);
        onProgressLog?.Invoke($"[Detection] Directorio normalizado: '{directorioNorm}'");

        // Levels 1-3: exact match and bidirectional contains
        foreach (var empresa in config.Empresas)
        {
            var nombre = empresa.NombreEmpresa ?? empresa.NombreCarpeta ?? string.Empty;
            var carpetaNorm = NormalizeForComparison(empresa.NombreCarpeta);
            var empresaNorm = NormalizeForComparison(empresa.NombreEmpresa);
            var aliasNorm = NormalizeForComparison(empresa.Alias);

            // Level 1: Exact match by NombreCarpeta
            if (!string.IsNullOrWhiteSpace(carpetaNorm))
            {
                if (archivoNorm == carpetaNorm || directorioNorm == carpetaNorm)
                {
                    onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs '{nombre}' | Metodo: ExactoCarpeta | Resultado: MATCH");
                    return empresa;
                }
            }

            // Level 2: Exact match by NombreEmpresa
            if (!string.IsNullOrWhiteSpace(empresaNorm))
            {
                if (archivoNorm == empresaNorm || directorioNorm == empresaNorm)
                {
                    onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs '{nombre}' | Metodo: ExactoNombre | Resultado: MATCH");
                    return empresa;
                }
            }

            // Level 3: Bidirectional Contains
            if (!string.IsNullOrWhiteSpace(carpetaNorm))
            {
                if (archivoNorm.Contains(carpetaNorm) || directorioNorm.Contains(carpetaNorm) ||
                    carpetaNorm.Contains(archivoNorm) || carpetaNorm.Contains(directorioNorm))
                {
                    onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs '{nombre}' | Metodo: ContainsCarpeta | Resultado: MATCH");
                    return empresa;
                }
            }

            if (!string.IsNullOrWhiteSpace(empresaNorm))
            {
                if (archivoNorm.Contains(empresaNorm) || directorioNorm.Contains(empresaNorm) ||
                    empresaNorm.Contains(archivoNorm) || empresaNorm.Contains(directorioNorm))
                {
                    onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs '{nombre}' | Metodo: ContainsNombre | Resultado: MATCH");
                    return empresa;
                }
            }

            if (!string.IsNullOrWhiteSpace(aliasNorm))
            {
                if (archivoNorm.Contains(aliasNorm) || directorioNorm.Contains(aliasNorm) ||
                    aliasNorm.Contains(archivoNorm) || aliasNorm.Contains(directorioNorm))
                {
                    onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs '{nombre}' | Metodo: ContainsAlias | Resultado: MATCH");
                    return empresa;
                }
            }

            foreach (var alias in config.AliasEmpresa ?? [])
            {
                var globalAliasNorm = NormalizeForComparison(alias.Alias);
                if (!string.IsNullOrWhiteSpace(globalAliasNorm))
                {
                    if (archivoNorm.Contains(globalAliasNorm) || directorioNorm.Contains(globalAliasNorm) ||
                        globalAliasNorm.Contains(archivoNorm) || globalAliasNorm.Contains(directorioNorm))
                    {
                        var targetEmpresa = config.Empresas.FirstOrDefault(e =>
                            (e.NombreEmpresa ?? e.NombreCarpeta ?? string.Empty) == alias.NombreEmpresaDestino);
                        if (targetEmpresa != null)
                        {
                            onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs {alias.NombreEmpresaDestino} | Metodo: ContainsAliasGlobal | Resultado: MATCH");
                            return targetEmpresa;
                        }
                    }
                }
            }
        }

        // Level 4: Fuzzy matching across all empresas
        var fuzzyCandidates = new List<(EmpresaConfig Empresa, double Score, string TextoComparado)>();
        foreach (var empresa in config.Empresas)
        {
            var nombre = empresa.NombreEmpresa ?? empresa.NombreCarpeta ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nombre)) continue;
            var nombreNorm = NormalizeForComparison(nombre);

            var scoreArchivo = LevenshteinSimilarity(archivoNorm, nombreNorm);
            var scoreDir = LevenshteinSimilarity(directorioNorm, nombreNorm);
            var bestScore = Math.Max(scoreArchivo, scoreDir);

            onProgressLog?.Invoke($"[Detection]   Fuzzy: '{archivoNorm}' vs '{nombreNorm}' = {bestScore:P1}");

            if (bestScore >= 0.85)
            {
                fuzzyCandidates.Add((empresa, bestScore, nombre));
            }
            else if (bestScore >= 0.70)
            {
                onProgressLog?.Invoke($"[Detection]   Warning: posible coincidencia con '{nombre}' score {bestScore:P1} (70-84.99%), no se acepta automaticamente");
            }
        }

        if (fuzzyCandidates.Count > 0)
        {
            var best = fuzzyCandidates.OrderByDescending(f => f.Score).First();
            onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' vs '{best.TextoComparado}' | Score: {best.Score:P1} | Metodo: Fuzzy | Resultado: MATCH");
            return best.Empresa;
        }

        onProgressLog?.Invoke($"[Detection] '{nombreArchivo}' | Resultado: NO MATCH");
        return null;
    }

    public EmpresaConfig? DetectarEmpresaEnArchivos(List<string> archivos, EmpresaConfig empresa, Action<string>? onProgressLog = null)
    {
        foreach (var archivo in archivos)
        {
            var result = DetectarEmpresa(archivo, new ConfiguracionCore
            {
                Empresas = [empresa]
            }, onProgressLog);
            if (result != null)
                return result;
        }
        return null;
    }

    public static string NormalizeForComparison(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var result = texto.ToUpperInvariant();
        result = FoldLatinCharacters(result);
        result = result.Replace('_', ' ');
        result = result.Replace('-', ' ');

        var sb = new StringBuilder();
        foreach (var ch in result)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
                sb.Append(ch);
        }

        var final = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return final;
    }

    private static string FoldLatinCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            AppendFolded(builder, ch);
        }
        return builder.ToString();
    }

    private static double LevenshteinSimilarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0.0;

        var lenA = a.Length;
        var lenB = b.Length;

        var matrix = new int[lenA + 1, lenB + 1];

        for (var i = 0; i <= lenA; i++) matrix[i, 0] = i;
        for (var j = 0; j <= lenB; j++) matrix[0, j] = j;

        for (var i = 1; i <= lenA; i++)
        {
            for (var j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        var distance = matrix[lenA, lenB];
        var maxLen = Math.Max(lenA, lenB);
        return maxLen == 0 ? 1.0 : 1.0 - (double)distance / maxLen;
    }

    private static void AppendFolded(StringBuilder builder, char ch)
    {
        switch (ch)
        {
            case 'Á': case 'À': case 'Â': case 'Ã': case 'Ä': case 'Å': case 'Ā': case 'Ă': case 'Ą': case 'Ǎ':
            case 'á': case 'à': case 'â': case 'ã': case 'ä': case 'å': case 'ā': case 'ă': case 'ą': case 'ǎ':
                builder.Append('A'); break;
            case 'Æ': case 'Ǽ': case 'æ': case 'ǽ':
                builder.Append("AE"); break;
            case 'Ç': case 'Ć': case 'Ĉ': case 'Ċ': case 'Č':
            case 'ç': case 'ć': case 'ĉ': case 'ċ': case 'č':
                builder.Append('C'); break;
            case 'Ð': case 'Ď': case 'Đ': case 'ð': case 'ď': case 'đ':
                builder.Append('D'); break;
            case 'É': case 'È': case 'Ê': case 'Ë': case 'Ē': case 'Ĕ': case 'Ė': case 'Ę': case 'Ě':
            case 'é': case 'è': case 'ê': case 'ë': case 'ē': case 'ĕ': case 'ė': case 'ę': case 'ě':
                builder.Append('E'); break;
            case 'Ĝ': case 'Ğ': case 'Ġ': case 'Ģ':
            case 'ĝ': case 'ğ': case 'ġ': case 'ģ':
                builder.Append('G'); break;
            case 'Ĥ': case 'Ħ': case 'ĥ': case 'ħ':
                builder.Append('H'); break;
            case 'Í': case 'Ì': case 'Î': case 'Ï': case 'Ĩ': case 'Ī': case 'Ĭ': case 'Į': case 'İ': case 'Ǐ':
            case 'í': case 'ì': case 'î': case 'ï': case 'ĩ': case 'ī': case 'ĭ': case 'į': case 'ı': case 'ǐ':
                builder.Append('I'); break;
            case 'Ĵ': case 'ĵ':
                builder.Append('J'); break;
            case 'Ķ': case 'ķ':
                builder.Append('K'); break;
            case 'Ĺ': case 'Ļ': case 'Ľ': case 'Ŀ': case 'Ł':
            case 'ĺ': case 'ļ': case 'ľ': case 'ŀ': case 'ł':
                builder.Append('L'); break;
            case 'Ñ': case 'Ń': case 'Ņ': case 'Ň':
            case 'ñ': case 'ń': case 'ņ': case 'ň':
                builder.Append('N'); break;
            case 'Ó': case 'Ò': case 'Ô': case 'Õ': case 'Ö': case 'Ø': case 'Ō': case 'Ŏ': case 'Ő': case 'Ǒ':
            case 'ó': case 'ò': case 'ô': case 'õ': case 'ö': case 'ø': case 'ō': case 'ŏ': case 'ő': case 'ǒ':
                builder.Append('O'); break;
            case 'Œ': case 'œ':
                builder.Append("OE"); break;
            case 'Ŕ': case 'Ŗ': case 'Ř': case 'ŕ': case 'ŗ': case 'ř':
                builder.Append('R'); break;
            case 'Ś': case 'Ŝ': case 'Ş': case 'Š': case 'ś': case 'ŝ': case 'ş': case 'š':
                builder.Append('S'); break;
            case 'ß':
                builder.Append("SS"); break;
            case 'Ţ': case 'Ť': case 'Ŧ': case 'ţ': case 'ť': case 'ŧ':
                builder.Append('T'); break;
            case 'Ú': case 'Ù': case 'Û': case 'Ü': case 'Ũ': case 'Ū': case 'Ŭ': case 'Ů': case 'Ű': case 'Ų': case 'Ǔ':
            case 'ú': case 'ù': case 'û': case 'ü': case 'ũ': case 'ū': case 'ŭ': case 'ů': case 'ű': case 'ų': case 'ǔ':
                builder.Append('U'); break;
            case 'Ý': case 'Ÿ': case 'Ŷ': case 'ý': case 'ÿ': case 'ŷ':
                builder.Append('Y'); break;
            case 'Ź': case 'Ż': case 'Ž': case 'ź': case 'ż': case 'ž':
                builder.Append('Z'); break;
            default:
                builder.Append(ch); break;
        }
    }
}
