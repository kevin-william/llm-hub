# MCP do Hub

O Hub expõe o endpoint oficial Streamable HTTP em `POST /mcp`, implementado com `ModelContextProtocol.AspNetCore` 2.2.0 e a revisão de protocolo `2026-07-28`. O transporte é sem sessão. Chamadas devem incluir `MCP-Protocol-Version`, `Mcp-Method` e aceitar `application/json, text/event-stream`; respostas de chamadas são frames SSE `event: message`.

O catálogo atual contém as oito ferramentas abaixo. O SDK as publica em `snake_case`.

| Tool | Efeito |
|---|---|
| `list_endpoints` | Lista endpoints registrados. |
| `open_channel` | Abre canal com destino e participantes opcionais. |
| `send_message` | Aceita a mensagem e cria o run. |
| `get_message` | Recupera mensagem imutável. |
| `get_channel_history` | Recupera histórico por cursor de sequência. |
| `get_run` | Recupera estado de execução. |
| `cancel_run` | Solicita cancelamento de um run não terminal. |
| `ack_message` | Reconhece uma entrega de forma idempotente. |

As ferramentas usam os mesmos contratos de canais, mensagens e runs usados pela API HTTP; o teste de integração consulta `tools/list` pelo transporte MCP e executa `open_channel` e `send_message` pelo fluxo JSON-RPC real.

O servidor já responde ao `server/discover` oferecido pelo SDK. A extensão `events/*` da prova permanece em `/mcp/events-probe`, somente em Development, porque a revisão MCP atual não padroniza essa família de operações. A ligação dessa extensão às inscrições persistentes, a retomada em um host ChatGPT real e a segurança completa de callback continuam pendentes das validações externas e das tarefas de segurança.
