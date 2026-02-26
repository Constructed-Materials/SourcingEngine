# Sourcing Engine - Construction Materials Search Application

A .NET search engine that matches construction BOM (Bill of Materials) line items to product recommendations from a Supabase PostgreSQL database. Runs as a **console CLI** for development and as **AWS Lambda functions** in production, connected via Amazon MQ RabbitMQ.

## 🏗️ Architecture Overview

### Event-Driven Pipeline

```
┌──────────────┐      RabbitMQ       ┌──────────────────┐      RabbitMQ       ┌─────────────────┐
│   Frontend   │ ──extract.request─▶ │ BOM Extraction   │ ──extract.result──▶ │ Search Lambda   │
│   (submit    │                     │ Lambda           │                     │ (product search │
│    BOM file) │                     │ (Bedrock Nova)   │                     │  per BOM item)  │
└──────────────┘                     └──────────────────┘                     └───────┬─────────┘
                                                                                      │
                                                                         ┌────────────┴────────────┐
                                                                         │                         │
                                                                  search.result          search.zero-result
                                                                         │                         │
                                                                         ▼                         ▼
                                                               ┌─────────────────┐     ┌───────────────────┐
                                                               │ results-queue   │     │ zero-results-queue│
                                                               │ (≥1 products)   │     │ (0 products)      │
                                                               └─────────────────┘     └───────────────────┘
```

| Exchange | Queue | Routing Key | Purpose |
|----------|-------|-------------|---------|
| `bom.extraction` | `bom-extraction-queue` | `extract.request` | Incoming BOM file extraction requests |
| `bom.extraction` | `bom-extraction-result-queue` | `extract.result` | Extracted BOM line items → triggers Search Lambda |
| `sourcing.engine` | `sourcing-engine-search-results-queue` | `search.result` | Products found (≥1 match per item) |
| `sourcing.engine` | `sourcing-engine-search-zero-results-queue` | `search.zero-result` | Items with 0 matches |
| `bom.extraction.dlx` | `bom-extraction-poison-queue` | `extract.poison` | Extraction failures |
| `sourcing.engine.dlx` | `sourcing-engine-poison-queue` | `search.poison` | Search failures (DLX on result queue) |

### Core Layers

```
┌─────────────────────────────────────────────────────────┐
│              Console / Lambda Entry Points                │
│  • CLI for dev/testing                                   │
│  • BOM Extraction Lambda (Bedrock Nova Pro)              │
│  • Search Lambda (SearchOrchestrator)                    │
└────────────────┬────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────┐
│                    Core Layer                            │
│  • Domain models (BomItem, Product, SearchResult)        │
│  • Business logic services                               │
│    - SizeCalculator (bidirectional imperial ↔ metric)   │
│    - SynonymExpander (material terminology)             │
│    - SearchOrchestrator (8-step search pipeline)        │
│  • Repository interfaces (contracts)                     │
└────────────────┬────────────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────────────┐
│                    Data Layer                            │
│  • Npgsql repository implementations                     │
│  • Dynamic schema discovery                              │
│  • Parallel vendor query execution                       │
│  • Supabase PostgreSQL connection factory                │
└─────────────────────────────────────────────────────────┘
```

### Key Architectural Principles

1. **Dependency Inversion**: Core layer defines interfaces; Data layer implements them
2. **Single Responsibility**: Each service has one clear purpose
3. **Testability**: Interface-based design enables easy mocking and unit testing
4. **Scalability**: Parallel queries across vendor schemas for performance

## 🔍 8-Step Search Logic

The application implements a sophisticated search pipeline based on the documented search logic:

```mermaid
graph TD
    A[BOM Input: "8 inch masonry block"] --> B[Step 1: Parse & Normalize]
    B --> C[Step 2: Find Material Family]
    C --> D[Step 3: Resolve CSI Code]
    D --> E[Step 4: Find Vendors]
    E --> F[Step 5: Filter by Size]
    F --> G[Step 6: Get Product Intelligence]
    G --> H[Step 7: Get Deep Vendor Data]
    H --> I[Step 8: Aggregate Results]
    I --> J[JSON Output]
```

### Step Details

