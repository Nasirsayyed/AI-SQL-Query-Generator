using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AIQueryGenerator.Application.Abstractions;
using AIQueryGenerator.Shared.Contracts;
using AIQueryGenerator.Shared.Exceptions;

namespace AIQueryGenerator.Application.Sql;

/// <summary>Validates and converts client-supplied parameter values into typed ADO.NET parameter values.</summary>
public static partial class ParameterBinder
{
    [GeneratedRegex("^@?[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex NamePattern();

    public static IReadOnlyList<BoundParameter> Bind(IReadOnlyList<QueryParameterDto>? parameters, int maxCount, int maxValueLength)
    {
        parameters ??= [];
        var errors = new List<ApiError>();
        if (parameters.Count > maxCount)
            errors.Add(new ApiError("parameters", $"At most {maxCount} parameters are allowed."));

        var bound = new List<BoundParameter>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in parameters)
        {
            var field = $"parameters.{p.Name}";
            if (string.IsNullOrWhiteSpace(p.Name) || !NamePattern().IsMatch(p.Name))
            {
                errors.Add(new ApiError(field, "Parameter names must be valid identifiers (letters, digits, underscore)."));
                continue;
            }

            var name = p.Name.TrimStart('@');
            if (!names.Add(name))
            {
                errors.Add(new ApiError(field, $"Parameter @{name} is specified more than once."));
                continue;
            }

            if (p.Value is { Length: var len } && len > maxValueLength)
            {
                errors.Add(new ApiError(field, $"Parameter @{name} exceeds {maxValueLength} characters."));
                continue;
            }

            if (!Enum.IsDefined(p.Type))
            {
                errors.Add(new ApiError(field, $"Parameter @{name} has an unsupported type."));
                continue;
            }

            if (TryConvert(p.Type, p.Value, out var value))
                bound.Add(new BoundParameter(name, p.Type, value));
            else
                errors.Add(new ApiError(field, $"Value '{Shorten(p.Value)}' is not a valid {p.Type} for parameter @{name}."));
        }

        if (errors.Count > 0)
            throw new BusinessRuleException(errors[0].Message, ErrorCodes.InvalidParameters);
        return bound;
    }

    public static bool TryConvert(SqlParameterType type, string? raw, out object? value)
    {
        value = null;
        if (raw is null) return true; // NULL
        var s = raw.Trim();
        var inv = CultureInfo.InvariantCulture;
        switch (type)
        {
            case SqlParameterType.String: value = raw; return true;
            case SqlParameterType.Int when int.TryParse(s, NumberStyles.Integer, inv, out var i): value = i; return true;
            case SqlParameterType.BigInt when long.TryParse(s, NumberStyles.Integer, inv, out var l): value = l; return true;
            case SqlParameterType.Decimal when decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, inv, out var d): value = d; return true;
            case SqlParameterType.Bool when bool.TryParse(s, out var b): value = b; return true;
            case SqlParameterType.Bool when s is "1" or "0": value = s == "1"; return true;
            case SqlParameterType.Date when DateOnly.TryParse(s, inv, DateTimeStyles.None, out var dt): value = dt.ToDateTime(TimeOnly.MinValue); return true;
            case SqlParameterType.Date when DateTime.TryParse(s, inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt2): value = dt2.Date; return true;
            case SqlParameterType.DateTime when DateTime.TryParse(s, inv, DateTimeStyles.RoundtripKind, out var dtt): value = dtt; return true;
            case SqlParameterType.Guid when Guid.TryParse(s, out var g): value = g; return true;
            default: return false;
        }
    }

    /// <summary>Canonical form of a parameter set used to bind validation tokens to parameter values.</summary>
    public static string Signature(IReadOnlyList<QueryParameterDto>? parameters)
    {
        var sb = new StringBuilder();
        foreach (var p in (parameters ?? []).OrderBy(p => p.Name.TrimStart('@'), StringComparer.OrdinalIgnoreCase))
            sb.Append(p.Name.TrimStart('@').ToLowerInvariant()).Append(':').Append(p.Type).Append(':')
              .Append(p.Value is null ? "␀" : Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Value))).Append('|');
        return sb.ToString();
    }

    private static string Shorten(string? value) => value is null ? "null" : value.Length > 30 ? value[..30] + "…" : value;
}
