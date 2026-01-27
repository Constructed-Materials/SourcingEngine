# 🔄 CMU DATA MIGRATION PLAN

**Purpose:** Transform flat CMU tables → Kawneer-style schemas  
**Challenge:** Lots of data in wrong structure  
**Solution:** Phased migration with SQL transformation + manual intelligence

---

## 🛡️ DATA SAFETY GUARANTEE (CRITICAL!)

> **RULE: The flat tables are NEVER deleted during migration.**
>
> - We only CREATE new tables and INSERT into them
> - Original data remains accessible at all times
> - If anything goes wrong, we can start over

### Safety Workflow:
```
STEP 1: COUNT all source rows (documented below)
STEP 2: CREATE backup tables (before ANY change)
STEP 3: CREATE new empty schema/tables
STEP 4: INSERT from flat tables (copy, not move)
STEP 5: VERIFY row counts match
STEP 6: ADD intelligence fields
STEP 7: KEEP flat tables for 30 days minimum
```

### Recovery Plan:
```sql
-- If ANYTHING goes wrong:
DROP SCHEMA richvale CASCADE;  -- Drop the new schema
-- Flat tables (richvale_*) are STILL THERE - unchanged!
-- Start over
```

---

## 📊 COMPLETE DATA INVENTORY (VERIFIED 2026-01-18)

### RICHVALE YORK (22 tables, 320 records)

| Table | Records | Category |
|-------|---------|----------|
| richvale_acoustic_reference | 11 | Performance |
| richvale_block_weight_comparison | 6 | Specs |
| richvale_carboclave | 1 | Technology |
| richvale_carboclave_leed_credits | 8 | Sustainability |
| richvale_csa_four_facet | 4 | Standards |
| richvale_density_types | 2 | Classification |
| richvale_fire_ratings | 16 | Compliance |
| richvale_frr_examples | 1 | Compliance |
| richvale_horizontal_coursing | 52 | Dimensions |
| richvale_leed_recycled_content | 4 | Sustainability |
| richvale_leed_summary | 1 | Sustainability |
| richvale_masonry_dimensions | 2 | Dimensions |
| richvale_mpa_designations | 4 | Strength |
| richvale_product_specifications | 2 | Specs |
| richvale_size_codes | 6 | Classification |
| richvale_ultra_lite_specs | 16 | Products |
| richvale_unit_catalog | 27 | **PRODUCTS** |
| richvale_vertical_coursing | 52 | Dimensions |
| richvale_york_colors | 37 | **FINISHES** |
| richvale_york_locations | 2 | Business |
| richvale_york_products | 47 | Products |
| richvale_york_terms | 19 | Business |
| **TOTAL** | **320** | |

### BRAMPTON BRICK (23 tables, 341 records)

| Table | Records | Category |
|-------|---------|----------|
| brampton_brick_block_specs | 15 | Specs |
| brampton_brick_business_terms | 4 | Business |
| brampton_brick_carboclave | 1 | Technology |
| brampton_brick_carboclave_shapes | 14 | Products |
| brampton_brick_cleaning_methods | 4 | Application |
| brampton_brick_compressive_strength | 30 | Performance |
| brampton_brick_contact | 1 | Business |
| brampton_brick_csa_facets | 4 | Standards |
| brampton_brick_dimensions | 5 | Dimensions |
| brampton_brick_fire_ratings | 31 | Compliance |
| brampton_brick_locations | 8 | Business |
| brampton_brick_physical_properties | 6 | Performance |
| brampton_brick_references | 8 | Standards |
| brampton_brick_sealant_guidance | 21 | Application |
| brampton_brick_sound_properties | 30 | Performance |
| brampton_brick_special_features | 3 | Features |
| brampton_brick_textures | 3 | **FINISHES** |
| brampton_brick_thermal | 10 | Performance |
| brampton_brick_tolerances | 6 | Standards |
| brampton_brick_unit_mass | 60 | Specs |
| brampton_brick_units | 12 | **PRODUCTS** |
| brampton_brick_wall_mass | 60 | Specs |
| brampton_brick_water_absorption | 5 | Performance |
| **TOTAL** | **341** | |

