# Fluxo MVP: ChatGPT → OpenCode → ChatGPT

1. Abra um canal para `agent:opencode/default`.
2. Crie a inscrição `message.created` antes de enviar uma mensagem com `await_response=true`.
3. Envie a mensagem, que cria o run e a outbox no PostgreSQL.
4. O worker registrado faz claim, renova o lease e conclui o run. A resposta, a sessão e os metadados de artefatos são persistidos antes da notificação.
5. O dispatcher cria e entrega o webhook com `eventId` e cursor estáveis. O consumidor pode recuperar a resposta pelo identificador de mensagem.

O teste `ChatGptOpenCodeFlowTests` usa um adapter e callback controlados, cobre a sequência acima e rejeita o envio que espera resposta sem inscrição anterior.
