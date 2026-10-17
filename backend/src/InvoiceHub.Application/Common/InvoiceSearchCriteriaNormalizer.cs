using InvoiceHub.Application.Contracts;

namespace InvoiceHub.Application.Common;

/// <summary>
/// Normaliza y sanea los criterios de busqueda antes de que lleguen al adaptador de persistencia.
/// Es el unico punto que decide que campos de ordenamiento son validos (lista blanca), de modo que el
/// adaptador SQL pueda interpolar de forma segura el identificador de columna.
/// </summary>
public static class InvoiceSearchCriteriaNormalizer
{
    public const string DefaultSortBy = "number";
    public const string DefaultSortDirection = "desc";

    /// <summary>Campos de ordenamiento soportados por la busqueda.</summary>
    public static readonly string[] AllowedSortFields =
    {
        "number",
        "invoiceDate",
        "dueDate",
        "customer",
        "total",
        "outstanding",
        "creditNotes",
        "daysOverdue",
        "status"
    };

    public static void Normalize(InvoiceSearchCriteria criteria)
    {
        criteria.Page = criteria.Page < 1 ? 1 : Math.Min(criteria.Page, 10_000);
        criteria.PageSize = criteria.PageSize < 1 ? 25 : Math.Min(criteria.PageSize, 200);
        criteria.Text = Normalize(criteria.Text);
        criteria.CustomerRun = Normalize(criteria.CustomerRun);

        var sortBy = Normalize(criteria.SortBy);

        // La comparacion es insensible a mayusculas para tolerar variaciones de la query string, pero el
        // valor se canonicaliza al texto declarado en AllowedSortFields: los scripts SQL comparan @SortBy
        // contra literales como 'dueDate' o 'creditNotes', por lo que la capitalizacion es contrato.
        criteria.SortBy = Array.Find(
            AllowedSortFields,
            field => string.Equals(field, sortBy, StringComparison.OrdinalIgnoreCase)) ?? DefaultSortBy;

        var direction = Normalize(criteria.SortDirection);
        criteria.SortDirection = direction == "asc" ? "asc" : DefaultSortDirection;

        if (criteria.MinDaysOverdue is < 0)
        {
            criteria.MinDaysOverdue = null;
        }
    }

    /// <summary>Representacion canonica de los criterios, usada como base de la clave de cache.</summary>
    public static string Describe(InvoiceSearchCriteria criteria)
    {
        Normalize(criteria);

        return string.Join(
            '|',
            criteria.Number?.ToString() ?? string.Empty,
            criteria.InvoiceStatus?.ToString() ?? string.Empty,
            criteria.PaymentStatus?.ToString() ?? string.Empty,
            criteria.IsConsistent?.ToString() ?? string.Empty,
            criteria.OnlyActiveSystem.ToString(),
            criteria.CustomerRun ?? string.Empty,
            criteria.Text ?? string.Empty,
            criteria.MinDaysOverdue?.ToString() ?? string.Empty,
            criteria.SortBy,
            criteria.SortDirection);
    }

    /// <summary>Huella estable (FNV-1a de 32 bits) de los criterios normalizados.</summary>
    public static int Fingerprint(InvoiceSearchCriteria criteria)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            var hash = offset;
            foreach (var character in Describe(criteria))
            {
                hash ^= character;
                hash *= prime;
            }

            return (int)hash;
        }
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }
}
