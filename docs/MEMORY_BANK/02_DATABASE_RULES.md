# 🗄️ DATABASE RULES - Backbone & Structure

**Purpose:** Understand database architecture before making any changes  
**Database:** DEV (dtxsieykjcvspzbsrrln)

---

## 🛑 CRITICAL: PUBLIC SCHEMA ONLY FOR APPLICATION CODE

> **All application runtime queries (search, embedding, enrichment) MUST target `public.*` tables only.**
> Non-public vendor schemas (e.g., `kawneer.*`, `sto.*`, `boehmers.*`) are for **data curation/staging only**
> and MUST NOT be queried by the search engine at runtime.

**Allowed runtime tables:**
- `public.products` — product catalog
- `public.vendors` — vendor/manufacturer info  
- `public.product_knowledge` — description, use_cases, specifications
- `public.cm_master_materials` — material family backbone

**NEVER** use `information_schema.tables` to discover schemas dynamically at runtime.  
**NEVER** query `{vendor}.products_enriched` or any non-public schema from application code.

Enrichment data (description, use_cases, specifications) is sourced from `public.product_knowledge`,
which is already joined in the semantic search query.

---

## 🎯 THE BACKBONE: cm_master_materials

> **In DEV database, the backbone table is `cm_master_materials`**  
> (In older docs, this was called `taxonomy_full_v2`)

### Structure:
```sql
cm_master_materials (119 families)
├─ family_label (varchar) PRIMARY KEY  ← THE KEY FIELD
├─ family_name (varchar)               "Curtain Wall Systems"
├─ industry (varchar)                  "construction"
├─ csi_division (varchar)              "08"
├─ typical_lead_time_days (integer)    14
├─ typical_shipping_class (varchar)    "LTL"
└─ installation_required (boolean)     true
```

### Why It's Called THE BACKBONE:
- **EVERYTHING** connects through `family_label`
- All vendor schemas link to this for classification
- Products must have a valid `family_label`

### Example Queries:
```sql
-- List all families
SELECT family_label, family_name, csi_division 
FROM cm_master_materials 
ORDER BY family_label;

-- Check if CMU family exists
SELECT * FROM cm_master_materials WHERE family_label = 'cmu';

-- Check curtain wall family
SELECT * FROM cm_master_materials WHERE family_label = 'curtain_wall';
```

---

## 📊 DATABASE STRUCTURE

### Layer 1: Backbone (cm_master_materials)
```
cm_master_materials (119 families)
├─ "curtain_wall" - Curtain Wall Systems (CSI 08)
├─ "cmu" - Concrete Masonry Units (CSI 04)
├─ "stucco" - Stucco/Plaster (CSI 09)
├─ "storefront" - Storefront Systems (CSI 08)
└─ ... 115 more families
```

### Layer 2: Vendor Schemas (Track B Intelligence)
```
kawneer.* (THE REFERENCE PATTERN)
├─ products_enriched (3 products)
├─ detail_drawings (18 records)
├─ assembly_knowledge (33 components)
├─ product_alternatives (4 relationships)
└─ product_finishes (13 finishes)

richvale.* (TO BE CREATED - currently flat tables)
brampton_brick.* (TO BE CREATED - currently flat tables)
boehmers.* (TO BE CREATED - currently flat tables)
```

### Layer 3: Universal Tables (public.*)
```
public.products - Universal product catalog
public.product_attribute_values - Searchable attributes
public.product_knowledge - Application intelligence
public.product_relationships - Alternatives
public.csi_sections - CSI MasterFormat codes (6,425)
```

---

## 🔗 FOREIGN KEY RULES

### Rule 1: Products link to Backbone
```sql
-- Every product MUST have a valid family_label
products.family_label → cm_master_materials.family_label

-- Even if not a formal FK, this MUST match!
{vendor}.products_enriched.family_label → cm_master_materials.family_label
```

