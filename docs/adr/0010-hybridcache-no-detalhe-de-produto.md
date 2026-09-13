# ADR 0010: HybridCache no detalhe de produto, com invalidação em toda escrita

**Status:** aceita

## Contexto

O legado tinha um "cache" com `BinaryFormatter` num `Dictionary` estático, que nunca era invalidado: na reprodução, o detalhe mostrava estoque 13 enquanto a listagem mostrava 12. O detalhe de produto (`GET /api/produtos/{id}`) é a consulta mais provável de ser repetida pelos clientes.

## Decisão

- **`HybridCache`** do .NET (`AddHybridCache`) no `GET /api/produtos/{id}`, com chave `produto:{id}` e expiração de **30 segundos**.
- **Invalidação depois do commit** em toda escrita que muda o que a resposta mostra: criação de produto, `PUT`, `DELETE`, **reserva de estoque na criação de pedido** e **devolução no cancelamento**.
- **Um id inexistente também fica em cache** (404), e a criação do produto remove essa entrada.
- **A reserva de estoque nunca lê do cache**: a decisão de vender usa sempre o banco.

## Alternativas consideradas

- **`IMemoryCache`:** funciona, mas sem proteção contra *stampede* (várias requisições recalculando a mesma entrada ao mesmo tempo) e sem caminho para cache distribuído.
- **Redis como segundo nível:** o `HybridCache` aceita sem mudar o código, mas é infraestrutura a mais para este desafio.
- **Cache na listagem e na busca:** a invalidação por página e por termo seria complexa, com ganho incerto.

## Consequências

- Leituras repetidas do mesmo produto não vão ao banco, e nenhuma escrita deixa o cache desatualizado **na mesma réplica** (provado por `CacheDeProdutosTests`).
- **Com várias réplicas**, a invalidação feita numa réplica não chega às outras: elas podem mostrar o valor antigo por até 30 segundos. Por isso a expiração é curta, e a venda não depende do cache.
- **Uma corrida residual:** uma leitura iniciada antes do commit pode gravar o valor antigo depois da invalidação; a expiração limita o efeito a 30 segundos.
