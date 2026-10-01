# 02 — Prova técnica de MCP Events

> **Status:** Em andamento — validação com ChatGPT pendente  
> **Progresso:** 75%  
> **Dependências:** 01  
> **Commit único:** `feat(events): validar assinatura e retomada MCP`

## 1. Por que

Reduz primeiro o risco do protocolo ainda em draft. Entrega um servidor MCP mínimo, isolado do domínio, capaz de catalogar eventos, registrar uma inscrição, verificar callback e disparar um evento assinado.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Fase 0 — Prova de risco do MCP Events

Antes de construir o Hub inteiro:

1. criar MCP mínimo com um evento `message.created`;
2. conectar como plugin no ChatGPT;
3. implementar `events/list`, `subscribe` e `unsubscribe`;
4. verificar o callback recebido;
5. disparar um evento manual assinado;
6. confirmar que o ChatGPT retoma o chat;
7. testar duplicata, expiração e assinatura inválida.

Critério de saída: um evento externo acorda o chat correto e o ChatGPT executa a instrução configurada.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Api/McpEventsProbe/*` | Criar protocolo isolado e endpoint de disparo manual apenas em Development |
| Teste | `tests/LlmHub.IntegrationTests/McpEventsProbeTests.cs` | Criar testes HTTP e assinatura |
| Documentação | `docs/mcp-events-prova.md` | Criar roteiro de plugin, callback e resultado observado |

## 4. Como implementar

1. Usar a revisão MCP `2026-07-28`; implementar `events/list`, `events/subscribe` e `events/unsubscribe` como JSON-RPC HTTP isolado atrás de `IMcpEventsService`.
2. Persistir inscrições do experimento somente em memória e modelar expiração, id de evento estável e validação de assinatura; o armazenamento definitivo pertence à tarefa 12.
3. Criar verificação de callback e entrega de `message.created`; registrar no documento o resultado real da retomada no ChatGPT e incompatibilidades da versão-alvo.

## 5. Testes no mesmo commit

- Catálogo, inscrição, desinscrição, expiração e duplicata retornam contratos previsíveis.
- Callback de desafio e payload assinado válido são aceitos pelo servidor de teste; assinatura inválida é rejeitada.
- A retomada no ChatGPT é uma validação manual externa, não substituída por mock.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~McpEventsProbe
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer URL HTTPS pública, plugin ChatGPT e MCP 2.0 habilitado.
- Exceção deliberada: é uma prova isolada, não um recurso de produção completo; subscriptions persistentes e dispatcher ficam na tarefa 12.

## 8. Critérios de aceitação

- [ ] Prova mínima de eventos implementada
- [ ] Testes automatizados executados
- [ ] Roteiro e evidência da validação externa documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Concluída: endpoint isolado em Development e callbacks em memória |
| Testes | Concluídos: catálogo, headers, desafio, duplicata, desinscrição e HMAC |
| Documentação | Concluída em `docs/mcp-events-prova.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 75%. A revisão MCP atual não padroniza `events/*`; a extensão experimental requer teste externo com plugin ChatGPT e callback HTTPS público.
