using System.Globalization;
using Estoque.Api.Data;
using Estoque.Api.Features.Pedidos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Estoque.IntegrationTests.ApiFactory))]

namespace Estoque.IntegrationTests;

// Uma API e um PostgreSQL real (em container) compartilhados por todos os testes do assembly.
// Cada teste cria os próprios dados, com SKUs únicos, então a ordem de execução não importa.
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ChaveApi = "chave-de-teste-com-no-minimo-32-caracteres";

    // Quarta-feira, 09/09/2026, 12h em Brasília: relógio fixo, sem promoção de sexta (coberta nos testes unitários).
    public static readonly DateTimeOffset Agora = new(2026, 9, 9, 15, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public ApiFactory()
    {
        // Força a cultura pt-BR, que usa vírgula decimal. Se algum valor para o frete for formatado pela cultura
        // do servidor, o frete falso responde 404, como o simulador real, e os testes quebram.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
    }

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
        builder.UseSetting("Frete:UrlBase", "http://frete.teste");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Agora));
            services.AddHttpClient<ServicoFrete>().ConfigurePrimaryHttpMessageHandler(() => new FreteFalso());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
