# Artefatos e sessões

Artefatos são enviados ao armazenamento S3 compatível configurado em `ArtifactStorage__*`. O Hub aceita no máximo 20 MiB por objeto, os tipos `application/json`, `application/octet-stream`, `text/markdown` e `text/plain`, e calcula SHA-256 antes do envio. Um checksum informado pelo produtor precisa coincidir; os objetos recebem a chave determinística `sha256/{hash}`.

O conteúdo não deve viajar em webhooks. O banco armazena metadados e referências ao objeto, enquanto o consumidor autorizado busca o conteúdo no storage. A conclusão de um run persiste `run_id`, `channel_id`, hash, tipo e tamanho dos artefatos na mesma transação da resposta e da outbox. Ela também atualiza a associação única entre canal, adapter e sessão externa; o próximo claim entrega essa sessão ao adapter. Repetir a conclusão já aceita não duplica os registros.

A leitura autorizada pela API será concluída junto da autorização por tenant.
