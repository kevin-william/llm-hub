# 10 — Worker local do OpenCode

> **Status:** Em andamento  
> **Progresso:** 65%  
> **Dependências:** 07, 09  
> **Commit único:** `feat(opencode): executar runs por adapter local`

## 1. Por que

Conecta o contrato genérico de claim a uma execução real, mantendo a decisão específica do OpenCode dentro de um adapter e preservando a autoridade do Hub sobre leases e estados.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### OpenCode Worker

Daemon local que:

- mantém conexão de saída com o Hub;
- busca trabalhos;
- inicia ou reutiliza sessões OpenCode;
- traduz mensagens do Hub para o formato aceito pelo OpenCode;
- coleta resposta final;
- envia arquivos e resultados;
- reporta heartbeat, erro e conclusão.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.OpenCodeWorker/*` | Criar daemon, cliente Hub e adapter decidido na tarefa 09 |
| Produção | `src/LlmHub.Contracts/Adapters/*` | Estender somente DTOs necessários |
| Teste | `tests/LlmHub.IntegrationTests/OpenCode/OpenCodeWorkerTests.cs` | Criar testes com servidor fake/OpenCode controlado |
| Documentação | `docs/opencode-worker.md` | Criar instalação, configuração e limites |

## 4. Como implementar

1. Registrar o worker e executar loop de claim com concorrência limitada, backoff e shutdown cooperativo; nunca conectar diretamente ao Redis.
2. Mapear o run para `IAgentAdapter`, propagar deadline/cancelamento, enviar heartbeats e comunicar complete/fail com lease token atual.
3. Reutilizar sessão por `channel_id` conforme contrato da tarefa 11; até então manter uma referência transitória e não inventar persistência alternativa.

## 5. Testes no mesmo commit

- Worker claimado envia prompt e completa resposta por adapter fake.
- Cancelamento do Hub chama cancel no adapter; erro transitório falha pelo contrato correto.
- Reinício do processo não permite conclusão com lease antigo.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~OpenCodeWorker
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Implementar somente a integração constatada na tarefa 09; mudança de interface reabre o portão de compatibilidade.
- Fora de escopo: artefatos persistentes, MCP do Hub dentro do OpenCode e generalização para segundo adapter.

## 8. Critérios de aceitação

- [ ] Daemon e adapter OpenCode executam runs
- [ ] Testes de claim, cancelamento e fencing executados
- [ ] Instalação e limites documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Loop de registro, claim, fencing de lease, heartbeats periódicos, execução por adapter, cancelamento e conclusão/falha implementados; adapter OpenCode concreto pendente da prova de compatibilidade |
| Testes | Cobertos com adapter e gateway fake para execução, cancelamento e heartbeat durante execução longa |
| Documentação | Configuração e comportamento do processo documentados |
| Validação do usuário | Pendente |

**Progresso geral:** 65%. Falta a integração concreta com a versão-alvo do OpenCode.
