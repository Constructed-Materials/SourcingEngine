# 🛡️ DATA SAFETY PROTOCOL - CMU Migration

**Purpose:** Ensure ZERO data loss during migration  
**Rule:** NEVER delete source tables until migration verified  
**Created:** 2026-01-18

---

## 🔒 SAFETY PRINCIPLES

### 1. ADDITIVE ONLY
```
✅ CREATE new tables in new schemas
✅ INSERT data from flat tables
✅ KEEP flat tables intact

❌ NEVER DELETE flat tables during migration
❌ NEVER DROP tables without backup
❌ NEVER UPDATE source data
```

### 2. BACKUP BEFORE ANYTHING
```sql
-- Before ANY migration, create backup tables
CREATE TABLE backup_richvale_unit_catalog AS 
SELECT * FROM richvale_unit_catalog;

CREATE TABLE backup_richvale_colors AS 
SELECT * FROM richvale_colors;
-- etc.
```

### 3. VERIFY COUNTS AT EVERY STEP
```sql
-- Before migration
SELECT 'richvale_unit_catalog' as source, COUNT(*) as rows 
FROM richvale_unit_catalog;

-- After migration
SELECT 'richvale.products_enriched' as target, COUNT(*) as rows 
FROM richvale.products_enriched;

-- They should match!
```

---

## 📋 DATA SAFETY CHECKLIST

### Before Migration:
```
□ 1. Count all source table rows
□ 2. Create backup tables
□ 3. Document what data exists
□ 4. Screenshot/export critical data
□ 5. Confirm backup exists before proceeding
```

### During Migration:
```
□ 1. Use INSERT ... SELECT (not DELETE)
□ 2. Verify counts after each step
□ 3. Test queries on new data
□ 4. Keep flat tables untouched
□ 5. Document any issues
```

### After Migration:
```
□ 1. Compare source vs target counts
□ 2. Run validation queries
□ 3. Test intelligence fields work
□ 4. Keep flat tables for 30 days
□ 5. Only archive/delete after user approval
```

---

## 📊 PRE-MIGRATION DATA INVENTORY

Run this BEFORE any migration to document what exists:

```sql
-- RICHVALE DATA INVENTORY
SELECT 
    'richvale_unit_catalog' as table_name,
    (SELECT COUNT(*) FROM richvale_unit_catalog) as rows
UNION ALL SELECT 'richvale_colors', (SELECT COUNT(*) FROM richvale_colors)
UNION ALL SELECT 'richvale_fire_ratings', (SELECT COUNT(*) FROM richvale_fire_ratings)
UNION ALL SELECT 'richvale_block_weight_comparison', (SELECT COUNT(*) FROM richvale_block_weight_comparison)
UNION ALL SELECT 'richvale_carboclave', (SELECT COUNT(*) FROM richvale_carboclave)
UNION ALL SELECT 'richvale_csa_four_facet', (SELECT COUNT(*) FROM richvale_csa_four_facet)
UNION ALL SELECT 'richvale_leed_credit_contributions', (SELECT COUNT(*) FROM richvale_leed_credit_contributions)
ORDER BY table_name;
```

---

## 🔄 MIGRATION SAFETY WORKFLOW

```
STEP 1: INVENTORY
├─ Run count queries on ALL flat tables
├─ Save results to file/document
└─ Screenshot for reference

STEP 2: BACKUP
├─ Create backup_* tables for each source
├─ Verify backup has same row count
└─ Confirm before proceeding

STEP 3: CREATE NEW STRUCTURE
├─ CREATE SCHEMA (empty)
├─ CREATE TABLE (empty structure)
└─ No data touched yet

STEP 4: MIGRATE DATA
├─ INSERT ... SELECT from flat tables
├─ Transform data as needed
├─ Original flat tables UNCHANGED

STEP 5: VERIFY
├─ Compare row counts
├─ Spot check specific records
├─ Test queries
├─ Confirm data integrity

STEP 6: ADD INTELLIGENCE
├─ UPDATE new tables with use_when, best_for
├─ Original flat tables still UNCHANGED
└─ This ADDS value, doesn't lose data

STEP 7: KEEP ORIGINALS
├─ DO NOT DELETE flat tables
├─ Rename to archive_* after 30 days
└─ Only delete after explicit user approval
```

---

## 🛟 RECOVERY PLAN

### If Something Goes Wrong:

```sql
-- The flat tables are still there!
-- New schema can be dropped and recreated

-- Option 1: Start over
DROP SCHEMA richvale CASCADE;
-- Flat tables still exist, try again

-- Option 2: Restore from backup
INSERT INTO richvale.products_enriched 
SELECT * FROM backup_richvale_products_enriched;

-- Option 3: Query original data
-- Flat tables (richvale_*) are never touched
SELECT * FROM richvale_unit_catalog;  -- Still works!
```

---

## ✅ DATA SAFETY GUARANTEES

| Guarantee | How It's Achieved |
|-----------|-------------------|
| **No data loss** | Flat tables never deleted |
| **Rollback possible** | New schema can be dropped |
| **Backup exists** | backup_* tables created first |
| **Verifiable** | Count queries at every step |
| **Reversible** | Original data always accessible |

---

## 📝 MIGRATION LOG TEMPLATE

Keep this updated during migration:

```
RICHVALE YORK MIGRATION LOG
============================
Date Started: 2026-01-18

PRE-MIGRATION COUNTS:
- richvale_unit_catalog: __ rows
- richvale_colors: __ rows
- richvale_fire_ratings: __ rows
- (etc.)

BACKUPS CREATED:
- backup_richvale_unit_catalog: ✅
- backup_richvale_colors: ✅

MIGRATION STEPS:
- [ ] Schema created
- [ ] products_enriched created
- [ ] Products migrated (__ rows)
- [ ] Colors migrated (__ rows)
- [ ] Intelligence added

POST-MIGRATION VERIFICATION:
- richvale.products_enriched: __ rows (expected: __)
- richvale.product_finishes: __ rows (expected: __)

STATUS: IN PROGRESS / COMPLETE / ROLLED BACK
```

---

**BOTTOM LINE:** The flat tables are NEVER touched. We only CREATE new tables and INSERT into them. If anything goes wrong, the original data is still there.
