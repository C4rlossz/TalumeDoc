# API HTTP

Base local: `http://localhost:5080`. Base AWS: output `ApplicationUrl` do CDK. OpenAPI: `/openapi/v1.json`, protegido pelo mesmo mecanismo de autenticação da API.

Em produção envie `Authorization: Bearer <access_token>` emitido pelo Cognito. Tokens de identidade (`id_token`) não são aceitos. O token precisa ter assinatura válida, emissor correto, validade, `token_use=access`, `client_id` esperado e `sub` válido.

| Método e rota | Resultado |
|---|---|
| `GET /health` | Liveness público (não testa conexões com serviços externos) |
| `GET /config` | Configuração pública do frontend; não contém segredos |
| `GET /api/documents/` | Até 200 documentos recentes do usuário |
| `POST /api/documents/` | Multipart com um campo `file`; retorna 202 |
| `GET /api/documents/{id}` | Metadados e estado |
| `GET /api/documents/{id}/report` | Resumo e primeiras 100 ocorrências |
| `GET /api/documents/{id}/report?download=true` | CSV de todas as ocorrências |
| `POST /api/documents/{id}/retry` | Reenvia falha operacional; retorna 202 |

```bash
curl -F 'file=@samples/produtos-com-erros.csv' http://localhost:5080/api/documents/
curl http://localhost:5080/api/documents/
```

No PowerShell, use `curl.exe` para evitar o alias de versões antigas. Em produção acrescente `-H "Authorization: Bearer SEU_TOKEN"` e use a URL HTTPS publicada.

Códigos: 400 formato de envio inválido; 401 token ausente/inválido; 404 documento inexistente ou pertencente a outra pessoa; 409 relatório indisponível ou reenvio incompatível com o estado; 413 corpo HTTP acima do limite; 429 excesso de envios; 500 falha inesperada sem detalhes internos.

A API usa o identificador do token como proprietário, inclusive na leitura do relatório e no reenvio. A prévia tem `totalIssues` e `issues`; a quantidade de ocorrências pode ser maior que a de registros inválidos.
