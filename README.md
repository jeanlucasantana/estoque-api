# API de Estoque e Pedidos (.NET 10)

Modernização da API legada de estoque e pedidos da Distribuidora Andorinha, de .NET 6 para .NET 10, conforme o `REQUISITOS.md` do desafio. Os problemas encontrados no legado e como cada um foi tratado estão em [DIAGNOSTICO.md](DIAGNOSTICO.md).

**Stack:** .NET 10 e C# 14, ASP.NET Core Minimal APIs, EF Core 10 com PostgreSQL 17, xUnit v3 com Testcontainers, Docker Compose e GitHub Actions.

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

# Criar um pedido (use o id devolvido na criação do produto)
curl -X POST http://localhost:8080/api/pedidos \
  -H "X-Api-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"clienteNome":"Maria Souza","clienteCpf":"52998224725","clienteEmail":"maria@example.com","cep":"01001000","itens":[{"produtoId":1,"quantidade":100}]}'

# Listar pedidos pagos
curl "http://localhost:8080/api/pedidos?pagina=1&tamanhoPagina=20&status=Pago" -H "X-Api-Key: $API_KEY"
```

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

---

## Como as migrações são aplicadas

A API **nunca** altera o esquema do banco na inicialização. Com várias réplicas, cada uma tentaria migrar ao mesmo tempo, e uma falha de migração derrubaria todas.

- O `Dockerfile` tem um estágio `migrations` com o **bundle de migrações do EF Core**: um executável que aplica as migrações pendentes e termina.
- No Compose, o serviço `migrations` roda esse bundle depois que o PostgreSQL fica saudável. A `api` declara `depends_on` com `condition: service_completed_successfully`, então só sobe se a migração terminar sem erro.
- Em produção, a mesma imagem roda como uma etapa do deploy (por exemplo, um Job no Kubernetes ou um passo do pipeline) antes da nova versão da API.

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
| `Estoque.IntegrationTests` (70 testes) | Endpoints de produtos e pedidos contra PostgreSQL real: autenticação, validação e limites de valores, SKU duplicado, busca com injeção de SQL, paginação, frete lento, com erro e com resposta inválida, **30 pedidos simultâneos para 10 unidades**, cancelamentos simultâneos, `PUT` que perde a corrida para uma reserva, dados pessoais nas respostas **e nos logs**, confirmação em segundo plano, banco indisponível e geração do documento OpenAPI |

**Determinismo:**
- **Relógio:** os testes de integração usam um `FakeTimeProvider` fixo, e o domínio recebe o instante como parâmetro.
- **Serviço de frete:** substituído por um `HttpMessageHandler` falso, que reproduz o contrato e os CEPs especiais do simulador. O `HttpClient` real e o timeout real de 2 s continuam em uso.
- **Ordem de execução:** cada teste cria os próprios dados, com SKUs únicos.
- **Cultura:** os testes forçam `pt-BR`, que usa vírgula decimal, para pegar qualquer formatação dependente de cultura na chamada de frete.

### Roteiro dos critérios de aceite

Com o ambiente do Compose no ar, o script abaixo verifica os 10 critérios de aceite do `REQUISITOS.md` contra a API real. Precisa de `bash` e `curl`; no Windows, rode no Git Bash.

```bash
./scripts/aceite.sh
```

---

## Configuração

Toda configuração obrigatória é validada na inicialização (`ValidateOnStart`). A API **não sobe** se faltar algum valor ou se algum for inválido.

| Variável de ambiente | Obrigatória | Descrição |
| :--- | :--- | :--- |
| `ConnectionStrings__Estoque` | Sim | Connection string do PostgreSQL |
| `Api__Chave` | Sim, com no mínimo 32 caracteres | Valor esperado no cabeçalho `X-Api-Key` |
| `Frete__UrlBase` | Sim, URL http ou https | Endereço base do serviço de frete |
| `ASPNETCORE_ENVIRONMENT` | Não | `Production` no Compose; `Development` publica o OpenAPI |

- **Segredos:** nenhum segredo é versionado. No Compose os valores vêm do `.env`, fora do Git. Em produção devem vir de um cofre de segredos, como Key Vault, Secrets Manager ou Kubernetes Secrets.
- **Chave do legado:** está comprometida e deve ser revogada. Ela não funciona na nova versão.
- **Precedência:** o `__` separa seções aninhadas nas variáveis de ambiente (`Api__Chave` equivale a `Api:Chave`). A ordem padrão é `appsettings.json` < `appsettings.{Ambiente}.json` < user secrets (só em Development) < variáveis de ambiente < argumentos de linha de comando.

---

## Organização do código

```
src/Estoque.Api/
  Domain/              Regras de negócio puras: valores do pedido, CPF, ciclo de vida
  Data/                DbContext, mapeamento e migrações
  Features/Produtos/   Endpoints e contratos de produtos
  Features/Pedidos/    Endpoints, contratos, cliente de frete e confirmação em segundo plano
  Common/              Chave de API, tratamento de erros, paginação e configurações
