# ADR 0006: Outbox transacional para as confirmações de pedido

**Status:** aceita (substitui a fila em memória da primeira versão)

## Contexto

A RN08 pede que a confirmação seja enviada sem atrasar a resposta. A primeira versão usava uma fila em memória (`Channel`) consumida por um `BackgroundService`: rápida, mas as confirmações na fila se perdiam se o processo reiniciasse, e nada registrava a perda. Enviar dentro da requisição atrasaria a resposta; enviar depois do commit, sem registro, não garante o envio.

## Decisão

- Na **mesma transação** do pedido, gravar uma linha em `confirmacoes_pendentes`. Se o pedido existe, a confirmação pendente também existe.
- O `ProcessadorDeConfirmacoes` (um `BackgroundService`) lê lotes com `SELECT ... FOR UPDATE SKIP LOCKED`, envia, marca `enviada_em` e faz commit.
- Em falha, incrementa `tentativas` e agenda `proxima_tentativa_em` com espera crescente (até 5 minutos).
- Um índice parcial (`WHERE enviada_em IS NULL`) mantém a busca rápida mesmo com o histórico crescendo.
- `Confirmacoes:Habilitado` permite ter instâncias que só atendem requisições.

## Alternativas consideradas

- **Fila em memória (`Channel`):** a versão anterior; perde mensagens em reinícios.
- **Broker de mensagens (RabbitMQ, Kafka, Service Bus):** robusto, mas publicar no broker e gravar no banco são duas operações sem transação comum; sem outbox, o problema continuaria. Infraestrutura desproporcional para o desafio.
- **`LISTEN/NOTIFY` do PostgreSQL em vez de polling:** reduziria a latência e as consultas ociosas, mas acrescenta complexidade de conexão; fica como otimização futura.

## Consequências

- Nenhuma confirmação se perde num reinício, e várias réplicas processam em paralelo sem envio duplicado (provado por `OutboxTests`).
- A entrega é "pelo menos uma vez": se o processo cair depois de enviar e antes do commit, a confirmação é enviada de novo. O provedor de email real precisaria deduplicar pelo `id` da confirmação.
- Cada instância consulta a tabela a cada intervalo (2 segundos por padrão), mesmo sem pendências.
- A tabela cresce; uma rotina de limpeza das enviadas antigas seria necessária em produção.
