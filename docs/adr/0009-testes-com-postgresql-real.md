# ADR 0009: Testes de integração com PostgreSQL real e frete falso no HttpMessageHandler

**Status:** aceita

## Contexto

O REQUISITOS pede testes de integração contra um PostgreSQL real e testes determinísticos, que não dependam do relógio real, da ordem de execução ou de serviços externos. As regras mais importantes do sistema vivem no banco: índice único, `CHECK`, `UPDATE` condicional, transações, `xmin`, `ILIKE` e `FOR UPDATE SKIP LOCKED`.

## Decisão

- **Testcontainers** sobe um PostgreSQL 17 descartável por execução; as migrações são aplicadas no início.
- **`WebApplicationFactory`** sobe a API real em memória.
- **Frete falso** como `HttpMessageHandler`, reproduzindo o contrato e os CEPs especiais do simulador; o `ServicoFrete` e o `HttpClient` reais, com o timeout real, continuam em uso.
- **`FakeTimeProvider`** fixo e **cultura `pt-BR` forçada**.
- **Cada teste cria os próprios dados**, com SKUs únicos.

## Alternativas consideradas

- **Provedor InMemory do EF Core:** não é relacional (sem índice único, `CHECK`, transação, `xmin`, `ILIKE`); os testes passariam por motivos errados.
- **SQLite em memória:** relacional, mas com outro dialeto e outro comportamento de concorrência.
- **WireMock.Net nos testes:** mais uma dependência, sem ganho sobre o handler falso para este contrato simples.

## Consequências

- Os testes provam o comportamento que vai rodar em produção, inclusive concorrência.
- É preciso ter Docker para rodar os testes de integração, localmente e no CI.
- A suíte de integração leva cerca de um minuto.
