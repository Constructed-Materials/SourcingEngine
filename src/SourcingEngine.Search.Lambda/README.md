# SourcingEngine Search Lambda

AWS Lambda function that runs construction product search for each BOM line item. Triggered by extraction results from the BOM Extraction Lambda via Amazon MQ (RabbitMQ). Uses Amazon Bedrock for semantic embeddings and Supabase PostgreSQL for product catalog queries.

## Architecture

```
Amazon MQ (RabbitMQ)                    AWS Lambda                         Amazon Bedrock
┌──────────────────┐    Event Source   ┌──────────────────┐   Embeddings  ┌───────────────────┐
│ bom-extraction-  │───Mapping (ESM)──▶│  Function.cs     │─────────────▶│ Titan Embed v2    │
│ result-queue     │                   │  (container img) │              └───────────────────┘
└──────────────────┘                   │                  │   Parsing     ┌───────────────────┐
                                       │  Per BOM item:   │─────────────▶│ Nova Lite v1      │
                                       │  1. Search       │              └───────────────────┘
                                       │  2. Split output │
                                       │  3. Publish      │──▶ Supabase PostgreSQL
┌──────────────────┐                   └──────┬───────────┘
│ search-results-  │◀──search.result──────────┤
│ queue (≥1 match) │                          │
└──────────────────┘                          │
┌──────────────────┐                          │
│ zero-results-    │◀──search.zero-result─────┤
│ queue (0 matches)│                          │
└──────────────────┘                          │ (on failure)
┌──────────────────┐                          │
│ sourcing-engine- │◀──DLX routing────────────┘
│ poison-queue     │   (via policy on trigger queue)
└──────────────────┘
```

## Message Contracts

**Input** (consumed from `bom-extraction-result-queue`, published by BOM Extraction Lambda):
```json
{
  "traceId": "uuid-string",
  "projectId": 42,
  "sourceFile": "estimate.csv",
  "sourceUrl": "https://...",
  "itemCount": 15,
  "items": [
    { "bomItem": "CMU Block", "spec": "8 inch masonry block" },
    { "bomItem": "Floor Truss", "spec": "Pre Engineered Wood Floor Trusses" }
  ],
  "warnings": [],
  "modelUsed": "us.amazon.nova-pro-v1:0"
}
```

**Result** (published to `sourcing-engine-search-results-queue`, routing key `search.result`):
```json
{
  "traceId": "uuid-string",
  "fileName": "estimate.csv",
  "items": [
    {
      "bomItem": "CMU Block",
      "spec": "8 inch masonry block",
      "products": [
        {
          "productId": "uuid",
          "vendor": "Boehmers Block",
          "modelName": "Stretcher 20cm",
          "modelCode": "BOE-STD-6",
          "familyLabel": "cmu_blocks",
          "score": 0.92
        }
      ]
    }
  ],
  "publishedAt": "2026-02-25T12:00:00Z"
}
```

**Zero Results** (published to `sourcing-engine-search-zero-results-queue`, routing key `search.zero-result`):
```json
{
  "traceId": "uuid-string",
  "fileName": "estimate.csv",
  "items": [
    { "bomItem": "Custom Trim", "spec": "custom aluminum trim piece" }
  ],
  "publishedAt": "2026-02-25T12:00:00Z"
}
```

## Project Structure

```
src/
  SourcingEngine.Search.Lambda/                 # Lambda function
    Function.cs                                 # Entry point handler
    Dockerfile                                  # Container image build
    appsettings.json                            # Default config
    Configuration/
      SearchLambdaSettings.cs                   # Broker/exchange config
    Services/
      RabbitMqSearchResultPublisher.cs          # Publishes to results queues

  SourcingEngine.Search.Lambda.LocalRunner/     # Console app for F5 debugging
    Program.cs
    LocalRunner.cs                              # Live consumer + event replay

  SourcingEngine.Common/Models/                 # Shared contracts
    QueueMessages.cs                            # ExtractionResultMessage,
                                                # SourcingResultMessage,
                                                # SourcingZeroResultsMessage

infra/
  BomExtractionLambdaCdk/                       # CDK infrastructure
    BomExtractionLambdaStack.cs                 # Stack (includes Search Lambda)
    lambda/rabbitmq-topology/index.py           # Custom Resource for queues

tests/
  SourcingEngine.Search.Lambda.Tests/           # Unit tests (16 tests)
    FunctionTests.cs                            # Handler logic (10 tests)
    QueueMessageSerializationTests.cs           # JSON contract tests (5 tests)

scripts/
  deploy-sourcing-lambda.sh                     # Build + push + deploy
```

## Local Development

### Prerequisites
- .NET 9 SDK
- Docker Desktop
- AWS CLI (configured with Bedrock access in us-east-2)
- Access to Supabase PostgreSQL database

### 1. Start local RabbitMQ
```bash
cd src/SourcingEngine.BomExtraction.Lambda/local
docker compose -f docker-compose.local.yml up -d
```

The `rabbitmq-definitions.json` file pre-creates all exchanges, queues, and bindings for both the BOM Extraction and Search pipelines.

RabbitMQ Management UI: http://localhost:15672 (guest/guest)

### 2. Debug with VS Code (F5)
Two launch configurations are available:

