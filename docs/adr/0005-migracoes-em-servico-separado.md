# ADR 0005: Migrações aplicadas por um serviço separado, nunca na inicialização da API

**Status:** aceita

## Contexto

O REQUISITOS proíbe a API de aplicar migrações durante a inicialização em produção. O legado usava `EnsureCreated()` ao subir. Com várias réplicas, cada uma tentaria migrar ao mesmo tempo, a subida ficaria lenta, uma falha de migração derrubaria todas, e a aplicação precisaria de permissão para alterar o esquema.

## Decisão

- O `Dockerfile` tem um estágio `migrations` com o **bundle de migrações do EF Core** (`dotnet ef migrations bundle`), um executável que aplica o que falta e termina.
- No Compose, o serviço `migrations` roda depois que o PostgreSQL fica saudável, e a `api` depende dele com `condition: service_completed_successfully`.
- Em produção, a mesma imagem roda como etapa do deploy (Job no Kubernetes ou passo do pipeline), antes da nova versão da API.

## Alternativas consideradas

- **`Database.Migrate()` na inicialização:** simples, mas é exatamente o que o requisito proíbe, com os problemas descritos acima.
- **Script SQL gerado (`migrations script --idempotent`):** também válido; o bundle foi escolhido por ser autocontido e rodar como container.

## Consequências

- A migração é uma etapa explícita e observável do deploy, e a API só sobe com o esquema pronto.
- Mudanças de esquema precisam ser compatíveis com a versão anterior da aplicação (expand/contract), para permitir rollback.
- Um serviço a mais no Compose.