1. **Parse & Normalize Input**
   - Extract keywords: `["masonry", "block"]`
   - Calculate size variants: `["8\"", "8 inch", "20cm", "200mm"]`
   - Expand synonyms: `["cmu", "concrete block", "masonry unit"]`

2. **Find Material Family**
   - Query `cm_master_materials` table using keywords
   - Result: `family_label = "cmu_blocks"`

3. **Resolve CSI Code**
   - Match family to CSI MasterFormat
   - Result: `csi_code = "042200"` (Concrete Unit Masonry)

4. **Find Vendors**
   - Query `products` table filtered by family
   - Join with `vendors` table for metadata

5. **Filter by Size**
   - Apply size patterns to `model_name` column
   - Uses ILIKE pattern matching: `%20cm%`, `%8"%`

6. **Get Product Intelligence**
   - Query `product_knowledge` table
   - Retrieve use cases, specifications, applications

7. **Get Deep Vendor Data (Parallel)**
   - Query all 12 vendor schemas simultaneously using `Task.WhenAll`
   - Each schema: `{vendor}.products_enriched`
   - Extract: `use_when`, `key_features`, `technical_specs`, `performance_data`

8. **Aggregate Results**
   - Combine product base data with enriched intelligence
   - Return unified JSON response

## 🗂️ Project Structure

```
SourcingEngine/
├── SourcingEngine.sln
│
├── src/
│   ├── SourcingEngine.Common/                       # Shared models & contracts
│   │   └── Models/
│   │       └── QueueMessages.cs                     # DTOs for RabbitMQ messages
│   │
│   ├── SourcingEngine.Core/                         # Domain & Business Logic
│   │   ├── Models/
│   │   │   ├── BomItem.cs                           # Normalized input
│   │   │   ├── Product.cs                           # Base product
│   │   │   ├── ProductEnriched.cs                   # Vendor intelligence
│   │   │   ├── ProductMatch.cs                      # Search result item
│   │   │   ├── SearchResult.cs                      # Complete search response
│   │   │   └── MaterialFamily.cs                    # Material taxonomy
│   │   ├── Repositories/                            # Interfaces
│   │   └── Services/
│   │       ├── SizeCalculator.cs                    # Bidirectional size conversion
│   │       ├── SynonymExpander.cs                   # Terminology expansion
│   │       ├── InputNormalizer.cs                   # BOM text processing
│   │       └── SearchOrchestrator.cs                # 8-step search pipeline
│   │
│   ├── SourcingEngine.Data/                         # Database Access Layer
│   │   ├── NpgsqlConnectionFactory.cs               # Connection management
│   │   └── Repositories/                            # Npgsql implementations
│   │
│   ├── SourcingEngine.Console/                      # CLI Entry Point
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── SourcingEngine.BomExtraction/                # BOM extraction service
│   ├── SourcingEngine.BomExtraction.Lambda/         # BOM Extraction Lambda
│   │   ├── Function.cs                              # Lambda handler
│   │   ├── Dockerfile                               # Container image
│   │   └── local/
│   │       ├── rabbitmq-definitions.json             # Local RabbitMQ topology
│   │       └── test-event-template.json              # Sample event for replay
│   ├── SourcingEngine.BomExtraction.Lambda.LocalRunner/  # F5 debug harness
│   │
│   ├── SourcingEngine.Search.Lambda/                # ★ Search Lambda
│   │   ├── Function.cs                              # Lambda handler
│   │   ├── Dockerfile                               # Container image
│   │   ├── Configuration/
│   │   │   └── SearchLambdaSettings.cs              # Broker/exchange config
│   │   └── Services/
│   │       └── RabbitMqSearchResultPublisher.cs      # Publishes to results queues
│   └── SourcingEngine.Search.Lambda.LocalRunner/    # F5 debug harness
│       └── LocalRunner.cs                           # Live consumer + event replay
│
├── tests/
│   ├── SourcingEngine.Tests/                        # Core unit/integration tests
│   ├── SourcingEngine.BomExtraction.Tests/
│   ├── SourcingEngine.BomExtraction.Lambda.Tests/
│   └── SourcingEngine.Search.Lambda.Tests/          # ★ Search Lambda tests (16)
│       ├── FunctionTests.cs                         # Handler logic (10 tests)
│       └── QueueMessageSerializationTests.cs        # JSON contracts (5 tests)
│
├── infra/
│   └── BomExtractionLambdaCdk/                      # CDK infrastructure (C#)
│       ├── BomExtractionLambdaStack.cs               # Both Lambdas + topology
│       ├── cdk.json                                  # Context values
│       └── lambda/
│           └── rabbitmq-topology/
│               └── index.py                          # Custom Resource handler
│
└── scripts/
    ├── deploy-lambda.sh                              # BOM Extraction deploy
    └── deploy-sourcing-lambda.sh                     # Search Lambda deploy
```

