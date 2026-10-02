# 13 — Servidor e ferramentas MCP do Hub

> **Status:** Em andamento — validação externa pendente  
> **Progresso:** 75%  
> **Dependências:** 05, 12  
> **Commit único:** `feat(mcp): expor ferramentas e eventos do hub`

## 1. Por que

Expõe o Hub ao ChatGPT pela mesma camada de aplicação da API HTTP, sem duplicar regras de negócio. Combina ferramentas e métodos de eventos persistentes já existentes.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
As ferramentas poderiam ser classes explícitas:

```text
ListEndpointsTool
OpenChannelTool
SendMessageTool
GetMessageTool
GetChannelHistoryTool
GetRunTool
CancelRunTool
AckMessageTool
```

Eu evitaria colocar regras de negócio dentro dessas classes. Elas devem somente traduzir MCP para comandos da aplicação.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Api/Mcp/*` | Criar configuração, tools e mapeamento `/mcp` |
| Produção | `src/LlmHub.Application/*` | Reutilizar comandos/queries existentes; ajustar apenas contratos necessários |
| Teste | `tests/LlmHub.IntegrationTests/Mcp/*` | Criar testes JSON-RPC e equivalência HTTP |
| Documentação | `docs/mcp-hub.md` | Criar catálogo de tools, exemplos e sequência de inscrição |

## 4. Como implementar

1. Configurar `ModelContextProtocol.AspNetCore` 2.x com HTTP transport em `/mcp` e adicionar tools explícitas para as oito operações do plano.
2. Cada tool valida contrato, propaga principal/correlação e chama o mesmo handler usado pela API; não acrescentar regra de negócio nas classes MCP.
3. Expor `server/discover`, `events/list`, `events/subscribe` e `events/unsubscribe` pela implementação persistente da tarefa 12, incluindo o requisito de inscrição antes de `await_response`.

## 5. Testes no mesmo commit

- Cada ferramenta gera o mesmo resultado e erro da API HTTP equivalente.
- Fluxo MCP abre canal, inscreve, envia, lê mensagem/histórico/run, cancela e reconhece mensagem.
- Métodos de eventos validam filtro e expiração.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Mcp
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- A versão do SDK e a extensão draft devem seguir o resultado documentado da tarefa 02.
- Fora de escopo: autenticação OAuth real, proteção SSRF e integração real OpenCode ponta a ponta.

## 8. Critérios de aceitação

- [ ] Endpoint MCP, tools e eventos implementados
- [ ] Testes de equivalência HTTP/MCP executados
- [ ] Uso do plugin documentado
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Endpoint `/mcp`, dez ferramentas e inscrições persistentes concluídos; a extensão experimental `events/*` segue isolada por não ser padrão do protocolo |
| Testes | Concluídos para `tools/list`, `open_channel`, `send_message`, `subscribe_events` e `unsubscribe_events` pelo transporte MCP |
| Documentação | Concluída em `docs/mcp-hub.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 75%. Falta validar o endpoint com host ChatGPT em HTTPS público e decidir como retomar eventos conforme o protocolo suportado por esse host.
