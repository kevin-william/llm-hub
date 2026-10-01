# 05 — API HTTP de canais, mensagens e runs

> **Status:** Concluída  
> **Progresso:** 100%  
> **Dependências:** 04  
> **Commit único:** `feat(api): expor canais mensagens e runs`

## 1. Por que

Oferece a interface HTTP equivalente às ferramentas futuras, permitindo criar canal, enviar com idempotência, consultar histórico e solicitar cancelamento sem ainda expor MCP.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### API HTTP equivalente

```text
POST   /v1/channels
GET    /v1/channels/{channelId}
GET    /v1/channels/{channelId}/messages

POST   /v1/channels/{channelId}/messages
GET    /v1/messages/{messageId}

GET    /v1/runs/{runId}
POST   /v1/runs/{runId}/cancel

POST   /v1/deliveries/{messageId}/ack
```
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Api/Endpoints/{Channels,Messages,Runs}Endpoints.cs` | Criar |
| Produção | `src/LlmHub.Application/Commands/*`, `Queries/*` | Criar handlers |
| Teste | `tests/LlmHub.IntegrationTests/Api/*` | Criar testes de contrato HTTP |
| Documentação | `docs/http-api.md`, `src/LlmHub.Api/openapi/*` | Criar OpenAPI e exemplos |

## 4. Como implementar

1. Implementar os endpoints listados, paginação/cursor de histórico e Problem Details para erros de domínio.
2. Exigir `client_message_id`; quando `await_response=true`, verificar uma inscrição ativa pela abstração de subscriptions e retornar `SUBSCRIPTION_REQUIRED` antes de aceitar o run.
3. Cancelamento é cooperativo: muda o estado somente quando permitido e deixa o worker observar a solicitação. `ack` permanece idempotente.

## 5. Testes no mesmo commit

- Abrir canal, enviar, consultar mensagem/run e paginar histórico.
- Reenvio idempotente retorna o mesmo recurso; canal ou destinatário inválido retorna erro definido.
- `await_response` sem inscrição retorna `SUBSCRIPTION_REQUIRED`; cancelamento repetido é seguro.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~Api
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- A autorização real entra na tarefa 15; até lá usar principal de desenvolvimento explicitamente documentado.
- Fora de escopo: transporte Redis, execução de worker e endpoint MCP.

## 8. Critérios de aceitação

- [ ] Endpoints HTTP básicos implementados
- [ ] Contratos e erros cobertos por integração
- [ ] OpenAPI e exemplos atualizados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Concluída |
| Testes | Aprovados: 3 cenários HTTP adicionais |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 100%
