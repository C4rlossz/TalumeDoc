# Verificação da entrega

Data: 29 de setembro de 2026.

## Executado com sucesso

| Verificação | Resultado |
|---|---|
| Compilação C#/.NET | API, Core, Infrastructure e Worker compilados sem avisos/erros |
| Testes .NET | 31 testes aprovados |
| Testes de infraestrutura | 6 testes aprovados |
| Compilação TypeScript | Aprovada |
| Síntese CDK/CloudFormation | Template gerado localmente; não implantado |
| Grafo de dependências CloudFormation | Sem ciclos no template verificado |
| Publicação Linux x64 | API empacotada com Amazon.Lambda.Tools 7.0.0; Worker publicado para Lambda |
| Integração da interface | DOM conectado à API local real: envio de exemplo, processamento, indicadores, relatório e busca aprovados |
| Restauração com lock do NuGet | Executada em modo bloqueado |

Os testes C# incluem cabeçalhos, valores, duplicados, CSV com aspas/quebras de linha, codificação, limites, concorrência, idempotência, worker antigo, isolamento por proprietário, upload, relatório, retry, OpenAPI e bloqueio do modo local em produção. Os testes de autenticação validam tokens RSA temporários, ausência de token, cliente/emissor incorretos, expiração e rejeição de ID tokens.

## Limitações de validação

- **AWS:** não houve deploy. Cognito real, IAM, S3, DynamoDB Streams, EventBridge Pipes e entrega SQS precisam do smoke test pós-deploy da conta de destino.
- **Navegador:** Chromium não pôde iniciar porque a política do ambiente bloqueou uma operação de socket local. A revisão visual desktop/mobile e o teste de navegador completo permanecem pendentes. O script `npm run test:browser` está incluído para execução em um ambiente compatível.
- **Docker:** Docker não estava disponível; Dockerfile e Compose estão incluídos, mas a imagem não foi construída aqui.
- **GitHub:** workflows preparados; ainda não executados em um repositório remoto TalumeDoc.

As amostras e as métricas dos testes são dados locais de demonstração. Não representam uso por clientes, testes de carga ou operação em produção.

## Atualização de 30 de setembro de 2026

Adicionados tema escuro e diálogo Sobre o projeto. A preferência é aplicada antes da folha de estilos, persiste no navegador e sincroniza entre abas. Quando não há escolha salva, o tema acompanha o sistema. A alternância continua disponível se o armazenamento local estiver desabilitado.

Nesta atualização, foram verificados a sintaxe JavaScript, a alternância de tema, persistência, preferência do sistema, funcionamento sem localStorage, abertura/fechamento do diálogo, autoria e link do LinkedIn. A verificação usa DOM com respostas simuladas da API, pois não houve alteração do backend. Não representa nova validação na AWS nem revisão visual em navegador real.
