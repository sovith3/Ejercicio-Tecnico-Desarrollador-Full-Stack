using InvoiceHub.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Application;

/// <summary>
/// Registro de los casos de uso en el contenedor. Cada caso de uso es una clase concreta con sus
/// dependencias en el constructor, de modo que la API solo resuelve el handler que necesita y las
/// pruebas pueden instanciarlo con dobles.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationUseCases(this IServiceCollection services)
    {
        // Consultas (lado lectura).
        services.AddScoped<SearchInvoicesHandler>();
        services.AddScoped<GetInvoiceDetailHandler>();
        services.AddScoped<GetInvoiceStatusCatalogHandler>();
        services.AddScoped<GetSourceFileStatusHandler>();
        services.AddScoped<GetDashboardHandler>();
        services.AddScoped<GetPaymentStatusSummaryReportHandler>();
        services.AddScoped<GetOverdueWithoutActivityReportHandler>();
        services.AddScoped<GetInconsistentInvoicesReportHandler>();

        // Comandos (lado escritura).
        services.AddScoped<ImportInvoicesHandler>();
        services.AddScoped<AddCreditNoteHandler>();
        services.AddScoped<RegisterInvoicePaymentHandler>();
        services.AddScoped<LoginHandler>();

        return services;
    }
}
