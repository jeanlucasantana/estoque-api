# ADR 0002: Reserva de estoque com UPDATE condicional atômico

**Status:** aceita

## Contexto

A RN03 exige que o estoque nunca fique negativo, inclusive com muitas requisições simultâneas e várias réplicas da API, e que a reserva de todos os itens seja atômica. O legado lia o saldo, conferia em memória e gravava depois: na reprodução, 30 pedidos simultâneos para 10 unidades foram todos aceitos, e o estoque terminou em 9.

## Decisão

Para cada item, em ordem crescente de `ProdutoId` e dentro de uma transação:

```sql
UPDATE produtos SET quantidade = quantidade - @q
WHERE id = @id AND ativo AND quantidade >= @q
```

executado com `ExecuteUpdateAsync`. Zero linhas afetadas significa saldo insuficiente: a transação é descartada e a API responde 409 com a lista de produtos. Um `CHECK (quantidade >= 0)` no banco é a última defesa.

## Alternativas consideradas

- **Concorrência otimista (`xmin`) com retry:** correta, mas sob disputa alta cada conflito gera nova tentativa, em cascata.
- **`SELECT ... FOR UPDATE`:** correta, com o mesmo efeito, mas uma ida a mais ao banco e mais código.
- **Lock em memória (`lock`, `SemaphoreSlim`):** só protege uma instância; não funciona com várias réplicas.
- **Isolamento `SERIALIZABLE`:** o banco aborta conflitos, e a aplicação precisaria repetir.

## Consequências

- A conferência e a baixa são uma operação só, com a trava no banco: funciona com qualquer número de réplicas, sem retry.
- A ordem fixa por id evita deadlock entre pedidos que disputam os mesmos produtos.
- A regra fica em SQL, fora do change tracker do EF, e precisa de teste de integração com banco real: `Trinta_pedidos_simultaneos_para_dez_unidades_vendem_exatamente_dez`.
