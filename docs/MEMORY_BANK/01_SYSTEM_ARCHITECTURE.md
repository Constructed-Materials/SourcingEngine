# 🏗️ SYSTEM ARCHITECTURE - Three Track Overview

**Purpose:** Complete understanding of how Tracks A, B, C connect  
**Source:** Consolidated from COMPLETE_SYSTEM_ARCHITECTURE.md, TRACKS_LOGIC_AND_REASONING.md, MARKETPLACE_PLATFORM_VISION.md

---

## 🎯 WHAT WE'RE BUILDING

### NOT:
- ❌ Product information catalog
- ❌ Construction Wikipedia
- ❌ Passive database

### YES:
- ✅ **TRANSACTIONAL MARKETPLACE** - Real commerce
- ✅ **AI-DRIVEN MATCHING** - Intelligence recommends products
- ✅ **PLAN → BUY → SHIP** - Complete procurement workflow

---

## 🚀 THE THREE TRACKS

```
┌─────────────────────────────────────────────────────────────────┐
│  TRACK A: UNIVERSAL EXTRACTION (Foundation)                     │
│  ├─ Extract products from ANY vendor catalog (automated)        │
│  ├─ Status: WORKING, maintenance mode (paused)                  │
│  ├─ Result: 71 vendors, 558 models in database                  │
│  └─ Purpose: Build the product database quickly                 │
└─────────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────────┐
│  TRACK B: VENDOR INTELLIGENCE (Knowledge Layer) ← ACTIVE        │
│  ├─ Deep product knowledge (when, where, why to use)            │
│  ├─ Status: ACTIVE - Kawneer pilot complete                     │
│  ├─ Result: AI that understands construction                    │
│  └─ Purpose: Teach AI product application logic                 │
└─────────────────────────────────────────────────────────────────┘
                            ↑
┌─────────────────────────────────────────────────────────────────┐
│  TRACK C: VISION PLAN ANALYSIS (Detection Layer)                │
│  ├─ Detect components on plan sets                              │
│  ├─ Calculate quantities (sqft, linear ft)                      │
│  ├─ Match to Track B products                                   │
│  ├─ Status: STARTING                                            │
│  └─ Purpose: Automated Plan → BOM workflow                      │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔗 HOW TRACKS CONNECT

```
USER UPLOADS: Floor plan (PDF)
        ↓
TRACK C - DETECTION:
├─ Vision LLM detects: "Curtain wall, 10 stories"
├─ Measures: 200 ft × 120 ft = 24,000 sqft
└─ Queries Track B for validation
        ↓
TRACK B - INTELLIGENCE:
├─ Validates: "10 stories = curtain wall ✅"
├─ Matches: "Kawneer 1600UT SS (3-25 stories)"
├─ Generates BOM: Complete kit with SKUs
└─ Provides alternatives with reasoning
        ↓
TRACK A - CATALOG:
├─ Universal search across all vendors
├─ Track B products + Track A products
└─ Combined searchable catalog
        ↓
OUTPUT: Product recommendation with reasoning + complete BOM
```

---

## 🧠 THE VALUE OF TRACK B INTELLIGENCE

### Without Track B:
```
Track C: "I detect a 120-foot tall glazed element"
System: "Is that curtain wall? Window wall? I don't know."
Result: ❌ Can't validate, can't match products
```

### With Track B:
```
Track C: "I detect a 120-foot tall glazed element"
Track B: "120 ft = 10 stories = high-rise = CURTAIN WALL ✅"
Track B: "Match: Kawneer 1600UT SS (3-25 stories)"
Track B: "Alternative: 1600 Wall #1 (lower cost, shear block)"
Result: ✅ Validated detection + product match + reasoning
```

---

## 📊 TRACK B INTELLIGENCE STRUCTURE (KAWNEER PATTERN)

```sql
-- THE CORRECT PATTERN FOR EVERY VENDOR:

CREATE SCHEMA {vendor_name};  -- e.g., kawneer, richvale, boehmers

-- Main intelligence table
CREATE TABLE {vendor}.products_enriched (
    product_id UUID PRIMARY KEY,
    product_name TEXT,
    model_code TEXT,
    family_label TEXT REFERENCES cm_master_materials(family_label),
    
    -- INTELLIGENCE FIELDS (THE SECRET SAUCE)
    use_when TEXT,              -- "Loadbearing walls, fire 2hr needed"
    dont_use_when TEXT,         -- "Non-structural partitions"
    best_for TEXT,              -- "Commercial foundation"
    not_recommended_for TEXT,   -- "Interior non-loadbearing"
    
    key_features JSONB,         -- Flexible product specs
    performance_data JSONB,     -- Fire, thermal, acoustic
    sustainability JSONB        -- GWP, EPD, LEED
);

-- Component/assembly options
CREATE TABLE {vendor}.assembly_knowledge (
    id UUID PRIMARY KEY,
    product_id UUID REFERENCES products_enriched(product_id),
    component_type TEXT,
    option_code TEXT,
    use_when TEXT,
    compatible_with TEXT[]
);

-- Alternatives (upsell/cross-sell)
CREATE TABLE {vendor}.product_alternatives (
    id UUID PRIMARY KEY,
    product_id UUID REFERENCES products_enriched(product_id),
    alternative_id UUID,
    comparison JSONB,
    when_to_switch TEXT
);

-- Colors/finishes
CREATE TABLE {vendor}.product_finishes (
    id UUID PRIMARY KEY,
    product_id UUID REFERENCES products_enriched(product_id),
    finish_name TEXT,
    finish_code TEXT,
    color_family TEXT
);

-- CAD drawings (if applicable)
CREATE TABLE {vendor}.detail_drawings (
    id UUID PRIMARY KEY,
    product_id UUID REFERENCES products_enriched(product_id),
    drawing_name TEXT,
    drawing_type TEXT,
    components JSONB
);
```

---

## 📤 DISTRIBUTION FLOW (Track B → Public)

```
STEP 1: Vendor schema contains deep intelligence
        {vendor}.products_enriched (with use_when, best_for)

STEP 2: Distribution to public schema
        ↓
        public.products (universal catalog)
        public.product_attribute_values (searchable attributes)
        public.product_knowledge (application intelligence)
        public.product_relationships (alternatives)

STEP 3: Universal search combines all vendors
        User searches → Gets Track A + Track B products
```

---

## 🎯 SUCCESS METRICS

### Track B Success:
- Products have `use_when`, `dont_use_when`, `best_for` fields
- AI can recommend products by building type/height
- AI can explain "why this product, not that one"
- Complete BOMs generated from assembly_knowledge

### Track C Success:
- Detect elements on elevation drawings (±10% accuracy)
- Calculate quantities from scale (±10% accuracy)
- Match to Track B products (90%+ correct)
- Generate complete BOM with components

### Complete System Success:
- Upload plan → Get BOM in < 5 minutes
- Quantity accuracy: ±10% vs. manual takeoff
- User confidence: "I trust this recommendation"

---

## 🚨 CRITICAL REMINDERS

1. **Track B teaches WHAT** - System learns construction vocabulary
2. **Track C detects WHERE** - Pattern matching + measurement
3. **Track B validates** - Size confirms classification
4. **Track B matches** - Returns product with reasoning
5. **Intelligence fields are REQUIRED** - Not optional
6. **Follow Kawneer pattern** - For ALL vendors

---

**Next File:** `02_DATABASE_RULES.md` - Database backbone & structure
