# 12 — Inscrições e dispatcher de webhooks

> **Status:** Em andamento — validação MCP externa pendente  
> **Progresso:** 85%  
> **Dependências:** 02, 04  
> **Commit único:** `feat(events): entregar notificações assinadas`

## 1. Por que

Converte eventos de outbox em notificações duráveis para inscritos, com cursor, retries e idempotência de entrega. A segurança reforçada do callback entra em tarefa própria depois que o fluxo básico estiver testável.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
#### Delivery

Tentativa de entrega de um evento:

```text
pending → sending → accepted
                  ↘ retry_wait → dead
```

`accepted` significa que o callback devolveu `2xx`. Não significa necessariamente que o ChatGPT concluiu toda a continuação. Para rastreamento completo, pode existir um `ack_message` opcional.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Application/Subscriptions/*` | Criar ciclo de inscrição e matching |
| Produção | `src/LlmHub.Workers/WebhookDispatcher.cs`, `src/LlmHub.Infrastructure/Webhooks/*` | Criar dispatcher e cliente tipado |
| Teste | `tests/LlmHub.IntegrationTests/Webhooks/*` | Criar testes de callback controlado |
| Documentação | `docs/eventos-e-webhooks.md` | Criar catálogo, payload, cursor e semântica |

## 4. Como implementar

1. Persistir principal, filtros por canal/evento, callback, cursor, expiração, estado de verificação e segredo protegido pela abstração de segredos.
2. Ao concluir run/mensagem, criar evento de outbox; dispatcher encontra inscrições, cria delivery e envia payload pequeno com `eventId` estável e cursor replayável.
3. Tratar 2xx como `accepted`, 5xx como retry com backoff e `410` como expiração/desativação documentada; `ack_message` é opcional e idempotente.

## 5. Testes no mesmo commit

- Inscrição filtrada recebe somente evento correspondente e cursor pode reproduzir lacuna.
- 500 agenda retry com mesmo `eventId`; 2xx aceita; 410 desativa conforme política.
- Eventos duplicados e fora de ordem não corrompem cursor/delivery.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Webhooks
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Resultado da tarefa 02 define detalhes de compatibilidade MCP Events.
- Fora de escopo: OAuth, SSRF/DNS rebinding e criptografia concreta de segredos, que entram nas tarefas 15 e 16.

## 8. Critérios de aceitação

- [ ] Inscrições, deliveries e dispatcher implementados
- [ ] Testes de retry, cursor e duplicata executados
- [ ] Catálogo e semântica documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Concluída |
| Testes | Concluídos: filtro, cursor, duplicata, retry, aceite e `410` |
| Documentação | Concluída em `docs/eventos-e-webhooks.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 85%. Falta validar a dependência MCP da tarefa 02 em endpoint HTTPS público; os controles de segurança ficam na tarefa 16.
