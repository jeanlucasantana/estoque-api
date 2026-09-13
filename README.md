# API de Estoque e Pedidos (.NET 10)

Modernização da API legada de estoque e pedidos da Distribuidora Andorinha, de .NET 6 para .NET 10, conforme o `REQUISITOS.md` do desafio. Os problemas encontrados no legado e como cada um foi tratado estão em [DIAGNOSTICO.md](DIAGNOSTICO.md). As decisões de arquitetura estão registradas em [docs/adr](docs/adr/README.md).

**Stack:** .NET 10 e C# 14, ASP.NET Core Minimal APIs, EF Core 10 com PostgreSQL 17, xUnit v3 com Testcontainers, OpenTelemetry, Docker Compose, Kubernetes (Kustomize) e GitHub Actions.

## Sumário

1. [Como executar](#como-executar)
2. [Como as migrações são aplicadas](#como-as-migrações-são-aplicadas)
3. [Como rodar os testes](#como-rodar-os-testes)
4. [Configuração](#configuração)
5. [Organização do código](#organização-do-código)
6. [Decisões e trade-offs](#decisões-e-trade-offs)
7. [Premissas](#premissas)
8. [Quebras de contrato em relação ao legado](#quebras-de-contrato-em-relação-ao-legado)
9. [Confirmação de pedidos e reinícios](#confirmação-de-pedidos-e-reinícios)
10. [Observabilidade e operação](#observabilidade-e-operação)
11. [Diferenciais](#diferenciais)
12. [O que eu faria com mais tempo](#o-que-eu-faria-com-mais-tempo)
13. [Uso de ferramentas de IA](#uso-de-ferramentas-de-ia)

---

## Como executar

**Pré-requisito:** Docker com Docker Compose. O SDK do .NET só é necessário para rodar os testes.

1. Copie o arquivo de variáveis. Os valores de exemplo funcionam para uso local.

   ```bash
   cp .env.example .env
   ```

   No PowerShell: `Copy-Item .env.example .env`

2. Suba o ambiente:

   ```bash
   docker compose up --build
   ```

   | Serviço | O que faz | Porta no host |
   | :--- | :--- | :--- |
   | `postgres` | PostgreSQL 17 com volume e health check | não exposta |
   | `migrations` | Aplica as migrações e termina | não exposta |
   | `frete` | Simulador WireMock do serviço de frete | 8081 |
   | `api` | A API, que só sobe depois que as migrações terminam com sucesso | 8080 |
   | `aspire-dashboard` | **Opcional** (profile `observabilidade`): painel de traces, métricas e logs | 18888 |

3. Verifique que o ambiente está pronto:

   ```bash
   curl http://localhost:8080/health/ready
   ```

   Resposta esperada: `200 Healthy`.

### Exemplos de uso

Todas as rotas sob `/api` exigem o cabeçalho `X-Api-Key` com o valor de `API_KEY` do `.env`. O banco começa vazio.

```bash
export API_KEY=$(grep ^API_KEY= .env | cut -d= -f2)

# Criar um produto
curl -X POST http://localhost:8080/api/produtos \
  -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"nome":"Parafuso sextavado 10mm","sku":"PAR010","preco":0.35,"custoUnitario":0.12,"quantidade":5000}'

# Criar um pedido (use o id devolvido na criação do produto).
# O Idempotency-Key é opcional: repetir a mesma requisição com a mesma chave devolve o mesmo pedido.
curl -X POST http://localhost:8080/api/pedidos \
  -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" -H "Idempotency-Key: 3f6c1a52-9d1e-4a4b-8f2e-6b1d2c3a4e5f" \
  -d '{"clienteNome":"Maria Souza","clienteCpf":"52998224725","clienteEmail":"maria@example.com","cep":"01001000","itens":[{"produtoId":1,"quantidade":100}]}'

# Listar pedidos pagos
curl "http://localhost:8080/api/pedidos?pagina=1&tamanhoPagina=20&status=Pago" -H "X-Api-Key: $API_KEY"
```

### Observabilidade (opcional)

Para ver traces, métricas e logs no Aspire Dashboard:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889 docker compose --profile observabilidade up --build
```

O painel fica em `http://localhost:18888`. Sem essa variável, a API não exporta telemetria.

### Documento OpenAPI

O documento OpenAPI só é publicado em ambiente de desenvolvimento. Para subir a API em Development:

```bash
ASPNETCORE_ENVIRONMENT=Development docker compose up --build
```

No PowerShell: `$env:ASPNETCORE_ENVIRONMENT="Development"; docker compose up --build`

O documento fica em `http://localhost:8080/openapi/v1.json`.

### Parar o ambiente

```bash
docker compose down      # mantém os dados do banco
docker compose down -v   # apaga também o volume do banco
```

### Kubernetes

Os manifestos ficam em [deploy/k8s](deploy/k8s), para aplicar com Kustomize: Job de migração, Deployment com probes, recursos e desligamento gracioso, Service, PodDisruptionBudget e HorizontalPodAutoscaler. O Secret não é versionado; o [secret.example.yaml](deploy/k8s/secret.example.yaml) mostra como criá-lo. Detalhes e trade-offs no [ADR 0012](docs/adr/0012-manifestos-de-kubernetes.md).

```bash
kubectl kustomize deploy/k8s     # monta os manifestos sem aplicar
kubectl apply -k deploy/k8s      # aplica (depois de criar o Secret e rodar o Job de migração)
```

---

## Como as migrações são aplicadas

A API **nunca** altera o esquema do banco na inicialização. Com várias réplicas, cada uma tentaria migrar ao mesmo tempo, e uma falha de migração derrubaria todas.

- O `Dockerfile` tem um estágio `migrations` com o **bundle de migrações do EF Core**: um executável que aplica as migrações pendentes e termina.
- No Compose, o serviço `migrations` roda esse bundle depois que o PostgreSQL fica saudável. A `api` declara `depends_on` com `condition: service_completed_successfully`, então só sobe se a migração terminar sem erro.
- No Kubernetes, a mesma imagem roda como `Job` antes da nova versão da API.

Para criar uma nova migração durante o desenvolvimento:

```bash
dotnet tool restore
dotnet ef migrations add NomeDaMigracao --project src/Estoque.Api --output-dir Data/Migrations
dotnet format
```

O `dotnet format` normaliza as quebras de linha dos arquivos gerados. No Windows eles saem com CRLF, e a verificação de formatação do CI falharia.

---

## Como rodar os testes

**Pré-requisitos:** SDK do .NET 10 e Docker em execução. Os testes de integração sobem um PostgreSQL real em container; a primeira execução baixa a imagem `postgres:17-alpine`.

```bash
dotnet test --solution Estoque.slnx                 # todos os testes
dotnet test --project tests/Estoque.UnitTests       # só os unitários, sem Docker
```

| Projeto | O que cobre |
| :--- | :--- |
| `Estoque.UnitTests` (43 testes) | Arredondamento comercial, cálculo do pedido, promoção de sexta com bordas de fuso, validação de CPF e todas as transições de estado |
| `Estoque.IntegrationTests` (85 testes) | Endpoints contra PostgreSQL real: autenticação, validação e limites de valores, SKU duplicado, busca com injeção de SQL, paginação, frete lento, com erro e com resposta inválida, **30 pedidos simultâneos para 10 unidades**, cancelamentos simultâneos, `PUT` que perde a corrida para uma reserva, dados pessoais nas respostas e nos logs, banco indisponível, OpenAPI e os diferenciais: outbox (inclusive duas instâncias processando ao mesmo tempo), Idempotency-Key (inclusive requisições simultâneas), rate limiting, invalidação do cache e métricas |

**Determinismo:**
- **Relógio:** os testes de integração usam um `FakeTimeProvider` fixo, e o domínio recebe o instante como parâmetro.
- **Serviço de frete:** substituído por um `HttpMessageHandler` falso, que reproduz o contrato e os CEPs especiais do simulador. O `HttpClient` real e o timeout real de 2 s continuam em uso.
- **Ordem de execução:** cada teste cria os próprios dados, com SKUs únicos. Os testes do outbox, que sobem instâncias extras processando o mesmo banco, rodam sem paralelismo.
- **Cultura:** os testes forçam `pt-BR`, que usa vírgula decimal, para pegar qualquer formatação dependente de cultura na chamada de frete.

### Roteiro dos critérios de aceite

Com o ambiente do Compose no ar, o script abaixo verifica os 10 critérios de aceite do `REQUISITOS.md` contra a API real. Precisa de `bash` e `curl`; no Windows, rode no Git Bash.

```bash
./scripts/aceite.sh
```

---

## Configuração

Toda configuração é validada na inicialização (`ValidateOnStart`). A API **não sobe** se faltar algum valor obrigatório ou se algum for inválido.

| Variável de ambiente | Obrigatória | Descrição |
| :--- | :--- | :--- |
| `ConnectionStrings__Estoque` | Sim | Connection string do PostgreSQL |
| `Api__Chave` | Sim, com no mínimo 32 caracteres | Valor esperado no cabeçalho `X-Api-Key` |
| `Frete__UrlBase` | Sim, URL http ou https | Endereço base do serviço de frete |
| `ASPNETCORE_ENVIRONMENT` | Não | `Production` no Compose; `Development` publica o OpenAPI |
| `LimiteDeRequisicoes__PermissoesPorJanela` | Não (300) | Requisições permitidas por IP em cada janela |
| `LimiteDeRequisicoes__JanelaSegundos` | Não (10) | Duração da janela do rate limiting |
| `Confirmacoes__IntervaloSegundos` | Não (2) | Intervalo entre as buscas do outbox |
| `Confirmacoes__TamanhoDoLote` | Não (20) | Confirmações por lote |
| `Confirmacoes__Habilitado` | Não (`true`) | Desliga o processamento de confirmações nesta instância |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Não | Coletor OTLP; sem ele, a telemetria não é exportada |

- **Segredos:** nenhum segredo é versionado. No Compose os valores vêm do `.env`, fora do Git. Em produção devem vir de um cofre de segredos, como Key Vault, Secrets Manager ou Kubernetes Secrets.
- **Chave do legado:** está comprometida e deve ser revogada. Ela não funciona na nova versão.
- **Precedência:** o `__` separa seções aninhadas nas variáveis de ambiente (`Api__Chave` equivale a `Api:Chave`). A ordem padrão é `appsettings.json` < `appsettings.{Ambiente}.json` < user secrets (só em Development) < variáveis de ambiente < argumentos de linha de comando.

---

## Organização do código

```
src/Estoque.Api/
  Domain/              Regras de negócio puras: valores do pedido, CPF, ciclo de vida
  Data/                DbContext, mapeamento e migrações
  Features/Produtos/   Endpoints, contratos e cache do detalhe de produto
  Features/Pedidos/    Endpoints, contratos, cliente de frete, outbox de confirmações e Idempotency-Key
  Common/              Chave de API, erros, paginação, rate limiting, telemetria e configurações
tests/
  Estoque.UnitTests/           Regras de domínio, sem banco
  Estoque.IntegrationTests/    Endpoints contra PostgreSQL real
deploy/k8s/                    Manifestos de Kubernetes (Kustomize)
docs/adr/                      Registros de decisão de arquitetura
scripts/aceite.sh              Critérios de aceite contra o Compose
frete_fake/                    Mapeamentos do simulador de frete
```

**Por que assim** ([ADR 0001](docs/adr/0001-organizacao-por-funcionalidade.md)):
- **Um projeto de API, organizado por funcionalidade.** O domínio é pequeno. Separar em projetos de Application, Domain e Infrastructure acrescentaria cerimônia sem resolver nenhum problema real. Cada funcionalidade concentra seus endpoints e contratos, e a leitura segue o fluxo da requisição.
- **Domínio sem dependências.** Cálculo, CPF e transições de estado não conhecem HTTP nem banco, por isso têm testes unitários rápidos e determinísticos.
- **Sem repositório genérico sobre o EF Core.** O `DbContext` já é unidade de trabalho e repositório. Uma camada a mais esconderia justamente o que importa aqui: o `ExecuteUpdate` condicional, as transações, o controle de concorrência e o `FOR UPDATE SKIP LOCKED`.
- **Minimal APIs com `TypedResults`.** As respostas possíveis de cada endpoint ficam explícitas na assinatura e alimentam o documento OpenAPI.

---

## Decisões e trade-offs

As decisões principais têm um ADR em [docs/adr](docs/adr/README.md), com contexto, alternativas e consequências.

| Decisão | Por quê | Custo ou alternativa descartada |
| :--- | :--- | :--- |
| **Reserva de estoque com `UPDATE` condicional atômico** (`quantidade = quantidade - q WHERE id = @id AND ativo AND quantidade >= q`), um por produto, em ordem crescente de id, numa transação ([ADR 0002](docs/adr/0002-reserva-de-estoque-com-update-condicional.md)) | Conferir e baixar no mesmo comando: o PostgreSQL trava a linha e reavalia o `WHERE`. Funciona com qualquer número de réplicas e nunca precisa de nova tentativa. A ordem fixa evita deadlock. `CHECK (quantidade >= 0)` no banco é a última defesa | A regra fica em SQL, fora do change tracker. Descartados: concorrência otimista com retry (tentativas em cascata sob disputa) e `SELECT ... FOR UPDATE` (mesmo efeito, com uma ida a mais ao banco) |
| **`xmin` como token de concorrência** nas transições do pedido e no `PUT` de produto ([ADR 0003](docs/adr/0003-concorrencia-otimista-com-xmin.md)) | Conflitos são raros nesses casos. Detectar na gravação não trava nada durante o processamento. Dois cancelamentos simultâneos devolvem o estoque uma única vez | O cliente recebe 409 e precisa consultar e repetir |
| **Frete consultado antes da transação**, com timeout de 2 s e sem retry ([ADR 0004](docs/adr/0004-frete-antes-da-transacao-sem-retry.md)) | Nenhuma trava fica presa durante uma chamada externa. Um retry estouraria o limite de 2 s da RN05 | O preço gravado é o lido antes da consulta de frete. O estoque continua garantido pelo `UPDATE` condicional |
| **Outbox transacional** para as confirmações ([ADR 0006](docs/adr/0006-outbox-transacional-para-confirmacoes.md)) | A confirmação pendente é gravada na mesma transação do pedido: um reinício não perde nada. `FOR UPDATE SKIP LOCKED` permite várias réplicas processando em paralelo | Entrega "pelo menos uma vez" (o provedor de email precisa deduplicar); uma consulta ao banco a cada intervalo, por réplica |
| **Idempotency-Key** na criação de pedidos ([ADR 0007](docs/adr/0007-idempotency-key-na-criacao-de-pedidos.md)) | O cliente pode repetir o `POST` com segurança. A chave é chave primária, gravada na transação do pedido: requisições simultâneas com a mesma chave criam um único pedido | Requisições simultâneas com a mesma chave consultam o frete e reservam antes de descobrir o conflito, e desfazem depois |
| **Rate limiting por IP antes da autenticação** ([ADR 0008](docs/adr/0008-rate-limiting-por-ip.md)) | Um cliente abusivo, com ou sem chave, recebe 429 sem afetar os demais | O limite é por instância; atrás de proxy, exige `ForwardedHeaders` configurado |
| **HybridCache no detalhe de produto** ([ADR 0010](docs/adr/0010-hybridcache-no-detalhe-de-produto.md)) | Leituras repetidas não vão ao banco. Toda escrita (inclusive reserva e cancelamento) invalida a entrada depois do commit, e a venda nunca lê do cache | Sem cache distribuído, outras réplicas podem mostrar o valor antigo por até 30 s |
| **OpenTelemetry** com exportação OTLP opcional ([ADR 0011](docs/adr/0011-opentelemetry.md)) | Traces do HTTP ao SQL, métricas de negócio e logs correlacionados, sem prender a aplicação a um fornecedor | Um pequeno custo por requisição; em produção, a amostragem seria ajustada ao volume |
| **`HttpClient` tipado via `IHttpClientFactory`** | Reaproveita conexões (o legado criava um `HttpClient` por chamada) e renova handlers periodicamente | Sem circuit breaker (ver "O que eu faria com mais tempo") |
| **Validação nativa do .NET 10 (`AddValidation`)** com Data Annotations e `IValidatableObject` | Sem dependência externa de validação | As regras do `IValidatableObject` só rodam depois que os atributos passam: com erros dos dois tipos, o cliente os recebe em duas rodadas |
| **Paginação com parâmetros comuns e `[Range]`**, e não com `[AsParameters]` | Com `[AsParameters]` e atributos de validação, a geração do documento OpenAPI do .NET 10 falha com `InvalidCastException`. Um teste de integração garante que o documento é gerado | Os dois parâmetros se repetem nas três rotas paginadas |
| **Chave de API em middleware**, e não em filtro de endpoint | Responde 401 antes de binding e validação, inclusive em rotas inexistentes sob `/api`. A comparação usa `CryptographicOperations.FixedTimeEquals` sobre hashes SHA-256, resistente a ataque de tempo | Uma chave única para todos os clientes, como no legado. Autenticação por cliente fica fora do escopo |
| **Migrações em um serviço separado**, com o bundle do EF Core ([ADR 0005](docs/adr/0005-migracoes-em-servico-separado.md)) | A API nunca altera o esquema; a migração é uma etapa explícita do deploy | Um serviço a mais no Compose e um Job no Kubernetes |
| **Imagem `aspnet:10.0-noble-chiseled-extra`**, usuário não root, porta 8080 | Sem shell, sem gerenciador de pacotes e com superfície de ataque menor. A variante `extra` inclui tzdata e ICU, sem os quais o fuso `America/Sao_Paulo` não existe | Sem shell dentro do container para depurar; o diagnóstico é por logs, traces e health checks |
| **Frete falso no `HttpMessageHandler`** em vez de WireMock.Net nos testes ([ADR 0009](docs/adr/0009-testes-com-postgresql-real.md)) | Sem dependência extra e sem rede, exercitando o `HttpClient` real com seu timeout | Os mapeamentos do simulador são reproduzidos em código |
| **SKU e preço gravados no item do pedido** | O histórico do pedido não muda se o produto for alterado depois | Um pouco de duplicação de dados, intencional |
| **Status do pedido gravado como texto** | Legível em consultas e imune a reordenação do enum | Ocupa mais espaço que um inteiro |

---

## Premissas

Pontos em que o `REQUISITOS.md` deixa margem de interpretação:

- **Promoção de sexta-feira:** vale o relógio do servidor no momento da criação, convertido para `America/Sao_Paulo`. Um lojista em outro fuso (por exemplo, em Manaus) segue o calendário de Brasília. O relógio do cliente nunca é usado, porque o cliente o controla.
- **Itens repetidos do mesmo produto:** são consolidados em um só item, somando as quantidades (RN02 permite consolidar ou rejeitar).
- **Limites de entrada**, para que nenhum valor estoure as colunas do banco nem o `int`. Violá-los resulta em 400:
  - até 100 itens por pedido, cada um com 1 a 1.000.000 unidades;
  - preço e custo de até 10.000.000,00;
  - estoque de até 1.000.000.000 unidades;
  - página de no máximo 1.000.000.

  Com esses limites, o maior subtotal possível fica abaixo de 10^15, e os totais do pedido usam `numeric(18,2)`.
- **CPF e CEP:** só dígitos, sem pontuação (11 e 8 dígitos).
- **SKU:** único entre todos os produtos, inclusive os inativos.
- **Preço e custo com mais de duas casas decimais:** são rejeitados com 400, não arredondados.
- **Frete:** um valor zero devolvido explicitamente pelo serviço é aceito. Resposta sem valor, com valor negativo, acima de 1.000.000,00 ou fora do contrato resulta em 503.
- **Produto inativo:** sai da listagem e da busca, mas `GET /api/produtos/{id}` continua respondendo (com `ativo: false`) para consulta do histórico. `PUT` em produto inativo é permitido. `DELETE` de um produto já inativo responde 204.
- **Busca de produtos:** paginada como a listagem, só com produtos ativos e sem diferenciar maiúsculas. `%` e `_` são tratados como texto literal.
- **Listagem de pedidos:** resumo com id, status, data, nome do cliente, quantidade de itens e total, do mais recente para o mais antigo. Não traz CPF, email nem itens; os itens estão no detalhe.
- **Transições e cancelamento:** respondem 200 com o pedido atualizado.
- **Idempotency-Key:** opcional, de 1 a 100 caracteres, com escopo global (há uma única chave de API). Só é registrada quando o pedido é criado; tentativas que falham podem ser repetidas com a mesma chave. A repetição devolve o estado **atual** do pedido.
- **Banco inicial vazio:** o legado criava três produtos de exemplo na inicialização; a nova versão não cria dados.

---

## Quebras de contrato em relação ao legado

| Aspecto | Legado (.NET 6) | Nova versão |
| :--- | :--- | :--- |
| Busca de produtos | `GET /api/Produtos/buscar?nome=`, lista completa | `GET /api/produtos/busca?nome=`, paginada |
| Cancelamento | `POST /api/Pedidos/{id}/cancelar` | `POST /api/pedidos/{id}/cancelamento` |
| Novas rotas | | `GET /api/pedidos/{id}`, `POST /api/pedidos/{id}/pagamento`, `POST /api/pedidos/{id}/envio`, `/health/live`, `/health/ready` |
| Listagens | Lista completa, sem paginação | `{ itens, pagina, tamanhoPagina, total }`; pedidos com filtro por `status` |
| Criação de produto e de pedido | 200 com a entidade inteira | 201 com cabeçalho `Location` e o contrato do `REQUISITOS.md` |
| Campos removidos das respostas | `custoUnitario`, `dataCadastro`, CPF e email do cliente | CPF só no detalhe e mascarado (`cpfFinal`) |
| Produto inexistente | 204 sem corpo | 404 |
| Exclusão de produto | Física, 200 sem corpo | Lógica, 204 (ou 404) |
| Estoque insuficiente | 400 com texto | 409 com a lista de produtos sem saldo |
| Formato de erro | Texto ou stack trace | ProblemDetails com `traceId` |
| Valores monetários | `double`, sem arredondamento | `decimal` com duas casas e arredondamento comercial |
| Status do pedido | Texto livre | `Novo`, `Pago`, `Enviado` ou `Cancelado` |
| Chave de API | `chave_super_secreta_producao_2023`, no `appsettings.json` | Nova chave via variável de ambiente; a antiga não é aceita |
| Documentação | Swagger em `/swagger`, em qualquer ambiente | OpenAPI em `/openapi/v1.json`, só em Development |
| Porta e CORS | Porta 80, CORS liberado para qualquer origem | Porta 8080, sem CORS |
| Volume de requisições | Sem limite | 429 com `Retry-After` acima do limite por IP |
| Criação de pedido repetida | Sempre cria outro pedido | Com `Idempotency-Key`, devolve o mesmo pedido |

---

## Confirmação de pedidos e reinícios

A RN08 é atendida com um **outbox transacional** ([ADR 0006](docs/adr/0006-outbox-transacional-para-confirmacoes.md)):

1. Na **mesma transação** do pedido, uma linha é gravada em `confirmacoes_pendentes`. Se o pedido existe, a confirmação pendente também existe, e a resposta sai sem esperar o envio.
2. O `ProcessadorDeConfirmacoes`, um `BackgroundService`, busca lotes a cada 2 segundos com `SELECT ... FOR UPDATE SKIP LOCKED`, envia (neste desafio, um log sem dados pessoais), marca `enviada_em` e faz commit.
3. Em caso de falha, soma a tentativa e agenda a próxima com espera crescente, até 5 minutos.

**O que acontece num reinício:** nada se perde. As confirmações pendentes estão no banco e são enviadas quando a aplicação volta, por ela ou por qualquer outra réplica. O teste `Confirmacao_gravada_por_uma_instancia_sem_processador_e_enviada_por_outra` prova esse cenário, e `Duas_instancias_processando_o_mesmo_outbox_enviam_cada_confirmacao_uma_unica_vez` prova o processamento concorrente sem duplicidade.

**Limites conhecidos:**
- A entrega é "pelo menos uma vez": se o processo cair depois de enviar e antes do commit, a confirmação sai de novo. O provedor de email real deduplicaria pelo id da confirmação.
- A primeira versão usava uma fila em memória (`Channel`), que perdia as confirmações num reinício. O outbox a substituiu.

---

## Observabilidade e operação

- **Logs estruturados**, sem interpolação de strings. Fora de Development saem em JSON, com `TraceId` e `SpanId` em cada linha. CPF, email, nome do cliente, custo e chaves nunca são registrados, e um teste verifica isso em todos os caminhos que geram log.
- **Erros** seguem ProblemDetails e sempre trazem `traceId`, o mesmo que aparece nos logs e nos traces. Erros inesperados respondem 500 sem detalhes internos.
- **OpenTelemetry** ([ADR 0011](docs/adr/0011-opentelemetry.md)):
  - **traces** de requisições HTTP, chamadas ao frete, comandos no PostgreSQL e lotes de confirmação;
  - **métricas de negócio:** `estoque.pedidos.criados`, `estoque.pedidos.recusados_por_estoque`, `estoque.frete.falhas` (com o motivo), `estoque.frete.duracao` e `estoque.confirmacoes.enviadas`;
  - **métricas de plataforma** de ASP.NET Core, HttpClient, Npgsql e runtime, e os **logs**.

  A exportação usa OTLP e só é ligada com `OTEL_EXPORTER_OTLP_ENDPOINT`.
- **Rate limiting:** 429 em ProblemDetails, com `Retry-After`, acima de 300 requisições a cada 10 segundos por IP, só em `/api`.
- **Banco inacessível:** as rotas da API respondem **503** (indisponibilidade temporária) em vez de 500, e o `/health/ready` também responde 503.
- **Health checks:** `/health/live` confirma só que o processo responde (para liveness); `/health/ready` confirma também o acesso ao banco (para readiness). Nenhum dos dois exige chave nem entra no rate limiting.
- **Kubernetes:** probes, recursos, `preStop` e prazo de término, PodDisruptionBudget e HPA em [deploy/k8s](deploy/k8s) ([ADR 0012](docs/adr/0012-manifestos-de-kubernetes.md)).
- **CI** ([.github/workflows/ci.yml](.github/workflows/ci.yml)), a cada push e pull request: restore, verificação de formatação, build em Release com avisos como erros, todos os testes (com PostgreSQL via Testcontainers) e build das imagens da API e da migração.
- **Qualidade de build:** nullable habilitado, avisos do compilador e do MSBuild tratados como erros e auditoria do NuGet, que quebra o build diante de pacote com vulnerabilidade conhecida. Nenhum aviso foi suprimido no código escrito à mão; os únicos `#pragma` são os que o próprio EF Core gera nos arquivos de migração.

---

## Diferenciais

Todos os diferenciais do `REQUISITOS.md` foram implementados, cada um com testes e um registro de decisão:

| Diferencial | Onde | Decisão |
| :--- | :--- | :--- |
| Instrumentação com OpenTelemetry (traces e métricas) | `Common/Telemetria.cs`, profile `observabilidade` no Compose | [ADR 0011](docs/adr/0011-opentelemetry.md) |
| `Idempotency-Key` na criação de pedidos | `Features/Pedidos/Idempotencia.cs` | [ADR 0007](docs/adr/0007-idempotency-key-na-criacao-de-pedidos.md) |
| Rate limiting | `Common/LimiteDeRequisicoes.cs` | [ADR 0008](docs/adr/0008-rate-limiting-por-ip.md) |
| HybridCache com invalidação correta | `Features/Produtos/CacheDeProdutos.cs` | [ADR 0010](docs/adr/0010-hybridcache-no-detalhe-de-produto.md) |
| Outbox transacional para a confirmação | `Features/Pedidos/ConfirmacaoDePedido.cs` | [ADR 0006](docs/adr/0006-outbox-transacional-para-confirmacoes.md) |
| Central Package Management e `.slnx` | `Directory.Packages.props`, `Estoque.slnx` | Versões num lugar só, com pinning das transitivas |
| Manifestos de Kubernetes | `deploy/k8s` | [ADR 0012](docs/adr/0012-manifestos-de-kubernetes.md) |
| ADRs | `docs/adr` | 12 registros |

**Ordem de implementação e por quê:** primeiro os que corrigem riscos reais da API (outbox, que resolvia a perda de confirmações admitida na versão anterior; Idempotency-Key, contra pedidos duplicados), depois proteção e desempenho (rate limiting, cache), por fim operação (telemetria, Kubernetes) e documentação (ADRs).

---

## O que eu faria com mais tempo

Em ordem de prioridade:

1. **Circuit breaker no cliente de frete**, para responder 503 na hora quando o serviço estiver fora do ar, em vez de esperar 2 s a cada pedido.
2. **Chave de API por cliente**, guardada como hash no banco: revogação individual, rate limiting e Idempotency-Key escopados por cliente.
3. **Rotinas de retenção** para as confirmações já enviadas e para as Idempotency-Keys antigas.
4. **Validação que devolva todos os erros de uma vez**, juntando atributos e regras entre campos.
5. **`ForwardedHeaders` configurado** com os proxies confiáveis, para o rate limiting enxergar o IP real atrás de um load balancer.
6. **Cache distribuído (Redis) como segundo nível** do HybridCache e rate limiting global entre réplicas.
7. **Teste de integração da promoção de sexta**, com um relógio próprio por teste.
8. **Publicação das imagens num registry pelo CI**, com varredura de vulnerabilidades, e os manifestos aplicados num cluster real.
9. **`LISTEN/NOTIFY` do PostgreSQL** no outbox, para reduzir a latência e as consultas ociosas.

---

## Uso de ferramentas de IA

Usei o **Claude (Anthropic), pelo Claude Code**, como par de programação durante todo o desafio:

- **Diagnóstico:** fiz primeiro uma leitura própria do legado e depois comparei com a análise feita com a IA, que virou o `DIAGNOSTICO.md`. A IA também executou o legado no Docker para reproduzir os problemas.
- **Decisões:** as escolhas de arquitetura e os trade-offs foram discutidos com a IA, e as que adotei estão registradas neste README e nos ADRs.
- **Código:** a maior parte do código, dos testes, dos arquivos de container, do pipeline, dos manifestos e desta documentação foi gerada com a IA, a partir dessas decisões. Os diferenciais foram implementados num branch separado e só entraram na `main` depois da minha revisão manual.
- **Validação:** executei tudo localmente (testes, Docker Compose, roteiro dos critérios de aceite e a revisão manual de cada diferencial) e no CI. Os problemas encontrados no caminho foram corrigidos e estão no histórico de commits.
- **Estudo:** estudei cada parte do código, com foco nas mais críticas (reserva de estoque, concorrência, fuso horário, outbox e testes), para conseguir explicar e alterar qualquer trecho.
