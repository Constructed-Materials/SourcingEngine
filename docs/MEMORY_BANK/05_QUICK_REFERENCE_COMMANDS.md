# ⚡ QUICK REFERENCE COMMANDS

**Purpose:** Common SQL queries and commands for Track A/B/C work  
**Database:** DEV (dtxsieykjcvspzbsrrln)

---

## 🔌 SESSION START COMMANDS

### 1. Verify DEV Connection
```sql
SELECT 'DEV Connected' as status, 
       (SELECT COUNT(*) FROM cm_master_materials) as families,
       (SELECT COUNT(*) FROM information_schema.tables 
        WHERE table_schema = 'kawneer') as kawneer_tables;
```

### 2. Check Backbone
```sql
SELECT family_label, family_name, csi_division 
FROM cm_master_materials 
WHERE family_label IN ('curtain_wall', 'cmu', 'stucco', 'storefront')
ORDER BY family_label;
```

### 3. Check Kawneer Schema (THE PATTERN)
```sql
SELECT table_name, 
       (SELECT COUNT(*) FROM kawneer.products_enriched) as products,
       (SELECT COUNT(*) FROM kawneer.assembly_knowledge) as assembly,
       (SELECT COUNT(*) FROM kawneer.product_alternatives) as alternatives
FROM information_schema.tables 
WHERE table_schema = 'kawneer' 
AND table_name = 'products_enriched';
```

---

## 📊 DATABASE EXPLORATION

### List All Schemas
```sql
SELECT schema_name 
FROM information_schema.schemata 
WHERE schema_name NOT IN (
    'information_schema', 'pg_catalog', 'pg_toast', 
    'extensions', 'graphql', 'graphql_public', 
    'realtime', 'supabase_functions', 'storage', 
    'vault', 'pgsodium', 'pgsodium_masks', 'auth'
)
ORDER BY schema_name;
```

### List Tables in Schema
```sql
-- Kawneer (THE PATTERN)
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'kawneer'
ORDER BY table_name;

-- Public (check for flat tables)
SELECT table_name 
FROM information_schema.tables 
WHERE table_schema = 'public'
AND table_name LIKE '%vendor_prefix%'
ORDER BY table_name;
```

### Count Records in Table
```sql
SELECT 
    'cm_master_materials' as table_name, COUNT(*) as records 
FROM cm_master_materials
UNION ALL
SELECT 'kawneer.products_enriched', COUNT(*) FROM kawneer.products_enriched
UNION ALL
SELECT 'kawneer.assembly_knowledge', COUNT(*) FROM kawneer.assembly_knowledge;
```

---

## 🔍 KAWNEER SCHEMA QUERIES

### View Products with Intelligence
```sql
SELECT 
    product_name,
    model_code,
    use_when,
    dont_use_when,
    best_for
FROM kawneer.products_enriched;
```

### View Assembly Components
```sql
SELECT 
    pe.product_name,
    ak.component_type,
    ak.option_code,
    ak.use_when
FROM kawneer.assembly_knowledge ak
JOIN kawneer.products_enriched pe ON pe.product_id = ak.product_id
ORDER BY pe.product_name, ak.component_type;
```

### View Alternatives
```sql
SELECT 
    pe.product_name as main_product,
    pa.relationship_type,
    pa.when_to_switch,
    pa.trade_offs
FROM kawneer.product_alternatives pa
JOIN kawneer.products_enriched pe ON pe.product_id = pa.product_id;
```

### View Finishes
```sql
SELECT 
    pe.product_name,
    pf.finish_name,
    pf.finish_type,
    pf.color_family
FROM kawneer.product_finishes pf
JOIN kawneer.products_enriched pe ON pe.product_id = pf.product_id
ORDER BY pe.product_name, pf.finish_type;
```

---

## 📋 CMU FLAT TABLES QUERIES

### List All CMU Flat Tables
```sql
SELECT table_name, 
       (xpath('//row/count/text()', 
              query_to_xml('SELECT COUNT(*) FROM ' || table_name, false, false, ''))
       )[1]::text::int as row_count
FROM information_schema.tables 
WHERE table_schema = 'public'
AND (table_name LIKE 'richvale%' 
     OR table_name LIKE 'brampton_brick%' 
     OR table_name LIKE 'boehmers%')
ORDER BY table_name;
```

### Simpler Count Query
```sql
SELECT 'richvale_*' as prefix, COUNT(*) as tables
FROM information_schema.tables 
WHERE table_schema = 'public' AND table_name LIKE 'richvale%'
UNION ALL
SELECT 'brampton_brick_*', COUNT(*)
FROM information_schema.tables 
WHERE table_schema = 'public' AND table_name LIKE 'brampton_brick%'
UNION ALL
SELECT 'boehmers_*', COUNT(*)
FROM information_schema.tables 
WHERE table_schema = 'public' AND table_name LIKE 'boehmers%';
```