### BOEHMERS (25 tables, 180 records)

| Table | Records | Category |
|-------|---------|----------|
| boehmers_2rib_split_face_dimensions | 10 | Products |
| boehmers_3rib_split_face_dimensions | 10 | Products |
| boehmers_4rib_vslot_dimensions | 10 | Products |
| boehmers_6rib_split_face_dimensions | 10 | Products |
| boehmers_autoclave_benefits | 6 | Technology |
| boehmers_business_terms | 7 | Business |
| boehmers_company_info | 1 | Business |
| boehmers_contact | 1 | Business |
| boehmers_frr_examples | 2 | Compliance |
| boehmers_manufacturing_marks | 3 | Classification |
| boehmers_mpa_designations | 4 | Strength |
| boehmers_ncma_rvalue_evaluation | 18 | Performance |
| boehmers_single_score_dimensions | 11 | Products |
| boehmers_smooth_ledge_dimensions | 10 | Products |
| boehmers_special_considerations | 6 | Application |
| boehmers_specification_suggestions | 5 | Application |
| boehmers_split_face_ashlar_dimensions | 1 | Products |
| boehmers_split_face_dimensions | 10 | Products |
| boehmers_split_face_ledge_dimensions | 10 | Products |
| boehmers_standard_block_dimensions | 11 | **PRODUCTS** |
| boehmers_therma_bloc | 1 | Technology |
| boehmers_therma_bloc_features | 15 | Features |
| boehmers_therma_bloc_performance | 4 | Performance |
| boehmers_therma_bloc_sizes | 3 | Products |
| boehmers_v_slot_dimensions | 11 | Products |
| **TOTAL** | **180** | |

### GRAND TOTAL

| Vendor | Tables | Records |
|--------|--------|---------|
| Richvale York | 22 | 320 |
| Brampton Brick | 23 | 341 |
| Boehmers | 25 | 180 |
| **TOTAL** | **70** | **841** |

---

## 🎯 THE APPROACH: HYBRID MIGRATION

```
PHASE 1: Create new schemas (empty structure)
    ↓
PHASE 2: Migrate SPECS via SQL (automated)
    ↓
PHASE 3: Add INTELLIGENCE manually (use_when, best_for)
    ↓
PHASE 4: Add ALTERNATIVES manually (cross-vendor)
    ↓
PHASE 5: Verify & distribute to public
```

---

## 📊 DATA MAPPING: Flat Tables → New Schema

### RICHVALE YORK (29 flat tables → 4 schema tables)

| Flat Table(s) | → | New Location | Method |
|---------------|---|--------------|--------|
| `richvale_unit_catalog` | → | `richvale.products_enriched` | SQL Transform |
| `richvale_fire_ratings` | → | `products_enriched.performance_data` JSONB | SQL Transform |
| `richvale_block_weight_comparison` | → | `products_enriched.technical_specs` JSONB | SQL Transform |
| `richvale_csa_four_facet` | → | `products_enriched.technical_specs` JSONB | SQL Transform |
| `richvale_carboclave` | → | `products_enriched.sustainability` JSONB | SQL Transform |
| `richvale_leed_*` | → | `products_enriched.sustainability` JSONB | SQL Transform |
| `richvale_colors` | → | `richvale.product_finishes` | SQL Transform |
| `richvale_*_coursing` | → | Keep in public (universal) | No change |
| *(NEW)* | → | `richvale.assembly_knowledge` | **MANUAL** |
| *(NEW)* | → | `richvale.product_alternatives` | **MANUAL** |
| **Intelligence fields** | → | `use_when`, `best_for` | **MANUAL** |

### BRAMPTON BRICK (23 flat tables → 4 schema tables)

