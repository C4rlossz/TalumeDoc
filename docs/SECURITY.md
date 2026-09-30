# Segurança e limites

## Controles implementados

- Cognito Authorization Code + PKCE (`S256`) com estado aleatório, validade de dez minutos para a tentativa de login e callback na origem publicada.
- JWT de acesso: assinatura, expiração, emissor, `token_use`, `client_id` e `sub` verificados pelo backend. ID tokens não autorizam chamadas.
- Identidade vem do token; endpoints não aceitam proprietário fornecido pelo cliente.
- Consulta por chave composta do proprietário, inclusive para relatórios e reenvios. Resposta 404 para documento de outra conta.
- Bucket privado, HTTPS obrigatório, criptografia gerenciada e nomes de objetos internos. Relatórios são baixados pela API autenticada, sem bucket público.
- Permissões distintas: API grava entradas e lê relatórios; worker lê entradas e grava relatórios. Ações de DynamoDB limitadas por componente.
- Atualização condicional e token de concessão impedem finalização por worker antigo.
- Limites de tamanho/linhas e nomes; nenhuma execução de fórmulas ou de conteúdo do CSV.
- Mensagens do relatório são geradas pelo sistema e não incluem células fornecidas pelo usuário, reduzindo o risco de CSV formula injection.
- A interface usa `textContent` ao apresentar nomes e resultados. CSP sem scripts inline, bloqueio de frames e `nosniff`.
- Tokens de sessão ficam em `sessionStorage`, com expiração. Não há refresh token persistido. Fechar a aba remove a sessão local; tokens emitidos podem continuar válidos até expirar.
- Modo local explicitamente impedido em produção e no AWS Lambda.
- GitHub Actions usa OIDC; nenhum segredo AWS é incluído no projeto.

## Antes de abrir para uso real

Esta versão é um projeto de portfólio. Revise política de retenção, termos de uso, proteção contra abuso, quotas por usuário e necessidade de malware scanning conforme os dados e usuários reais. O cadastro Cognito está habilitado. Não anuncie conformidade regulatória, certificação, auditoria independente ou uso empresarial em produção sem realizar essas etapas.

O modo local tem um único usuário e não é apropriado para exposição pública. Os testes com JWT usam chaves RSA temporárias geradas durante a execução; essas chaves não fazem parte da autenticação em produção.

## Relatar problemas

Abra uma issue sem tokens, chaves, dados pessoais ou arquivos confidenciais. Para vulnerabilidade explorável em uma implantação, use o recurso privado de security advisory do repositório, se habilitado pelo proprietário.
