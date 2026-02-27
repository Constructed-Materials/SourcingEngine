# 📐 TRACK B VENDOR SCHEMA PATTERN

**Purpose:** The exact pattern to follow for EVERY vendor  
**Reference:** Kawneer schema in DEV database  
**Rule:** ALL vendors must follow this structure

> **⚠️ IMPORTANT:** Vendor schemas are for **data curation and staging only**.
> The application code (search engine, embedding generation, Lambda functions) **NEVER** queries
> vendor schemas at runtime. Once data is ready, it is distributed to `public.product_knowledge`
> for use by the search pipeline. See `02_DATABASE_RULES.md` for the full schema restriction rules.

---

## 🎯 THE KAWNEER PATTERN (REFERENCE)

Kawneer was the pilot vendor. Its schema is THE PATTERN to replicate.

```sql
-- Kawneer schema structure (6 tables):
kawneer.products_enriched      -- Main product catalog with intelligence
kawneer.detail_drawings        -- CAD drawing analysis
kawneer.assembly_knowledge     -- Component options (jamb types, etc.)
kawneer.product_alternatives   -- Better/cheaper/faster alternatives
kawneer.product_finishes       -- Color and coating options
kawneer.product_images         -- Product imagery (if applicable)
```

---

## 📊 TABLE 1: products_enriched (MAIN TABLE)

This is the CORE table. It contains the product AND the intelligence.

```sql
CREATE TABLE {vendor}.products_enriched (
    -- IDENTITY
    product_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_name TEXT NOT NULL,
    model_code TEXT,
    family_label TEXT NOT NULL,  -- Links to cm_master_materials
    csi_section TEXT,
    
    -- INTELLIGENCE FIELDS (CRITICAL!)
    use_when TEXT,              -- "When to use this product"
    dont_use_when TEXT,         -- "When NOT to use"
    best_for TEXT,              -- "Ideal applications"
    not_recommended_for TEXT,   -- "Avoid for..."
    
    -- FLEXIBLE SPECS (JSONB)
    key_features JSONB,         -- Product features array
    advantages JSONB,           -- Why this product
    technical_specs JSONB,      -- Dimensions, measurements
    performance_data JSONB,     -- Fire, thermal, acoustic, wind
    sustainability JSONB,       -- GWP, EPD, LEED, certifications
    
    -- METADATA
    source_documents TEXT[],    -- Where data came from
    last_verified DATE,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);
```

### Example Intelligence Fields:

```sql
-- For Kawneer 1600UT SS:
use_when = 'Commercial high-rise buildings 3-25 stories, U-factor critical applications'
dont_use_when = 'Low-rise residential (1-2 stories), triple glazing required'
best_for = 'Fast-track construction, high-performance thermal requirements'
not_recommended_for = 'Budget projects (use 1600 Wall #1), maximum acoustic isolation'

-- For Richvale 20cm Block (TO BUILD):
use_when = 'Loadbearing walls, fire rating 2hr required, commercial/industrial'
dont_use_when = 'Non-structural partitions, interior aesthetic walls'
best_for = 'Foundation walls, commercial buildings, fire-rated assemblies'
not_recommended_for = 'Decorative applications (use split-face), lightweight needs'
```

---

## 📊 TABLE 2: assembly_knowledge (COMPONENTS)

Components and options that go WITH the product.

