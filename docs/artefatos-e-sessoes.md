# Artefatos e sessões

Artefatos são enviados ao armazenamento S3 compatível configurado em `ArtifactStorage__*`. O Hub aceita no máximo 20 MiB por objeto, os tipos `application/json`, `application/octet-stream`, `text/markdown` e `text/plain`, e calcula SHA-256 antes do envio. Um checksum informado pelo produtor precisa coincidir; os objetos recebem a chave determinística `sha256/{hash}`.

Um worker envia bytes em `POST /v1/workers/{worker_id}/artifacts`, com `Content-Type` permitido e, opcionalmente, `X-Artifact-Sha256`. A resposta traz a referência que deve ser enviada em `complete`. Com OIDC, essa rota exige `hub.admin` e o `worker_id` do token.

O conteúdo não deve viajar em webhooks. O banco armazena metadados e referências ao objeto, enquanto o consumidor autorizado busca o conteúdo no storage. A conclusão de um run persiste `run_id`, `channel_id`, hash, tipo e tamanho dos artefatos na mesma transação da resposta e da outbox. Ela também atualiza a associação única entre canal, adapter e sessão externa; o próximo claim entrega essa sessão ao adapter. Repetir a conclusão já aceita não duplica os registros.

`GET /v1/artifacts/{artifact_id}` retorna os bytes com o tipo persistido somente para participantes do canal. Para outros principais, retorna `404` e não revela a existência do artefato.
