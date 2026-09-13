# Diagnóstico da API legada (.NET 6)

Problemas encontrados em `legado_net6/`, com severidade, impacto em produção e tratamento na nova versão.

**Severidades**

* **Crítica:** falha de segurança explorável, perda de dinheiro ou de dados, ou violação direta de uma regra central do negócio.
* **Alta:** derruba requisições ou o processo, corrompe dados ou expõe informação em um cenário comum.
* **Média:** comportamento incorreto com impacto limitado ou contornável.
* **Baixa:** qualidade, manutenção ou aderência ao contrato.

## Resumo

| # | Problema | Severidade |
| :--- | :--- | :--- |
| C1 | Injeção de SQL na busca de produtos | Crítica |
| C2 | `BinaryFormatter` no cache de produtos | Crítica |
| C3 | Venda acima do estoque sob concorrência | Crítica |
| C4 | Falha do serviço de frete vira frete zero | Crítica |
| C5 | Chave de API versionada, comparação insegura e autenticação que falha aberta | Crítica |
| C6 | Dados pessoais e custo nos logs | Crítica |
| C7 | Quantidade zero ou negativa aceita no pedido | Crítica |
| A1 | `async void` em endpoint e na confirmação | Alta |
| A2 | Bloqueio síncrono com `.Result` | Alta |
| A3 | `HttpClient` criado a cada chamada e sem timeout | Alta |
| A4 | Valor do frete formatado pela cultura do servidor | Alta |
| A5 | Valores monetários em `double` | Alta |
| A6 | Promoção de sexta-feira no fuso errado e aplicada sobre o frete | Alta |
| A7 | Cancelamento sem regras e sem devolução de estoque | Alta |
| A8 | Produto inexistente derruba o pedido e produto inativo é vendido | Alta |
| A9 | CPF, email e custo expostos nas respostas | Alta |
| A10 | Stack trace devolvido ao cliente | Alta |
| A11 | Entidade usada como contrato de entrada (overposting) | Alta |
| A12 | Exclusão física de produto | Alta |
| A13 | Banco criado pela API, sem migrações, em SQLite dentro do container | Alta |
| A14 | Imagem Docker de desenvolvimento usada em produção | Alta |
| A15 | Plataforma fora de suporte e pacote com vulnerabilidade conhecida | Alta |
| A16 | Avisos do compilador suprimidos e nullable desabilitado | Alta |
| M1 | Estoque insuficiente responde 400 sem identificar os produtos | Média |
| M2 | Produto inexistente responde 204 em vez de 404 | Média |
| M3 | N+1 e listagens sem paginação | Média |
| M4 | Produto sem validação e SKU sem unicidade | Média |
| M5 | CPF, email e CEP sem validação | Média |
| M6 | Swagger em qualquer ambiente e CORS aberto | Média |
| M7 | Datas em horário local do servidor | Média |
| M8 | Status do pedido como texto livre | Média |
| M9 | `SaveChanges` síncrono dentro de action assíncrona | Média |
| M10 | Sem health checks, logs sem estrutura e erros sem correlação | Média |
| M11 | Criação responde 200 em vez de 201 com `Location` | Média |
| B1 | Sem testes automatizados e sem pipeline | Baixa |
| B2 | Rotas fora do contrato especificado | Baixa |

---

## Crítica

### C1. Injeção de SQL na busca de produtos
**Onde:** `ProdutosController.Buscar`; o aviso `EF1000`, que alerta exatamente para isso, está suprimido no `.csproj`.
**Problema:** o termo de busca é interpolado direto no SQL de `FromSqlRaw`.
**Impacto:** `' OR 1=1 --` devolve todos os produtos, inclusive inativos. Com `UNION SELECT` é possível ler outras tabelas, como CPF e email da tabela de pedidos.
**Tratamento:** consulta LINQ com `EF.Functions.ILike`, que gera SQL parametrizado, escapando `%` e `_` do termo. Um teste de integração cobre o termo do critério de aceite 9.

