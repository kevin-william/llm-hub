# Retries e recuperação

Quando um lease expira, o Hub remove o worker e token associados ao run, incrementa a tentativa e muda o estado para `retry_wait`. O backoff inicial cresce em intervalos de cinco segundos até sessenta segundos. Quando `retry_at` vence, o run volta a `queued`.

Somente falhas transitórias são elegíveis para retry. Após a terceira expiração de lease, o Hub move o run para `dead_letter`; falhas permanentes, de política e cancelamentos continuam terminais.

`FakeWorkerExecutor` usa o contrato HTTP interno do gateway e produz uma mensagem final automática. Ele permite validar o ciclo completo antes do adapter OpenCode.