## 🚀 Key Features

### 1. Bidirectional Size Conversion

Automatically converts between imperial and metric units in both directions:

```
Input: "8 inch masonry block"
→ Output: ["8\"", "8 inch", "20cm", "20 cm", "200mm", "200 mm"]

Input: "20cm concrete block"
→ Output: ["20cm", "20 cm", "200mm", "200 mm", "8\"", "8 inch"]
```

**Supported conversions:**
- 4" ↔ 10cm ↔ 100mm
- 6" ↔ 15cm ↔ 150mm
- 8" ↔ 20cm ↔ 200mm
- 10" ↔ 25cm ↔ 250mm
- 12" ↔ 30cm ↔ 300mm

### 2. Synonym Expansion

Expands construction terminology for comprehensive search coverage:

```
"masonry block" → ["cmu", "concrete block", "masonry unit", "block"]
"floor truss"   → ["joist", "i-joist", "bci", "floor joist"]
"stucco"        → ["eifs", "plaster", "stucco system"]
"railing"       → ["guardrail", "handrail", "balustrade"]
```

### 3. Dynamic Schema Discovery

Automatically discovers all vendor schemas at startup:

- Queries: `information_schema.tables WHERE table_name = 'products_enriched'`
- Discovers: 12 vendor schemas (boehmers, sto, kawneer, boise_cascade, etc.)
- Caches results for the session

### 4. Parallel Vendor Queries

Queries all vendor schemas simultaneously for optimal performance:

```csharp
var tasks = schemas.Select(schema => QuerySchemaAsync(schema, productIds));
var results = await Task.WhenAll(tasks);
```

**Resilient error handling:** If one schema query fails, continues with partial results from other schemas.

### 5. Comprehensive Logging

Uses `Microsoft.Extensions.Logging` throughout:

```
[INFO] Starting search for: 8" Masonry block
[DEBUG] Step 1: Normalizing input...
[INFO] Extracted 4 keywords, 6 size variants, 12 synonyms
[DEBUG] Step 2: Finding material family...
[INFO] Found material family: cmu_blocks (Concrete Masonry Units)
[INFO] Querying 12 vendor schemas in parallel...
[INFO] Found 7 products matching criteria
[INFO] Search completed in 245ms with 7 matches
```

## 📦 Dependencies

### Core Dependencies
- **.NET 9.0** - Target framework
- `Microsoft.Extensions.Logging.Abstractions` 8.0.0

### Data Layer Dependencies
- `Npgsql` 8.0.6 - PostgreSQL driver for .NET
- `Microsoft.Extensions.Options` 8.0.0 - Configuration binding

### Console Application
- `Microsoft.Extensions.Hosting` 8.0.0 - DI & configuration
- `Microsoft.Extensions.Configuration.Json` 8.0.0 - JSON config

### Test Dependencies
- `xunit` 2.7.0 - Test framework
- `Microsoft.NET.Test.Sdk` 17.9.0 - Test runner

## ⚙️ Setup & Configuration

### 1. Prerequisites

- .NET 9.0 SDK installed
- Access to Supabase PostgreSQL database
- Network access to Supabase (port 5432 or session pooler)

### 2. Database Connection

Update `appsettings.json` with your Supabase connection string:

```json
{
  "Database": {
    "ConnectionString": "Host=aws-1-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.dtxsieykjcvspzbsrrln;Password=YOUR_PASSWORD;SSL Mode=Require;Trust Server Certificate=true"
  }
}
```

**Connection Options:**

- **Direct Connection** (port 5432): Best for VMs/servers with static IPs
- **Session Pooler** (port 5432): Recommended for IPv4 networks with connection pooling
- **Transaction Pooler** (port 6543): For serverless environments