tests/
  Estoque.UnitTests/           Regras de domínio, sem banco
  Estoque.IntegrationTests/    Endpoints contra PostgreSQL real
scripts/aceite.sh              Critérios de aceite contra o Compose
frete_fake/                    Mapeamentos do simulador de frete
```

**Por que assim:**
- **Um projeto de API, organizado por funcionalidade.** O domínio é pequeno. Separar em projetos de Application, Domain e Infrastructure acrescentaria cerimônia sem resolver nenhum problema real. Cada funcionalidade concentra seus endpoints e contratos, e a leitura segue o fluxo da requisição.
- **Domínio sem dependências.** Cálculo, CPF e transições de estado não conhecem HTTP nem banco, por isso têm testes unitários rápidos e determinísticos.
- **Sem repositório genérico sobre o EF Core.** O `DbContext` já é unidade de trabalho e repositório. Uma camada a mais esconderia justamente o que importa aqui: o `ExecuteUpdate` condicional, as transações e o controle de concorrência.
- **Minimal APIs com `TypedResults`.** As respostas possíveis de cada endpoint ficam explícitas na assinatura e alimentam o documento OpenAPI.

---

## Decisões e trade-offs

| Decisão | Por quê | Custo ou alternativa descartada |
| :--- | :--- | :--- |
| **Reserva de estoque com `UPDATE` condicional atômico** (`quantidade = quantidade - q WHERE id = @id AND ativo AND quantidade >= q`), um por produto, em ordem crescente de id, numa transação | Conferir e baixar no mesmo comando: o PostgreSQL trava a linha e reavalia o `WHERE`. Funciona com qualquer número de réplicas e nunca precisa de nova tentativa. A ordem fixa evita deadlock. `CHECK (quantidade >= 0)` no banco é a última defesa | A regra fica em SQL, fora do change tracker. Descartados: concorrência otimista com retry (tentativas em cascata sob disputa) e `SELECT ... FOR UPDATE` (mesmo efeito, com uma ida a mais ao banco) |
| **`xmin` como token de concorrência** nas transições do pedido e no `PUT` de produto | Conflitos são raros nesses casos. Detectar na gravação não trava nada durante o processamento. Dois cancelamentos simultâneos devolvem o estoque uma única vez | O cliente recebe 409 e precisa consultar e repetir |
| **Frete consultado antes da transação**, com timeout de 2 s e sem retry | Nenhuma trava fica presa durante uma chamada externa. Um retry estouraria o limite de 2 s da RN05 | O preço gravado é o lido antes da consulta de frete. O estoque continua garantido pelo `UPDATE` condicional |
| **`HttpClient` tipado via `IHttpClientFactory`** | Reaproveita conexões (o legado criava um `HttpClient` por chamada) e renova handlers periodicamente | Sem circuit breaker (ver "O que eu faria com mais tempo") |
| **Validação nativa do .NET 10 (`AddValidation`)** com Data Annotations e `IValidatableObject` | Sem dependência externa de validação | As regras do `IValidatableObject` só rodam depois que os atributos passam: com erros dos dois tipos, o cliente os recebe em duas rodadas |
| **Paginação com parâmetros comuns e `[Range]`**, e não com `[AsParameters]` | Com `[AsParameters]` e atributos de validação, a geração do documento OpenAPI do .NET 10 falha com `InvalidCastException`. Um teste de integração garante que o documento é gerado | Os dois parâmetros se repetem nas três rotas paginadas |
| **Chave de API em middleware**, e não em filtro de endpoint | Responde 401 antes de binding e validação, inclusive em rotas inexistentes sob `/api`. A comparação usa `CryptographicOperations.FixedTimeEquals` sobre hashes SHA-256, resistente a ataque de tempo | Uma chave única para todos os clientes, como no legado. Autenticação por cliente fica fora do escopo |
| **Migrações em um serviço separado**, com o bundle do EF Core | A API nunca altera o esquema; a migração é uma etapa explícita do deploy | Um serviço a mais no Compose |
| **Imagem `aspnet:10.0-noble-chiseled-extra`**, usuário não root, porta 8080 | Sem shell, sem gerenciador de pacotes e com superfície de ataque menor (imagem final de cerca de 250 MB). A variante `extra` inclui tzdata e ICU, sem os quais o fuso `America/Sao_Paulo` não existe | Sem shell dentro do container para depurar; o diagnóstico é por logs e health checks |
| **Frete falso no `HttpMessageHandler`** em vez de WireMock.Net nos testes | Sem dependência extra e sem rede, exercitando o `HttpClient` real com seu timeout | Os mapeamentos do simulador são reproduzidos em código |
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

---

## Confirmação de pedidos e reinícios

Depois do commit do pedido, o id é colocado numa fila em memória (`Channel`), e a resposta sai sem esperar. Um `BackgroundService` consome a fila, cria o próprio escopo de injeção de dependência para acessar o banco e registra o envio no log, sem dados pessoais.

**O que acontece num reinício:** as confirmações que ainda estavam na fila **se perdem**. Os pedidos continuam gravados, mas os clientes não recebem a confirmação, e nada registra essa falha.

**Como eu resolveria em produção:** com um **outbox transacional**.
1. Na **mesma transação** do pedido, gravar uma linha numa tabela `confirmacoes_pendentes`. Se o pedido existe, a confirmação pendente também existe.
2. Um worker lê as pendentes com `SELECT ... FOR UPDATE SKIP LOCKED`. Assim, várias réplicas processam ao mesmo tempo sem pegar a mesma linha.
3. O worker envia a confirmação, marca como enviada e tenta de novo com espera crescente em caso de falha. Um identificador por mensagem permite ao provedor de email descartar duplicatas.

---

## Observabilidade e operação

- **Logs estruturados**, sem interpolação de strings. Fora de Development saem em JSON, com `TraceId` e `SpanId` em cada linha. CPF, email, nome do cliente, custo e chaves nunca são registrados.
- **Erros** seguem ProblemDetails e sempre trazem `traceId`, o mesmo que aparece nos logs: um erro reportado pelo cliente pode ser localizado. Erros inesperados respondem 500 sem detalhes internos.
- **Banco inacessível:** as rotas da API respondem **503** (indisponibilidade temporária) em vez de 500, e o `/health/ready` também responde 503.
- **Health checks:** `/health/live` confirma só que o processo responde (para liveness); `/health/ready` confirma também o acesso ao banco (para readiness). Nenhum dos dois exige chave.
- **CI** ([.github/workflows/ci.yml](.github/workflows/ci.yml)), a cada push e pull request: restore, verificação de formatação, build em Release com avisos como erros, todos os testes (com PostgreSQL via Testcontainers) e build das imagens da API e da migração.
- **Qualidade de build:** nullable habilitado, avisos do compilador e do MSBuild tratados como erros e auditoria do NuGet, que quebra o build diante de pacote com vulnerabilidade conhecida. Nenhum aviso foi suprimido no código escrito à mão; os únicos `#pragma` são os que o próprio EF Core gera nos arquivos de migração.

