# Redis Streams

O Redis é transporte, nunca fonte de verdade. Runs seguem para `hub:runs:<adapter>`; a integração inicial publica em `hub:runs:opencode`. O evento persistido inclui `event_id`, `run_id`, `channel_id` e horário de enfileiramento.

O publisher marca `outbox_events.published_at` somente depois de `XADD`. Se falhar antes da marcação, a próxima execução republica o mesmo `event_id`; consumidores devem deduplicá-lo. Consumer groups, `XACK`, `XPENDING` e `XAUTOCLAIM` serão usados pelos claims de worker.

O processo `LlmHub.Workers` ativa o publisher quando `Workers__EnableOutboxPublisher=true`. Nessa situação, `ConnectionStrings__Hub` e `ConnectionStrings__Redis` são obrigatórias.