| Flat Table(s) | → | New Location | Method |
|---------------|---|--------------|--------|
| `brampton_brick_units` | → | `brampton_brick.products_enriched` | SQL Transform |
| `brampton_brick_fire_ratings` | → | `performance_data` JSONB | SQL Transform |
| `brampton_brick_thermal` | → | `performance_data` JSONB | SQL Transform |
| `brampton_brick_sound_properties` | → | `performance_data` JSONB | SQL Transform |
| `brampton_brick_physical_properties` | → | `technical_specs` JSONB | SQL Transform |
| `brampton_brick_rainbloc` | → | `key_features` JSONB | SQL Transform |
| `brampton_brick_carboclave` | → | `sustainability` JSONB | SQL Transform |
| `brampton_brick_textures` | → | `brampton_brick.product_finishes` | SQL Transform |
| *(NEW)* | → | `brampton_brick.assembly_knowledge` | **MANUAL** |
| *(NEW)* | → | `brampton_brick.product_alternatives` | **MANUAL** |

### BOEHMERS (24 flat tables → 4 schema tables)

| Flat Table(s) | → | New Location | Method |
|---------------|---|--------------|--------|
| `boehmers_standard_block_dimensions` | → | `boehmers.products_enriched` | SQL Transform |
| `boehmers_*_dimensions` (11 tables) | → | Multiple products in `products_enriched` | SQL Transform |
| `boehmers_autoclave_benefits` | → | `key_features` JSONB | SQL Transform |
| `boehmers_therma_bloc*` | → | Separate product in `products_enriched` | SQL Transform |
| `boehmers_ncma_rvalue_evaluation` | → | `performance_data` JSONB | SQL Transform |
| `boehmers_mpa_designations` | → | `technical_specs` JSONB | SQL Transform |
| *(NEW)* | → | `boehmers.assembly_knowledge` | **MANUAL** |
| *(NEW)* | → | `boehmers.product_alternatives` | **MANUAL** |

---

## 📋 PHASE 1: CREATE SCHEMAS (30 min)

```sql
-- Create vendor schemas
CREATE SCHEMA IF NOT EXISTS richvale;
CREATE SCHEMA IF NOT EXISTS brampton_brick;
CREATE SCHEMA IF NOT EXISTS boehmers;

-- Create products_enriched for each
-- (Use template from 03_TRACK_B_VENDOR_SCHEMA_PATTERN.md)
```

---

## 📋 PHASE 2: MIGRATE SPECS (SQL Automated)

### Example: Richvale products_enriched

```sql
-- Step 1: Create the new table
CREATE TABLE richvale.products_enriched (
    product_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_name TEXT NOT NULL,
    unit_number TEXT,                    -- From richvale_unit_catalog
    size_code INTEGER,                   -- From richvale_unit_catalog
    family_label TEXT DEFAULT 'cmu',
    
    -- INTELLIGENCE FIELDS (add manually later)
    use_when TEXT,
    dont_use_when TEXT,
    best_for TEXT,
    not_recommended_for TEXT,
    
    -- JSONB for flexible specs
    technical_specs JSONB,     -- CSA facets, dimensions
    performance_data JSONB,    -- Fire, thermal, acoustic
    sustainability JSONB,      -- GWP, LEED, Carboclave
    key_features JSONB,        -- Weight, special features
    
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Step 2: Insert products from unit_catalog
INSERT INTO richvale.products_enriched (
    product_name,
    unit_number,
    size_code,
    family_label
)
SELECT 
    unit_name,
    unit_number,
    size_code,
    'cmu'
FROM richvale_unit_catalog;

-- Step 3: Update with fire rating data (JSONB)
UPDATE richvale.products_enriched pe
SET performance_data = jsonb_build_object(
    'fire_ratings', (
        SELECT jsonb_agg(jsonb_build_object(
            'thickness', fr.thickness,
            'rating_1hr', fr.rating_1hr,
            'rating_2hr', fr.rating_2hr
        ))
        FROM richvale_fire_ratings fr
        WHERE fr.size_code = pe.size_code
    )
);

-- Step 4: Update with weight data
UPDATE richvale.products_enriched pe
SET key_features = COALESCE(key_features, '{}'::jsonb) || jsonb_build_object(
    'weight', (
        SELECT jsonb_build_object(
            'normal_weight_kg', bw.normal_weight,
            'ultra_lite_kg', bw.ultra_lite_weight,
            'weight_reduction_pct', bw.reduction_percent
        )
        FROM richvale_block_weight_comparison bw
        WHERE bw.size_code = pe.size_code
    )
);

-- Step 5: Update with sustainability data
UPDATE richvale.products_enriched pe
SET sustainability = jsonb_build_object(
    'carboclave', (SELECT row_to_json(c) FROM richvale_carboclave c LIMIT 1),
    'leed_credits', (SELECT jsonb_agg(row_to_json(l)) FROM richvale_leed_credit_contributions l),
    'ccmpa_epd', TRUE,
    'ccmpa_gwp_category', 'Normal Weight CMU'
);
```

