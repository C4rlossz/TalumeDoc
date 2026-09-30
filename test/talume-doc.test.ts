import * as cdk from 'aws-cdk-lib';
import { Template, Match } from 'aws-cdk-lib/assertions';
import { TalumeDocStack } from '../lib/talume-doc-stack';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'talumedoc-cdk-'));
for (const name of ['api', 'worker']) { fs.mkdirSync(path.join(root, name)); fs.writeFileSync(path.join(root, name, 'test-asset.txt'), 'Test asset only'); }
fs.writeFileSync(path.join(root, 'api.zip'), 'Test asset only');
const app = new cdk.App();
const stack = new TalumeDocStack(app, 'Test', { assetRoot: root, env: { account: '111111111111', region: 'us-east-1' } });
const template = Template.fromStack(stack);
afterAll(() => fs.rmSync(root, { recursive: true }));
test('Private encrypted bucket requires HTTPS', () => {
  template.hasResourceProperties('AWS::S3::Bucket', { PublicAccessBlockConfiguration: { BlockPublicAcls: true, BlockPublicPolicy: true, IgnorePublicAcls: true, RestrictPublicBuckets: true }, BucketEncryption: Match.anyValue() });
  template.hasResourceProperties('AWS::S3::BucketPolicy', { PolicyDocument: { Statement: Match.arrayWith([Match.objectLike({ Effect: 'Deny', Condition: { Bool: { 'aws:SecureTransport': 'false' } } })]) } });
});
test('Durable dispatch uses stream filter, retry and dead-letter queue', () => {
  template.hasResourceProperties('AWS::DynamoDB::Table', { BillingMode: 'PAY_PER_REQUEST', StreamSpecification: { StreamViewType: 'NEW_AND_OLD_IMAGES' }, TimeToLiveSpecification: { AttributeName: 'expiresAt', Enabled: true } });
  template.hasResourceProperties('AWS::Pipes::Pipe', { SourceParameters: { DynamoDBStreamParameters: Match.objectLike({ StartingPosition: 'TRIM_HORIZON', DeadLetterConfig: Match.anyValue() }), FilterCriteria: Match.anyValue() } });
});
test('Worker has partial-batch failures and bounded concurrency', () => {
  template.hasResourceProperties('AWS::Lambda::EventSourceMapping', { BatchSize: 1, FunctionResponseTypes: ['ReportBatchItemFailures'], ScalingConfig: { MaximumConcurrency: 2 } });
  template.hasResourceProperties('AWS::SQS::Queue', { VisibilityTimeout: 180, RedrivePolicy: Match.objectLike({ maxReceiveCount: 5 }) });
});
test('API cannot enable local authentication in AWS', () => {
  template.hasResourceProperties('AWS::Lambda::Function', { Runtime: 'dotnet10', Handler: 'TalumeDoc.Api', Environment: { Variables: Match.objectLike({ StorageMode: 'Aws', ASPNETCORE_ENVIRONMENT: 'Production', COGNITO_CLIENT_ID: Match.anyValue() }) } });
});
test('Web client uses authorization code and no client secret', () => {
  template.hasResourceProperties('AWS::Cognito::UserPoolClient', { GenerateSecret: false, AllowedOAuthFlows: ['code'] });
});
test('Operational alarms exist and resources are retained', () => {
  template.resourceCountIs('AWS::CloudWatch::Alarm', 3);
  template.hasResource('AWS::DynamoDB::Table', { DeletionPolicy: 'Retain' });
  template.hasResource('AWS::S3::Bucket', { DeletionPolicy: 'Retain' });
});