### C2. `BinaryFormatter` no cache de produtos
**Onde:** `CacheHelper`; `SYSLIB0011` suprimido e `EnableUnsafeBinaryFormatterSerialization` ligado no `.csproj`.
**Problema:** desserialização com `BinaryFormatter` em um `Dictionary` estático.
**Impacto:** `BinaryFormatter` é um vetor conhecido de execução remota de código e foi removido no .NET 9, onde lança `PlatformNotSupportedException`, então o código nem roda no .NET 10. Além disso, o `Dictionary` não é thread-safe e pode corromper sob concorrência, não expira, nunca é invalidado (PUT e DELETE continuam servindo o produto antigo) e é local a cada réplica.
**Tratamento:** cache removido. A consulta vai direto ao banco com `AsNoTracking`. HybridCache com invalidação fica registrado como próximo passo no README.

### C3. Venda acima do estoque sob concorrência
**Onde:** `PedidosController.Criar`.
**Problema:** lê a quantidade, compara em memória e grava depois, sem transação nem controle de concorrência.
**Impacto:** requisições simultâneas leem o mesmo saldo, todas passam na conferência e cada uma grava o próprio "saldo menos um", sobrescrevendo as demais. Na reprodução, **30 pedidos simultâneos de 1 unidade para um produto com 10 foram todos aceitos, e o estoque terminou em 9**: vendeu 30 e baixou 1. Com várias réplicas, nenhum lock em memória resolve.
**Tratamento:** reserva com `UPDATE` condicional atômico (`quantidade = quantidade - q WHERE id = @id AND ativo AND quantidade >= q`) para cada item, dentro de uma transação e na ordem de `ProdutoId` para evitar deadlock. Há `CHECK (quantidade >= 0)` no banco como última defesa, e a resposta é 409 listando os produtos sem saldo. Um teste de integração dispara 30 pedidos simultâneos contra 10 unidades.

### C4. Falha do serviço de frete vira frete zero
**Onde:** `FreteService.Calcular`.
**Problema:** `catch` genérico que devolve `0`, sem log.
**Impacto:** qualquer instabilidade do serviço gera pedidos sem frete. É prejuízo silencioso, e ninguém fica sabendo.
**Tratamento:** falha, demora acima de 2 segundos ou resposta inválida resultam em 503. O pedido não é criado e nenhum estoque é reservado.

### C5. Chave de API versionada, comparação insegura e autenticação que falha aberta
**Onde:** `appsettings.json` e o middleware em `Program.cs`.
**Problema:** a chave de produção está no repositório (e dentro da imagem, por causa do `COPY . .`); a comparação usa `!=`; e, se `ApiKey` estiver ausente na configuração, uma requisição **sem** o cabeçalho é aceita, porque um `StringValues` vazio é considerado igual a `null`. O 401 também sai sem corpo.
**Impacto:** qualquer pessoa com acesso ao repositório ou à imagem chama a API de produção. A comparação com `!=` permite ataque de tempo. Um deploy com configuração faltando deixa a API aberta.
**Tratamento:** a chave vem de variável de ambiente (`.env` fora do Git) e é validada na inicialização. A comparação usa `CryptographicOperations.FixedTimeEquals` sobre hashes SHA-256, e o 401 sai em ProblemDetails. A chave antiga é tratada como comprometida e deve ser revogada.

### C6. Dados pessoais e custo nos logs
**Onde:** `PedidosController.Criar` (nome, CPF e email), `EnviarEmailConfirmacao` (email) e `ProdutosController.Criar` (custo unitário).
**Impacto:** viola a LGPD, porque logs são replicados para outras ferramentas, retidos por muito tempo e acessados por mais pessoas que o banco. O custo é informação comercial interna.
**Tratamento:** logs estruturados apenas com identificadores e contagens. Nenhum dado pessoal, chave ou custo.

### C7. Quantidade zero ou negativa aceita no pedido
**Onde:** `CriarPedidoRequest` e `PedidosController.Criar`, sem nenhuma validação.
**Impacto:** um item com quantidade `-10` **aumenta** o estoque (`Quantidade - (-10)`) e deixa o total do pedido negativo. Na reprodução, o estoque da trena foi de 3 para 13 e o pedido foi gravado com total -245. CPF, email e CEP inválidos também foram aceitos com 200. Lista de itens nula ou vazia gera 500.
**Tratamento:** validação de entrada responde 400 antes de qualquer acesso ao estoque. Itens repetidos do mesmo produto são consolidados.

## Alta