```sql
CREATE TABLE {vendor}.assembly_knowledge (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES {vendor}.products_enriched(product_id),
    
    -- COMPONENT IDENTITY
    component_type TEXT NOT NULL,  -- "Jamb", "Sill", "Header", "Mortar"
    option_code TEXT,              -- "5A", "5B", "Type S"
    option_name TEXT,
    
    -- INTELLIGENCE
    use_when TEXT,                 -- "When to use this option"
    compatible_with TEXT[],        -- What it works with
    incompatible_with TEXT[],      -- What it conflicts with
    
    -- SPECS
    specifications JSONB,
    dimensions JSONB,
    
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

### Example for Kawneer:
```sql
-- Jamb options for 1600UT SS:
component_type = 'Jamb'
option_code = '5A'
use_when = 'Thermally broken applications, where thermal performance is critical'
compatible_with = ['Thermal Spacer', 'Setting Block Type A']
```

### Example for CMU (TO BUILD):
```sql
-- Mortar options for Richvale block:
component_type = 'Mortar'
option_code = 'Type S'
use_when = 'Below grade, high wind, seismic zones'
compatible_with = ['Loadbearing walls', 'Reinforced masonry']
```

---

## 📊 TABLE 3: product_alternatives (RECOMMENDATIONS)

Alternative products for upsell/cross-sell/better fit.

```sql
CREATE TABLE {vendor}.product_alternatives (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES {vendor}.products_enriched(product_id),
    alternative_product_id UUID,  -- Can be same vendor or different
    alternative_vendor TEXT,      -- If different vendor
    
    -- RELATIONSHIP
    relationship_type TEXT,       -- "upgrade", "downgrade", "equivalent", "complement"
    
    -- INTELLIGENCE (CRITICAL!)
    when_to_switch TEXT,          -- "If budget constrained..."
    comparison JSONB,             -- Price, performance delta
    trade_offs TEXT,              -- What you gain/lose
    
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

### Example for Kawneer:
```sql
product_id = (1600UT SS)
alternative_product_id = (1600 Wall #1)
relationship_type = 'downgrade'
when_to_switch = 'If budget constrained and thermal performance is secondary'
trade_offs = '-15% cost, +2 weeks fabrication, shear block method'
```

### Example for CMU (TO BUILD):
```sql
product_id = (Richvale 20cm Standard)
alternative_product_id = (Brampton Brick 20cm)
alternative_vendor = 'Brampton Brick'
relationship_type = 'equivalent'
when_to_switch = 'If RainBloc water repellent system required'
trade_offs = 'Similar performance, Brampton has integral water repellent'
```

---

## 📊 TABLE 4: product_finishes (COLORS/COATINGS)

Color and finish options for the product.

```sql
CREATE TABLE {vendor}.product_finishes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES {vendor}.products_enriched(product_id),
    
    -- FINISH IDENTITY
    finish_name TEXT NOT NULL,
    finish_code TEXT,
    color_family TEXT,           -- "Grey", "Earth Tones", "Premium"
    finish_type TEXT,            -- "Standard", "Premium", "Custom"
    
    -- SPECS
    specifications JSONB,
    certifications TEXT[],       -- "C2C Certified", "LEED"
    premium BOOLEAN DEFAULT FALSE,
    lead_time_days INTEGER,
    
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

### Example for CMU (TO BUILD):
```sql
-- Richvale York colors:
finish_name = 'Cambridge Premier - Charcoal'
finish_code = 'CP-CHR'
color_family = 'Cambridge Premier'
finish_type = 'Premium'
premium = TRUE
```

---

## 📊 TABLE 5: detail_drawings (CAD ANALYSIS)

CAD drawing analysis with component details.

```sql
CREATE TABLE {vendor}.detail_drawings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES {vendor}.products_enriched(product_id),
    
    -- DRAWING IDENTITY
    drawing_name TEXT,
    drawing_number TEXT,
    drawing_type TEXT,           -- "Section", "Elevation", "Detail"
    page_number INTEGER,
    
    -- EXTRACTED DATA
    components JSONB,            -- Components shown in drawing
    dimensions JSONB,            -- Key dimensions
    notes TEXT[],                -- Drawing notes
    
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

---

## ✅ COMPLETE VENDOR CREATION WORKFLOW

### Step 1: Create Schema
```sql
CREATE SCHEMA IF NOT EXISTS richvale;
```

### Step 2: Create Tables (Follow Pattern)
```sql
-- Create products_enriched FIRST
CREATE TABLE richvale.products_enriched (
    -- Copy structure from above
);

-- Then create related tables
CREATE TABLE richvale.assembly_knowledge (...);
CREATE TABLE richvale.product_alternatives (...);
CREATE TABLE richvale.product_finishes (...);
```

### Step 3: Populate with Intelligence
```sql
-- Don't just insert data - include INTELLIGENCE FIELDS
INSERT INTO richvale.products_enriched (
    product_name,
    family_label,
    use_when,           -- REQUIRED!
    dont_use_when,      -- REQUIRED!
    best_for,           -- REQUIRED!
    key_features,
    performance_data
) VALUES (
    '20cm Standard Block',
    'cmu',
    'Loadbearing walls, fire rating 2hr, commercial/industrial',
    'Non-structural partitions, decorative applications',
    'Foundation walls, fire-rated assemblies',
    '{"compressive_strength": "15 MPa", "density": "Normal weight"}',
    '{"fire_2hr": "190mm", "sound_stc": 45}'
);
```

### Step 4: Link Components
```sql
INSERT INTO richvale.assembly_knowledge (
    product_id,
    component_type,
    option_code,
    use_when
) VALUES (
    (SELECT product_id FROM richvale.products_enriched WHERE product_name = '20cm Standard Block'),
    'Mortar',
    'Type S',
    'Below grade, high wind, seismic zones'
);
```

### Step 5: Add Alternatives
```sql
INSERT INTO richvale.product_alternatives (
    product_id,
    alternative_vendor,
    relationship_type,
    when_to_switch,
    trade_offs
) VALUES (
    (SELECT product_id FROM richvale.products_enriched WHERE product_name = '20cm Standard Block'),
    'Boehmers',
    'equivalent',
    'If autoclave-cured required for moisture-critical applications',
    'Boehmers has autoclave curing (no efflorescence), Richvale has more colors'
);
```

### Step 6: Distribute to Public
```sql
-- After vendor schema complete:
INSERT INTO public.products (...)
SELECT ... FROM richvale.products_enriched;

INSERT INTO public.product_attribute_values (...)
SELECT ... FROM richvale.products_enriched;

INSERT INTO public.product_knowledge (...)
SELECT product_id, use_when, dont_use_when, best_for 
FROM richvale.products_enriched;
```

---

## 🚨 WHAT NOT TO DO

```sql
-- WRONG: Flat tables in public schema
CREATE TABLE richvale_fire_ratings (...);  -- NO!
CREATE TABLE richvale_colors (...);        -- NO!
CREATE TABLE richvale_dimensions (...);    -- NO!

-- WRONG: No intelligence fields
CREATE TABLE richvale.products (
    name TEXT,
    specs JSONB
);  -- Missing use_when, dont_use_when, best_for!

-- WRONG: No foreign keys
CREATE TABLE richvale.components (
    component_type TEXT
);  -- Not linked to products_enriched!
```

---

**Next File:** `04_CURRENT_STATE_CMU_VENDORS.md` - Current CMU vendor status
