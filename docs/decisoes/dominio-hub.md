# Domínio do Hub

O Hub trata uma conversa como `Channel`. Um canal serial só permite um `Run` ativo, possui sequência monotônica de mensagens e limita automaticamente a cadeia a oito saltos por padrão.

`Message` é imutável e guarda raiz, resposta e causa para rastrear uma conversa. `Run` inicia em `accepted`, passa por `queued`, `leased` e `running`, e termina em sucesso, falha, cancelamento, timeout ou dead letter. Somente falhas transitórias entram em `retry_wait`.

O domínio não conhece HTTP, MCP, PostgreSQL nem Redis. Esses detalhes entram em adaptadores que preservam as invariantes acima.
