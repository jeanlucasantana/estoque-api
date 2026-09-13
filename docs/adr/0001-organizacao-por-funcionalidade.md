# ADR 0001: Um projeto de API organizado por funcionalidade

**Status:** aceita

## Contexto

O legado era um único projeto com controllers, models e services misturados, sem testes. A nova versão precisa ser fácil de ler, testável e, segundo o ENUNCIADO, sem cerimônia que o problema não pede: "excesso de abstração sem justificativa conta contra". O domínio é pequeno: produtos, pedidos e frete.

## Decisão

Um único projeto `Estoque.Api`, organizado assim:
- `Domain/`: regras de negócio puras (cálculo do pedido, CPF, ciclo de vida), sem dependência de HTTP ou banco.
- `Data/`: `DbContext`, mapeamento e migrações.
- `Features/Produtos` e `Features/Pedidos`: endpoints (Minimal APIs) e contratos de cada funcionalidade.
- `Common/`: o que é transversal (chave de API, erros, paginação, limites, configurações).

Sem repositório genérico: os endpoints usam o `DbContext` diretamente.

## Alternativas consideradas

- **Clean Architecture em vários projetos** (Domain, Application, Infrastructure, Api): separação forte, mas quatro projetos e interfaces para um domínio deste tamanho seriam cerimônia.
- **Repositório genérico sobre o EF Core:** esconderia justamente o que importa aqui (`ExecuteUpdate` condicional, transações, `xmin`) e duplicaria o que o `DbContext` já oferece.

## Consequências

- A leitura segue o fluxo da requisição, e cada funcionalidade concentra o que precisa.
- O domínio tem testes unitários rápidos, sem banco.
- Se o sistema crescer (novos contextos, várias equipes), separar em projetos é um passo futuro natural, e a organização por funcionalidade facilita essa separação.
