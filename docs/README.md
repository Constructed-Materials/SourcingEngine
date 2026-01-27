# 📦 MVP PARTNER PACKAGE
> Sourcing Engine Validation Package  
> Created: 2026-01-21

---

## 🎯 Objective

Establish a clear data map and testing protocol to validate how the sourcing engine identifies and retrieves database entries based on an uploaded Bill of Materials (BOM).

---

## 📁 Package Contents

| # | File | Description |
|---|------|-------------|
| 01 | `01_DATABASE_SCHEMA_SIMPLE.md` | Database structure & relevant tables |
| 02 | `02_TEST_CASES_WITH_RESULTS.md` | 5 test products with query results |
| 03 | `03_BOM_ANNOTATED.md` | Full BOM with coverage annotations |
| 04 | `04_QUERY_LOG.md` | SQL queries & MCP tool calls |

---

## 📊 Quick Stats

| Metric | Value |
|--------|-------|
| **Database** | Supabase DEV (dtxsieykjcvspzbsrrln) |
| **Total Vendors** | 81 |
| **Total Products** | 200 |
| **Material Families** | 123 |
| **Track B Vendors** | 9 (with deep intelligence) |

### Track B Vendors (Full Data):
1. **CMU/Masonry:** Richvale York, Brampton Brick, Boehmers, Willamette Graystone
2. **Stucco/EIFS:** Sto Corp, DuROCK
3. **Curtain Wall:** Kawneer
4. **Railings:** Century Railings, Baros Vision
5. **Engineered Wood:** Boise Cascade

---

## ✅ Success Criteria

For the 5 test products:
- ✅ Engine finds **2-3 matching products** per BOM item
- ✅ Matches include product name, vendor, and CSI code
- ✅ Partial matching works (e.g., "8 inch block" → "20cm CMU")

---

## 🔗 Related Documents

- Full BOM: `docs/TRACK_B_BOM_VENDOR_CHECKLIST/BOM_2STORY_HOME_FT_PIERCE.md`
- Memory Bank: `docs/MEMORY_BANK/`
- Data Dictionary: `docs/MEMORY_BANK/DATA_DICTIONARY_TEAM_GUIDE.md`
