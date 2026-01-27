# 🛠️ NEW VENDOR IMPLEMENTATION GUIDE
> **Step-by-step protocol for adding new manufacturers**  
> For AI Agent Use - Follow EXACTLY  
> Last Updated: 2026-01-18

---

## ⚠️ CRITICAL RULES

1. **NEVER** add flat tables to `public.*` for vendor data
2. **ALWAYS** create vendor-specific schema first
3. **ALWAYS** follow the Kawneer pattern
4. **NEVER** delete existing data without backup
5. **ALWAYS** verify row counts after each step

---

## 📋 PRE-FLIGHT CHECKLIST

Before starting, verify:

```sql
-- 1. Check cm_master_materials has the family
SELECT * FROM public.cm_master_materials WHERE family_label = 'YOUR_FAMILY';

-- 2. Check csi_sections has the code
SELECT * FROM public.csi_sections WHERE section_code = 'YOUR_CSI_CODE';

-- 3. Check vendor doesn't already exist
SELECT * FROM public.vendors WHERE name ILIKE '%VENDOR_NAME%';
```

---

## 🔄 IMPLEMENTATION WORKFLOW

### PHASE 1: CREATE VENDOR SCHEMA

```sql
-- Step 1.1: Create schema
CREATE SCHEMA IF NOT EXISTS vendor_name;

-- Step 1.2: Create products_enriched table
CREATE TABLE vendor_name.products_enriched (
    product_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_name TEXT NOT NULL,
    model_code TEXT,
    family_label TEXT DEFAULT 'FAMILY_LABEL',
    use_when TEXT,
    dont_use_when TEXT,
    best_for TEXT,
    not_recommended_for TEXT,
    technical_specs JSONB,
    performance_data JSONB,
    sustainability JSONB,
    key_features JSONB,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Step 1.3: Create product_finishes table
CREATE TABLE vendor_name.product_finishes (
    finish_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES vendor_name.products_enriched(product_id),
    finish_name TEXT NOT NULL,
    finish_category TEXT,
    color_hex TEXT,
    price_tier TEXT DEFAULT 'standard',
    is_stock BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Step 1.4: Create product_alternatives table
CREATE TABLE vendor_name.product_alternatives (
    alt_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES vendor_name.products_enriched(product_id),
    alternative_name TEXT,
    alternative_vendor TEXT,
    reason TEXT,
    cost_comparison TEXT,
    performance_comparison TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Step 1.5: Create assembly_knowledge table
CREATE TABLE vendor_name.assembly_knowledge (
    knowledge_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id UUID REFERENCES vendor_name.products_enriched(product_id),
    assembly_type TEXT,
    description TEXT,
    components JSONB,
    installation_notes TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
```

---

### PHASE 2: ADD VENDOR TO PUBLIC

```sql
-- Step 2.1: Insert vendor
INSERT INTO public.vendors (
    name,
    website,
    headquarters_location,
    service_region,
    specializations
) VALUES (
    'Vendor Name',
    'https://vendor.com',
    'City, Province/State',
    'Region description',
    ARRAY['specialization1', 'specialization2']
);

-- Step 2.2: Get the vendor_id
SELECT vendor_id FROM public.vendors WHERE name = 'Vendor Name';
-- Save this ID for next steps!
```

---

### PHASE 3: POPULATE VENDOR SCHEMA

```sql
-- Step 3.1: Insert products into vendor schema
INSERT INTO vendor_name.products_enriched (
    product_name,
    model_code,
    family_label,
    use_when,
    best_for,
    technical_specs,
    performance_data,
    sustainability
) VALUES (
    'Product Name',
    'MODEL-001',
    'cmu_blocks',  -- or appropriate family_label
    'Use when you need X',
    'Best for Y applications',
    '{"size": "20cm", "weight_kg": 15}'::jsonb,
    '{"fire_rating_hours": 2, "sound_transmission_class": 45}'::jsonb,
    '{"ccmpa_member": true, "gwp_kg_co2": 260}'::jsonb
);

-- Step 3.2: Insert finishes
INSERT INTO vendor_name.product_finishes (
    product_id,
    finish_name,
    finish_category,
    price_tier
)
SELECT 
    pe.product_id,
    'Color Name',
    'Category',
    'standard'
FROM vendor_name.products_enriched pe
WHERE pe.product_name = 'Product Name';

-- Step 3.3: Insert alternatives
INSERT INTO vendor_name.product_alternatives (
    product_id,
    alternative_name,
    alternative_vendor,
    reason,
    cost_comparison
)
SELECT 
    pe.product_id,
    'Alternative Product',
    'Other Vendor',
    'Similar performance',
    'comparable'
FROM vendor_name.products_enriched pe
WHERE pe.product_name = 'Product Name';
```

