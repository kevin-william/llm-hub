# 14 — Fluxo ChatGPT → OpenCode → ChatGPT

> **Status:** Em andamento  
> **Progresso:** 85%  
> **Dependências:** 10, 11, 12, 13  
> **Commit único:** `test(e2e): cobrir fluxo chatgpt opencode chatgpt`

## 1. Por que

Integra os comportamentos já entregues sem criar outra camada de produção: demonstra que a inscrição anterior ao envio evita perda de notificação e que a resposta persiste antes de acordar o chat.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
O fluxo recomendado evita a corrida em que o OpenCode termina antes de o ChatGPT se inscrever.

### Passo 1 — Abrir o canal

O ChatGPT chama:

```text
open_channel(destination: "agent:opencode/default")
```

O Hub devolve `channel_id`.

### Passo 2 — Criar a inscrição
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Api/*`, `src/LlmHub.OpenCodeWorker/*` | Ajustar somente lacunas descobertas pelo teste de integração |
| Teste | `tests/LlmHub.EndToEndTests/ChatGptOpenCodeFlowTests.cs` | Criar cenário completo com callback e adapter controlado |
| Documentação | `docs/fluxo-mvp.md` | Criar roteiro operacional e matriz de evidências |

## 4. Como implementar

1. Montar ambiente com PostgreSQL, Redis, MinIO, Hub, dispatcher e worker/adapter OpenCode controlável.
2. Exercitar exatamente: abrir canal, inscrever callback, enviar com `await_response`, claim/executar, persistir resposta/artefato, receber evento e recuperar conteúdo por `get_message`.
3. Corrigir somente falhas de integração que contradigam contratos anteriores; mudanças de desenho devem voltar à tarefa responsável em vez de serem escondidas neste commit.

## 5. Testes no mesmo commit

- Fluxo completo termina sem polling e contém correlação, cursor e resposta persistida.
- Envio antes de inscrição falha com `SUBSCRIPTION_REQUIRED` quando espera resposta.
- Reenvio, delivery duplicado e callback lento não geram duas respostas aceitas.

## 6. Validação

```bash
dotnet test tests/LlmHub.EndToEndTests --filter FullyQualifiedName~ChatGptOpenCodeFlow
dotnet test LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Validação contra o ChatGPT real requer ambiente público e configuração do plugin; o teste automatizado usa callback e adapter controlados.
- Fora de escopo: quotas, OAuth e hardening completo, que são pré-requisitos de produção e não do fluxo funcional.

## 8. Critérios de aceitação

- [ ] Fluxo completo demonstrado em ambiente controlado
- [ ] Testes de corrida e duplicata executados
- [ ] Roteiro do MVP documentado
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Fluxo HTTP, worker controlado, persistência da resposta e dispatcher de webhook integrados em ambiente controlado |
| Testes | Cobrem inscrição anterior, conclusão, callback com cursor/evento, rejeição de await sem inscrição, repetição de processamento e callback lento sem duplicar entrega |
| Documentação | Roteiro MVP criado em `docs/fluxo-mvp.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 85%. Ainda falta o adapter OpenCode concreto.
