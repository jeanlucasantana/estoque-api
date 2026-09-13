# ADR 0008: Rate limiting por IP antes da autenticação

**Status:** aceita

## Contexto

A API não tinha nenhuma proteção contra volume abusivo: um cliente com defeito em loop, um script mal escrito ou uma tentativa de adivinhar a chave de API podem consumir conexões do banco e degradar o serviço para todos.

## Decisão

- Rate limiter nativo do ASP.NET Core (`AddRateLimiter`), com **janela fixa por endereço IP**, só nas rotas `/api`.
- Padrão de **300 requisições a cada 10 segundos**, configurável em `LimiteDeRequisicoes:PermissoesPorJanela` e `LimiteDeRequisicoes:JanelaSegundos`.
- O middleware roda **antes** da checagem da chave: requisições sem chave também contam.
- Rejeição com **429** em ProblemDetails e cabeçalho `Retry-After`.
- Health checks ficam fora do limite, para não afetar os probes do orquestrador.

## Alternativas consideradas

- **Partição pela chave de API:** faria mais sentido com uma chave por cliente; hoje há uma chave única, e quem não tem chave ficaria sem limite.
- **Janela deslizante ou token bucket:** distribuem melhor as rajadas na virada da janela; a janela fixa é mais simples de explicar e suficiente aqui.
- **Rate limiting no API Gateway ou ingress:** a melhor opção quando existe; o limite na aplicação protege mesmo sem ele.

## Consequências

- Um cliente abusivo recebe 429 sem afetar os demais.
- **O limite é por instância:** com 3 réplicas, o limite efetivo é até 3 vezes maior. Um limite global exigiria um contador compartilhado (Redis) ou o gateway.
- **Atrás de proxy ou load balancer**, a API precisa do `ForwardedHeaders` configurado com os proxies confiáveis; sem isso, todos os clientes teriam o IP do proxy e dividiriam a mesma cota.
