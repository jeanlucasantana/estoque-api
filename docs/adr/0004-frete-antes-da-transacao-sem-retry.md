# ADR 0004: Frete consultado antes da transação, com timeout de 2 s e sem retry

**Status:** aceita

## Contexto

A RN05 limita a espera pelo serviço de frete a 2 segundos e exige que, em falha, demora ou resposta inválida, o pedido não seja criado, nenhum estoque seja reservado e a API responda 503. O legado criava um `HttpClient` por chamada, sem timeout, e transformava qualquer falha em frete zero.

## Decisão

- **Cliente tipado** (`ServicoFrete`) registrado com `IHttpClientFactory`, com `Timeout` de 2 segundos.
- **Sem retry:** uma segunda tentativa estouraria o limite de 2 segundos.
- **Consulta antes de abrir a transação** de reserva.
- Qualquer falha (status de erro, timeout, JSON inválido, valor ausente, negativo ou acima de 1.000.000) resulta em `null`, e o endpoint responde 503.
- O valor é formatado com `CultureInfo.InvariantCulture`.

## Alternativas consideradas

- **Consultar dentro da transação:** manteria travas de linha e uma conexão ocupadas durante até 2 segundos de chamada externa, reduzindo a vazão.
- **Retry com Polly ou `AddStandardResilienceHandler`:** útil em geral, mas incompatível com o limite de 2 segundos.

## Consequências

- Nenhuma trava fica presa durante a chamada externa, e em falha não há o que desfazer.
- O preço gravado é o lido antes da consulta de frete; se ele mudar nesses 2 segundos, vale o lido. O estoque continua garantido pelo `UPDATE` condicional (ADR 0002).
- Com o serviço fora do ar, cada pedido ainda espera até 2 segundos antes do 503. Um circuit breaker resolveria, e está registrado como próximo passo.
