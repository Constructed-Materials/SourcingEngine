# 📊 CURRENT STATE - CMU VENDORS

**Purpose:** Document current state of CMU vendor data  
**Status:** Data exists but needs restructure  
**Rule:** DO NOT DELETE - Restructure when user approves

---

## ⚠️ CRITICAL: DATA EXISTS BUT WRONG STRUCTURE

The CMU vendor data was collected correctly, but stored in the WRONG structure.

### What Was Done (WRONG):
```
Created 120+ flat tables in public schema:
├─ richvale_* (29 tables)
├─ brampton_brick_* (23 tables)
├─ boehmers_* (24 tables)
└─ Universal tables (39+ tables)
```

### What Should Have Been Done (CORRECT):
```
Create vendor schemas following Kawneer pattern:
├─ richvale.products_enriched (with use_when, best_for)
├─ richvale.assembly_knowledge
├─ richvale.product_alternatives
├─ richvale.product_finishes
└─ Same for brampton_brick.*, boehmers.*
```

---

## 📋 RICHVALE YORK BLOCK INC.

### Company Info:
- **Country:** Canada 🇨🇦
- **Location:** Ontario (Gormley, London, Kingston)
- **Website:** richvaleyork.com
- **Specialty:** CMU, Cambridge Architectural Stone

### Current Data (Flat Tables):
| Table | Records | Content |
|-------|---------|---------|
| richvale_unit_catalog | 27 | Unit types and size codes |
| richvale_fire_ratings | ~10 | Fire endurance data |
| richvale_colors | 37 | Color options |
| richvale_block_weight_comparison | ~10 | Weight data |
| richvale_carboclave | ~5 | Carbon capture technology |
| richvale_leed_* | Multiple | LEED credit data |
| richvale_csa_four_facet | ~4 | CSA classification |
| richvale_*_coursing | ~52 each | Metric coursing tables |
| ... | ... | 29 total tables |

### Unique Features:
- **Ultra Lite blocks** (27% lighter)
- **Carboclave carbon capture** (0.23 kg CO₂ per 20cm unit)
- **37 colors** (Cambridge Premier, Standard, Pastels, Premium)
- **CCMPA member** (links to EPD data)

### Intelligence Needed (TO ADD):
```
use_when = "Loadbearing commercial, fire-rated assemblies, LEED projects"
dont_use_when = "Budget projects without sustainability requirements"
best_for = "Ontario region, carbon-conscious projects, Cambridge aesthetics"
```

---

## 📋 BRAMPTON BRICK

### Company Info:
- **Country:** Canada 🇨🇦 + USA 🇺🇸
- **Locations:** 8 manufacturing plants (6 Canada, 2 USA)
- **Website:** bramptonbrick.com
- **Specialty:** Standard & Lightweight block, CarboClave

### Current Data (Flat Tables):
| Table | Records | Content |
|-------|---------|---------|
| brampton_brick_units | 12 | Unit types |
| brampton_brick_textures | 3 | Surface textures |
| brampton_brick_physical_properties | ~10 | Strength, absorption |
| brampton_brick_fire_ratings | 31 | Fire endurance |
| brampton_brick_thermal | 10 | Thermal properties |
| brampton_brick_sound_properties | 30 | Acoustic data |
| brampton_brick_carboclave | ~5 | Carbon capture |
| brampton_brick_rainbloc | ~5 | Water repellent |
| ... | ... | 23 total tables |

### Unique Features:
- **RainBloc® Integral Water Repellent** (ASTM C1384)
- **CarboClave carbon capture** (250g CO₂ per 20cm block)
- **Lightweight fire rating** (2hr ULC approved)
- **8 manufacturing locations**

### Intelligence Needed (TO ADD):
```
use_when = "Exterior walls, water-critical applications, fire-rated"
dont_use_when = "Interior non-loadbearing, budget without water concerns"
best_for = "Projects requiring integral water repellent, multi-region supply"
```

---

## 📋 BOEHMERS BLOCK (Hargest Block Ltd.)

### Company Info:
- **Country:** Canada 🇨🇦
- **Location:** Ontario (Kitchener)
- **Website:** boehmers.com
- **Specialty:** Autoclave-cured CMU, THERMA bloc

