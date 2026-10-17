using InvoiceHub.Domain.Entities;

namespace InvoiceHub.Application.Ports;

/// <summary>Puerto de salida para el catalogo de clientes.</summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByRunAsync(string run, CancellationToken cancellationToken);

    /// <summary>Devuelve los clientes existentes indexados por RUN para la importacion masiva.</summary>
    Task<IReadOnlyDictionary<string, Customer>> GetByRunsAsync(
        IEnumerable<string> runs,
        CancellationToken cancellationToken);

    Task AddAsync(Customer customer, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}

/// <summary>Puerto de salida para el registro de ejecuciones de importacion.</summary>
public interface IImportRunRepository
{
    Task AddAsync(ImportRun importRun, CancellationToken cancellationToken);

    Task<ImportRun?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>Unidad de trabajo: confina la transaccion de escritura.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);

    /// <summary>
    /// Descarta las entidades rastreadas. Se usa entre lotes de una carga masiva para no acumular
    /// grafos completos en memoria durante la importacion del archivo JSON.
    /// </summary>
    void ClearChangeTracker();
}
