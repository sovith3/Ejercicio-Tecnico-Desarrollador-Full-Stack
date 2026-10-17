using InvoiceHub.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceHub.Infrastructure.Tests.Persistence;

/// <summary>
/// Pruebas del catalogo de scripts SQL. La numeracion de la carpeta es un contrato del proyecto: debe ser
/// contigua del 01 al 21, sin huecos ni archivos huerfanos, de modo que el orden alfabetico coincida con el
/// orden de aplicacion y que una numeracion salteada no haga pensar en scripts faltantes.
/// </summary>
public sealed class SqlScriptCatalogTests : IAsyncLifetime
{
    private const int ExpectedScriptCount = 21;

    private readonly TestHost _host = new();

    public Task InitializeAsync() => _host.InitializeAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void ScriptsFolder_IsNumberedFromOneToTwentyOneWithoutGaps()
    {
        var files = ScriptFiles();

        Assert.Equal(ExpectedScriptCount, files.Length);

        for (var position = 0; position < files.Length; position++)
        {
            var expectedPrefix = $"{position + 1:00}_";
            Assert.StartsWith(expectedPrefix, files[position]);
        }
    }

    [Fact]
    public void Catalog_DeclaresExactlyTheScriptsOfTheFolderInNumericOrder()
    {
        var declared = SqlScriptCatalog.RequiredScripts;

        Assert.Equal(ExpectedScriptCount, declared.Count);
        Assert.Equal(declared.OrderBy(name => name, StringComparer.Ordinal), declared);
        Assert.Equal(ScriptFiles(), declared);
        Assert.Equal(SqlScriptCatalog.DdlScripts, declared.Take(SqlScriptCatalog.DdlScripts.Length));
    }

    [Fact]
    public void Catalog_DeclaresEveryScriptOnlyOnce()
    {
        var declared = SqlScriptCatalog.RequiredScripts.OrderBy(name => name, StringComparer.Ordinal).ToList();

        Assert.Equal(declared.Count, declared.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            declared.Select(name => name[..2]).Distinct(StringComparer.Ordinal).Count(),
            declared.Count);
    }

    [Fact]
    public async Task Catalog_AgainstTheRealFolder_ReportsNoMissingAndNoOrphanScript()
    {
        var (missing, orphans) = await _host.WithScopeAsync(provider =>
        {
            var catalog = provider.GetRequiredService<SqlScriptCatalog>();
            var declared = SqlScriptCatalog.RequiredScripts.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphans = Directory.GetFiles(catalog.Directory, "*.sql")
                .Select(Path.GetFileName)
                .Where(name => !declared.Contains(name!))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult((catalog.MissingScripts(), orphans));
        });

        Assert.Empty(missing);
        Assert.Empty(orphans);
    }

    private static string[] ScriptFiles() => Directory
        .GetFiles(TestHost.ScriptsPath, "*.sql")
        .Select(Path.GetFileName)
        .Where(name => name is not null)
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();
}
