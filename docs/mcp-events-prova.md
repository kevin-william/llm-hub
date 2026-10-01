# Prova MCP Events

O endpoint experimental `POST /mcp/events-probe` só é exposto no ambiente `Development`. Ele aceita JSON-RPC com os cabeçalhos `MCP-Protocol-Version: 2026-07-28` e `Mcp-Method` correspondente ao método enviado no corpo.

O catálogo contém somente `message.created`. A extensão de prova oferece `events/list`, `events/subscribe`, `events/unsubscribe` e `events/publish`. A inscrição exige callback HTTPS, envia um desafio antes de ativá-la e mantém em memória uma única inscrição ativa para o mesmo callback e segredo. A publicação manual gera um `eventId` estável e envia um corpo assinado com HMAC-SHA256; a assinatura tem o formato `v1,<hex>` sobre `timestamp.payload`.

A revisão MCP `2026-07-28` é sem sessão e usa `MCP-Protocol-Version` em cada chamada. Ela não padroniza as operações `events/list`, `events/subscribe` e `events/unsubscribe` usadas no plano original; portanto, esta prova as trata como uma extensão experimental do Hub, sem alegar compatibilidade automática com um host MCP. A interface de inscrições atual do MCP concentra notificações padrão em `subscriptions/listen`.

Os testes automatizados validam catálogo, cabeçalhos incompatíveis, desafio recusado, inscrição duplicada, desinscrição e assinatura válida ou adulterada. Falta a validação externa: publicar o endpoint em HTTPS, conectá-lo como plugin no ChatGPT, configurar um callback real e confirmar que uma entrega retoma o chat correto. Esse resultado deve ser registrado aqui quando o ambiente estiver disponível.