Get your connection string from Supabase Dashboard → **Settings → Database**.

### 3. Build & Restore

```bash
cd SourcingEngine
dotnet restore
dotnet build
```

## 🎯 Usage

### Running the Console Application

```bash
# Basic search
dotnet run --project src/SourcingEngine.Console -- "8 inch masonry block"

# With metric size
dotnet run --project src/SourcingEngine.Console -- "20cm concrete block"

# Complex BOM item
dotnet run --project src/SourcingEngine.Console -- "Pre Engineered Wood Floor Trusses"

# Stucco system
dotnet run --project src/SourcingEngine.Console -- "5/8 stucco on block"

# Railing
dotnet run --project src/SourcingEngine.Console -- "Ext Railing"
```

### Sample Output

```json
{
  "query": "8 inch masonry block",
  "sizeVariants": ["8\"", "8 inch", "20cm", "20 cm", "200mm", "200 mm"],
  "keywords": ["masonry", "block", "cmu", "concrete block", "masonry unit"],
  "familyLabel": "cmu_blocks",
  "csiCode": "042200",
  "matchCount": 7,
  "matches": [
    {
      "productId": "uuid-here",
      "vendor": "Boehmers Block",
      "modelName": "Stretcher 20cm (BOE-STD-6)",
      "modelCode": "BOE-STD-6",
      "csiCode": "042200",
      "useWhen": "Standard load-bearing walls in residential construction",
      "keyFeatures": ["High compressive strength", "Thermal mass benefits"],
      "technicalSpecs": {
        "width": "20cm",
        "height": "20cm",
        "length": "40cm"
      },
      "sourceSchema": "boehmers"
    }
  ],
  "executionTimeMs": 245,
  "warnings": []
}
```

## 🧪 Testing

### Run All Tests

```bash
dotnet test
```

### Run Only Unit Tests (No Database Required)

```bash
dotnet test --filter "Category!=Integration"
```

**27 unit tests** covering:
- Size calculator (imperial ↔ metric conversion)
- Synonym expander (terminology expansion)
- Input normalizer (keyword extraction)

### Run Integration Tests (Requires Database)

```bash
dotnet test --filter "Category=Integration"
```

**Integration tests** validate:
- Schema discovery (finds 12+ vendor schemas)
- Material family resolution
- Product repository queries
- Parallel vendor data fetching

### Run Acceptance Tests (End-to-End)

```bash
dotnet test --filter "Category=Acceptance"
```

**5 acceptance tests** based on documented test cases:
1. ✅ Masonry block search: ≥3 matches from ≥2 vendors
2. ✅ Floor joists search: ≥5 matches
3. ✅ Stucco system search: ≥3 matches
4. ✅ Railing search: ≥5 matches
5. ✅ Stair search: ≥3 matches
6. ✅ Bidirectional size conversion validation

**Test data stability:** Uses minimum thresholds instead of exact counts to handle production data changes.

## 🗄️ Database Schema

### Core Tables

| Table | Records | Purpose |
|-------|---------|---------|
| `public.cm_master_materials` | 124 | Material family taxonomy (THE BACKBONE) |
| `public.csi_sections` | 6,428 | CSI MasterFormat codes |
| `public.vendors` | 83 | Manufacturer directory |
| `public.products` | 205 | Main product catalog |
| `public.product_knowledge` | 151 | Deep product intelligence |

### Vendor Schemas

Each vendor has a dedicated schema with `products_enriched` table:

- `boehmers.products_enriched` - CMU blocks
- `richvale.products_enriched` - CMU blocks
- `brampton_brick.products_enriched` - CMU blocks
- `sto.products_enriched` - Stucco/EIFS systems
- `kawneer.products_enriched` - Curtain wall systems
- `boise_cascade.products_enriched` - Engineered wood
- `durock.products_enriched` - Stucco systems
- `century_railings.products_enriched` - Railings
- `baros_vision.products_enriched` - Glass railings
- ... and 3 more

### Guaranteed Columns Across All Vendor Schemas

```sql
SELECT 
  product_id,
  model_code,
  use_when,
  key_features,        -- JSONB
  technical_specs,     -- JSONB
  performance_data     -- JSONB
FROM {vendor_schema}.products_enriched
```

