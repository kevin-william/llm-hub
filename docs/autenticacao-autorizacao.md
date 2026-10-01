# Autenticação e autorização

Enquanto o provedor OIDC não é configurado, o Hub usa o principal resolvido pela borda da aplicação. Esse principal precisa participar do canal para enviar mensagens, criar inscrições, ler histórico, buscar mensagens ou consultar/cancelar runs. Leituras sem participação retornam `404` para não revelar a existência do canal.

Para ativar OAuth/OIDC, configure `Authentication__Issuer` e `Authentication__Audience`. A API passa a validar bearer JWT pelo discovery do issuer, exige usuário autenticado e reconhece os claims `scope` ou `scp`. As rotas de leitura exigem `hub.read`, envio e inscrições exigem `hub.send`, cancelamento exige `hub.cancel` e operações de worker exigem `hub.admin`. O claim `sub` vira o principal interno `principal:oidc/{sub}`.

Ainda falta configurar o provedor por ambiente, mapear o claim de tenant e emitir tokens de serviço rotacionáveis para workers.
