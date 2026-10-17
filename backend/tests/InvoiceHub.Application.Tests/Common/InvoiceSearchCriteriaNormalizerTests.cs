using InvoiceHub.Application.Common;
using InvoiceHub.Application.Contracts;
using InvoiceHub.Domain.Enums;

namespace InvoiceHub.Application.Tests.Common;

/// <summary>
/// Pruebas de <see cref="InvoiceSearchCriteriaNormalizer"/>. Es el unico punto que decide que campos de
/// ordenamiento pueden alcanzar el SQL, por lo que la lista blanca y el saneo de paginas son criticos
/// tanto para la seguridad como para el contrato de la API.
/// </summary>
public sealed class InvoiceSearchCriteriaNormalizerTests
{
    [Theory]
    [InlineData("number")]
    [InlineData("invoiceDate")]
    [InlineData("dueDate")]
    [InlineData("customer")]
    [InlineData("total")]
    [InlineData("outstanding")]
    [InlineData("creditNotes")]
    [InlineData("daysOverdue")]
    [InlineData("status")]
    public void Normalize_WithAllowedSortField_KeepsIt(string sortBy)
    {
        var criteria = new InvoiceSearchCriteria { SortBy = sortBy };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(sortBy, criteria.SortBy);
    }

    [Theory]
    [InlineData("campo-inexistente")]
    [InlineData("cliente")]
    [InlineData("dueDate; DROP TABLE Invoice")]
    [InlineData("")]
    [InlineData(null)]
    public void Normalize_WithUnknownSortField_FallsBackToDefault(string? sortBy)
    {
        var criteria = new InvoiceSearchCriteria { SortBy = sortBy };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(InvoiceSearchCriteriaNormalizer.DefaultSortBy, criteria.SortBy);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("ASC")]
    [InlineData("  asc  ")]
    public void Normalize_WithAscendingDirection_KeepsAscending(string direction)
    {
        var criteria = new InvoiceSearchCriteria { SortDirection = direction };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal("asc", criteria.SortDirection);
    }

    [Theory]
    [InlineData("desc")]
    [InlineData("descendente")]
    [InlineData("")]
    [InlineData(null)]
    public void Normalize_WithAnyOtherDirection_FallsBackToDescending(string? direction)
    {
        var criteria = new InvoiceSearchCriteria { SortDirection = direction };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(InvoiceSearchCriteriaNormalizer.DefaultSortDirection, criteria.SortDirection);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(999_999, 10_000)]
    public void Normalize_ClampsPage(int page, int expected)
    {
        var criteria = new InvoiceSearchCriteria { Page = page };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(expected, criteria.Page);
    }

    [Theory]
    [InlineData(0, 25)]
    [InlineData(-1, 25)]
    [InlineData(500, 200)]
    public void Normalize_ClampsPageSize(int pageSize, int expected)
    {
        var criteria = new InvoiceSearchCriteria { PageSize = pageSize };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(expected, criteria.PageSize);
    }

    [Fact]
    public void Normalize_TrimsAndLowercasesTextAndRun()
    {
        var criteria = new InvoiceSearchCriteria { Text = "  ACME  SpA ", CustomerRun = " 12.345.678-9 " };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal("acme  spa", criteria.Text);
        Assert.Equal("12.345.678-9", criteria.CustomerRun);
    }

    [Theory]
    [InlineData("INVOICEDATE", "invoiceDate")]
    [InlineData("duedate", "dueDate")]
    [InlineData("creditnotes", "creditNotes")]
    [InlineData("DUE_DATE", "number")]
    [InlineData("days overdue", "number")]
    public void Normalize_CanonicalizesSortFieldCasingToTheSqlContract(string requested, string expected)
    {
        var criteria = new InvoiceSearchCriteria { SortBy = requested };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(expected, criteria.SortBy);
        Assert.Contains(criteria.SortBy, InvoiceSearchCriteriaNormalizer.AllowedSortFields);
    }

    [Fact]
    public void Normalize_WithNegativeDaysOverdue_RemovesTheFilter()
    {
        var criteria = new InvoiceSearchCriteria { MinDaysOverdue = -1 };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Null(criteria.MinDaysOverdue);
    }

    [Fact]
    public void Normalize_KeepsZeroDaysOverdue()
    {
        var criteria = new InvoiceSearchCriteria { MinDaysOverdue = 0 };

        InvoiceSearchCriteriaNormalizer.Normalize(criteria);

        Assert.Equal(0, criteria.MinDaysOverdue);
    }

    [Fact]
    public void Fingerprint_IsStableForEquivalentCriteria()
    {
        var first = new InvoiceSearchCriteria { Text = "  acme ", SortBy = "total", Page = 1, PageSize = 25 };
        var second = new InvoiceSearchCriteria { Text = "ACME", SortBy = "total", Page = 1, PageSize = 25 };

        Assert.Equal(
            InvoiceSearchCriteriaNormalizer.Fingerprint(first),
            InvoiceSearchCriteriaNormalizer.Fingerprint(second));
    }

    [Fact]
    public void Fingerprint_IgnoresPageAndPageSize()
    {
        var first = new InvoiceSearchCriteria { Text = "acme", Page = 1, PageSize = 25 };
        var second = new InvoiceSearchCriteria { Text = "acme", Page = 9, PageSize = 100 };

        Assert.Equal(
            InvoiceSearchCriteriaNormalizer.Fingerprint(first),
            InvoiceSearchCriteriaNormalizer.Fingerprint(second));
    }

    [Fact]
    public void Fingerprint_DiffersWhenFiltersChange()
    {
        var issued = new InvoiceSearchCriteria { InvoiceStatus = InvoiceStatus.Issued };
        var partial = new InvoiceSearchCriteria { InvoiceStatus = InvoiceStatus.Partial };
        var withPaymentFilter = new InvoiceSearchCriteria { PaymentStatus = PaymentStatus.Overdue };

        Assert.NotEqual(
            InvoiceSearchCriteriaNormalizer.Fingerprint(issued),
            InvoiceSearchCriteriaNormalizer.Fingerprint(partial));
        Assert.NotEqual(
            InvoiceSearchCriteriaNormalizer.Fingerprint(issued),
            InvoiceSearchCriteriaNormalizer.Fingerprint(withPaymentFilter));
    }

    [Fact]
    public void Fingerprint_HasUnknownFieldsAsEmptySegments()
    {
        var describe = InvoiceSearchCriteriaNormalizer.Describe(new InvoiceSearchCriteria());

        Assert.StartsWith("|||", describe);
        Assert.EndsWith("|number|desc", describe);
    }
}
