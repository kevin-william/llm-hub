# Observabilidade e auditoria

O Hub publica `ActivitySource` e `Meter` com o nome `LlmHub`. Os spans usam somente `channel_id`, `message_id`, `run_id`, `event_id` e `delivery_id`; conteúdo, tokens e segredos não são tags.

As métricas mínimas são `hub.messages.accepted`, `hub.runs.completed`, `hub.webhooks.retries` e `hub.leases.expired`. A tabela `audit_events` registra `route.selected`, `run.claimed`, `run.completed`, `lease.expired` e `webhook.accepted`, também sem payloads sensíveis.

Alertas operacionais devem acompanhar aumento de retries, leases expirados e deliveries em estado `dead`.