---

## Diferenciais

**Implementados:**
- **Central Package Management** (`Directory.Packages.props`), com pinning de dependências transitivas, e solução no formato **`.slnx`**. Custo quase zero, e as versões ficam num lugar só.

**Não implementados**, para priorizar os requisitos obrigatórios com qualidade: OpenTelemetry, `Idempotency-Key`, rate limiting, HybridCache, outbox, manifestos de Kubernetes e ADRs. As decisões ficaram registradas neste README.

---

## O que eu faria com mais tempo

Em ordem de prioridade:

1. **Outbox transacional** para as confirmações, conforme descrito acima.
2. **`Idempotency-Key` na criação de pedidos:** hoje, um cliente que repete a requisição por timeout de rede cria um pedido duplicado.
3. **Circuit breaker no cliente de frete**, para responder 503 na hora quando o serviço estiver fora do ar, em vez de esperar 2 s a cada pedido.
4. **OpenTelemetry** com traces e métricas (latência do frete, pedidos recusados por estoque).
5. **Rate limiting** por chave de API.
6. **HybridCache** na consulta de produto, com invalidação no `PUT` e no `DELETE`.
7. **Validação que devolva todos os erros de uma vez**, juntando atributos e regras entre campos.
8. **Teste de integração da promoção de sexta**, com um relógio próprio por teste.
9. **Manifestos de Kubernetes** com probes, limites de recursos, desligamento gracioso e um Job para a migração.
10. **Varredura de vulnerabilidades da imagem no CI** e publicação da imagem num registry.

---

## Uso de ferramentas de IA

Usei o **Claude (Anthropic), pelo Claude Code**, como par de programação durante todo o desafio:

- **Diagnóstico:** fiz primeiro uma leitura própria do legado e depois comparei com a análise feita com a IA, que virou o `DIAGNOSTICO.md`.
- **Decisões:** as escolhas de arquitetura e os trade-offs foram discutidos com a IA, e as que adotei estão registradas neste README.
- **Código:** a maior parte do código, dos testes, dos arquivos de container, do pipeline e deste README foi gerada com a IA, a partir dessas decisões.
- **Validação:** executei tudo localmente (testes, Docker Compose e o roteiro dos critérios de aceite) e no CI. Os problemas encontrados no caminho foram corrigidos e estão no histórico de commits.
- **Estudo:** estudei cada parte do código, com foco nas mais críticas (reserva de estoque, concorrência, fuso horário e testes), para conseguir explicar e alterar qualquer trecho.
