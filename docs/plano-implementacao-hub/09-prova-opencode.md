# 09 — Prova de compatibilidade da versão-alvo do OpenCode

> **Status:** Em andamento  
> **Progresso:** 40%  
> **Dependências:** 01  
> **Commit único:** `docs(opencode): registrar contrato da integração alvo`

## 1. Por que

Evita acoplar o Hub a uma interface presumida. A tarefa escolhe SDK, HTTP ou CLI a partir da versão instalada e fixa um contrato verificável para o adapter posterior.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
Como não há OpenCode instalado neste workspace, a primeira etapa técnica deve validar qual integração a versão-alvo oferece: SDK, servidor HTTP ou execução não interativa. O restante do Hub ficará isolado dessa decisão por uma interface:

```ts
interface AgentAdapter {
  execute(input: ExecutionInput): Promise<ExecutionResult>;
  cancel(runId: string): Promise<void>;
  health(): Promise<AdapterHealth>;
}
```
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Contracts/Adapters/IAgentAdapter.cs` | Criar contrato C# equivalente |
| Teste | `tests/LlmHub.IntegrationTests/OpenCode/OpenCodeCompatibilityTests.cs` | Criar teste opt-in contra versão instalada |
| Documentação | `docs/opencode-compatibilidade.md` | Criar matriz de versão, interface, comandos e decisão |

## 4. Como implementar

1. Instalar ou apontar explicitamente uma versão-alvo em ambiente de integração; registrar versão, sistema, autenticação e capacidades observadas.
2. Exercitar sessão, envio de prompt, coleta de resposta, cancelamento e health pela interface realmente oferecida.
3. Definir `IAgentAdapter` em Contracts e um fake para testar sua semântica; documentar se a implementação da tarefa 10 será C# HTTP, processo CLI ou processo TypeScript.

## 5. Testes no mesmo commit

- Fake satisfaz execute, cancel e health.
- O teste de compatibilidade falha claramente quando OpenCode não está configurado e pode ser executado em CI de integração autorizado.
- A execução real registra evidência no documento, sem segredo ou prompt sensível.

## 6. Validação

```bash
dotnet test tests/LlmHub.IntegrationTests --filter FullyQualifiedName~OpenCodeCompatibility
dotnet build LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer OpenCode instalado e versão-alvo fornecida pelo operador.
- Exceção deliberada: este é um spike de compatibilidade; não introduz processamento de runs real, que pertence à tarefa 10.

## 8. Critérios de aceitação

- [ ] Contrato de adapter definido
- [ ] Teste e evidência da versão-alvo produzidos
- [ ] Decisão de integração documentada
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Contrato de adapter e implementação desabilitada criados |
| Testes | Contrato desabilitado coberto; integração real pendente |
| Documentação | Matriz de decisão iniciada |
| Validação do usuário | Pendente |

**Progresso geral:** 40%