---

### PHASE 4: DISTRIBUTE TO PUBLIC

```sql
-- Step 4.1: Insert products into public.products
INSERT INTO public.products (
    vendor_id,
    model_name,
    family_label,
    csi_section_code,
    description
)
SELECT 
    VENDOR_ID_HERE,  -- Replace with actual vendor_id from Phase 2
    pe.product_name,
    pe.family_label,
    'XX XX XX',  -- CSI code
    'Product description'
FROM vendor_name.products_enriched pe
ON CONFLICT (vendor_id, model_name) DO NOTHING;

-- Step 4.2: Get product_ids mapping
-- Store this for next steps
SELECT p.product_id, p.model_name, pe.product_id as enriched_id
FROM public.products p
JOIN vendor_name.products_enriched pe ON p.model_name = pe.product_name
WHERE p.vendor_id = VENDOR_ID_HERE;

-- Step 4.3: Insert product_knowledge
INSERT INTO public.product_knowledge (
    product_id,
    model,
    vendor_key,
    family_hint,
    specifications,
    environmental_data
)
SELECT 
    p.product_id,
    pe.product_name,
    'vendor_name',
    pe.family_label,
    pe.technical_specs,
    pe.sustainability
FROM public.products p
JOIN vendor_name.products_enriched pe ON p.model_name = pe.product_name
WHERE p.vendor_id = VENDOR_ID_HERE;

-- Step 4.4: Insert product_finishes
INSERT INTO public.product_finishes (
    product_id,
    finish_name,
    finish_type,
    color_category,
    price_tier,
    vendor_key
)
SELECT 
    p.product_id,
    pf.finish_name,
    'color',
    pf.finish_category,
    pf.price_tier,
    'vendor_name'
FROM vendor_name.product_finishes pf
JOIN vendor_name.products_enriched pe ON pf.product_id = pe.product_id
JOIN public.products p ON p.model_name = pe.product_name
WHERE p.vendor_id = VENDOR_ID_HERE;

-- Step 4.5: Insert product_alternatives
INSERT INTO public.product_alternatives (
    product_id,
    alternative_name,
    alternative_vendor,
    reason,
    cost_comparison
)
SELECT 
    p.product_id,
    pa.alternative_name,
    pa.alternative_vendor,
    pa.reason,
    pa.cost_comparison
FROM vendor_name.product_alternatives pa
JOIN vendor_name.products_enriched pe ON pa.product_id = pe.product_id
JOIN public.products p ON p.model_name = pe.product_name
WHERE p.vendor_id = VENDOR_ID_HERE;
```

---

### PHASE 5: ADD CERTIFICATIONS

```sql
-- Step 5.1: Check if certifications exist
SELECT * FROM public.certifications 
WHERE cert_name ILIKE '%relevant_cert%';

-- Step 5.2: Add missing certifications
INSERT INTO public.certifications (cert_name, cert_type, description)
VALUES 
    ('CCMPA Member', 'membership', 'Canadian Concrete Masonry Producers Association'),
    ('CSA A165.1', 'standard', 'Canadian Standards Association CMU Standard')
ON CONFLICT DO NOTHING;

-- Step 5.3: Link products to certifications
INSERT INTO public.product_certifications (product_id, cert_id)
SELECT p.product_id, c.cert_id
FROM public.products p
CROSS JOIN public.certifications c
WHERE p.vendor_id = VENDOR_ID_HERE
AND c.cert_name IN ('CCMPA Member', 'CSA A165.1');
```

---

### PHASE 6: ADD ATTRIBUTE VALUES

```sql
-- Step 6.1: Check attribute keys exist
SELECT * FROM public.attributes_dictionary_full_v1_1 
WHERE attribute_key IN ('size_code', 'fire_rating', 'material_type');

-- Step 6.2: Add missing attribute keys
INSERT INTO public.attributes_dictionary_full_v1_1 (attribute_key, attribute_name, data_type, unit)
VALUES 
    ('size_code', 'Size Code', 'integer', 'cm'),
    ('fire_rating', 'Fire Resistance Rating', 'text', 'hours')
ON CONFLICT DO NOTHING;

-- Step 6.3: Insert attribute values
INSERT INTO public.product_attribute_values (product_id, attribute_key, attribute_value)
SELECT p.product_id, 'fire_rating', '2 hours'
FROM public.products p
WHERE p.vendor_id = VENDOR_ID_HERE;
```

