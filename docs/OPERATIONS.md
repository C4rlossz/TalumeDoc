# Operação, custos e recuperação

## Acompanhar o sistema

Outputs do CDK identificam URL, bucket, tabela, filas, Pipe, pool e tópico de alertas. O dashboard CloudWatch reúne invocações/erros de Lambda e profundidade das filas. Há alarmes para as duas filas de falhas e para idade elevada da fila principal. Inscreva um e-mail com `alarmEmail` e confirme a inscrição para receber notificações.

O worker registra o ID da mensagem e o tipo de exceção sem imprimir arquivos, tokens ou registros de produtos. A API registra erros de infraestrutura pelo mecanismo de logging do ASP.NET Core. Logs ficam por sete dias.

Falhas parciais retornadas pelo handler podem não elevar a métrica `Errors` da Lambda, pois a invocação retornou normalmente. Para elas, acompanhe profundidade/idade da fila, DLQ e logs. O dashboard não é um indicador completo de sucesso de negócio.

## Documento parado

- `Queued`: veja se o Pipe está `RUNNING`, se as permissões de Stream/SQS estão corretas e se há falhas na fila de despacho.
- `Processing`: verifique logs, timeout e nova entrega depois da expiração da concessão. A visibilidade da mensagem é de 180 segundos.
- `Rejected`: corrija o CSV e faça novo upload. Não é falha de infraestrutura.
- `Completed` com inválidos: baixe o relatório e corrija os dados.
- `Failed`: corrija a causa e use **Tentar novamente**. Isso redefine tentativas e gera uma nova alteração `Queued` para o Pipe. A API também permite recuperar uma quinta execução interrompida após expirar a concessão.

SQS conta entregas; o documento conta claims adquiridos. Se mensagens forem para a DLQ antes de cinco claims (por exemplo, erro antes de ler a tabela), um operador deve corrigir o problema e reenviar a mensagem ao processamento. A concessão expirada permite retomar. Para estados `Failed`, use primeiro o endpoint de retry e remova a mensagem antiga da DLQ somente após confirmar o novo processamento.

## Interrupção do despacho por mais de 24 horas

DynamoDB Streams tem retenção de 24 horas. Mensagens antigas podem exigir reconciliação. Não desative e reative o Stream: isso altera seu ARN e exige recriar o Pipe.

1. Corrija o Pipe/role e confirme seu estado.
2. Consulte os documentos afetados na tabela usando o proprietário e ID; preserve os arquivos.
3. Para um item que continua `Queued`, atualize um atributo de auditoria como `dispatchReplayAt` com o horário atual. A nova imagem ainda terá `status=Queued` e o Pipe produzirá uma nova mensagem. Não altere `payload`, `ownerId`, `id` ou `version` nesse procedimento.
4. Confirme o processamento antes de excluir qualquer mensagem da DLQ de despacho. Ela pode conter o evento original do Stream, não o formato `ProcessingJob`; não faça redrive cego para a fila do worker.

Uma automação de reconciliação periódica é uma evolução possível; não está implementada nesta versão.

## Retenção

S3 expira arquivos e relatórios após 30 dias. Metadados têm TTL de 30 dias, com exclusão assíncrona do DynamoDB. Pode haver um intervalo em que o metadado ainda aparece e o objeto já expirou; essa janela não é garantia de recuperação. O local não aplica expiração automática.

S3 não tem versionamento nesta versão. DynamoDB tem recuperação pontual habilitada; restaurar a tabela não restaura objetos do S3. Faça backup de arquivos necessários antes do vencimento.

## Custos

Não há promessa de gratuidade. Os custos incluem Lambda, API Gateway, S3 (armazenamento e operações), DynamoDB (leituras, gravações e recuperação pontual), EventBridge Pipes, SQS, Cognito, logs, métricas, dashboard, alarmes, SNS e transferência de dados. O bootstrap também mantém recursos.

Use a [AWS Pricing Calculator](https://calculator.aws/) com sua região e volume. Crie um orçamento no AWS Budgets e configure notificações. Um alerta de orçamento não interrompe automaticamente os serviços. O modo local funciona sem consumo de AWS.

A arquitetura evita instâncias RDS, containers permanentemente ativos e NAT Gateway. Há throttle agregado de 10 requisições/s com burst 20 no API Gateway, 20 uploads/minuto por usuário por instância e no máximo dois workers simultâneos. Esses limites reduzem carga; não são um teto de cobrança.

## Remover a implantação

Revise antes de executar:

```bash
npx cdk destroy TalumeDoc
```

**Bucket S3, tabela DynamoDB e User Pool Cognito são retidos** para evitar perda automática de dados. Eles podem continuar gerando custos. Inspecione os recursos retidos, faça os backups necessários e exclua-os manualmente apenas quando não precisar mais deles. As filas são removidas com a stack; processe ou preserve pendências antes. Recursos de bootstrap permanecem e não devem ser apagados se outros projetos os usam.
