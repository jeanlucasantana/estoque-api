using System.Net;
using System.Net.Http.Json;
using Estoque.Api.Data;
using Estoque.Api.Domain;
using Estoque.Api.Features.Produtos;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Estoque.IntegrationTests;

public sealed class ProdutosConcorrenciaTests(ApiFactory api)
{
    // Um PUT lê o produto, e antes de gravar uma reserva de estoque altera a mesma linha. O xmin mudou:
    // o PUT precisa receber 409 em vez de sobrescrever a quantidade reservada.
    [Fact]
    public async Task Put_que_perde_a_corrida_para_uma_reserva_retorna_409_e_nao_sobrescreve_o_estoque()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cliente = api.CriarClienteAutenticado();
        var sku = "C" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        var criacao = await cliente.PostAsJsonAsync(
            "/api/produtos", new { nome = $"Produto {sku}", sku, preco = 10m, custoUnitario = 1m, quantidade = 10 }, ct);
        var produto = await criacao.Content.ReadFromJsonAsync<ProdutoResponse>(ct);
        Assert.NotNull(produto);

        // O interceptor cria a corrida de forma determinística: roda a "reserva" exatamente entre a leitura e a gravação do PUT.
        using var comReservaSimultanea = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(new ReservaAntesDeGravar(produto.Id)))));
        using var clienteDoPut = comReservaSimultanea.CreateClient();
        clienteDoPut.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ChaveApi);

        var put = await clienteDoPut.PutAsJsonAsync(
            $"/api/produtos/{produto.Id}", new { nome = "Nome do PUT", sku, preco = 10m, custoUnitario = 1m, quantidade = 50 }, ct);
        var final = await cliente.GetFromJsonAsync<ProdutoResponse>($"/api/produtos/{produto.Id}", ct);

        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.NotNull(final);
        Assert.Equal(9, final.Quantidade);
        Assert.Equal($"Produto {sku}", final.Nome);
    }

    // Antes do SaveChanges do PUT, baixa 1 unidade do produto por outra conexão, como faria um pedido simultâneo.
    private sealed class ReservaAntesDeGravar(int produtoId) : SaveChangesInterceptor
    {
        private int _jaReservou;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var contexto = eventData.Context;
            var gravandoOProduto = contexto?.ChangeTracker.Entries<Produto>()
                .Any(entrada => entrada.State == EntityState.Modified && entrada.Entity.Id == produtoId) == true;

            if (contexto is not null && gravandoOProduto && Interlocked.Exchange(ref _jaReservou, 1) == 0)
            {
                await using var conexao = new NpgsqlConnection(contexto.Database.GetConnectionString());
                await conexao.OpenAsync(cancellationToken);
                await using var reserva = new NpgsqlCommand("UPDATE produtos SET quantidade = quantidade - 1 WHERE id = @id", conexao);
                reserva.Parameters.AddWithValue("id", produtoId);
                await reserva.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
