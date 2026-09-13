using Estoque.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Estoque.IntegrationTests.ApiFactory))]

namespace Estoque.IntegrationTests;

// Uma API e um PostgreSQL real (em container) compartilhados por todos os testes do assembly.
// Cada teste cria os próprios dados, com SKUs únicos, então a ordem de execução não importa.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ChaveApi = "chave-de-teste-com-no-minimo-32-caracteres";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Nos testes as migrações são aplicadas aqui; em produção quem aplica é o serviço de migração do Compose.
        await using var escopo = Services.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public HttpClient CriarClienteAutenticado()
    {
        var cliente = CreateClient();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", ChaveApi);
        return cliente;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Estoque", _postgres.GetConnectionString());
        builder.UseSetting("Api:Chave", ChaveApi);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
