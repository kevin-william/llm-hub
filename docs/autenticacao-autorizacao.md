# Autenticação e autorização

Enquanto o provedor OIDC não é configurado, o Hub usa o principal resolvido pela borda da aplicação. Esse principal precisa participar do canal para enviar mensagens, criar inscrições, ler histórico, buscar mensagens ou consultar/cancelar runs. A regra vale para a API HTTP e para as ferramentas MCP. Leituras HTTP sem participação retornam `404`; as ferramentas MCP não retornam o recurso, e o histórico resulta vazio.

Para ativar OAuth/OIDC, configure `Authentication__Issuer` e `Authentication__Audience`. A API passa a validar bearer JWT pelo discovery do issuer, exige usuário autenticado e reconhece os claims `scope` ou `scp`. As rotas de leitura exigem `hub.read`, envio e inscrições exigem `hub.send`, cancelamento exige `hub.cancel` e operações de worker exigem `hub.admin`. O claim `sub` vira o principal interno `principal:oidc/{sub}`.

Tokens autenticados também precisam conter um dos claims `tenant_id`, `tid` ou `tenant`. O Hub persiste o tenant em canais, endpoints e workers, filtra a seleção de endpoint e os claims de worker por esse limite, e rejeita acesso a canais de outro tenant mesmo se o `sub` coincidir. Tokens de worker precisam ainda conter `worker_id`. O valor deve ser o mesmo usado no registro e no claim; heartbeat, conclusão e falha só são aceitos para runs atribuídos a esse worker no mesmo tenant. Essas verificações não se aplicam ao modo local sem autenticação, usado somente no desenvolvimento.

Ainda falta configurar o provedor por ambiente e emitir tokens de serviço curtos e rotacionáveis para workers.
