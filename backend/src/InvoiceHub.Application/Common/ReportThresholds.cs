namespace InvoiceHub.Application.Common;

/// <summary>
/// Umbrales compartidos por los reportes y el panel principal. Se centralizan para que la interfaz, la
/// API y las consultas SQL usen exactamente los mismos cortes de negocio.
/// </summary>
public static class ReportThresholds
{
    /// <summary>Dias de mora que usa el reporte de facturas vencidas sin actividad cuando no se indica otro valor.</summary>
    public const int DefaultOverdueMinDays = 30;

    /// <summary>Dias de mora minimo aceptado por el reporte.</summary>
    public const int MinOverdueDays = 1;

    /// <summary>Dias de mora maximo aceptado por el reporte (evita consultas sin sentido).</summary>
    public const int MaxOverdueDays = 365;

    /// <summary>Ventana, en dias, con la que el panel clasifica facturas "proximas a vencer".</summary>
    public const int DueSoonDays = 7;

    /// <summary>Mes de la serie historica de facturacion del panel.</summary>
    public const int MonthlyBillingMonths = 12;

    /// <summary>Cantidad de deudores mostrados en el panel.</summary>
    public const int TopDebtors = 5;

    public static bool IsValidOverdueMinDays(int days) =>
        days is >= MinOverdueDays and <= MaxOverdueDays;
}
