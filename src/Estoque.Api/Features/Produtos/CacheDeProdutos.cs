using Microsoft.Extensions.Caching.Hybrid;

namespace Estoque.Api.Features.Produtos;

// Cache do detalhe de produto (GET /api/produtos/{id}). Toda escrita que muda o que essa resposta mostra
// (criação, PUT, DELETE, reserva e devolução de estoque) remove a entrada depois do commit.
// A reserva de estoque nunca lê do cache: a fonte da verdade para vender é sempre o banco.
public static class CacheDeProdutos
{
    // Curto de propósito. Sem cache distribuído, cada réplica tem a sua memória, e a invalidação feita numa réplica
    // não chega às outras: a expiração é o atraso máximo que outra réplica pode mostrar.
    public static readonly HybridCacheEntryOptions Opcoes = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    public static string Chave(int produtoId) => $"produto:{produtoId}";

    public static ValueTask InvalidarAsync(HybridCache cache, IEnumerable<int> produtoIds, CancellationToken cancellationToken) =>
        cache.RemoveAsync(produtoIds.Distinct().Select(Chave), cancellationToken);
}
