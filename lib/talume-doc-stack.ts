import * as cdk from 'aws-cdk-lib';
import { Construct } from 'constructs';
import * as path from 'path';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import * as sqs from 'aws-cdk-lib/aws-sqs';
import * as lambda from 'aws-cdk-lib/aws-lambda';
import * as events from 'aws-cdk-lib/aws-lambda-event-sources';
import * as cognito from 'aws-cdk-lib/aws-cognito';
import * as apigateway from 'aws-cdk-lib/aws-apigatewayv2';
import * as integrations from 'aws-cdk-lib/aws-apigatewayv2-integrations';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as pipes from 'aws-cdk-lib/aws-pipes';
import * as logs from 'aws-cdk-lib/aws-logs';
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
import * as actions from 'aws-cdk-lib/aws-cloudwatch-actions';
import * as sns from 'aws-cdk-lib/aws-sns';
import * as subscriptions from 'aws-cdk-lib/aws-sns-subscriptions';

export interface TalumeDocStackProps extends cdk.StackProps { assetRoot?: string; }
export class TalumeDocStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: TalumeDocStackProps = {}) {
    super(scope, id, props);
    cdk.Tags.of(this).add('Project', 'TalumeDoc');
    cdk.Tags.of(this).add('ProductFamily', 'Talume');
    cdk.Tags.of(this).add('ManagedBy', 'AWS-CDK');
    const assets = props.assetRoot ?? path.join(__dirname, '../artifacts');
    const bucket = new s3.Bucket(this, 'Documents', {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL, enforceSSL: true,
      encryption: s3.BucketEncryption.S3_MANAGED, removalPolicy: cdk.RemovalPolicy.RETAIN,
      lifecycleRules: [{ expiration: cdk.Duration.days(30), abortIncompleteMultipartUploadAfter: cdk.Duration.days(1) }]
    });
    const table = new dynamodb.Table(this, 'Metadata', {
      partitionKey: { name: 'ownerId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'id', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      stream: dynamodb.StreamViewType.NEW_AND_OLD_IMAGES,
      pointInTimeRecoverySpecification: { pointInTimeRecoveryEnabled: true },
      timeToLiveAttribute: 'expiresAt', removalPolicy: cdk.RemovalPolicy.RETAIN
    });
    const dead = new sqs.Queue(this, 'ProcessingDeadLetters', { retentionPeriod: cdk.Duration.days(14), enforceSSL: true });
    const dispatchDead = new sqs.Queue(this, 'DispatchDeadLetters', { retentionPeriod: cdk.Duration.days(14), enforceSSL: true });
    const queue = new sqs.Queue(this, 'ProcessingQueue', {
      visibilityTimeout: cdk.Duration.seconds(180), retentionPeriod: cdk.Duration.days(4),
      deadLetterQueue: { queue: dead, maxReceiveCount: 5 }, enforceSSL: true
    });
    const http = new apigateway.HttpApi(this, 'HttpApi', { apiName: 'TalumeDoc', createDefaultStage: true });
    const stage = http.defaultStage!.node.defaultChild as apigateway.CfnStage;
    stage.defaultRouteSettings = { throttlingBurstLimit: 20, throttlingRateLimit: 10 };
    const pool = new cognito.UserPool(this, 'Users', {
      userPoolName: 'TalumeDoc', selfSignUpEnabled: true, signInAliases: { email: true },
      autoVerify: { email: true }, accountRecovery: cognito.AccountRecovery.EMAIL_ONLY,
      passwordPolicy: { minLength: 12, requireLowercase: true, requireUppercase: true, requireDigits: true, requireSymbols: true },
      removalPolicy: cdk.RemovalPolicy.RETAIN
    });
    const domain = pool.addDomain('Login', { cognitoDomain: { domainPrefix: `talumedoc-${this.account}-${this.region}` } });
    const client = pool.addClient('Web', {
      generateSecret: false, preventUserExistenceErrors: true,
      accessTokenValidity: cdk.Duration.minutes(60), idTokenValidity: cdk.Duration.minutes(60),
      oAuth: { flows: { authorizationCodeGrant: true }, scopes: [cognito.OAuthScope.OPENID, cognito.OAuthScope.EMAIL, cognito.OAuthScope.PROFILE],
        callbackUrls: [http.url!], logoutUrls: [http.url!] }
    });
    const environment = { DOCUMENT_BUCKET: bucket.bucketName, DOCUMENT_TABLE: table.tableName };
    const apiLogs = new logs.LogGroup(this, 'ApiLogs', { retention: logs.RetentionDays.ONE_WEEK, removalPolicy: cdk.RemovalPolicy.DESTROY });
    const workerLogs = new logs.LogGroup(this, 'WorkerLogs', { retention: logs.RetentionDays.ONE_WEEK, removalPolicy: cdk.RemovalPolicy.DESTROY });
    const api = new lambda.Function(this, 'Api', {
      runtime: lambda.Runtime.DOTNET_10, architecture: lambda.Architecture.X86_64,
      handler: 'TalumeDoc.Api', code: lambda.Code.fromAsset(path.join(assets, 'api.zip')),
      memorySize: 512, timeout: cdk.Duration.seconds(30), logGroup: apiLogs,
      environment: { ...environment, StorageMode: 'Aws', ASPNETCORE_ENVIRONMENT: 'Production',
        COGNITO_ISSUER: `https://cognito-idp.${this.region}.${this.urlSuffix}/${pool.userPoolId}`, COGNITO_CLIENT_ID: client.userPoolClientId,
        COGNITO_DOMAIN: domain.baseUrl() }
    });
    const worker = new lambda.Function(this, 'Worker', {
      runtime: lambda.Runtime.DOTNET_10, architecture: lambda.Architecture.X86_64,
      handler: 'TalumeDoc.Worker::TalumeDoc.Worker.Function::Handle',
      code: lambda.Code.fromAsset(path.join(assets, 'worker')), memorySize: 1024,
      timeout: cdk.Duration.seconds(30), logGroup: workerLogs, environment
    });
    table.grant(api, 'dynamodb:GetItem', 'dynamodb:PutItem', 'dynamodb:Query');
    table.grant(worker, 'dynamodb:GetItem', 'dynamodb:PutItem');
    bucket.grantPut(api, 'inputs/*');
    bucket.grantRead(api, 'reports/*');
    bucket.grantRead(worker, 'inputs/*');
    bucket.grantPut(worker, 'reports/*');
    worker.addEventSource(new events.SqsEventSource(queue, { batchSize: 1, reportBatchItemFailures: true, maxConcurrency: 2 }));
    const integration = new integrations.HttpLambdaIntegration('ApiIntegration', api);
    http.addRoutes({ path: '/', methods: [apigateway.HttpMethod.ANY], integration });
    http.addRoutes({ path: '/{proxy+}', methods: [apigateway.HttpMethod.ANY], integration });
    const pipeRole = new iam.Role(this, 'DispatchRole', { assumedBy: new iam.ServicePrincipal('pipes.amazonaws.com') });
    table.grantStreamRead(pipeRole);
    queue.grantSendMessages(pipeRole);
    dispatchDead.grantSendMessages(pipeRole);
    const pipe = new pipes.CfnPipe(this, 'Dispatch', {
      roleArn: pipeRole.roleArn, source: table.tableStreamArn!, target: queue.queueArn,
      sourceParameters: {
        filterCriteria: { filters: [{ pattern: JSON.stringify({ eventName: ['INSERT', 'MODIFY'], dynamodb: { NewImage: { status: { S: ['Queued'] } } } }) }] },
        dynamoDbStreamParameters: { startingPosition: 'TRIM_HORIZON', batchSize: 1,
          maximumRecordAgeInSeconds: 82800, maximumRetryAttempts: 10000,
          deadLetterConfig: { arn: dispatchDead.queueArn } }
      },
      targetParameters: { inputTemplate: '{"ownerId": "<$.dynamodb.NewImage.ownerId.S>", "documentId": "<$.dynamodb.NewImage.id.S>"}' }
    });
    pipe.node.addDependency(pipeRole);
    const topic = new sns.Topic(this, 'OperationalAlerts');
    const alarmEmail = this.node.tryGetContext('alarmEmail') as string | undefined;
    if (alarmEmail) topic.addSubscription(new subscriptions.EmailSubscription(alarmEmail));
    for (const [name, monitored] of [['Processing', dead], ['Dispatch', dispatchDead]] as const) {
      const alarm = new cloudwatch.Alarm(this, `${name}Failures`, {
        metric: monitored.metricApproximateNumberOfMessagesVisible({ period: cdk.Duration.minutes(1) }),
        threshold: 1, evaluationPeriods: 1, treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING
      });
      alarm.addAlarmAction(new actions.SnsAction(topic));
    }
    const backlog = new cloudwatch.Alarm(this, 'QueueAge', {
      metric: queue.metricApproximateAgeOfOldestMessage({ period: cdk.Duration.minutes(1) }),
      threshold: 600, evaluationPeriods: 2, treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING
    });
    backlog.addAlarmAction(new actions.SnsAction(topic));
    const dashboard = new cloudwatch.Dashboard(this, 'Operations');
    dashboard.addWidgets(new cloudwatch.GraphWidget({ title: 'Processamento e falhas', left: [worker.metricInvocations(), worker.metricErrors(), dead.metricApproximateNumberOfMessagesVisible()] }),
      new cloudwatch.GraphWidget({ title: 'Fila e API', left: [queue.metricApproximateNumberOfMessagesVisible(), api.metricErrors()] }));
    new cdk.CfnOutput(this, 'ApplicationUrl', { value: http.url! });
    new cdk.CfnOutput(this, 'UserPoolId', { value: pool.userPoolId });
    new cdk.CfnOutput(this, 'BucketName', { value: bucket.bucketName });
    new cdk.CfnOutput(this, 'TableName', { value: table.tableName });
    new cdk.CfnOutput(this, 'QueueUrl', { value: queue.queueUrl });
    new cdk.CfnOutput(this, 'ProcessingDeadLetterUrl', { value: dead.queueUrl });
    new cdk.CfnOutput(this, 'DispatchDeadLetterUrl', { value: dispatchDead.queueUrl });
    new cdk.CfnOutput(this, 'PipeName', { value: pipe.ref });
    new cdk.CfnOutput(this, 'AlertsTopicArn', { value: topic.topicArn });
    new cdk.CfnOutput(this, 'DeploymentInfo', { value: `${this.node.tryGetContext('repoName') ?? 'manual'} @ ${this.node.tryGetContext('gitCommit') ?? 'local'}` });
  }
}
