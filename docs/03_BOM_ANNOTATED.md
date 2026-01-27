# 📋 BOM ANNOTATED - MVP Coverage
> REQ-03: BOM MVP Scoping  
> Project: 2-Story Home (Toronto)  
> Last Updated: 2026-01-21

---

## 📊 COVERAGE LEGEND

| Symbol | Meaning |
|--------|---------|
| ✅ | **Has data in DB** - Engine can match |
| 🔨 | **Needs breakdown** - Sub-assemblies required |
| ⬜ | **No data yet** - Future phase |

---

## 📈 COVERAGE SUMMARY

| Category | Items | Covered | Partial | Not Covered |
|----------|-------|---------|---------|-------------|
| Masonry | 8 | 6 | 2 | 0 |
| Framing | 32 | 8 | 4 | 20 |
| Railings | 2 | 2 | 0 | 0 |
| Roofing | 3 | 0 | 0 | 3 |
| Windows | 24 | 0 | 0 | 24 |
| Doors | 12 | 0 | 0 | 12 |
| **TOTAL** | **81** | **16** | **6** | **59** |

**MVP Focus:** Masonry, Framing (EWP), Railings = 22 items (27% coverage)

---

## 🧱 CATEGORY 7: MASONRY ($71,552)

| Item | Qty | Status | Vendors Available |
|------|-----|--------|-------------------|
| 8" Masonry block | 9,620 SF | ✅ | Richvale, Brampton, Boehmers, Willamette |
| 5/8 stucco on block | 9,620 SF | ✅ | Sto Corp, DuROCK |
| 8" Decorative Lintels | 20 EA | ✅ | Richvale (Bond Beam) |
| Masonry Grout | 1 LS | 🔨 | Needs mortar/grout breakdown |
| Masonry Reinforcing | 1 LS | 🔨 | Needs rebar breakdown |
| Control Joints | 180 LF | ⬜ | — |
| Masonry Flashing | 1 LS | ⬜ | — |
| Weep Vents | 50 EA | ⬜ | — |

**Vendors:** Richvale York (27 products), Brampton Brick (12), Boehmers (24), Willamette (61), Sto Corp (37), DuROCK (16)

---

## 🪵 CATEGORY 9: FRAMING ($173,685)

### Engineered Wood Products (EWP) ✅

| Item | Qty | Status | Vendors Available |
|------|-----|--------|-------------------|
| Pre Engineered Wood Floor Trusses | 2,900 SF | ✅ | Boise Cascade (BCI Joists) |
| LVL Stair Stringers | 3 EA | ✅ | Boise Cascade (Versa-Lam, Versa-Strand) |
| Roof Truss Package | 3,800 SF | 🔨 | RedBuilt (data captured, schema pending) |
| 3/4" Plywood Subfloor | 3,300 SF | 🔨 | Boise Cascade rim board? |
| Rimboard | 210 LF | ✅ | Boise Cascade |
| LVL Headers | various | ✅ | Boise Cascade (2.1E 3100) |

**Vendors:** Boise Cascade (19 products, 18 tables, Canadian certifications)

### Dimensional Lumber ⬜

| Item | Qty | Status | Notes |
|------|-----|--------|-------|
| 2x4 Studs | Various | ⬜ | Commodity - low priority |
| 2x6 Wall Framing | Various | ⬜ | Commodity |
| 2x10 Floor Framing | Various | ⬜ | Commodity |
| Treated Lumber | Various | ⬜ | Commodity |

### Connectors 🔨

| Item | Qty | Status | Notes |
|------|-----|--------|-------|
| Simpson Anchor Package | 1 LS | 🔨 | 260 connectors in boise_cascade.compatible_connectors |
| Joist Hangers | Various | 🔨 | Simpson + MiTek data captured |
| Hurricane Ties | Various | 🔨 | Data in Boise schema |

---

## 🏗️ CATEGORY 8: METALS ($8,142)

| Item | Qty | Status | Vendors Available |
|------|-----|--------|-------------------|
| Ext Railing | 43 LF | ✅ | Century Railings, Baros Vision |
| Handrails | Various | ✅ | Century Railings, Baros Vision |

**Vendors:** Century Railings (~50 products), Baros Vision (~15 products)  
**Note:** Products in vendor schemas, pending distribution to public

---

## 🏠 CATEGORY 11: ROOFING ($40,470)

| Item | Qty | Status | Notes |
|------|-----|--------|-------|
| Roofing Shingles | 3,800 SF | ⬜ | Future phase |
| Underlayment | 3,800 SF | ⬜ | Future phase |
| Flashing | 1 LS | ⬜ | Future phase |

---

## 🪟 CATEGORY 14: WINDOWS ($54,910)

| Item | Qty | Status | Notes |
|------|-----|--------|-------|
| Impact Windows (various) | 24 units | ⬜ | Future phase - Track A has some data |

---

## 🚪 CATEGORY 13: DOORS ($37,542)

| Item | Qty | Status | Notes |
|------|-----|--------|-------|
| Entry Doors | 3 EA | ⬜ | Future phase |
| Interior Doors | 12 EA | ⬜ | Future phase |
| Garage Door | 1 EA | ⬜ | Future phase |

---

## 🔨 ITEMS REQUIRING BREAKDOWN

These line items need to be deconstructed into smaller parts:

| BOM Item | Breakdown Required |
|----------|-------------------|
| Masonry Grout | → Grout type, mortar type, admixtures |
| Masonry Reinforcing | → Rebar size, wire mesh, joint reinforcement |
| Simpson Anchor Package | → Specific hangers, ties, straps (260 in DB) |
| Roof Truss Package | → Truss profiles, spans, spacing |
| 3/4" Plywood Subfloor | → Panel size, grade, attachment |

---

## 📍 MVP FOCUS ITEMS (5 Test Cases)

| # | BOM Item | Category | Est. Value | DB Coverage |
|---|----------|----------|------------|-------------|
| 1 | 8" Masonry Block | Masonry | ~$30,000 | ✅ 4 vendors |
| 2 | BCI Floor Joists | Framing | ~$15,000 | ✅ 1 vendor (10 series) |
| 3 | 5/8 Stucco | Masonry | ~$20,000 | ✅ 2 vendors |
| 4 | Ext Railing | Metals | ~$8,000 | ✅ 2 vendors |
| 5 | LVL Stair Stringer | Framing | ~$2,000 | ✅ 1 vendor (8 products) |

**Total MVP Coverage Value:** ~$75,000 (4.5% of project, but high-value Track B items)

---

*End of Annotated BOM*