### Current Data (Flat Tables):
| Table | Records | Content |
|-------|---------|---------|
| boehmers_standard_block_dimensions | 11 | Standard sizes |
| boehmers_single_score_dimensions | 11 | Single score |
| boehmers_v_slot_dimensions | 11 | V-slot pattern |
| boehmers_smooth_ledge_dimensions | 10 | Smooth ledge |
| boehmers_split_face_* | Multiple | Split face lines |
| boehmers_therma_bloc | 1 | Insulated system |
| boehmers_therma_bloc_features | 15 | Features |
| boehmers_ncma_rvalue_evaluation | 18 | R-value testing |
| boehmers_autoclave_benefits | 6 | Curing benefits |
| ... | ... | 24 total tables |

### Unique Features:
- **AUTOCLAVE CURING** (key differentiator)
  - Harder blocks, <1% moisture
  - No chipping, no efflorescence
  - Dimensionally stable
- **11 architectural product lines** (scored, ledge, split face, ribbed)
- **THERMA bloc** pre-insulated system (R-5.0)
- **CSA M Class** moisture rating

### Intelligence Needed (TO ADD):
```
use_when = "Moisture-critical applications, architectural finish required, no efflorescence allowed"
dont_use_when = "Budget projects, standard block acceptable"
best_for = "High-end architectural, below-grade, moisture-sensitive installations"
```

---

## 📋 UNIVERSAL CMU DATA

### Tables Created:
| Category | Tables | Records | Description |
|----------|--------|---------|-------------|
| CMU Standards | 7 | ~50 | Terminology, dimensions, unit types |
| CCMPA EPD | 5 | ~30 | Canadian EPD data (GWP, LCA) |
| OBC Fire | 3 | ~15 | Ontario Building Code fire ratings |
| CSA Standards | 2 | ~10 | Four Facet system |
| Impact Resistance | 8 | ~35 | CCMPA/RCMP ballistics |
| Insulation | 2 | ~20 | Terminology, k-values |
| LCA | 1 | ~10 | ISO 21930 stages |
| **TOTAL** | **28** | **~170** | Universal CMU knowledge |

### These ARE Correct (Keep in public):
- Universal data applies to ALL CMU vendors
- Links via `family_label = 'cmu'`
- Example: `ccmpa_cmu_gwp_per_block` applies to Richvale, Brampton, Boehmers

---

## 🔄 RESTRUCTURE PLAN (WHEN APPROVED)

### Phase 1: Create Schemas
```sql
CREATE SCHEMA richvale;
CREATE SCHEMA brampton_brick;
CREATE SCHEMA boehmers;
```

### Phase 2: Create products_enriched Tables
```sql
-- Transform flat data → products_enriched with intelligence
-- Add use_when, dont_use_when, best_for fields
-- Use JSONB for flexible specs
```

### Phase 3: Migrate Data
```sql
-- Copy relevant data from flat tables to new structure
-- Map: richvale_unit_catalog → richvale.products_enriched
-- Map: richvale_colors → richvale.product_finishes
-- Map: richvale_fire_ratings → performance_data JSONB
```

### Phase 4: Add Intelligence
```sql
-- Add the missing intelligence fields
-- This is MANUAL work - requires understanding of each product
```

### Phase 5: Link & Distribute
```sql
-- Link to cm_master_materials.family_label = 'cmu'
-- Distribute to public.products for universal search
```

### Phase 6: Verify & Clean
```sql
-- Verify new structure works
-- Only THEN drop old flat tables (after backup)
```

---

## 📊 DATA SUMMARY

| Vendor | Current Tables | Current Records | Status |
|--------|---------------|-----------------|--------|
| Richvale York | 29 flat tables | ~200+ | 🔴 Needs restructure |
| Brampton Brick | 23 flat tables | ~350+ | 🔴 Needs restructure |
| Boehmers | 24 flat tables | ~180+ | 🔴 Needs restructure |
| Universal | 28 tables | ~170+ | ✅ Correct (keep in public) |
| **TOTAL** | **104 tables** | **~900+ records** | Data exists, needs restructure |

---

## ⚠️ REMINDER

**DO NOT DELETE THE FLAT TABLES!**

The data was manually extracted from vendor catalogs and contains valuable information:
- Product specifications
- Fire ratings
- Colors
- Technical data
- Business terms

This data needs to be MIGRATED to the correct structure, not deleted.

---

**Next File:** `05_QUICK_REFERENCE_COMMANDS.md` - Common SQL & commands