### Rule 2: Vendor tables link to products_enriched
```sql
-- All vendor tables link to the main product table
{vendor}.assembly_knowledge.product_id → {vendor}.products_enriched.product_id
{vendor}.product_alternatives.product_id → {vendor}.products_enriched.product_id
{vendor}.product_finishes.product_id → {vendor}.products_enriched.product_id
{vendor}.detail_drawings.product_id → {vendor}.products_enriched.product_id
```

### Rule 3: Public distribution
```sql
-- After vendor schema complete, distribute to public
public.products.product_id ← {vendor}.products_enriched.product_id
public.product_attribute_values.product_id → public.products.product_id
public.product_knowledge.product_id → public.products.product_id
```

---

## 🚨 DATABASE RULES (MUST FOLLOW)

### Rule 1: Always Check Backbone First
```sql
-- Before creating products, verify family exists
SELECT * FROM cm_master_materials 
WHERE family_label = 'your_family';
```

### Rule 2: Use Schemas, Not Flat Tables
```sql
-- CORRECT:
CREATE SCHEMA richvale;
CREATE TABLE richvale.products_enriched (...);

-- WRONG (what was done):
CREATE TABLE richvale_products (...);  -- No schema!
CREATE TABLE richvale_fire_ratings (...);  -- Flat table!
```

### Rule 3: Include Intelligence Fields
```sql
-- REQUIRED fields for products_enriched:
use_when TEXT,           -- "When to use this product"
dont_use_when TEXT,      -- "When NOT to use"
best_for TEXT,           -- "Ideal applications"
not_recommended_for TEXT -- "Avoid for..."
```

### Rule 4: Link Everything via Foreign Keys
```sql
-- All tables in a vendor schema link to products_enriched
REFERENCES {vendor}.products_enriched(product_id)
```

### Rule 5: Use source_type for Tracking
```sql
-- When inserting to public, track the source
source_type = 'track_b'  -- For Track B intelligence
source_type = 'track_a'  -- For Track A extraction
```

---

## 📋 UNIVERSAL TABLES (Reference Only)

These tables contain industry-wide data:

| Table | Purpose | Records |
|-------|---------|---------|
| `cm_master_materials` | THE BACKBONE - material families | 119 |
| `csi_sections` | CSI MasterFormat codes | 6,425 |
| `ccmpa_*` | Canadian CMU EPD data | Multiple |
| `obc_fire_resistance_table` | Ontario Building Code fire | ~10 |
| `csa_a165_four_facet_system` | Canadian CMU standard | ~4 |
| `insulation_terminology` | Insulation definitions | ~6 |
| `insulation_k_values` | Material k-values | ~12 |
| `masonry_impact_resistance` | Impact comparison | ~7 |

---

## ✅ QUICK VERIFICATION QUERIES

### Check Backbone:
```sql
SELECT COUNT(*) as families FROM cm_master_materials;
-- Expected: 119
```

### Check Kawneer Schema (THE PATTERN):
```sql
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'kawneer'
ORDER BY table_name;
-- Expected: 5-6 tables
```

### Check CMU Flat Tables (CURRENT STATE - NEEDS RESTRUCTURE):
```sql
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public' 
AND (table_name LIKE 'richvale%' 
     OR table_name LIKE 'brampton%' 
     OR table_name LIKE 'boehmers%')
ORDER BY table_name;
-- Returns: 70+ flat tables (WRONG structure)
```

---

## 🔴 CURRENT ISSUES

### CMU Vendors Have WRONG Structure:
```
CURRENT (WRONG):
├─ richvale_* (29 flat tables in public)
├─ brampton_brick_* (23 flat tables in public)
├─ boehmers_* (24 flat tables in public)
└─ No schemas, no intelligence fields, no connections

CORRECT (TO BUILD):
├─ richvale.products_enriched (with use_when, best_for)
├─ brampton_brick.products_enriched (with use_when, best_for)
├─ boehmers.products_enriched (with use_when, best_for)
└─ Each with assembly_knowledge, alternatives, finishes
```

---

**Next File:** `03_TRACK_B_VENDOR_SCHEMA_PATTERN.md` - Kawneer pattern details