### Example: Migrate colors to product_finishes

```sql
CREATE TABLE richvale.product_finishes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES richvale.products_enriched(product_id),
    finish_name TEXT NOT NULL,
    color_family TEXT,
    premium BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Insert colors (linked to ALL products for now)
INSERT INTO richvale.product_finishes (product_id, finish_name, color_family, premium)
SELECT 
    pe.product_id,
    rc.color_name,
    rc.color_family,
    rc.premium
FROM richvale_colors rc
CROSS JOIN richvale.products_enriched pe;
-- Note: This creates finish options for all products
-- May need to refine which colors apply to which products
```

---

## 📋 PHASE 3: ADD INTELLIGENCE (MANUAL - Most Important!)

This is where the VALUE comes from. Must be done manually with understanding.

### Template for Each Product:

```sql
-- For Richvale 20cm Standard Block
UPDATE richvale.products_enriched
SET 
    use_when = 'Loadbearing walls requiring 2hr fire rating, commercial/industrial foundations, below-grade applications',
    dont_use_when = 'Non-structural interior partitions, decorative applications where split-face preferred',
    best_for = 'Ontario region, fire-rated assemblies, LEED projects (CCMPA EPD available)',
    not_recommended_for = 'Lightweight applications (use Ultra Lite), moisture-critical without proper detailing'
WHERE product_name LIKE '%20cm%Standard%';

-- For Richvale 20cm Ultra Lite Block
UPDATE richvale.products_enriched
SET 
    use_when = 'Fire-rated assemblies where weight reduction important, upper floors, seismic zones',
    dont_use_when = 'Below-grade without waterproofing, high-load bearing applications',
    best_for = 'Tilt-up construction, high-rise masonry, LEED projects',
    not_recommended_for = 'Applications where standard weight is required for ballast'
WHERE product_name LIKE '%Ultra Lite%';
```

### Intelligence Template for CMU:

| Product Type | use_when | dont_use_when | best_for |
|--------------|----------|---------------|----------|
| **Standard Block** | Loadbearing, fire-rated, foundations | Non-structural, decorative | Commercial, industrial |
| **Lightweight Block** | Fire-rated, upper floors | Below-grade, high moisture | High-rise, acoustic |
| **Split Face** | Architectural finish, exterior | Interior, painted finish | Facade, feature walls |
| **Scored** | Decorative pattern, exterior | Structural hidden | Commercial facades |
| **Ledge Block** | Window/door support | Non-opening walls | Jamb details |
| **Bond Beam** | Horizontal reinforcement | Standard coursing | Lintel, belt courses |

---

## 📋 PHASE 4: ADD ALTERNATIVES (MANUAL)

Cross-vendor alternatives enable AI to recommend competitors.

```sql
-- Create alternatives table
CREATE TABLE richvale.product_alternatives (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES richvale.products_enriched(product_id),
    alternative_vendor TEXT,
    alternative_product TEXT,
    relationship_type TEXT,  -- 'equivalent', 'upgrade', 'downgrade'
    when_to_switch TEXT,
    trade_offs TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Example: Richvale vs Brampton Brick
INSERT INTO richvale.product_alternatives (
    product_id,
    alternative_vendor,
    alternative_product,
    relationship_type,
    when_to_switch,
    trade_offs
)
SELECT 
    product_id,
    'Brampton Brick',
    '20cm Standard Block',
    'equivalent',
    'If RainBloc integral water repellent required',
    'Brampton has RainBloc system, Richvale has more color options'
FROM richvale.products_enriched
WHERE product_name LIKE '%20cm%Standard%';

-- Example: Richvale vs Boehmers
INSERT INTO richvale.product_alternatives (
    product_id,
    alternative_vendor,
    alternative_product,
    relationship_type,
    when_to_switch,
    trade_offs
)
SELECT 
    product_id,
    'Boehmers',
    '20cm Standard Block',
    'equivalent',
    'If autoclave curing required for moisture-critical applications',
    'Boehmers has autoclave (no efflorescence), Richvale has Cambridge colors'
FROM richvale.products_enriched
WHERE product_name LIKE '%20cm%Standard%';
```

