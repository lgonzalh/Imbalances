using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Imbalances.Core.Utils;

public static class TextNormalizer
{
    public static string NormalizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = FoldLatinCharacters(input.Trim());

        normalized = normalized.ToUpperInvariant();

        normalized = normalized.Replace('.', ' ')
                               .Replace('-', ' ')
                               .Replace('_', ' ');

        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }

    private static string FoldLatinCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            AppendFolded(builder, ch);
        }

        return builder.ToString();
    }

    private static void AppendFolded(StringBuilder builder, char ch)
    {
        switch (ch)
        {
            case 'Á': case 'À': case 'Â': case 'Ã': case 'Ä': case 'Å': case 'Ā': case 'Ă': case 'Ą': case 'Ǎ':
            case 'á': case 'à': case 'â': case 'ã': case 'ä': case 'å': case 'ā': case 'ă': case 'ą': case 'ǎ':
                builder.Append('A');
                break;
            case 'Æ': case 'Ǽ': case 'æ': case 'ǽ':
                builder.Append("AE");
                break;
            case 'Ç': case 'Ć': case 'Ĉ': case 'Ċ': case 'Č':
            case 'ç': case 'ć': case 'ĉ': case 'ċ': case 'č':
                builder.Append('C');
                break;
            case 'Ð': case 'Ď': case 'Đ': case 'ð': case 'ď': case 'đ':
                builder.Append('D');
                break;
            case 'É': case 'È': case 'Ê': case 'Ë': case 'Ē': case 'Ĕ': case 'Ė': case 'Ę': case 'Ě':
            case 'é': case 'è': case 'ê': case 'ë': case 'ē': case 'ĕ': case 'ė': case 'ę': case 'ě':
                builder.Append('E');
                break;
            case 'Ĝ': case 'Ğ': case 'Ġ': case 'Ģ':
            case 'ĝ': case 'ğ': case 'ġ': case 'ģ':
                builder.Append('G');
                break;
            case 'Ĥ': case 'Ħ': case 'ĥ': case 'ħ':
                builder.Append('H');
                break;
            case 'Í': case 'Ì': case 'Î': case 'Ï': case 'Ĩ': case 'Ī': case 'Ĭ': case 'Į': case 'İ': case 'Ǐ':
            case 'í': case 'ì': case 'î': case 'ï': case 'ĩ': case 'ī': case 'ĭ': case 'į': case 'ı': case 'ǐ':
                builder.Append('I');
                break;
            case 'Ĵ': case 'ĵ':
                builder.Append('J');
                break;
            case 'Ķ': case 'ķ':
                builder.Append('K');
                break;
            case 'Ĺ': case 'Ļ': case 'Ľ': case 'Ŀ': case 'Ł':
            case 'ĺ': case 'ļ': case 'ľ': case 'ŀ': case 'ł':
                builder.Append('L');
                break;
            case 'Ñ': case 'Ń': case 'Ņ': case 'Ň':
            case 'ñ': case 'ń': case 'ņ': case 'ň':
                builder.Append('N');
                break;
            case 'Ó': case 'Ò': case 'Ô': case 'Õ': case 'Ö': case 'Ø': case 'Ō': case 'Ŏ': case 'Ő': case 'Ǒ':
            case 'ó': case 'ò': case 'ô': case 'õ': case 'ö': case 'ø': case 'ō': case 'ŏ': case 'ő': case 'ǒ':
                builder.Append('O');
                break;
            case 'Œ': case 'œ':
                builder.Append("OE");
                break;
            case 'Ŕ': case 'Ŗ': case 'Ř': case 'ŕ': case 'ŗ': case 'ř':
                builder.Append('R');
                break;
            case 'Ś': case 'Ŝ': case 'Ş': case 'Š': case 'ś': case 'ŝ': case 'ş': case 'š':
                builder.Append('S');
                break;
            case 'ß':
                builder.Append("SS");
                break;
            case 'Ţ': case 'Ť': case 'Ŧ': case 'ţ': case 'ť': case 'ŧ':
                builder.Append('T');
                break;
            case 'Ú': case 'Ù': case 'Û': case 'Ü': case 'Ũ': case 'Ū': case 'Ŭ': case 'Ů': case 'Ű': case 'Ų': case 'Ǔ':
            case 'ú': case 'ù': case 'û': case 'ü': case 'ũ': case 'ū': case 'ŭ': case 'ů': case 'ű': case 'ų': case 'ǔ':
                builder.Append('U');
                break;
            case 'Ý': case 'Ÿ': case 'Ŷ': case 'ý': case 'ÿ': case 'ŷ':
                builder.Append('Y');
                break;
            case 'Ź': case 'Ż': case 'Ž': case 'ź': case 'ż': case 'ž':
                builder.Append('Z');
                break;
            default:
                builder.Append(ch);
                break;
        }
    }
}