### A1. `async void` em endpoint e na confirmação
**Onde:** `ProdutosController.Remover` e `PedidosController.EnviarEmailConfirmacao`.
**Impacto:** o MVC não espera um `async void`, então responde antes de a operação terminar. Em `Remover`, se o `DbContext` for descartado no fim da requisição antes da exclusão, ela falha com `ObjectDisposedException`. É uma condição de corrida: na reprodução, com SQLite local, a exclusão terminou a tempo, mas com um banco em rede e sob carga nada garante isso. Exceção em `async void` não tem quem a observe e derruba o processo. A confirmação pode se perder sem registro.
**Tratamento:** endpoints `async Task`. A confirmação vai para uma fila em memória (`Channel`) consumida por um `BackgroundService`.

### A2. Bloqueio síncrono com `.Result`
**Onde:** `ProdutosController.Listar` e `FreteService.Calcular`.
**Impacto:** cada requisição bloqueia uma thread do pool enquanto espera. Com o frete demorando, a carga causa esgotamento do thread pool e a API inteira fica lenta.
**Tratamento:** assíncrono de ponta a ponta.

### A3. `HttpClient` criado a cada chamada e sem timeout
**Onde:** `FreteService.Calcular`.
**Impacto:** sob carga, esgota as portas locais (conexões presas em `TIME_WAIT`). Sem timeout, vale o padrão de 100 segundos, e a requisição do cliente fica presa.
**Tratamento:** cliente tipado via `IHttpClientFactory`, com timeout de 2 segundos e sem retry, porque um retry estouraria o limite.

### A4. Valor do frete formatado pela cultura do servidor
**Onde:** `FreteService.Calcular`, na concatenação `"&valor=" + valorPedido`.
**Impacto:** em um servidor em pt-BR, `324.9` vira `324,9`. O serviço responde 404 e, somado ao C4, o pedido sai com frete zero.
**Tratamento:** formatação com `CultureInfo.InvariantCulture` e duas casas decimais.

### A5. Valores monetários em `double`
**Onde:** `Produto.Preco`, `Produto.CustoUnitario`, `Pedido.Total` e `ItemPedido.PrecoUnitario`.
**Impacto:** `double` é ponto flutuante binário: somas acumulam erro e os totais saem com centavos errados. Na reprodução, um pedido de 3 × 0,35 foi gravado com total `1.0499999999999998`. Também não há regra de arredondamento.
**Tratamento:** `decimal` no código, `numeric(12,2)` no banco e arredondamento comercial (`MidpointRounding.AwayFromZero`).

### A6. Promoção de sexta-feira no fuso errado e aplicada sobre o frete
**Onde:** `PedidosController.Criar`.
**Problema:** usa `DateTime.Now`, que depende do fuso do servidor, e aplica 10% sobre o total já com frete.
**Impacto:** em um container em UTC, pedidos de quinta a partir das 21h em Brasília ganham desconto e os de sexta a partir das 21h não ganham. O frete recebe desconto, o que a regra proíbe. A regra também não é testável, porque depende do relógio real.
**Tratamento:** `TimeProvider` injetado, conversão para `America/Sao_Paulo` e desconto só sobre o subtotal. Testes unitários cobrem as bordas da meia-noite.

### A7. Cancelamento sem regras e sem devolução de estoque
**Onde:** `PedidosController.Cancelar`.
**Impacto:** o estoque reservado nunca volta. É possível cancelar pedido enviado e cancelar de novo um já cancelado. Um id inexistente gera `NullReferenceException` (500).
**Tratamento:** máquina de estados no domínio (RN06). A devolução de estoque acontece na mesma transação, com 404 para id inexistente e 409 para transição inválida.

### A8. Produto inexistente derruba o pedido e produto inativo é vendido
**Onde:** `PedidosController.Criar`.
**Impacto:** `produto.Quantidade` com produto nulo gera 500, e o campo `Ativo` nunca é verificado.
**Tratamento:** erro de validação 400 que identifica o produto inexistente ou inativo.

### A9. CPF, email e custo expostos nas respostas
**Onde:** `PedidosController.Listar` e `Criar` devolvem a entidade inteira; os endpoints de produto devolvem `CustoUnitario`.
**Impacto:** vazamento de dados pessoais (LGPD) e de informação comercial para qualquer consumidor da API.
**Tratamento:** respostas próprias, separadas das entidades. A listagem de pedidos não tem CPF nem email. O detalhe mostra só os dois últimos dígitos do CPF, e o custo nunca sai.

