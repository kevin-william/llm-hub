# 18 — Quotas, retenção e confiabilidade operacional

> **Status:** Em andamento  
> **Progresso:** 60%  
> **Dependências:** 15, 16, 17  
> **Commit único:** `feat(operations): aplicar quotas retenção e recuperação`

## 1. Por que

Completa as proteções de produção e valida que o sistema recupera falhas relevantes, com limites que impedem abuso e dados operacionais suficientes para recuperação.

## 2. Referência exata no plano

Plano: [Texto colado.txt](../../../../../Users/kevinkwar/.codex/attachments/05cd7f81-7711-4574-b39a-f96244eb6abc/Texto colado.txt)

<!-- PLAN_EXCERPT_START -->
````text
### Fase 5 — Segurança e confiabilidade

Implementar:

- OAuth;
- criptografia de segredos;
- verificação SSRF;
- quotas;
- auditoria;
- rotação de credenciais;
- retenção;
- backups;
- testes de carga e caos.
````
<!-- PLAN_EXCERPT_END -->

## 3. Onde implementar

| Tipo | Arquivo | Ação |
|---|---|---|
| Produção | `src/LlmHub.Application/Policies/*`, `src/LlmHub.Workers/Maintenance/*` | Criar quotas, limpeza e rotação |
| Teste | `tests/LlmHub.EndToEndTests/{Load,Chaos,Recovery}/*` | Criar cenários automatizados e perfil de carga |
| Documentação | `docs/operacao/{retencao,backup,incidentes}.md` | Criar runbooks e metas |

## 4. Como implementar

1. Aplicar quotas por tenant para tamanho de mensagem/anexo, runs, canais, callbacks e custo/tokens quando disponíveis; retornar erro explícito e auditar recusa.
2. Implementar retenção configurável, exclusão/anonimização segura, rotação de credenciais e jobs de manutenção.
3. Criar procedimentos testáveis de backup/restauração e executar cenários de caos para PostgreSQL temporariamente indisponível, Redis reiniciado, publisher duplicado, worker interrompido e callback 5xx.

## 5. Testes no mesmo commit

- Cada quota limita o tenant correto sem afetar outro tenant.
- Backup é restaurado em ambiente limpo e mantém histórico/auditoria exigidos.
- Cenários de carga/caos atendem às metas de nenhuma perda terminal, recuperação e duplicatas sem efeitos adicionais.

## 6. Validação

```bash
dotnet test tests/LlmHub.EndToEndTests --filter FullyQualifiedName~Recovery
dotnet test tests/LlmHub.EndToEndTests --filter FullyQualifiedName~Chaos
dotnet test LlmHub.sln --configuration Release
```

## 7. Portões e fora de escopo

- Requer políticas aprovadas de retenção, RPO/RTO, quotas e local de backup antes da produção.
- Fora de escopo: segundo adapter e roteamento por capacidade da tarefa 19.

## 8. Critérios de aceitação

- [ ] Quotas, retenção, rotação e backups implementados
- [ ] Cenários de recuperação e caos executados
- [ ] Runbooks operacionais atualizados
- [ ] Referência literal validada
- [ ] Um commit criado com a mensagem planejada

---
## Status de implementação

| Item | Status |
|---|---|
| Implementação | Quotas configuráveis para mensagem, anexo, run, canal e callback, além do job de retenção de auditoria/deliveries terminais, implementadas |
| Testes | Rejeição sem criação de trabalho para quotas de entrada, canais e callbacks, retenção seletiva e recuperação ponta a ponta de worker interrompido cobertas |
| Documentação | Limites, retenção e variáveis de ambiente documentados em `docs/operacao-retencao.md` |
| Validação do usuário | Pendente |

**Progresso geral:** 60%. Backups, quotas de custo/tokens e cenários de caos em ambiente de produção ainda dependem de política operacional aprovada.
