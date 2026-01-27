# 🔍 SEARCH LOGIC A-Z: From BOM to Product Match
> Complete path showing how the engine finds products  
> Example: "8 inch masonry block"  
> Last Updated: 2026-01-21

---

## 🎯 THE GOAL

```
INPUT:  BOM Line Item → "8" masonry block"
OUTPUT: Matched products with vendor, specs, intelligence
```

---

## 📊 COMPLETE SEARCH FLOW DIAGRAM

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         BOM INPUT                                        │
│                    "8 inch masonry block"                               │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 1: PARSE & NORMALIZE                                              │
│  ─────────────────────────────                                          │
│  • "8 inch" → "8"" → "20cm" → "200mm"  (size conversion)               │
│  • "masonry block" → "CMU", "concrete block", "block"  (synonyms)      │
│  • Keywords: ["8", "20cm", "masonry", "block", "CMU"]                   │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 2: FIND MATERIAL FAMILY (cm_master_materials)                     │
│  ─────────────────────────────────────────────────                      │
│  Query: WHERE family_label ILIKE '%cmu%' OR '%masonry%' OR '%block%'   │
│                                                                         │
│  RESULT:                                                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ family_label    │ family_name              │ csi_division       │   │
│  ├─────────────────┼──────────────────────────┼───────────────────┤   │
│  │ cmu_blocks      │ Concrete Masonry Units   │ 04                 │   │
│  │ masonry_units   │ Unit Masonry             │ 04                 │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  MATCH: family_label = 'cmu_blocks' ✅                                  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 3: RESOLVE CSI CODE (csi_sections)                                │
│  ───────────────────────────────────────                                │
│  Query: WHERE section_label ILIKE '%concrete unit masonry%'            │
│                                                                         │
│  RESULT:                                                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ section_code │ section_label                    │ division      │   │
│  ├──────────────┼──────────────────────────────────┼──────────────┤   │
│  │ 042200       │ Concrete Unit Masonry            │ 04            │   │
│  │ 042219       │ Insulated Concrete Unit Masonry  │ 04            │   │
│  │ 042223       │ Architectural Concrete Unit...   │ 04            │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  MATCH: csi_section_code = '042200' ✅                                  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 4: FIND VENDORS WITH THIS PRODUCT TYPE                            │
│  ───────────────────────────────────────────                            │
│  Query: FROM products JOIN vendors                                      │
│         WHERE family_label = 'cmu_blocks'                               │
│         GROUP BY vendor_id                                              │
│                                                                         │
│  RESULT:                                                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ vendor_id │ vendor_name          │ location        │ products  │   │
│  ├───────────┼──────────────────────┼─────────────────┼───────────┤   │
│  │ 96        │ Willamette Graystone │ Oregon, USA     │ 61        │   │
│  │ 97        │ Richvale York Block  │ Ontario, Canada │ 27        │   │
│  │ 99        │ Boehmers Block       │ Ontario, Canada │ 24        │   │
│  │ 98        │ Brampton Brick       │ Ontario, Canada │ 12        │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  VENDORS FOUND: 4 ✅                                                    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 5: FILTER BY SIZE (8" = 20cm = 200mm)                             │
│  ──────────────────────────────────────────                             │
│  Query: WHERE model_name ILIKE '%20cm%' OR '%200mm%' OR '%8%'          │
│                                                                         │
│  RESULT:                                                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ vendor               │ product_name              │ csi_code    │   │
│  ├──────────────────────┼───────────────────────────┼─────────────┤   │
│  │ Boehmers Block       │ Stretcher 20cm            │ 042200      │   │
│  │ Boehmers Block       │ Breaker 20cm              │ 042200      │   │
│  │ Boehmers Block       │ Split Face Stretcher 20cm │ 042200      │   │
│  │ Boehmers Block       │ THERMA bloc 20cm          │ 042200      │   │
│  │ Richvale York        │ Bond Beam (20cm)          │ 042200      │   │
│  │ Richvale York        │ Chimney Block 20cm        │ 042200      │   │
│  │ Richvale York        │ 75% Solid Standard 20cm   │ 042200      │   │
│  │ Brampton Brick       │ Solid 75% (20cm)          │ 042200      │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  PRODUCTS MATCHED: 8 ✅                                                 │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 6: GET PRODUCT INTELLIGENCE (product_knowledge)                   │
│  ────────────────────────────────────────────────────                   │
│  Query: FROM product_knowledge WHERE product_id IN (matched_ids)       │
│                                                                         │
│  RESULT:                                                                │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ Product: Bond Beam (20cm)                                       │   │
│  │ ────────────────────────────────────────────────────────────    │   │
│  │ use_cases: ["Reinforced masonry walls, lintel construction,     │   │
│  │             seismic zones requiring horizontal reinforcement"]  │   │
│  │                                                                 │   │
│  │ specifications: {                                               │   │
│  │   "height_mm": 190,                                             │   │
│  │   "length_mm": 390,                                             │   │
│  │   "width_mm_options": [90, 140, 190]                            │   │
│  │ }                                                               │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 7: GET DEEP VENDOR DATA (vendor schema)                           │
│  ────────────────────────────────────────────                           │
│  Query: FROM boehmers.products_enriched WHERE product_name ILIKE '%20cm%'
│                                                                         │
│  RESULT (Boehmers Stretcher 20cm):                                     │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ use_when: "Loadbearing walls, foundations, moisture-critical    │   │
│  │           applications, projects requiring efflorescence-free"  │   │
│  │                                                                 │   │
│  │ best_for: "Southwestern Ontario, moisture-critical apps,        │   │
│  │           exposed masonry where efflorescence is concern"       │   │
│  │                                                                 │   │
│  │ technical_specs: {                                              │   │
│  │   "width_mm": 190,                                              │   │
│  │   "height_mm": 190,                                             │   │
│  │   "length_mm": 390,                                             │   │
│  │   "web_thickness_mm": 26,                                       │   │
│  │   "faceshell_thickness_mm": 32                                  │   │
│  │ }                                                               │   │
│  │                                                                 │   │
│  │ performance_data: {                                             │   │
│  │   "autoclave_curing": true,                                     │   │
│  │   "autoclave_benefits": [                                       │   │
│  │     "Harder and more stable",                                   │   │
│  │     "Preshrunk (<1% moisture)",                                 │   │
│  │     "Eliminates efflorescence"                                  │   │
│  │   ],                                                            │   │
│  │   "r_value": 5.6 (20cm with EPS inserts)                        │   │
│  │ }                                                               │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  STEP 8: GET UNIVERSAL KNOWLEDGE (optional enrichment)                  │
│  ─────────────────────────────────────────────────────                  │
│  Schema: universal_masonry_knowledge                                    │
│                                                                         │
│  Available tables for CMU:                                              │
│  • ccmpa_cmu_gwp_per_block (carbon footprint)                          │
│  • ccmpa_lca_results (life cycle assessment)                           │
│  • obc_fire_resistance_table (Ontario fire codes)                      │
│  • csa_a165_four_facet_system (Canadian standards)                     │
│  • carbon_capture_technologies                                          │
│  • blast_resistance_data                                                │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         FINAL OUTPUT                                     │
│  ════════════════════════════════════════════════════════════════       │
│                                                                         │
│  BOM Item: "8 inch masonry block"                                       │
│  ────────────────────────────────                                       │
│                                                                         │
│  ✅ MATCHED PRODUCTS: 8                                                 │
│  ✅ VENDORS: 3 (Boehmers, Richvale, Brampton)                           │
│  ✅ CSI: 042200 - Concrete Unit Masonry                                 │
│                                                                         │
│  RECOMMENDED:                                                           │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ #1 Boehmers Stretcher 20cm (BOE-STD-6)                          │   │
│  │    • Best for: Ontario, moisture-critical                       │   │
│  │    • Feature: Autoclave cured (no efflorescence)                │   │
│  │    • Dims: 190 x 190 x 390mm                                    │   │
│  │                                                                 │   │
│  │ #2 Boehmers THERMA bloc 20cm (BOE-THERMA-1)                     │   │
│  │    • Best for: Energy-efficient buildings                       │   │
│  │    • Feature: Pre-insulated, R-5.6                              │   │
│  │                                                                 │   │
│  │ #3 Richvale York 75% Solid Standard 20cm                        │   │
│  │    • Best for: Loadbearing, fire-rated                          │   │
│  │    • Feature: CCMPA EPD available                               │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 📝 COMPLETE SQL QUERIES (Copy-Paste Ready)

### STEP 1: Parse Input (Application Layer)
```javascript
// Pseudo-code for input normalization
function normalizeInput(bomItem) {
  const sizeMap = {
    '4"': '10cm', '4 inch': '10cm',
    '6"': '15cm', '6 inch': '15cm', 
    '8"': '20cm', '8 inch': '20cm',
    '10"': '25cm', '10 inch': '25cm',
    '12"': '30cm', '12 inch': '30cm'
  };
  
  const synonyms = {
    'masonry block': ['cmu', 'concrete block', 'block', 'masonry unit'],
    'cmu': ['masonry block', 'concrete block']
  };
  
  return {
    originalTerm: bomItem,
    normalizedSize: sizeMap[extractSize(bomItem)] || null,
    keywords: extractKeywords(bomItem, synonyms)
  };
}
```

---

### STEP 2: Find Material Family
```sql
-- Find family_label from BOM keywords
SELECT family_label, family_name, csi_division
FROM public.cm_master_materials
WHERE family_label ILIKE '%cmu%' 
   OR family_label ILIKE '%masonry%'
   OR family_label ILIKE '%block%'
   OR family_name ILIKE '%masonry%'
   OR family_name ILIKE '%block%';
```

**Result:**
| family_label | family_name | csi_division |
|--------------|-------------|--------------|
| **cmu_blocks** | Concrete Masonry Units | 04 |
| masonry_units | Unit Masonry | 04 |

---

### STEP 3: Resolve CSI Section Code
```sql
-- Get CSI section for the material family
SELECT section_code, section_label, division
FROM public.csi_sections
WHERE section_code = '042200'
   OR section_label ILIKE '%concrete unit masonry%'
ORDER BY section_code;
```

**Result:**
| section_code | section_label | division |
|--------------|---------------|----------|
| **042200** | Concrete Unit Masonry | 04 |
| 042219 | Insulated Concrete Unit Masonry | 04 |
| 042223 | Architectural Concrete Unit Masonry | 04 |

---

### STEP 4: Find All Vendors for This Product Type
```sql
-- Find vendors that have CMU products
SELECT DISTINCT 
    v.vendor_id,
    v.name as vendor_name,
    v.headquarters_location,
    COUNT(p.product_id) as product_count
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE p.family_label = 'cmu_blocks'
AND p.csi_section_code = '042200'
GROUP BY v.vendor_id, v.name, v.headquarters_location
ORDER BY product_count DESC;
```

**Result:**
| vendor_id | vendor_name | location | products |
|-----------|-------------|----------|----------|
| 96 | Willamette Graystone | Oregon, USA | 61 |
| 97 | Richvale York Block | Ontario, Canada | 27 |
| 99 | Boehmers Block | Ontario, Canada | 24 |
| 98 | Brampton Brick | Ontario, Canada | 12 |

---

### STEP 5: Filter Products by Size (8" = 20cm)
```sql
-- Find products matching size specification
SELECT 
    v.name as vendor,
    p.product_id,
    p.model_name,
    p.family_label,
    p.csi_section_code
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE p.family_label = 'cmu_blocks'
AND (
    p.model_name ILIKE '%20cm%'     -- 8" = 20cm
    OR p.model_name ILIKE '%20 cm%'
    OR p.model_name ILIKE '%200mm%' -- 8" = 200mm
)
ORDER BY v.name, p.model_name;
```

**Result:**
| vendor | product_id | model_name | csi_code |
|--------|------------|------------|----------|
| Boehmers Block | 75defc29-... | Breaker 20cm (BOE-STD-7) | 042200 |
| Boehmers Block | 88683e58-... | Split Face Breaker 20cm | 042200 |
| Boehmers Block | ffd45996-... | Split Face Stretcher 20cm | 042200 |
| Boehmers Block | c46ce6cf-... | Stretcher 20cm (BOE-STD-6) | 042200 |
| Boehmers Block | 1e868fb1-... | THERMA bloc 20cm | 042200 |
| Richvale York | a23ac9a5-... | Bond Beam (10, 15, 20cm) | 042200 |
| Richvale York | e2d92bd4-... | Chimney Block 20cm | 042200 |
| Richvale York | 539c3adb-... | Chimney Block 20cm (Unit 26) | 042200 |

---

### STEP 6: Get Product Intelligence
```sql
-- Get knowledge for matched products
SELECT 
    p.model_name,
    pk.use_cases,
    pk.ideal_applications,
    pk.specifications
FROM public.products p
JOIN public.product_knowledge pk ON p.product_id = pk.product_id
WHERE p.family_label = 'cmu_blocks'
AND p.model_name ILIKE '%20cm%';
```

**Result (sample):**
```json
{
  "model_name": "Bond Beam (10, 15, 20cm)",
  "use_cases": ["Reinforced masonry walls", "lintel construction", "seismic zones"],
  "specifications": {
    "height_mm": 190,
    "length_mm": 390,
    "size_codes": ["10", "15", "20"],
    "width_mm_options": [90, 140, 190]
  }
}
```

---

### STEP 7: Get Deep Vendor Data (Intelligence)
```sql
-- Get full intelligence from vendor schema
SELECT 
    product_name,
    use_when,
    best_for,
    technical_specs,
    performance_data
FROM boehmers.products_enriched
WHERE product_name ILIKE '%20cm%';
```

**Result (Stretcher 20cm):**
```json
{
  "product_name": "Stretcher 20cm",
  "use_when": "Loadbearing walls, foundations, moisture-critical applications",
  "best_for": "Southwestern Ontario, moisture-critical, exposed masonry",
  "technical_specs": {
    "width_mm": 190,
    "height_mm": 190,
    "length_mm": 390,
    "web_thickness_mm": 26,
    "faceshell_thickness_mm": 32
  },
  "performance_data": {
    "autoclave_curing": true,
    "autoclave_benefits": [
      "Harder and more stable",
      "Preshrunk (<1% moisture)",
      "No chipping or cracking",
      "Dimensionally stable",
      "Eliminates efflorescence"
    ],
    "r_value": {
      "20cm_with_eps": 5.6,
      "testing_authority": "NCMA"
    }
  }
}
```

---

### STEP 8: Universal Knowledge (Optional Enrichment)
```sql
-- Get GWP data for CMU from universal knowledge
SELECT * FROM universal_masonry_knowledge.ccmpa_cmu_gwp_per_block
WHERE block_size_cm = 20;

-- Get fire resistance data
SELECT * FROM universal_masonry_knowledge.obc_fire_resistance_table
WHERE thickness_cm = 20;
```

---

## 🔄 SEARCH FLOW SUMMARY

```
┌──────────────────────────────────────────────────────────────────────┐
│                    SEARCH PATH: "8 inch masonry block"               │
└──────────────────────────────────────────────────────────────────────┘

  INPUT: "8 inch masonry block"
     │
     ▼
  [1] NORMALIZE ──────────────► "20cm", "cmu", "block"
     │
     ▼
  [2] cm_master_materials ────► family_label = 'cmu_blocks'
     │
     ▼
  [3] csi_sections ───────────► csi_section_code = '042200'
     │
     ▼
  [4] vendors ────────────────► 4 vendors found (Willamette, Richvale, Boehmers, Brampton)
     │
     ▼
  [5] products ───────────────► 8 products match "20cm"
     │
     ▼
  [6] product_knowledge ──────► Use cases, specifications
     │
     ▼
  [7] {vendor}.products_enriched ► use_when, best_for, performance
     │
     ▼
  [8] universal_masonry_knowledge ► GWP, fire ratings, standards
     │
     ▼
  OUTPUT: 8 products, 3 vendors, full intelligence ✅
```

---

## 📊 TABLE HIERARCHY

```
                 ┌─────────────────────────┐
                 │   cm_master_materials   │  ◄─ STEP 2: Find family
                 │   (123 families)        │
                 └───────────┬─────────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
              ▼              ▼              ▼
       ┌────────────┐  ┌──────────┐  ┌────────────────────┐
       │ csi_sections│  │ products │  │ universal_masonry_ │
       │ (6,428)     │  │ (200)    │  │ knowledge (35 tbl) │
       └────────────┘  └────┬─────┘  └────────────────────┘
                            │              ◄─ STEP 8: Enrich
              ┌─────────────┼─────────────┐
              │             │             │
              ▼             ▼             ▼
       ┌──────────┐  ┌────────────┐  ┌─────────────────┐
       │ vendors  │  │ product_   │  │ {vendor}.       │
       │ (81)     │  │ knowledge  │  │ products_enriched│
       │          │  │ (146)      │  │ (deep intel)    │
       └──────────┘  └────────────┘  └─────────────────┘
            ◄─ STEP 4    ◄─ STEP 6       ◄─ STEP 7
```

---

*End of Search Logic Document*
