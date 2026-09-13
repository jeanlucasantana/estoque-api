#!/usr/bin/env bash
# Verifica os 10 critérios de aceite do REQUISITOS.md contra a API rodando no Docker Compose.
# Uso, na raiz do repositório e com o ambiente no ar: ./scripts/aceite.sh
# Lê API_KEY do .env (ou da variável de ambiente) e usa http://localhost:8080 (ou BASE_URL).
set -uo pipefail

BASE="${BASE_URL:-http://localhost:8080}"
CHAVE="${API_KEY:-$(grep -E '^API_KEY=' .env | cut -d= -f2- | tr -d '\r')}"
CPF="52998224725"
EMAIL="maria@example.com"
falhas=0

ok()   { printf '  \033[32mOK\033[0m     %s\n' "$1"; }
erro() { printf '  \033[31mFALHA\033[0m  %s\n' "$1"; falhas=$((falhas + 1)); }
confere() { if [[ "$2" == "$3" ]]; then ok "$1 ($2)"; else erro "$1: esperado $3, obtido $2"; fi; }

status() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
api() { curl -s -H "X-Api-Key: $CHAVE" -H 'Content-Type: application/json' "$@"; }

criar_produto() { # $1: quantidade em estoque. Imprime o id.
  local sku="ACEITE$(date +%s)$RANDOM"
  api -X POST "$BASE/api/produtos" \
    -d "{\"nome\":\"Produto aceite $sku\",\"sku\":\"$sku\",\"preco\":10.00,\"custoUnitario\":1.00,\"quantidade\":$1}" \
    | sed -E 's/.*"id":([0-9]+).*/\1/'
}

estoque() { api "$BASE/api/produtos/$1" | sed -E 's/.*"quantidade":([0-9]+).*/\1/'; }

corpo_pedido() { # $1: produtoId, $2: quantidade, $3: CEP
  echo "{\"clienteNome\":\"Maria Souza\",\"clienteCpf\":\"$CPF\",\"clienteEmail\":\"$EMAIL\",\"cep\":\"$3\",\"itens\":[{\"produtoId\":$1,\"quantidade\":$2}]}"
}

pedido() { # Imprime o status HTTP e o tempo total em segundos, terminando em quebra de linha (para contar com grep).
  api -o /dev/null -w '%{http_code} %{time_total}\n' -X POST "$BASE/api/pedidos" -d "$(corpo_pedido "$1" "$2" "${3:-01001000}")"
}

echo "Critérios de aceite contra $BASE"

echo "1. Ambiente no ar"
confere "GET /health/ready" "$(status "$BASE/health/ready")" 200

echo "2. Autenticação"
confere "Sem chave de API" "$(status "$BASE/api/produtos")" 401
confere "Health check sem chave" "$(status "$BASE/health/live")" 200

echo "3. Quantidade inválida"
p=$(criar_produto 10)
read -r codigo _ <<< "$(pedido "$p" 0)"
confere "Item com quantidade zero" "$codigo" 400
confere "Estoque inalterado" "$(estoque "$p")" 10

echo "4. Concorrência: 30 pedidos simultâneos de 1 unidade para 10 em estoque"
p=$(criar_produto 10)
tmp=$(mktemp -d)
for i in $(seq 30); do pedido "$p" 1 > "$tmp/$i" & done
wait
confere "Respostas 201" "$(cat "$tmp"/* | grep -c '^201 ')" 10
confere "Respostas 409" "$(cat "$tmp"/* | grep -c '^409 ')" 20
confere "Estoque final" "$(estoque "$p")" 0
rm -rf "$tmp"

echo "5. Frete lento (CEP 99999000)"
p=$(criar_produto 10)
read -r codigo tempo <<< "$(pedido "$p" 1 99999000)"
confere "Status" "$codigo" 503
if awk -v t="$tempo" 'BEGIN { exit !(t >= 1.5 && t <= 3.5) }'; then ok "Respondeu em ${tempo}s"; else erro "Tempo fora de ~2s: ${tempo}s"; fi
confere "Estoque inalterado" "$(estoque "$p")" 10

echo "6. Frete com erro (CEP 99999999)"
p=$(criar_produto 10)
read -r codigo _ <<< "$(pedido "$p" 1 99999999)"
confere "Status" "$codigo" 503
confere "Estoque inalterado" "$(estoque "$p")" 10

echo "7. Cancelamento"
p=$(criar_produto 10)
id=$(api -X POST "$BASE/api/pedidos" -d "$(corpo_pedido "$p" 4 01001000)" | sed -E 's/^\{"id":([0-9]+).*/\1/')
confere "Estoque após o pedido" "$(estoque "$p")" 6
confere "Cancelar pedido novo" "$(status -H "X-Api-Key: $CHAVE" -X POST "$BASE/api/pedidos/$id/cancelamento")" 200
confere "Estoque devolvido" "$(estoque "$p")" 10
confere "Cancelar de novo" "$(status -H "X-Api-Key: $CHAVE" -X POST "$BASE/api/pedidos/$id/cancelamento")" 409

echo "8. Dados pessoais na listagem"
lista=$(api "$BASE/api/pedidos?pagina=1&tamanhoPagina=100")
if [[ "$lista" != *"$CPF"* && "$lista" != *"$EMAIL"* ]]; then ok "Listagem sem CPF e sem email"; else erro "Listagem expõe CPF ou email"; fi

echo "9. Injeção de SQL na busca"
confere "Status" "$(status -H "X-Api-Key: $CHAVE" "$BASE/api/produtos/busca?nome=%27%20OR%201%3D1%20--")" 200
if [[ "$(api "$BASE/api/produtos/busca?nome=%27%20OR%201%3D1%20--")" == *'"itens":[]'* ]]; then ok "Nenhum produto retornado"; else erro "A busca retornou produtos"; fi

echo "10. Paginação"
confere "tamanhoPagina=1000" "$(status -H "X-Api-Key: $CHAVE" "$BASE/api/produtos?tamanhoPagina=1000")" 400

echo
if [[ $falhas -eq 0 ]]; then echo "Todos os critérios passaram."; else echo "$falhas verificação(ões) falharam."; fi
exit "$falhas"