---

## ✅ VERIFICATION CHECKLIST

After implementation, run these checks:

```sql
-- Check 1: Vendor schema tables exist
SELECT table_name FROM information_schema.tables 
WHERE table_schema = 'vendor_name';
-- Expected: products_enriched, product_finishes, product_alternatives, assembly_knowledge

-- Check 2: Products in public
SELECT COUNT(*) FROM public.products WHERE vendor_id = VENDOR_ID_HERE;

-- Check 3: Knowledge populated
SELECT COUNT(*) FROM public.product_knowledge pk
JOIN public.products p ON pk.product_id = p.product_id
WHERE p.vendor_id = VENDOR_ID_HERE;

-- Check 4: Finishes populated
SELECT COUNT(*) FROM public.product_finishes 
WHERE vendor_key = 'vendor_name';

-- Check 5: Certifications linked
SELECT COUNT(*) FROM public.product_certifications pc
JOIN public.products p ON pc.product_id = p.product_id
WHERE p.vendor_id = VENDOR_ID_HERE;
```

---

## 📊 DATA MAPPING REFERENCE

### For CMU Vendors:

| Source Data | Vendor Schema | Public Schema |
|-------------|---------------|---------------|
| Unit catalog | products_enriched | products |
| Size codes | products_enriched.technical_specs | product_attribute_values |
| Colors | product_finishes | product_finishes |
| Fire ratings | products_enriched.performance_data | product_attribute_values |
| Weight data | products_enriched.performance_data | product_knowledge |
| EPD/GWP | products_enriched.sustainability | product_knowledge |
| Alternatives | product_alternatives | product_alternatives |
| Certifications | (reference) | product_certifications |

### JSONB Field Templates:

```json
// technical_specs
{
  "size_code": 20,
  "width_mm": 190,
  "height_mm": 190,
  "length_mm": 390,
  "weight_kg": 15.2
}

// performance_data
{
  "fire_rating_hours": 2,
  "sound_transmission_class": 45,
  "fire_ratings": [
    {"size_cm": 20, "concrete_hours": 1.8, "lightweight_hours": 2.5}
  ]
}

// sustainability
{
  "ccmpa_member": true,
  "gwp_kg_co2_per_m3": 260,
  "carboclave": true,
  "leed_credits": ["MRc4", "MRc5"]
}
```

---

## 🚫 COMMON MISTAKES TO AVOID

| ❌ Wrong | ✅ Right |
|----------|----------|
| Create flat tables in public | Create vendor schema first |
| Insert directly to public.products | Populate vendor schema, then distribute |
| Skip certifications | Always link to certifications |
| Forget foreign keys | Always check cm_master_materials, csi_sections exist |
| Hardcode vendor_id | Always query for vendor_id first |
| Skip verification | Always run verification queries |

---

## 📁 UNIVERSAL KNOWLEDGE

If adding industry-wide data (not vendor-specific):

```sql
-- Add to universal_masonry_knowledge schema
INSERT INTO universal_masonry_knowledge.TABLE_NAME (...) VALUES (...);

-- Index it
INSERT INTO public.universal_knowledge_index (table_name, family_label, knowledge_category)
VALUES ('TABLE_NAME', 'cmu_blocks', 'standards');
```

---

## 🔄 ROLLBACK PROCEDURE

If something goes wrong:

```sql
-- Option 1: Delete from public (keep vendor schema)
DELETE FROM public.product_certifications 
WHERE product_id IN (SELECT product_id FROM public.products WHERE vendor_id = VENDOR_ID);

DELETE FROM public.product_knowledge WHERE vendor_key = 'vendor_name';
DELETE FROM public.product_finishes WHERE vendor_key = 'vendor_name';
DELETE FROM public.products WHERE vendor_id = VENDOR_ID;
DELETE FROM public.vendors WHERE vendor_id = VENDOR_ID;

-- Option 2: Drop entire vendor schema (nuclear option)
DROP SCHEMA vendor_name CASCADE;
```

---

*End of Implementation Guide*
