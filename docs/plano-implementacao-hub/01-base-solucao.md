# 01 — Base modular da solução

> **Status:** Concluída  
> **Progresso:** 100%  
> **Dependências:** nenhuma  
> **Commit único:** `chore(bootstrap): criar estrutura modular do hub`

## 1. Por que

Estabelece uma solução .NET 10 compilável, com limites entre camadas, configuração local e convenções de teste. É a menor base que permite as tarefas posteriores entregarem comportamento sem inventar estrutura incompatível.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
## Estrutura recomendada

Eu começaria como um **monólito modular**, não como vários microsserviços:

```text
src/
  LlmHub.Api/
  LlmHub.Domain/
  LlmHub.Application/
  LlmHub.Infrastructure/
  LlmHub.Contracts/
  LlmHub.Workers/
  LlmHub.OpenCodeWorker/

tests/
  LlmHub.UnitTests/
  LlmHub.IntegrationTests/
  LlmHub.EndToEndTests/
```
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `LlmHub.sln`, `src/LlmHub.*/*.csproj` | Criar |
| Produção | `src/LlmHub.Api/Program.cs` | Criar health check mínimo |
| Teste | `tests/LlmHub.UnitTests`, `tests/LlmHub.IntegrationTests`, `tests/LlmHub.EndToEndTests` | Criar projetos xUnit e teste de composição |
| Documentação | `README.md`, `docker-compose.yml`, `.env.example` | Criar instruções locais e serviços de desenvolvimento |

## 4. Como implementar

1. Criar a solution e os sete projetos com referências unidirecionais: Domain não depende de infraestrutura; Application depende de Domain e Contracts; Api, Workers e OpenCodeWorker são adaptadores de entrada/execução.
2. Fixar .NET 10, nullable e analisadores; incluir configurações para PostgreSQL, Redis, artefatos e telemetria sem segredos reais.
3. Disponibilizar `GET /health` e Docker Compose para PostgreSQL, Redis e MinIO. Não criar domínio, endpoints funcionais ou workers nesta tarefa.

## 5. Testes no mesmo commit

- A solução compila e todos os três projetos de teste são descobertos.
- Teste de integração inicial inicia a API e confirma `GET /health`.

## 6. Validação

```bash
dotnet build LlmHub.sln --configuration Release
dotnet test LlmHub.sln --configuration Release
docker compose config
```

## 7. Portões e fora de escopo

- Confirmar disponibilidade local de .NET 10 e Docker antes da implementação.
- Fora de escopo: modelos de domínio, migrations, Redis Streams, MCP e autenticação.

## 8. Critérios de aceitação

- [ ] Estrutura modular criada e compilável
- [ ] Testes automatizados executados
- [ ] README e ambiente local documentados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Concluída |
| Testes | Aprovados: 3 |
| Documentação | Concluída |
| Validação do usuário | Pendente |

**Progresso geral:** 100%
