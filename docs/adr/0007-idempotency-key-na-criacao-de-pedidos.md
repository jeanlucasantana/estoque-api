# ADR 0007: Idempotency-Key na criação de pedidos

**Status:** aceita

## Contexto

Um cliente que não recebe a resposta de `POST /api/pedidos` (timeout de rede, queda de conexão) não sabe se o pedido foi criado. Se repetir a requisição, cria um pedido duplicado e reserva estoque em dobro. O mesmo acontece com clique duplo ou retry automático de biblioteca.

## Decisão

- Cabeçalho opcional `Idempotency-Key` (1 a 100 caracteres), no padrão do rascunho da IETF.
- A chave é gravada em `chaves_idempotencia`, **como chave primária**, na **mesma transação** do pedido, junto com o SHA-256 do corpo da requisição.
- **Repetição com o mesmo corpo:** 201 com o pedido original e o cabeçalho `Idempotent-Replayed: true`, sem nova reserva.
- **Mesma chave com outro corpo:** 422.
- **Requisições simultâneas com a mesma chave:** a inserção da segunda espera o commit da primeira e falha pela chave primária; a segunda desfaz o próprio pedido e a reserva e devolve o pedido da primeira.
- **Tentativas que falham** (409, 503, 400) não gravam a chave e podem ser repetidas.

## Alternativas consideradas

- **Cache em memória das chaves:** não funciona com várias réplicas nem sobrevive a reinícios.
- **Lock distribuído (Redis) antes de processar:** mais infraestrutura, e a chave primária do banco já resolve a disputa de forma atômica.
- **Devolver 409 para a requisição simultânea** em vez do pedido original: mais simples, mas obriga o cliente a tratar mais um caso.

## Consequências

- O cliente pode repetir com segurança, e o estoque é reservado uma única vez (provado por `IdempotenciaTests`).
- Requisições simultâneas com a mesma chave consultam o frete e reservam estoque antes de descobrir o conflito, e desfazem depois.
- Chaves não expiram; em produção, uma rotina removeria as antigas (por exemplo, com mais de 24 horas).
- A chave é global: com uma chave de API por cliente, ela deveria ser escopada por cliente.