## 🔧 Extension Points

### Adding New Synonyms

Edit [SynonymExpander.cs](src/SourcingEngine.Core/Services/SynonymExpander.cs):

```csharp
private static readonly Dictionary<string, string[]> SynonymDictionary = new()
{
    ["your_term"] = ["synonym1", "synonym2", "synonym3"],
    // ...
};
```

### Adding New Size Conversions

The `SizeCalculator` automatically handles any imperial/metric conversion. To add custom mappings, extend the calculation logic in [SizeCalculator.cs](src/SourcingEngine.Core/Services/SizeCalculator.cs).

### Adding New Vendor Schemas

No code changes needed! The `SchemaDiscoveryService` automatically detects new vendor schemas at startup if they have a `products_enriched` table.

## 📊 Performance Characteristics

- **Schema Discovery**: ~50ms (cached after first call)
- **Material Family Lookup**: ~10-30ms
- **Product Search**: ~50-100ms
- **Parallel Vendor Queries**: ~100-200ms (12 schemas in parallel)
- **Total Search Time**: ~200-400ms typical

**Scalability:**
- Handles 100+ products per search
- Queries 12 vendor schemas in parallel
- Connection pooling via Npgsql
- Async/await throughout for non-blocking I/O

## 🐛 Troubleshooting

### Connection Refused Error

```
Failed to connect to 54.82.205.23:5432
```

**Solution:** Your network blocks port 5432. Use the **Session Pooler** connection string from Supabase Dashboard.

### "Tenant or user not found" Error

**Solution:** Verify the username format:
- Direct connection: `postgres`
- Session pooler: `postgres.{project_ref}`

### No Results Found

**Possible causes:**
1. Check if `family_label` was resolved (look at `warnings` in output)
2. Verify size pattern format (use quotes: `"8 inch"` not `8 inch`)
3. Enable debug logging: `"LogLevel": { "SourcingEngine": "Debug" }`

### Tests Fail with Database Errors

1. Verify connection string in `tests/SourcingEngine.Tests/appsettings.Test.json`
2. Check Supabase project is active (not paused)
3. Verify firewall allows outbound connections to Supabase

## ☁️ AWS Lambda Services

### BOM Extraction Lambda

Extracts structured BOM line items from uploaded documents (PDF, XLSX, CSV) using Amazon Bedrock Nova Pro.

- **Trigger:** `bom-extraction-queue` (via Amazon MQ event source mapping)
- **Output:** Publishes `ExtractionResultMessage` to `bom.extraction` exchange with routing key `extract.result`
- **Model:** `us.amazon.nova-pro-v1:0` (cross-region inference profile)
- **Timeout:** 180s, 512 MB

### Search Lambda

Runs the 8-step search pipeline for each BOM line item, finding matching products from the database.

- **Trigger:** `bom-extraction-result-queue` (output of the extraction Lambda)
- **Output:** Splits results into two queues:
  - `sourcing-engine-search-results-queue` — items with ≥1 product match
  - `sourcing-engine-search-zero-results-queue` — items with 0 matches
- **Models:** Titan Embed Text v2 (embeddings) + Nova Lite v1 (parsing)
- **Timeout:** 300s, 1024 MB
- **Dead Letter:** Failed messages go to `sourcing-engine-poison-queue` via DLX policy on the trigger queue

### Queue Message Contracts

All DTOs live in `SourcingEngine.Common/Models/QueueMessages.cs`:

| Message | Fields | Queue |
|---------|--------|-------|
| `ExtractionResultMessage` | traceId, fileName, items[], extractedAt | `bom-extraction-result-queue` |
| `SourcingResultMessage` | traceId, fileName, items[] (with products[]) | `sourcing-engine-search-results-queue` |
| `SourcingZeroResultsMessage` | traceId, fileName, items[], publishedAt | `sourcing-engine-search-zero-results-queue` |

## 🏗️ Infrastructure (CDK)

All infrastructure is managed in a single CDK stack: `infra/BomExtractionLambdaCdk/`.

### What the stack provisions

