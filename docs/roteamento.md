# Roteamento determinístico

O destino explícito de um canal usa `agent:{adapter}/{instância}`. Ele é persistido ao abrir o canal; cada mensagem usa esse destino, sem aceitar que o remetente o substitua. O roteador aceita `opencode` e `echo` e grava o adapter escolhido no run antes de publicá-lo na outbox.

Para selecionar por capacidade, abra o canal com `agent:{adapter}` e informe `requiredCapabilities`, por exemplo `role:reviewer` e `files`. O Hub considera somente endpoints `online` do adapter, exige todas as capacidades solicitadas e escolhe o primeiro por endereço e identificador. O endereço escolhido passa a ser o destino persistido do canal. Se nenhum endpoint compatível existir, a abertura retorna `ENDPOINT_NOT_AVAILABLE`. Workers registrados publicam endereço, capacidades e estado para descoberta, e `list_endpoints` mostra essas capacidades. Não há roteamento baseado em LLM.
