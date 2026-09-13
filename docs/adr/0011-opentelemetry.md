# ADR 0011: Observabilidade com OpenTelemetry

**Status:** aceita

## Contexto

A API já tinha logs estruturados em JSON com `TraceId` e health checks. Para operar com várias réplicas e dependências externas (frete, PostgreSQL), só isso não responde a perguntas como: quanto tempo o frete leva? Quantos pedidos são recusados por estoque? Onde está o tempo de uma requisição lenta?

## Decisão

- **OpenTelemetry** configurado em `Common/Telemetria.cs`, com o recurso identificado como `estoque-api`.
- **Traces:** requisições recebidas (ASP.NET Core, sem os health checks), chamadas HTTP feitas (frete), comandos no PostgreSQL (Npgsql) e o span próprio `confirmacoes.processar_lote`.
- **Métricas de negócio** (meter `Estoque.Api`):

  | Métrica | O que mede |
  | :--- | :--- |
  | `estoque.pedidos.criados` | Pedidos criados |
  | `estoque.pedidos.recusados_por_estoque` | Pedidos recusados por falta de estoque |
  | `estoque.frete.falhas` | Falhas de frete, com o motivo: `status_de_erro`, `resposta_invalida`, `timeout` ou `erro_de_comunicacao` |
  | `estoque.frete.duracao` | Duração da chamada de frete |
  | `estoque.confirmacoes.enviadas` | Confirmações enviadas pelo outbox |

- **Métricas de plataforma:** ASP.NET Core, HttpClient, Npgsql e runtime do .NET (`System.Runtime`).
- **Logs** também exportados por OpenTelemetry, com o `TraceId` do span ativo.
- **Exportação por OTLP** só quando `OTEL_EXPORTER_OTLP_ENDPOINT` estiver configurado. No Compose, o profile `observabilidade` sobe o Aspire Dashboard para ver tudo localmente.
- **As buscas vazias do processador de confirmações** rodam com a instrumentação suprimida (`SuppressInstrumentationScope`), para não gerar um trace a cada segundo.

## Alternativas consideradas

- **Só logs:** já existiam; não mostram latência por dependência nem agregados.
- **SDK de um fornecedor específico** (Application Insights, Datadog): prende a aplicação a um produto. Com OTLP, a troca de destino é só configuração.
- **Coletor OpenTelemetry no Compose** em vez do Aspire Dashboard: seria o caminho em produção, mas precisaria de um backend de visualização; o painel resolve o uso local com um container.

## Consequências

- Um erro reportado pelo cliente (com `traceId`) pode ser seguido do HTTP ao SQL.
- **A cardinalidade das métricas foi mantida baixa:** nenhum id de pedido, produto ou cliente vai para as tags.
- A instrumentação acrescenta um pequeno custo por requisição; em produção, a amostragem de traces seria ajustada ao volume.
- As métricas de negócio são verificadas por `TelemetriaTests`; os traces e o painel são verificados na revisão manual.
