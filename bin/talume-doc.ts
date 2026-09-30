#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib';
import { TalumeDocStack } from '../lib/talume-doc-stack';
const app = new cdk.App();
new TalumeDocStack(app, 'TalumeDoc', {
  stackName: 'TalumeDoc',
  env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: process.env.CDK_DEFAULT_REGION ?? 'us-east-1' }
});
