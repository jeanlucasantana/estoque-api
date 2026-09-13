# Registros de decisão de arquitetura (ADRs)

Cada ADR registra uma decisão com o contexto, as alternativas consideradas e as consequências, inclusive as negativas.

| # | Decisão | Status |
| :--- | :--- | :--- |
| [0001](0001-organizacao-por-funcionalidade.md) | Um projeto de API organizado por funcionalidade | Aceita |
| [0002](0002-reserva-de-estoque-com-update-condicional.md) | Reserva de estoque com UPDATE condicional atômico | Aceita |
| [0003](0003-concorrencia-otimista-com-xmin.md) | Concorrência otimista com `xmin` nas transições e no PUT de produto | Aceita |
| [0004](0004-frete-antes-da-transacao-sem-retry.md) | Frete consultado antes da transação, com timeout de 2 s e sem retry | Aceita |
| [0005](0005-migracoes-em-servico-separado.md) | Migrações aplicadas por um serviço separado | Aceita |
| [0006](0006-outbox-transacional-para-confirmacoes.md) | Outbox transacional para as confirmações de pedido | Aceita |
| [0007](0007-idempotency-key-na-criacao-de-pedidos.md) | Idempotency-Key na criação de pedidos | Aceita |
| [0008](0008-rate-limiting-por-ip.md) | Rate limiting por IP antes da autenticação | Aceita |
| [0009](0009-testes-com-postgresql-real.md) | Testes de integração com PostgreSQL real e frete falso no HttpMessageHandler | Aceita |
| [0010](0010-hybridcache-no-detalhe-de-produto.md) | HybridCache no detalhe de produto, com invalidação em toda escrita | Aceita |
| [0011](0011-opentelemetry.md) | Observabilidade com OpenTelemetry | Aceita |
| [0012](0012-manifestos-de-kubernetes.md) | Manifestos de Kubernetes com probes, recursos e desligamento gracioso | Aceita |