### Sample Richvale Data
```sql
SELECT * FROM richvale_unit_catalog LIMIT 5;
SELECT * FROM richvale_colors LIMIT 5;
SELECT * FROM richvale_fire_ratings LIMIT 5;
```

---

## 🏗️ SCHEMA CREATION COMMANDS

### Create New Vendor Schema
```sql
CREATE SCHEMA IF NOT EXISTS vendor_name;
```

### Create products_enriched Table
```sql
CREATE TABLE vendor_name.products_enriched (
    product_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_name TEXT NOT NULL,
    model_code TEXT,
    family_label TEXT NOT NULL,
    csi_section TEXT,
    
    -- INTELLIGENCE FIELDS
    use_when TEXT,
    dont_use_when TEXT,
    best_for TEXT,
    not_recommended_for TEXT,
    
    -- FLEXIBLE SPECS
    key_features JSONB,
    advantages JSONB,
    technical_specs JSONB,
    performance_data JSONB,
    sustainability JSONB,
    
    -- METADATA
    source_documents TEXT[],
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
```

### Create Supporting Tables
```sql
CREATE TABLE vendor_name.assembly_knowledge (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES vendor_name.products_enriched(product_id),
    component_type TEXT NOT NULL,
    option_code TEXT,
    option_name TEXT,
    use_when TEXT,
    compatible_with TEXT[],
    specifications JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE vendor_name.product_alternatives (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES vendor_name.products_enriched(product_id),
    alternative_product_id UUID,
    alternative_vendor TEXT,
    relationship_type TEXT,
    when_to_switch TEXT,
    comparison JSONB,
    trade_offs TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE vendor_name.product_finishes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES vendor_name.products_enriched(product_id),
    finish_name TEXT NOT NULL,
    finish_code TEXT,
    color_family TEXT,
    finish_type TEXT,
    specifications JSONB,
    premium BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

---

## 📤 DISTRIBUTION COMMANDS

### Insert to public.products
```sql
INSERT INTO public.products (
    product_id,
    vendor_id,
    family_label,
    model_name,
    csi_section_code
)
SELECT 
    product_id,
    (SELECT vendor_id FROM vendors WHERE name = 'Vendor Name'),
    family_label,
    product_name,
    csi_section
FROM vendor_name.products_enriched;
```

### Insert Attributes
```sql
-- Insert flattened attributes
INSERT INTO public.product_attribute_values (
    product_id,
    attribute_key,
    value_text,
    value_num,
    source_type
)
SELECT 
    product_id,
    'use_when',
    use_when,
    NULL,
    'track_b'
FROM vendor_name.products_enriched
WHERE use_when IS NOT NULL;
```

---

## 🔧 UTILITY COMMANDS

### Check Table Structure
```sql
SELECT column_name, data_type, is_nullable
FROM information_schema.columns 
WHERE table_schema = 'schema_name' 
AND table_name = 'table_name'
ORDER BY ordinal_position;
```

### Check Foreign Keys
```sql
SELECT 
    tc.constraint_name,
    tc.table_name,
    kcu.column_name,
    ccu.table_name AS foreign_table,
    ccu.column_name AS foreign_column
FROM information_schema.table_constraints tc
JOIN information_schema.key_column_usage kcu 
    ON tc.constraint_name = kcu.constraint_name
JOIN information_schema.constraint_column_usage ccu 
    ON tc.constraint_name = ccu.constraint_name
WHERE tc.constraint_type = 'FOREIGN KEY'
AND tc.table_schema = 'schema_name';
```

### Backup Table Before Changes
```sql
CREATE TABLE backup_table_name AS 
SELECT * FROM original_table_name;
```

---

## ❌ COMMANDS TO AVOID

```sql
-- NEVER run these without explicit user approval:
DROP TABLE ...;
DELETE FROM ...;
TRUNCATE ...;
DROP SCHEMA ...;

-- ALWAYS ask before:
ALTER TABLE ... DROP COLUMN ...;
UPDATE ... SET ...;
```

---

## 📚 REFERENCE DOCUMENTS

| Document | Location | Purpose |
|----------|----------|---------|
| System Architecture | `docs/MEMORY_BANK/01_SYSTEM_ARCHITECTURE.md` | Track overview |
| Database Rules | `docs/MEMORY_BANK/02_DATABASE_RULES.md` | Backbone & structure |
| Vendor Pattern | `docs/MEMORY_BANK/03_TRACK_B_VENDOR_SCHEMA_PATTERN.md` | Kawneer pattern |
| CMU Status | `docs/MEMORY_BANK/04_CURRENT_STATE_CMU_VENDORS.md` | Current state |
| Kawneer Pilot | `docs/2025-11-15_track_b_kawneer_pilot/` | Full Kawneer docs |
| Master Status | `docs/PROJECT_MASTER_STATUS.md` | Overall project |

---

**End of Quick Reference**
