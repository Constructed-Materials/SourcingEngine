# 🧠 MEMORY BANK - READ THIS FIRST AT EVERY SESSION START

**Purpose:** Master reference for Track A/B/C work  
**Rule:** READ THIS ENTIRE FOLDER before making any changes  
**Last Updated:** 2026-01-18

---

## ⚡ QUICK STATUS CHECK

```sql
-- Run this at session start to verify connection to DEV
SELECT 'DEV Connected' as status, COUNT(*) as families 
FROM cm_master_materials;

-- Check Kawneer schema (THE CORRECT PATTERN)
SELECT table_name FROM information_schema.tables 
WHERE table_schema = 'kawneer' ORDER BY table_name;
```

**Expected Results:**
- DEV Connected: 119 families
- Kawneer tables: products_enriched, detail_drawings, assembly_knowledge, product_alternatives, product_finishes

---

## 📁 MEMORY BANK FOLDER CONTENTS

| File | Purpose | When to Read |
|------|---------|--------------|
| `00_READ_FIRST_SESSION_START.md` | This file - quick orientation | ALWAYS first |
| `01_SYSTEM_ARCHITECTURE.md` | Complete 3-track architecture | Before ANY work |
| `02_DATABASE_RULES.md` | Database structure & backbone | Before DB changes |
| `03_TRACK_B_VENDOR_SCHEMA_PATTERN.md` | Kawneer pattern to follow | Before vendor work |
| `04_CURRENT_STATE_CMU_VENDORS.md` | CMU vendor status & issues | Before CMU work |
| `05_QUICK_REFERENCE_COMMANDS.md` | Common SQL & commands | During work |
| `DATA_DICTIONARY_TEAM_GUIDE.md` | **Team guide** - simple table explanations | Share with team |
| `NEW_VENDOR_IMPLEMENTATION_GUIDE.md` | **AI protocol** - step-by-step for new vendors | Before adding vendors |

---

## 🎯 THE MISSION (NEVER FORGET)

**We are building a MARKETPLACE, not a database.**

```
USER UPLOADS PLAN SET
        ↓
AI RECOMMENDS PRODUCTS WITH REASONING:
├─ "Use Richvale 20cm because..."
├─ "Alternative: Brampton Brick if..."
├─ "Complete kit: Stretchers + Corners + Mortar"
└─ "Fire rating: 2hr ✅, GWP: 1.29 kg/block"
```

---

## 🚨 CRITICAL RULES

### Rule 1: THE BACKBONE
```
cm_master_materials.family_label = THE BACKBONE (DEV database)
├─ 119 material families
├─ EVERYTHING links to this via family_label
└─ Example: "curtain_wall", "cmu", "stucco"
```

### Rule 2: FOLLOW KAWNEER PATTERN
```
For EVERY vendor, create:
├─ {vendor}.products_enriched     ← WITH use_when, dont_use_when, best_for
├─ {vendor}.assembly_knowledge    ← Component options
├─ {vendor}.product_alternatives  ← Better/cheaper options
├─ {vendor}.detail_drawings       ← CAD analysis (if applicable)
└─ {vendor}.product_finishes      ← Colors, coatings

DO NOT create flat tables like richvale_*, brampton_brick_*, boehmers_*
```

### Rule 3: INTELLIGENCE FIELDS (REQUIRED)
```sql
CREATE TABLE {vendor}.products_enriched (
    product_id UUID PRIMARY KEY,
    product_name TEXT,
    family_label TEXT REFERENCES cm_master_materials(family_label),
    
    -- INTELLIGENCE FIELDS (CRITICAL!)
    use_when TEXT,              -- "When to use this product"
    dont_use_when TEXT,         -- "When NOT to use"
    best_for TEXT,              -- "Ideal applications"
    not_recommended_for TEXT,   -- "Avoid for..."
    
    key_features JSONB,         -- Flexible specs
    performance_data JSONB,     -- Fire, thermal, acoustic
    sustainability JSONB        -- GWP, EPD, LEED
);
```

### Rule 4: DISTRIBUTION FLOW
```
{vendor}.products_enriched
        ↓ DISTRIBUTION
public.products + public.product_attribute_values + public.product_knowledge
        ↓ UNIVERSAL SEARCH
User finds products across ALL vendors
```

### Rule 5: DO NOT DELETE DATA
```
⚠️ The current CMU tables (richvale_*, brampton_brick_*, boehmers_*) 
contain VALUABLE DATA that was manually extracted.

DO NOT DELETE. 
RESTRUCTURE into proper schemas when user approves.
```

---

## 🗄️ DATABASE CONNECTIONS

### DEV Database (Track B Work)
```
Project ID: dtxsieykjcvspzbsrrln
Project Name: Dev
Region: us-east-1
Backbone: cm_master_materials (119 families)
```

### Feasibility Engine (Track D - SEPARATE)
```
Project ID: guvwgmirdhwlshfearek
DO NOT MIX with Track B work!
```

---

## 📚 ARCHITECTURE DOCUMENTS TO READ

Before starting work, read these in order:

1. **This file** (00_READ_FIRST)
2. **01_SYSTEM_ARCHITECTURE.md** - 3-track overview
3. **02_DATABASE_RULES.md** - Backbone & structure
4. **03_TRACK_B_VENDOR_SCHEMA_PATTERN.md** - Kawneer pattern

For deeper understanding:
- `docs/COMPLETE_SYSTEM_ARCHITECTURE.md`
- `docs/MARKETPLACE_PLATFORM_VISION.md`
- `docs/PROJECT_MASTER_STATUS.md`
- `docs/TRACKS_LOGIC_AND_REASONING.md`
- `docs/2025-11-15_track_b_kawneer_pilot/*.md`

---

## ✅ SESSION START CHECKLIST

```
□ 1. Read this file (00_READ_FIRST_SESSION_START.md)
□ 2. Connect to Supabase DEV (dtxsieykjcvspzbsrrln)
□ 3. Run status check SQL (above)
□ 4. Ask user: "What track/vendor are we working on today?"
□ 5. Read relevant Memory Bank files for that work
□ 6. Confirm approach before making changes
```

---

**REMEMBER:** The goal is AI that UNDERSTANDS construction and can RECOMMEND products with REASONING. Not just a database of specs.
