# Persistência e outbox

PostgreSQL é a fonte de verdade para canais, participantes, endpoints, mensagens, runs, tentativas, sessões, inscrições, eventos de outbox, deliveries e artefatos. As chaves compostas impedem duplicação de `client_message_id` por principal e de sequência por canal.

Ao aceitar uma mensagem, o Hub grava na mesma unidade de trabalho a mensagem imutável, o run em `queued` e o evento `run.queued`. Uma repetição do mesmo `client_message_id` retorna os mesmos identificadores, sem criar outro run ou evento. O publisher Redis só pode marcar um evento como publicado após enviar o evento persistido.

O modelo inclui tokens de concorrência nos canais e runs. A migration PostgreSQL inicial e a migration de delivery de webhooks estão versionadas; os testes contra PostgreSQL em container ainda dependem do daemon Docker disponível.
## Migrations

No ambiente local, a API aplica migrations pendentes na inicialização quando `Database__ApplyMigrations=true`. O processo usa a tabela `__EFMigrationsHistory`; em produção, a aplicação deve ser coordenada por uma única instância ou uma etapa dedicada de implantação.