### A10. Stack trace devolvido ao cliente
**Onde:** `ProdutosController.Criar`, com `BadRequest(ex.ToString())`.
**Impacto:** expõe stack trace, SQL e nomes de tabelas e colunas, que servem de mapa para um atacante.
**Tratamento:** ProblemDetails padronizado com `IExceptionHandler`, sem detalhes internos e com `traceId` para correlacionar com os logs.

### A11. Entidade usada como contrato de entrada (overposting)
**Onde:** `ProdutosController.Criar` e `Atualizar` recebem `Produto`.
**Impacto:** o cliente define `Id`, `Ativo` e `DataCadastro`. O `Update` em entidade desconectada sobrescreve todas as colunas, e um PUT com id inexistente gera exceção 500.
**Tratamento:** requests próprios com validação e busca da entidade antes de alterar, respondendo 404 quando não existir.

### A12. Exclusão física de produto
**Onde:** `ProdutosController.Remover`.
**Impacto:** apaga o histórico dos pedidos que referenciam o produto. Com chave estrangeira, simplesmente falharia. A RN01 exige exclusão lógica.
**Tratamento:** `DELETE` inativa o produto e responde 204 (ou 404). O produto inativo sai da listagem padrão.

### A13. Banco criado pela API, sem migrações, em SQLite dentro do container
**Onde:** `Program.cs`, com `EnsureCreated` e o seed.
**Impacto:** não há evolução versionada do esquema. Os dados se perdem quando o container é recriado, e SQLite não serve para várias réplicas.
**Tratamento:** PostgreSQL com migrações versionadas do EF Core, aplicadas por um serviço separado no Compose. A API nunca migra o banco na inicialização.

### A14. Imagem Docker de desenvolvimento usada em produção
**Onde:** `Dockerfile`.
**Problema:** imagem do SDK em runtime, build `Debug`, `ASPNETCORE_ENVIRONMENT=Development`, execução como root, `COPY . .` antes do restore e nenhum `.dockerignore`.
**Impacto:** imagem grande e com compilador, o que aumenta a superfície de ataque; página de exceção de desenvolvimento ativa; um processo comprometido tem root no container; todo build refaz o restore; `bin/`, `obj/` e o banco local vão para dentro da imagem.
**Tratamento:** build em múltiplos estágios, imagem final só de runtime (chiseled), usuário não root, porta 8080, `Release`, restore em camada própria e `.dockerignore`.

### A15. Plataforma fora de suporte e pacote com vulnerabilidade conhecida
**Onde:** `Estoque.Api.csproj`.
**Impacto:** o .NET 6 não recebe correções de segurança desde 12/11/2024. O `Newtonsoft.Json` 12.0.3 tem vulnerabilidade conhecida de negação de serviço (CVE-2024-21907, corrigida na 13.0.1), e o EF Core 6.0.0 está sem os patches da própria linha 6.0.
**Tratamento:** .NET 10 (LTS), `System.Text.Json` e auditoria do NuGet, que quebra o build diante de qualquer pacote vulnerável.

### A16. Avisos do compilador suprimidos e nullable desabilitado
**Onde:** `<NoWarn>CS1998;CS8618;SYSLIB0011;EF1000</NoWarn>` e `<Nullable>disable</Nullable>`.
**Impacto:** os avisos suprimidos apontavam exatamente para C1 e C2. Sem nullable, os `NullReferenceException` de A7 e A8 passam despercebidos.
**Tratamento:** nullable habilitado, avisos tratados como erros e nenhuma supressão.

## Média

### M1. Estoque insuficiente responde 400 sem identificar os produtos
**Onde:** `PedidosController.Criar`. **Impacto:** o cliente não sabe qual item ajustar, e o código não distingue entrada inválida de conflito de estado. **Tratamento:** 409 com a lista dos produtos sem saldo.

### M2. Produto inexistente responde 204 em vez de 404
**Onde:** `ProdutosController.Obter`, com `return Ok(null)`. O formatador padrão do MVC transforma o corpo `null` em **204 No Content**, confirmado na reprodução. **Impacto:** o cliente recebe um status de sucesso para um recurso que não existe e não distingue "não encontrado" de "sem conteúdo". **Tratamento:** 404 em ProblemDetails.

