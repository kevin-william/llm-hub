# API HTTP do Hub

Os endpoints HTTP ficam sob `/v1` e são equivalentes às ferramentas MCP. Durante o desenvolvimento, `X-Principal-Id` identifica o principal; OAuth/OIDC substitui esse cabeçalho na tarefa de segurança.

| Método | Rota | Resultado |
|---|---|---|
| POST | `/v1/channels` | Cria canal serial com destino explícito. |
| GET | `/v1/channels/{channelId}` | Lê canal e participantes. |
| GET | `/v1/channels/{channelId}/messages?afterSequence=&limit=` | Lista histórico por cursor de sequência. |
| POST | `/v1/channels/{channelId}/messages` | Aceita mensagem, cria run e outbox. |
| GET | `/v1/messages/{messageId}` | Lê conteúdo e correlação da mensagem. |
| GET | `/v1/runs/{runId}` | Lê estado do run. |
| POST | `/v1/runs/{runId}/cancel` | Solicita cancelamento cooperativo. |
| POST | `/v1/deliveries/{messageId}/ack` | Reconhece consumo de maneira idempotente. |

O envio exige `client_message_id`. Reenvios retornam o mesmo run. Quando `await_response` é verdadeiro e não existe uma inscrição `message.created` ativa, o Hub devolve `409` com o código `SUBSCRIPTION_REQUIRED`.

O documento OpenAPI fica disponível em `/openapi/v1.json`.
