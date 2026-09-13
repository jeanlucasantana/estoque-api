# ADR 0012: Manifestos de Kubernetes com probes, recursos e desligamento gracioso

**Status:** aceita

## Contexto

O Docker Compose resolve o ambiente local e a avaliação, mas a API precisa rodar com várias réplicas em produção, sobreviver a atualizações e manutenções do cluster sem perder requisições e ser implantada de forma reproduzível. As decisões anteriores (migração separada, health checks, outbox e reserva no banco) já foram tomadas pensando em várias réplicas.

## Decisão

Manifestos em `deploy/k8s`, aplicados com Kustomize:

| Recurso | Configuração principal |
| :--- | :--- |
| `Job` de migração | Roda o bundle antes de cada nova versão da API |
| `Deployment` | 3 réplicas, atualização sem indisponibilidade (`maxUnavailable: 0`) |
| Probes | `startupProbe` e `livenessProbe` em `/health/live`; `readinessProbe` em `/health/ready` |
| Recursos | `requests` e `limits` de CPU e memória |
| Segurança | Usuário não root, sistema de arquivos só leitura (com `/tmp` gravável), sem capabilities, sem escalada de privilégio |
| Desligamento | `preStop` de 5 s e `terminationGracePeriodSeconds: 40` |
| `PodDisruptionBudget` | Pelo menos 2 réplicas durante manutenções |
| `HorizontalPodAutoscaler` | De 3 a 10 réplicas por CPU |
| Configuração | `ConfigMap` para o que não é segredo; `Secret` criado fora do repositório |

## Alternativas consideradas

- **Helm:** mais flexível para vários ambientes, mas um chart acrescenta templates e valores para uma única aplicação; o Kustomize resolve com YAML puro.
- **Migração como `initContainer` da API:** rodaria uma vez por réplica, com a mesma disputa da migração na inicialização (ADR 0005).
- **Liveness apontando para `/health/ready`:** uma instabilidade do banco reiniciaria todas as réplicas ao mesmo tempo, piorando o incidente.

## Consequências

- **O `preStop` de 5 s** dá tempo para o Service tirar o pod dos endpoints antes do SIGTERM, evitando requisições enviadas a um pod que já está parando.
- **O prazo de 40 s** cobre o `preStop` e os 30 s padrão de `HostOptions.ShutdownTimeout`, para as requisições em andamento terminarem.
- **Outbox e reserva de estoque** já funcionam com várias réplicas.
- **O rate limiting é por réplica** (ADR 0008): o limite efetivo cresce com o número de pods.
- **Os manifestos não foram aplicados num cluster real neste desafio.** Foram validados com `kubectl kustomize`, e a revisão manual inclui aplicá-los no Kubernetes do Docker Desktop.
- **Ficam como próximos passos:** as imagens publicadas num registry pelo CI, o PostgreSQL gerenciado e o Secret vindo de um cofre (External Secrets, Key Vault).
