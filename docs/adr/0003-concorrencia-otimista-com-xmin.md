# ADR 0003: Concorrência otimista com `xmin` nas transições e no PUT de produto

**Status:** aceita

## Contexto

Duas situações podem sobrescrever dados em silêncio:
- **Dois cancelamentos simultâneos** do mesmo pedido leem o status `Novo` e devolvem o estoque duas vezes.
- **Um `PUT` de produto** baseado numa leitura antiga sobrescreve a quantidade reservada por um pedido feito no meio do caminho.

Nos dois casos, o conflito é raro.

## Decisão

Mapear a coluna de sistema `xmin` do PostgreSQL como token de concorrência (`IsRowVersion` numa propriedade de sombra) em `produtos` e `pedidos`. O EF inclui `AND xmin = @versaoLida` no `UPDATE`; se outra transação alterou a linha, o `SaveChanges` lança `DbUpdateConcurrencyException`, e a API responde 409.

No cancelamento, o status é gravado **antes** da devolução do estoque, na mesma transação: só um cancelamento passa desse ponto.

## Alternativas consideradas

- **Coluna de versão própria:** mesmo efeito, mas exige manutenção; o `xmin` o PostgreSQL já atualiza sozinho.
- **`SELECT ... FOR UPDATE`:** travaria a linha durante todo o processamento, para um conflito que quase nunca acontece.

## Consequências

- Nada fica travado durante o processamento, e o conflito é detectado na gravação.
- O cliente que perde a corrida recebe 409 e precisa consultar e repetir.
- Provado por `Cancelamentos_simultaneos_devolvem_o_estoque_uma_unica_vez` e `Put_que_perde_a_corrida_para_uma_reserva_retorna_409_e_nao_sobrescreve_o_estoque`.
