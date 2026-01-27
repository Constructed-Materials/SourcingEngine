# 🧠 MEMORY BANK

**Purpose:** Master reference folder for Track A/B/C work  
**Created:** 2026-01-18  
**Rule:** READ `00_READ_FIRST_SESSION_START.md` at EVERY session start

---

## 📁 FOLDER CONTENTS

| # | File | Purpose |
|---|------|---------|
| 00 | `00_READ_FIRST_SESSION_START.md` | **READ FIRST** - Quick orientation & checklist |
| 01 | `01_SYSTEM_ARCHITECTURE.md` | Complete 3-track architecture overview |
| 02 | `02_DATABASE_RULES.md` | Database backbone & structure rules |
| 03 | `03_TRACK_B_VENDOR_SCHEMA_PATTERN.md` | Kawneer pattern to follow for ALL vendors |
| 04 | `04_CURRENT_STATE_CMU_VENDORS.md` | CMU vendor data status & restructure plan |
| 05 | `05_QUICK_REFERENCE_COMMANDS.md` | Common SQL queries & commands |

---

## 🎯 HOW TO USE THIS FOLDER

### At Session Start:
1. **READ** `00_READ_FIRST_SESSION_START.md`
2. **CONNECT** to Supabase DEV
3. **RUN** verification queries
4. **ASK** user what we're working on
5. **READ** relevant files for that work

### Before Database Changes:
1. Read `02_DATABASE_RULES.md`
2. Read `03_TRACK_B_VENDOR_SCHEMA_PATTERN.md`
3. Confirm approach with user

### Before CMU Work:
1. Read `04_CURRENT_STATE_CMU_VENDORS.md`
2. Understand current flat table structure
3. DO NOT DELETE existing data

---

## 🔗 RELATED DOCUMENTS

### Detailed Architecture:
- `docs/COMPLETE_SYSTEM_ARCHITECTURE.md`
- `docs/MARKETPLACE_PLATFORM_VISION.md`
- `docs/PROJECT_MASTER_STATUS.md`
- `docs/TRACKS_LOGIC_AND_REASONING.md`

### Kawneer Pilot (Reference Implementation):
- `docs/2025-11-15_track_b_kawneer_pilot/`
- `docs/2025-11-15_track_b_kawneer_pilot/🎓_WHAT_WE_ARE_TEACHING.md`
- `docs/2025-11-15_track_b_kawneer_pilot/ARCHITECTURE_SUMMARY.md`
- `docs/2025-11-15_track_b_kawneer_pilot/DATABASE_ARCHITECTURE_EXPLAINED.md`
- `docs/2025-11-15_track_b_kawneer_pilot/SUPABASE_SCHEMA_VISUAL.md`
- `docs/2025-11-15_track_b_kawneer_pilot/SUPABASE_QUERY_GUIDE.md`

### Public Schema Analysis:
- `docs/PUBLIC_SCHEMA_COMPLETE_REFERENCE.md`
- `docs/PUBLIC_SCHEMA_RELATIONSHIPS_MAP.md`
- `docs/PUBLIC_SCHEMA_ALL_TABLES_ANALYSIS.md`

### Track B Protocol:
- `docs/TRACK_B_BOM_VENDOR_CHECKLIST/TRACK_B_RULES_AND_PROTOCOL.md`

---

## 🚨 CRITICAL REMINDERS

1. **cm_master_materials** = THE BACKBONE (119 families in DEV)
2. **Kawneer schema** = THE PATTERN to follow
3. **CMU data exists** = DO NOT DELETE, restructure when approved
4. **Intelligence fields required** = use_when, dont_use_when, best_for
5. **MARKETPLACE goal** = AI recommends with reasoning, not just catalog

---

## 📊 DATABASE QUICK REFERENCE

| Database | Project ID | Purpose |
|----------|------------|---------|
| **DEV** | dtxsieykjcvspzbsrrln | Track B work |
| Feasibility | guvwgmirdhwlshfearek | Track D (separate!) |

---

## ✅ SESSION CHECKLIST

```
□ Read 00_READ_FIRST_SESSION_START.md
□ Connect to Supabase DEV
□ Verify: cm_master_materials has 119 families
□ Verify: kawneer schema has 5-6 tables
□ Ask user: "What are we working on today?"
□ Read relevant Memory Bank files
□ Confirm approach before making changes
```

---

**Last Updated:** 2026-01-18  
**Maintainer:** AI Assistant