| Resource | Details |
|----------|---------|
| **BOM Extraction Lambda** | Docker container image, IAM role (Bedrock + S3 + MQ + Secrets Manager), event source mapping |
| **Search Lambda** | Docker container image, IAM role (Bedrock + MQ + Secrets Manager), event source mapping |
| **RabbitMQ Topology** | CDK Custom Resource that calls the RabbitMQ Management API to create exchanges, queues, bindings, and DLX policies |

### RabbitMQ Topology Custom Resource

Since CloudFormation has no native support for RabbitMQ queue/exchange management, we use a **Python Lambda Custom Resource** (`infra/BomExtractionLambdaCdk/lambda/rabbitmq-topology/index.py`) that:

1. Retrieves broker credentials from Secrets Manager
2. Calls the RabbitMQ Management HTTP API (port 443 on Amazon MQ)
3. Creates exchanges, queues, and bindings via idempotent PUT/POST calls
4. Applies DLX policies to existing queues (non-destructive)

**Properties:**
- **Idempotent** — safe to run on every deploy
- **Preserves on delete** — stack deletion does NOT remove queues/messages
- **Conditional** — only runs when `brokerSecretArn` is provided

### CDK Context Values (`cdk.json`)

| Key | Default | Purpose |
|-----|---------|--------|
| `brokerArn` | `arn:aws:mq:us-east-2:...` | Amazon MQ broker ARN |
| `brokerHost` | `b-24d11402-...on.aws` | Broker hostname for Management API |
| `brokerSecretArn` | (empty) | Secrets Manager ARN for broker credentials |
| `vpcId` | (empty) | VPC ID (only if broker is private) |
| `queueName` | `bom-extraction-queue` | BOM extraction trigger queue |
| `sourcingQueueName` | `bom-extraction-result-queue` | Search Lambda trigger queue |
| `sourcingDbConnectionString` | (empty) | Supabase PostgreSQL connection string |

### Deploy Commands

```bash
# BOM Extraction Lambda — build, push to ECR, deploy
./scripts/deploy-lambda.sh --deploy

# Search Lambda — build, push to ECR, deploy
./scripts/deploy-sourcing-lambda.sh --deploy

# CDK only (synth dry-run)
./scripts/deploy-lambda.sh --synth

# Image push only (no CDK deploy)
./scripts/deploy-sourcing-lambda.sh
```

Both scripts will:
1. Build a Docker image (`linux/amd64`)
2. Create the ECR repository if it doesn't exist
3. Push the image to ECR
4. (With `--deploy`) Run `cdk deploy` and update the Lambda function code

## 🔧 Local Development

### LocalRunner (F5 Debugging)

Each Lambda has a **LocalRunner** project for F5 debugging in VS Code:

| Launch Config | What it does |
|---------------|-------------|
| `Lambda: Local RabbitMQ Consumer` | Connects to local RabbitMQ, consumes from queue, invokes handler |
| `Lambda: Replay Event File` | Reads a saved JSON event file and replays it through the handler |
| `Search Lambda: Local RabbitMQ Consumer` | Same pattern for the Search Lambda |
| `Search Lambda: Replay Event File` | Replay a saved extraction result through search |

### Local RabbitMQ Setup

Use Docker Compose or standalone Docker with the definitions file:

```bash
docker run -d --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  -v $(pwd)/src/SourcingEngine.BomExtraction.Lambda/local/rabbitmq-definitions.json:/etc/rabbitmq/definitions.json \
  -e RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS='-rabbitmq_management load_definitions "/etc/rabbitmq/definitions.json"' \
  rabbitmq:3-management
```

This creates all exchanges, queues, and bindings locally (matching the production topology).

## 📝 License

Internal tool for MVP Partner Package. Not licensed for external use.

## 🤝 Contributing

This is a proof-of-concept implementation. For production use:

1. Add authentication/authorization
2. Implement caching (Redis/memory cache)
3. Add retry policies for database queries
4. Implement rate limiting
5. Add comprehensive error handling
6. Add API endpoints (REST/GraphQL)
7. Add pagination for large result sets

## 📚 Related Documentation

- [Database Schema](docs/01_DATABASE_SCHEMA_SIMPLE.md)
- [Test Cases](docs/02_TEST_CASES_WITH_RESULTS.md)
- [Search Logic A to Z](docs/05_SEARCH_LOGIC_A_TO_Z.md)
- [Memory Bank](docs/MEMORY_BANK/)
