// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Inverted Software.

using System.Text.RegularExpressions;

namespace InvertedSoftware.PLogger.Diagnostics.AI;

/// <summary>
/// Source-generated regex pipeline that strips secrets and PII before any
/// LLM call. Patterns are intentionally over-aggressive: false positives
/// become <c>&lt;redacted-{kind}&gt;</c>, false negatives are never tolerated
/// (a single leaked secret is a security incident).
/// </summary>
/// <remarks>
/// <para>The default rule set covers AWS / Azure / GCP / generic Bearer
/// API keys, JWTs, ADO.NET / PostgreSQL / MongoDB connection strings,
/// password=… key-value fields, email addresses, IPv4 + IPv6,
/// Luhn-validated credit card numbers, and common SSN shapes.</para>
/// <para>Composable: register additional <see cref="IPiiScanner"/> services
/// (e.g. Microsoft Presidio) via DI for the long-tail PII detection beyond
/// what regex can express.</para>
/// </remarks>
public static partial class RedactionEngine
{
    [GeneratedRegex(@"AKIA[0-9A-Z]{16}", RegexOptions.CultureInvariant)]
    private static partial Regex AwsAccessKeyRegex();

    [GeneratedRegex(@"(?i)(['""]?aws_secret_access_key['""]?\s*[:=]\s*)['""]?[A-Za-z0-9/+=]{40}['""]?", RegexOptions.CultureInvariant)]
    private static partial Regex AwsSecretRegex();

    [GeneratedRegex(@"AIza[0-9A-Za-z\-_]{35}", RegexOptions.CultureInvariant)]
    private static partial Regex GoogleApiKeyRegex();

    [GeneratedRegex(@"(?i)ey[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(?i)(authorization|bearer)\s*[:=]\s*['""]?[A-Za-z0-9._\-]{20,}['""]?", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)['""]?(password|passwd|pwd|secret|api[_-]?key|access[_-]?key|client[_-]?secret)['""]?\s*[:=]\s*['""]?[^\s'""&;,}<>]{4,}['""]?", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordKeyValueRegex();

    [GeneratedRegex(@"(?i)(server|data\s*source|host)\s*=\s*[^;]+;.*?(password|pwd)\s*=\s*[^;]+", RegexOptions.CultureInvariant)]
    private static partial Regex AdoConnectionStringRegex();

    [GeneratedRegex(@"(?i)mongodb(\+srv)?:\/\/[^\s/]+:[^@\s]+@[^\s/]+", RegexOptions.CultureInvariant)]
    private static partial Regex MongoConnectionStringRegex();

    [GeneratedRegex(@"(?i)postgres(ql)?:\/\/[^\s/]+:[^@\s]+@[^\s/]+", RegexOptions.CultureInvariant)]
    private static partial Regex PostgresConnectionStringRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IPv4Regex();

    [GeneratedRegex(@"\b(?:[A-Fa-f0-9]{1,4}:){7}[A-Fa-f0-9]{1,4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IPv6Regex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b(?:\d[ \-]?){13,19}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CreditCardCandidateRegex();

    /// <summary>
    /// Applies the entire pipeline and returns the redacted text. Output
    /// is deterministic for the same input — required for snapshot tests.
    /// </summary>
    public static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var working = input;
        // High-confidence credentials first — replacing entire matches with a tag.
        working = AwsAccessKeyRegex().Replace(working, "<redacted-aws-key>");
        working = AwsSecretRegex().Replace(working, "$1<redacted-aws-secret>");
        working = GoogleApiKeyRegex().Replace(working, "<redacted-google-key>");
        working = JwtRegex().Replace(working, "<redacted-jwt>");
        working = BearerTokenRegex().Replace(working, "<redacted-bearer>");
        working = PasswordKeyValueRegex().Replace(working, "<redacted-credential>");
        working = AdoConnectionStringRegex().Replace(working, "<redacted-connection-string>");
        working = MongoConnectionStringRegex().Replace(working, "<redacted-mongo-uri>");
        working = PostgresConnectionStringRegex().Replace(working, "<redacted-postgres-uri>");

        // PII — emails first because IPv4 can match parts of dotted IDs.
        working = EmailRegex().Replace(working, "<redacted-email>");
        working = SsnRegex().Replace(working, "<redacted-ssn>");
        working = IPv6Regex().Replace(working, "<redacted-ipv6>");
        working = IPv4Regex().Replace(working, "<redacted-ipv4>");

        // Credit cards: regex matches a candidate, Luhn validates it before redacting.
        working = CreditCardCandidateRegex().Replace(working, LuhnReplacer);

        return working;
    }

    /// <summary>Replacer that only redacts if the candidate passes Luhn.</summary>
    private static string LuhnReplacer(Match match)
    {
        var raw = match.Value;
        Span<char> digits = stackalloc char[raw.Length];
        var len = 0;
        foreach (var c in raw)
        {
            if (char.IsDigit(c))
            {
                digits[len++] = c;
            }
        }
        return len is >= 13 and <= 19 && IsLuhnValid(digits[..len])
            ? "<redacted-card>"
            : raw;
    }

    private static bool IsLuhnValid(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (alt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }
            sum += d;
            alt = !alt;
        }
        return sum % 10 == 0;
    }
}
