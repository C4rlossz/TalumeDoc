# GitHub e apresentação do projeto

## Criar um repositório independente

Nome sugerido: `TalumeDoc`. O projeto é uma ferramenta da família Talume, mas tem aplicação e infraestrutura próprias. Não é necessário alterar o repositório do Talume existente.

Crie no GitHub um repositório vazio (sem README, licença ou gitignore gerados pelo site). Na pasta extraída:

```bash
git init -b main
git add .
git commit -m "feat: initial TalumeDoc application and AWS infrastructure"
git remote add origin https://github.com/SEU_USUARIO/TalumeDoc.git
git push -u origin main
```

O ZIP não inclui `.git`, binários, dependências, dados locais ou credenciais. Os arquivos de lock estão incluídos. Se já existir um repositório local, confira `git status` e `git remote -v` antes de executar comandos de inicialização ou adicionar remotes.

Descrição sugerida para o repositório: **Validação assíncrona de documentos CSV com C#/.NET e AWS Lambda, S3, DynamoDB, SQS e CDK.**

Topics sugeridos: `csharp`, `dotnet`, `aws`, `serverless`, `aws-lambda`, `dynamodb`, `sqs`, `aws-cdk`, `portfolio-project`.

## Roteiro de demonstração de 2–3 minutos

1. Apresente o problema: revisar arquivos manualmente atrasa a importação de dados.
2. Envie o exemplo e mostre a passagem de fila para resultado.
3. Abra as ocorrências de SKU duplicado, quantidade negativa, nome ausente e preço inválido.
4. Baixe o CSV de erros e envie o arquivo válido.
5. Explique onde C# roda e por que o processamento é assíncrono.
6. Mostre a arquitetura, os testes, o controle de duplicidade e as permissões AWS.
7. Se tiver feito o deploy, apresente a URL e os logs reais; caso contrário, identifique a demonstração como local.

## Perguntas para estudar antes de entrevistas

- Por que SQS pode entregar a mesma mensagem mais de uma vez?
- O que acontece se o worker parar depois de escrever o relatório e antes de atualizar a tabela?
- Por que o relatório usa uma chave por tentativa?
- Como uma atualização condicional impede concorrência incorreta?
- Qual é a diferença entre erro no CSV e erro de infraestrutura?
- Como evitar que o usuário acesse documentos de outra conta?
- Por que DynamoDB faz sentido para os padrões de acesso escolhidos?
- O que muda se o limite de upload passar de 2 MiB para 500 MiB?
- Qual problema o Stream resolve e o que ele não resolve?
- O que significa uma mensagem ter expirado no Stream de 24 horas?
- Como OIDC elimina chaves AWS permanentes no GitHub?
- Quais recursos continuam cobrando depois de `cdk destroy`?

## Como descrever a experiência

Apresente como **projeto pessoal/prático**, distinguindo implementação, testes locais e implantação real na AWS. Após estudar e validar, descreva as decisões e funcionalidades que você consegue explicar. Não declare clientes, métricas de escala ou operação em produção que não ocorreram.

## Evoluções possíveis

- Paginação por cursor e políticas de uso por usuário.
- Upload direto ao S3 para arquivos grandes.
- Reconciliação agendada de trabalhos não despachados.
- Mais layouts de CSV e regras configuráveis.
- Auditoria de ações e compartilhamento controlado de documentos.
- CloudFront para os arquivos estáticos se o tráfego justificar separar o frontend.
- Testes automatizados de integração em uma conta AWS isolada.
