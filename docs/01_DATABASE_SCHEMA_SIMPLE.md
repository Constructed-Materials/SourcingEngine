# 📋 DATABASE SCHEMA - Simple Guide
> REQ-01: Documentation of Data Schema  
> Last Updated: 2026-01-21

---

## 🗄️ DATABASE OVERVIEW

| Item | Value |
|------|-------|
| **Platform** | Supabase (PostgreSQL) |
| **Project ID** | dtxsieykjcvspzbsrrln |
| **Project Name** | Dev |
| **Region** | us-east-1 |

---

## 📊 MVP-RELEVANT PUBLIC TABLES

These are the main tables the sourcing engine queries:

| Table | Records | Purpose | MVP Relevant |
|-------|---------|---------|--------------|
| **vendors** | 81 | Manufacturer directory | ✅ YES |
| **products** | 200 | Main product catalog | ✅ YES |
| **product_knowledge** | 146 | Deep product intelligence | ✅ YES |
| **product_certifications** | 404 | Product certifications | ✅ YES |
| **certifications** | 21 | Certification master list | ✅ YES |
| **cm_master_materials** | 123 | Material family taxonomy | ✅ YES |
| **csi_sections** | 6,428 | CSI MasterFormat codes | ✅ YES |

---

## 🔗 TABLE RELATIONSHIPS (Simple)

```
┌─────────────────────┐
│  vendors            │
│  (81 manufacturers) │
└──────────┬──────────┘
           │ vendor_id
           ▼
┌─────────────────────┐      ┌─────────────────────┐
│  products           │──────│  cm_master_materials│
│  (200 products)     │      │  (123 families)     │
└──────────┬──────────┘      └─────────────────────┘
           │ product_id
           ▼
┌─────────────────────┐      ┌─────────────────────┐
│  product_knowledge  │      │  product_            │
│  (146 records)      │      │  certifications     │
└─────────────────────┘      │  (404 records)      │
                             └─────────────────────┘
```

---

## 📦 KEY TABLES EXPLAINED

### 1. **vendors** - Who makes the products
```
Fields:
- vendor_id (ID)
- name (Company name)
- website
- headquarters_location
- service_region
```

### 2. **products** - What products exist
```
Fields:
- product_id (ID)
- vendor_id (→ vendors)
- model_name (Product name)
- family_label (→ cm_master_materials)
- csi_section_code (→ csi_sections)
```

### 3. **product_knowledge** - Product intelligence
```
Fields:
- product_id (→ products)
- use_cases (When to use)
- ideal_applications
- specifications (JSONB)
- environmental_data (JSONB)
```

### 4. **cm_master_materials** - Material taxonomy (123 families)
```
Examples:
- cmu_blocks (Concrete Masonry Units)
- floor_joists (Floor framing)
- wood_stairs (Stair components)
- stucco_eifs (Stucco systems)
- exterior_railings
- curtain_wall
```

### 5. **csi_sections** - Construction specification codes
```
Examples:
- 042200 = Concrete Unit Masonry (CMU)
- 061733 = Wood I-Joists
- 064313 = Wood Stairs
- 092423 = Stucco
```

---

## 🏭 VENDOR SCHEMAS (Deep Intelligence)

Each Track B vendor has its own schema with detailed data:

| Schema | Tables | Products | Status |
|--------|--------|----------|--------|
| `kawneer` | 7 | 3 | ✅ Pilot |
| `richvale` | 4 | 27 | ✅ Live |
| `brampton_brick` | 4 | 12 | ✅ Live |
| `boehmers` | 4 | 24 | ✅ Live |
| `willamette_graystone` | 4 | 61 | ✅ Live |
| `sto` | 8 | 37 | ✅ Live |
| `durock` | 5 | 16 | ✅ Live |
| `century_railings` | 4 | ~50 | 🟡 Schema only |
| `baros_vision` | 3 | ~15 | 🟡 Schema only |
| `boise_cascade` | 18 | 19 | ✅ Live |

**Note:** "Schema only" means data exists in vendor schema but not yet distributed to public.products.

---

## 🔍 SAMPLE QUERY: Find Products by BOM Item

```sql
-- Search for "8 inch masonry block"
SELECT 
    v.name as vendor,
    p.model_name as product,
    p.family_label,
    p.csi_section_code
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE p.family_label = 'cmu_blocks'
AND (p.model_name ILIKE '%20cm%' OR p.model_name ILIKE '%8%')
ORDER BY v.name;
```

---

## 📚 UNIVERSAL KNOWLEDGE

Industry-wide reference data in `universal_masonry_knowledge` schema:

| Table | Description |
|-------|-------------|
| `cmu_terminology` | Block definitions |
| `cmu_standard_dimensions` | Standard sizes |
| `obc_fire_resistance_table` | Ontario fire codes |
| `ccmpa_cmu_gwp_per_block` | Carbon footprint |

---

*End of Schema Documentation*