- **Search Lambda: Local RabbitMQ Consumer** — Connects to local RabbitMQ, consumes from `bom-extraction-result-queue`, runs the search pipeline, publishes results
- **Search Lambda: Replay Event File** — Replays a saved RabbitMQ Lambda event JSON through the handler

### 3. Debug manually
```bash
# Build and run the local runner
dotnet run --project src/SourcingEngine.Search.Lambda.LocalRunner

# Trigger by publishing to local RabbitMQ Management UI:
# Exchange: bom.extraction, Routing key: extract.result
# Payload: (paste an ExtractionResultMessage JSON)
```

### 4. Run tests
```bash
dotnet test tests/SourcingEngine.Search.Lambda.Tests
```

## Deployment

### First-time setup
```bash
# 1. Ensure Secrets Manager secret exists for broker credentials
#    (shared with BOM Extraction Lambda)

# 2. Update CDK context in infra/BomExtractionLambdaCdk/cdk.json
#    - Set sourcingDbConnectionString with Supabase connection string
#    - Verify brokerArn, brokerSecretArn are set

# 3. Bootstrap CDK if not already done
cd infra/BomExtractionLambdaCdk
cdk bootstrap aws://ACCOUNT_ID/us-east-2

# 4. Deploy everything
../../scripts/deploy-sourcing-lambda.sh --deploy
```

### Subsequent deployments
```bash
# Build + push new image + update Lambda
./scripts/deploy-sourcing-lambda.sh --deploy

# Or just build + push image (no CDK changes)
./scripts/deploy-sourcing-lambda.sh
```

### CDK dry run
```bash
./scripts/deploy-sourcing-lambda.sh --synth
```

## Configuration

Environment variables (set by CDK stack):

| Variable | Description | Default |
|---|---|---|
| `Database__ConnectionString` | Supabase PostgreSQL connection string | — |
| `Database__MaxConcurrentSchemaQueries` | Parallel vendor schema queries | `5` |
| `SemanticSearch__Enabled` | Enable vector similarity search | `true` |
| `SemanticSearch__DefaultMode` | Search mode | `FamilyFirst` |
| `SemanticSearch__MatchCount` | Max semantic matches to return | `10` |
| `SemanticSearch__SimilarityThreshold` | Minimum cosine similarity | `0.65` |
| `Bedrock__Enabled` | Enable Bedrock for embeddings | `true` |
| `Bedrock__Region` | AWS region for Bedrock | `us-east-2` |
| `Bedrock__EmbeddingModelId` | Embedding model | `amazon.titan-embed-text-v2:0` |
| `Bedrock__EmbeddingDimension` | Embedding vector size | `1024` |
| `Bedrock__ParsingModelId` | Query parsing model | `us.amazon.nova-lite-v1:0` |
| `Bedrock__TimeoutSeconds` | Bedrock call timeout | `30` |
| `Lambda__BrokerHost` | MQ broker hostname | — |
| `Lambda__BrokerPort` | AMQPS port | `5671` |
| `Lambda__BrokerSecretArn` | Secrets Manager ARN | — |
| `Lambda__BrokerUseSsl` | Use SSL for broker | `true` |
| `Lambda__ResultExchange` | Result exchange name | `sourcing.engine` |
| `Lambda__ResultRoutingKey` | Routing key for matches | `search.result` |
| `Lambda__ZeroResultRoutingKey` | Routing key for zero matches | `search.zero-result` |

## Key Design Decisions

| Decision | Rationale |
|---|---|
| **Container image** (not zip) | Multi-project build context (Common, Core, Data, Lambda); consistent with BOM Extraction Lambda |
| **1024 MB memory** | Semantic search + parallel DB queries + Bedrock embedding calls need more memory than extraction |
| **300s timeout** | Running search for N BOM items sequentially; each item involves embedding + DB queries |
| **Batch size = 1** | Each extraction result may have 15+ items requiring search; larger batches risk timeout |
| **Two output routing keys** | Zero-result items need separate handling (manual review, catalog gaps); clean separation |
| **DLX via RabbitMQ policy** | Non-destructive — applies DLX to existing `bom-extraction-result-queue` without recreating it |
| **Shared CDK stack** | Both Lambdas share broker, VPC, security group — single stack avoids cross-stack references |
| **RabbitMQ connection reuse** | Publisher lazy-inits once, reuses across warm Lambda invocations |
| **Separate LocalRunner project** | Lambda is a class library; console project enables F5 debugging with live RabbitMQ |

## RabbitMQ Topology

Created automatically by CDK Custom Resource during `cdk deploy`:

```
Exchanges:
  sourcing.engine       (direct, durable)    — results exchange
  sourcing.engine.dlx   (direct, durable)    — dead letter exchange

Queues:
  sourcing-engine-search-results-queue       ← routing key: search.result
  sourcing-engine-search-zero-results-queue  ← routing key: search.zero-result
  sourcing-engine-poison-queue               ← routing key: search.poison (DLX)

Policy:
  sourcing-dlx-on-extraction-results
    pattern: ^bom-extraction-result-queue$
    dead-letter-exchange: sourcing.engine.dlx
    dead-letter-routing-key: search.poison
```