---

## 📋 PHASE 5: VERIFY & DISTRIBUTE

### Verify Schema:
```sql
-- Check products
SELECT product_name, use_when, best_for 
FROM richvale.products_enriched;

-- Check finishes
SELECT pe.product_name, COUNT(pf.id) as finish_count
FROM richvale.products_enriched pe
JOIN richvale.product_finishes pf ON pf.product_id = pe.product_id
GROUP BY pe.product_name;

-- Check alternatives
SELECT pe.product_name, pa.alternative_vendor, pa.when_to_switch
FROM richvale.products_enriched pe
JOIN richvale.product_alternatives pa ON pa.product_id = pe.product_id;
```

### Distribute to Public:
```sql
-- Insert to public.products
INSERT INTO public.products (product_id, vendor_id, family_label, model_name)
SELECT product_id, 
       (SELECT vendor_id FROM vendors WHERE name = 'Richvale York'),
       family_label,
       product_name
FROM richvale.products_enriched;

-- Insert to public.product_knowledge
INSERT INTO public.product_knowledge (product_id, use_when, dont_use_when, best_for)
SELECT product_id, use_when, dont_use_when, best_for
FROM richvale.products_enriched
WHERE use_when IS NOT NULL;
```

---

## ⏱️ TIME ESTIMATE

| Phase | Richvale | Brampton | Boehmers | Total |
|-------|----------|----------|----------|-------|
| 1. Create schemas | 10 min | 10 min | 10 min | 30 min |
| 2. Migrate specs (SQL) | 1 hr | 1 hr | 1.5 hr | 3.5 hr |
| 3. Add intelligence | 2 hr | 2 hr | 2 hr | 6 hr |
| 4. Add alternatives | 1 hr | 1 hr | 1 hr | 3 hr |
| 5. Verify & distribute | 30 min | 30 min | 30 min | 1.5 hr |
| **TOTAL** | **~5 hr** | **~5 hr** | **~5.5 hr** | **~14 hr** |

---

## 🎯 RECOMMENDED APPROACH

### Option A: Do One Vendor Fully (Recommended)
```
1. Complete Richvale York first (5 hours)
2. Learn from the process
3. Apply to Brampton Brick (faster with learnings)
4. Apply to Boehmers (fastest)
```

### Option B: Do All Phases Together
```
1. Create all schemas (30 min)
2. Migrate all specs (3.5 hr)
3. Add all intelligence (6 hr) - This is the long part
4. Add all alternatives (3 hr)
5. Verify all (1.5 hr)
```

### Option C: Minimal First
```
1. Create schemas with just products_enriched
2. Add intelligence for TOP 5 products per vendor only
3. Skip finishes/alternatives initially
4. Add more detail as needed
```

---

## ⚠️ IMPORTANT NOTES

1. **DO NOT DELETE flat tables until new structure verified**
2. **Intelligence fields (use_when, best_for) require UNDERSTANDING**
   - Can't be automated - needs construction knowledge
3. **Universal tables stay in public** (coursing, EPD, fire codes)
4. **Alternatives can be added incrementally** after main migration

---

## ✅ READY TO START?

**Recommended first step:**
1. Create `richvale` schema
2. Create `richvale.products_enriched` table
3. Migrate products from `richvale_unit_catalog`
4. Add intelligence for the 20cm Standard Block (most common)
5. Verify it works
6. Continue with more products

**Want me to start with Richvale?**
