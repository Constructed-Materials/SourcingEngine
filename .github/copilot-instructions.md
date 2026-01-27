# Copilot Instructions - SourcingEngine

## Architecture Overview

This is a .NET 8 **construction materials marketplace** search engine connecting BOM (Bill of Materials) inputs to intelligent product recommendations. NOT a catalog—it's a transactional matching system.

**Three-Track Architecture:**
- **Track A** (complete): Universal product extraction → 71 vendors, 558 models in database
- **Track B** (active): Deep vendor intelligence with "use_when/dont_use_when" reasoning
- **Track C** (planned): Vision AI plan analysis → automated BOM generation

**Core Layers:**
- `SourcingEngine.Console` → CLI entry point, DI configuration
- `SourcingEngine.Core` → Domain models, services, repository interfaces
- `SourcingEngine.Data` → Npgsql implementations, parallel vendor queries

## Database Rules

**The Backbone:** `cm_master_materials.family_label` (119 families) is THE central FK. Every product links here.

**Vendor Schema Pattern** (follow Kawneer as reference):
```
{vendor}.products_enriched     ← Main table WITH intelligence fields
{vendor}.assembly_knowledge    ← Component options
{vendor}.product_alternatives  ← Upsell/cross-sell
{vendor}.product_finishes      ← Colors/coatings
```

**Required Intelligence Fields** in `products_enriched`:
- `use_when` - When to recommend this product
- `dont_use_when` - When NOT to use
- `best_for` - Ideal applications
- `not_recommended_for` - Avoid scenarios

**Safety Protocol:** NEVER delete source tables during migrations. Use INSERT...SELECT, backup first, verify counts.

## Search Pipeline (8 Steps)

The `SearchOrchestrator` implements: Parse → Find Family → Resolve CSI → Find Vendors → Filter Size → Get Intelligence → Parallel Vendor Data → Aggregate Results.

**Key Services:**
- `SizeCalculator` - Bidirectional imperial ↔ metric (`8"` ↔ `20cm` ↔ `200mm`)
- `SynonymExpander` - Construction terminology (`masonry block` → `cmu`, `concrete block`)
- `InputNormalizer` - Combines size + synonym expansion

## Build & Test Commands

```bash
dotnet build                                    # Build solution
dotnet run --project src/SourcingEngine.Console -- "8 inch masonry block"  # Run search
dotnet test                                     # All tests
dotnet test --filter "Category!=Integration"   # Unit tests only (no DB)
dotnet test --filter "Category=Acceptance"     # E2E acceptance tests
```

**Test Thresholds:** Acceptance tests use minimum match counts (e.g., `≥3 matches`) to handle production data changes.

## Code Patterns

**Repository Pattern:** Interfaces in `Core/Repositories/`, implementations in `Data/Repositories/`. Always inject via interface.

**Parallel Queries:** Use `Task.WhenAll` for multi-vendor schema queries (see `ProductEnrichedRepository`).

**Adding Synonyms:** Edit `SynonymExpander.SynonymDictionary` in [SynonymExpander.cs](src/SourcingEngine.Core/Services/SynonymExpander.cs).

**Adding Size Patterns:** Edit regex patterns in [SizeCalculator.cs](src/SourcingEngine.Core/Services/SizeCalculator.cs).

## Memory Bank

Before making database or vendor changes, read `/docs/MEMORY_BANK/` folder:
- `00_READ_FIRST_SESSION_START.md` - Quick orientation
- `02_DATABASE_RULES.md` - FK relationships, backbone rules
- `03_TRACK_B_VENDOR_SCHEMA_PATTERN.md` - Exact schema to replicate
- `07_DATA_SAFETY_PROTOCOL.md` - Migration safety checklist

## Connection

Database: Supabase PostgreSQL (DEV: `dtxsieykjcvspzbsrrln`). Connection string in `appsettings.json`. Use Session Pooler for IPv4 networks.