### M3. N+1 e listagens sem paginação
**Onde:** `PedidosController.Listar` faz uma consulta de itens por pedido; `ProdutosController.Listar` carrega a tabela inteira e filtra em memória. **Impacto:** tempo e memória crescem com o volume de dados até derrubar a API. **Tratamento:** paginação obrigatória (1 a 100 por página), filtro no banco e projeção só dos campos da resposta.

### M4. Produto sem validação e SKU sem unicidade
**Onde:** `Produto` e `ProdutosController`. **Impacto:** nomes vazios, preços negativos e SKUs duplicados. **Tratamento:** validação conforme RN01, índice único de SKU e 409 para SKU duplicado.

### M5. CPF, email e CEP sem validação
**Onde:** `CriarPedidoRequest`. **Impacto:** pedidos com dados inválidos, e o frete é chamado com CEP malformado. **Tratamento:** CPF com dígitos verificadores, email em formato válido e CEP com 8 dígitos.

### M6. Swagger em qualquer ambiente e CORS aberto
**Onde:** `Program.cs`. **Impacto:** documenta a superfície da API para qualquer um em produção e libera chamadas de navegador vindas de qualquer origem. **Tratamento:** OpenAPI só em Development. CORS removido, porque a API não é consumida por navegador.

### M7. Datas em horário local do servidor
**Onde:** `Pedido.DataPedido` e `Produto.DataCadastro`, com `DateTime.Now`. **Impacto:** datas ambíguas que mudam de sentido conforme o fuso do servidor. **Tratamento:** `DateTimeOffset` em UTC obtido via `TimeProvider`.

### M8. Status do pedido como texto livre
**Onde:** `Pedido.Status`. **Impacto:** erros de digitação viram estados válidos, e não há regra de transição. **Tratamento:** `enum StatusPedido` com transições encapsuladas na entidade.

### M9. `SaveChanges` síncrono dentro de action assíncrona
**Onde:** `PedidosController.Criar`. **Impacto:** bloqueia a thread durante a escrita no banco. **Tratamento:** `SaveChangesAsync` com `CancellationToken`.

### M10. Sem health checks, logs sem estrutura e erros sem correlação
**Onde:** toda a aplicação. **Impacto:** o orquestrador não sabe se a instância está saudável, e logs com interpolação não podem ser filtrados por campo. Um erro reportado por um cliente não pode ser encontrado nos logs. **Tratamento:** `/health/live` e `/health/ready` (com checagem do banco), logs estruturados e `traceId` em toda resposta de erro.

### M11. Criação responde 200 em vez de 201 com `Location`
**Onde:** `ProdutosController.Criar` e `PedidosController.Criar`. **Impacto:** o contrato HTTP não é seguido, e o cliente não recebe o endereço do recurso criado. **Tratamento:** 201 com cabeçalho `Location`.

## Baixa

### B1. Sem testes automatizados e sem pipeline
**Impacto:** toda mudança é validada manualmente em produção, e regressões só aparecem com o cliente. **Tratamento:** testes unitários e de integração com PostgreSQL real, e um pipeline no GitHub Actions a cada push e pull request.

### B2. Rotas fora do contrato especificado
**Onde:** `/api/Produtos/buscar` e `/api/Pedidos/{id}/cancelar`. **Impacto:** divergência em relação ao contrato do `REQUISITOS.md`. **Tratamento:** rotas conforme o contrato. As quebras em relação ao legado estão listadas no README.

---

## Itens tratados só em parte

Todos os problemas acima foram tratados. Em três deles a solução entregue resolve o defeito do legado, mas deixa uma limitação conhecida, registrada aqui e em "O que eu faria com mais tempo" no README:

| Item | O que foi feito | O que ficou de fora e por quê |
| :--- | :--- | :--- |
| **A1** (confirmação) | `async void` substituído por fila em memória + `BackgroundService`, sem dados pessoais no log | Confirmações ainda na fila se perdem se o processo reiniciar. A solução definitiva é um outbox transacional, que cabe numa entrega futura sem mudar o contrato da API |
| **C2** (cache) | `BinaryFormatter` e cache inseguro removidos | Não há cache substituto. O HybridCache com invalidação no PUT e no DELETE fica como próximo passo, quando houver medição de carga que justifique |
| **A3** (frete) | Cliente tipado, timeout de 2 s, falha vira 503 | Sem circuit breaker: com o serviço fora do ar, cada pedido ainda espera até 2 s antes do 503 |
