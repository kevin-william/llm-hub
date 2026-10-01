# Contrato de workers

Workers registram `worker_id`, adapter, capacidades, versão e limite de concorrência. O Hub entrega trabalho por `POST /v1/workers/{workerId}/claims`, mantendo Redis como detalhe interno.

Cada claim cria um lease de 60 segundos e token de fencing. Heartbeats renovam o lease e indicam `cancel_requested`. As operações de conclusão e falha exigem o token atual; um token expirado ou substituído é rejeitado.

Falhas `transient` retornam o run para `retry_wait`; `permanent` e `policy` terminam em falha; `cancelled` termina em cancelamento.
