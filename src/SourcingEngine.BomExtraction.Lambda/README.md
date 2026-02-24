# BOM Extraction Lambda

AWS Lambda function that extracts BOM (Bill of Materials) line items from construction documents using Amazon Bedrock's Converse API (Amazon Nova Pro model). Triggered via Amazon MQ (RabbitMQ) event source mapping.

## Architecture

```
Amazon MQ (RabbitMQ)                    AWS Lambda                         Amazon Bedrock
┌──────────────────┐    Event Source   ┌──────────────────┐   Converse    ┌─────────────┐
│ bom-extraction-  │───Mapping (ESM)──▶│  Function.cs     │───API───────▶│ Nova Pro v1 │
│ queue            │                   │  (container img) │              └─────────────┘
└──────────────────┘                   │                  │
                                       │  1. Download BOM │──▶ S3 / HTTPS
┌──────────────────┐                   │  2. Extract      │
│ bom-extraction-  │◀──Publish result──│  3. Publish      │
│ result-queue     │                   └──────────────────┘
└──────────────────┘                          │ (on failure)
┌──────────────────┐                          │
│ bom-extraction-  │◀──DLX routing────────────┘
│ poison-queue     │   (message TTL expiry)
└──────────────────┘
```

## Message Contracts

**Request** (published by upstream service to `bom-extraction-queue`):
```json
{
  "traceId": "uuid-string",
  "projectId": 42,
  "bomFiles": [
    { "fileName": "estimate.csv", "url": "https://s3.../presigned-url" }
  ]
}
```

**Result** (published per-file to `bom-extraction-result-queue`):
```json
{
  "traceId": "uuid-string",
  "projectId": 42,
  "sourceFile": "estimate.csv",
  "sourceUrl": "https://...",
  "itemCount": 15,
  "items": [{ "bomItem": "CMU Block", "spec": "8 inch masonry block" }],
  "warnings": [],
  "modelUsed": "us.amazon.nova-pro-v1:0",
  "inputTokens": 1200,
  "outputTokens": 450
}
```

## Project Structure

```
src/
  SourcingEngine.BomExtraction.Lambda/         # Lambda function
    Function.cs                                 # Entry point handler
    Dockerfile                                  # Container image build
    Configuration/LambdaSettings.cs             # Environment config
    Models/QueueMessages.cs                     # Request/result contracts
    Services/RemoteFileFetcher.cs               # Download from S3/HTTPS
    Services/RabbitMqResultPublisher.cs         # Publish results to broker
    local/                                      # Local dev files
      docker-compose.local.yml                  # Local RabbitMQ
      rabbitmq-definitions.json                 # Queue/exchange topology
      test-event-template.json                  # Sample Lambda event

  SourcingEngine.BomExtraction.Lambda.LocalRunner/  # Console app for F5 debugging
    Program.cs
    LocalRunner.cs

infra/
  BomExtractionLambdaCdk/                       # CDK infrastructure
    BomExtractionLambdaStack.cs                 # Stack definition
    Program.cs                                  # CDK app entry

tests/
  SourcingEngine.BomExtraction.Lambda.Tests/    # Unit tests
    FunctionTests.cs                            # Handler tests (17 tests)
    QueueMessageSerializationTests.cs           # Contract tests

scripts/
  deploy-lambda.sh                              # Build + push + deploy
```

## Local Development

### Prerequisites
- .NET 9 SDK
- Docker Desktop
- AWS CLI (configured with Bedrock access in us-east-2)

### 1. Start local RabbitMQ
```bash
cd src/SourcingEngine.BomExtraction.Lambda/local
docker compose -f docker-compose.local.yml up -d
```

RabbitMQ Management UI: http://localhost:15672 (guest/guest)

### 2. Debug with VS Code (F5)
Two launch configurations are available:

- **Lambda: Local RabbitMQ Consumer** — Connects to local RabbitMQ, consumes from `bom-extraction-queue`, processes messages through the full pipeline
- **Lambda: Replay Event File** — Replays a saved RabbitMQ Lambda event JSON file

### 3. Debug manually
```bash
# Build and run the local runner
dotnet run --project src/SourcingEngine.BomExtraction.Lambda.LocalRunner

# Publish test messages via RabbitMQ Management UI:
# Exchange: bom.extraction, Routing key: extract.request
# Payload: {"traceId":"test-001","projectId":1,"bomFiles":[{"fileName":"test.csv","url":"https://your-presigned-url"}]}
```

### 4. Run tests
```bash
dotnet test tests/SourcingEngine.BomExtraction.Lambda.Tests
```

## Deployment

### First-time setup
```bash
# 1. Create Secrets Manager secret for broker credentials
aws secretsmanager create-secret \
  --name bom-extraction/rabbitmq-credentials \
  --secret-string '{"username":"cm-app-queue","password":"YOUR_PASSWORD"}' \
  --region us-east-2

# 2. Update CDK context in infra/BomExtractionLambdaCdk/cdk.json
#    - Set brokerArn, brokerSecretArn, vpcId with real values

# 3. Bootstrap CDK (one-time per account/region)
cd infra/BomExtractionLambdaCdk
cdk bootstrap aws://ACCOUNT_ID/us-east-2

# 4. Deploy everything
../../scripts/deploy-lambda.sh --deploy
```

### Subsequent deployments
```bash
# Build + push new image + update Lambda
./scripts/deploy-lambda.sh --deploy

# Or just build + push image (no CDK changes)
./scripts/deploy-lambda.sh
```

### CDK dry run
```bash
./scripts/deploy-lambda.sh --synth
```

## Configuration

Environment variables (set by CDK stack):

| Variable | Description | Default |
|---|---|---|
| `BomExtraction__Region` | AWS region for Bedrock | `us-east-2` |
| `BomExtraction__ModelId` | Bedrock model ID | `us.amazon.nova-pro-v1:0` |
| `BomExtraction__MaxTokens` | Max response tokens | `5000` |
| `BomExtraction__Temperature` | Model temperature | `0` |
| `Lambda__BrokerHost` | MQ broker hostname | — |
| `Lambda__BrokerPort` | AMQPS port | `5671` |
| `Lambda__BrokerSecretArn` | Secrets Manager ARN | — |
| `Lambda__ResultExchange` | Result exchange name | `bom.extraction` |
| `Lambda__ResultRoutingKey` | Result routing key | `extract.result` |

## Key Design Decisions

| Decision | Rationale |
|---|---|
| **Container image** (not zip) | Need multi-project build context, ~120s Bedrock calls justify cold start |
| **Lambda built-in retry** (not app-level) | ESM handles retry + concurrency; DLX routing via message TTL for poison |
| **Batch size = 1** | Each extraction takes 30–120s; larger batches risk timeout |
| **VPC-attached** | Amazon MQ broker is VPC-only; Lambda needs ENI for AMQPS:5671 |
| **RabbitMQ connection reuse** | Publisher lazy-inits once, reuses on warm Lambda invocations |
| **Separate LocalRunner project** | Lambda projects are class libraries; console project enables F5 debugging |
