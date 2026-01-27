# 📝 QUERY LOG - SQL & MCP Tool Calls
> REQ-04: Query & Agent Transparency  
> Last Updated: 2026-01-21

---

## 🔧 TOOL USED

**MCP (Model Context Protocol) - Supabase Server**
- Function: `mcp_supabase_execute_sql`
- Database: `dtxsieykjcvspzbsrrln` (Dev)
- Platform: Supabase PostgreSQL

---

## 📋 ALL QUERIES EXECUTED

### Query 1: Session Verification
```sql
-- Verify database connection
SELECT 'DEV Connected' as status, 
       COUNT(*) as families 
FROM public.cm_master_materials;
```
**MCP Call:**
```json
{
  "tool": "mcp_supabase_execute_sql",
  "project_id": "dtxsieykjcvspzbsrrln",
  "query": "SELECT 'DEV Connected'..."
}
```
**Result:** 123 families ✅

---

### Query 2: List Vendor Schemas
```sql
SELECT table_schema, COUNT(*) as tables
FROM information_schema.tables 
WHERE table_schema NOT IN (
    'information_schema', 'pg_catalog', 'public', 
    'archive', 'auth', 'storage', 'extensions'
)
AND table_type = 'BASE TABLE'
GROUP BY table_schema
ORDER BY table_schema;
```
**Result:** 11 vendor schemas found

---

### Query 3: MVP Table Counts
```sql
SELECT 'vendors' as table_name, COUNT(*) as rows FROM public.vendors
UNION ALL SELECT 'products', COUNT(*) FROM public.products
UNION ALL SELECT 'product_knowledge', COUNT(*) FROM public.product_knowledge
UNION ALL SELECT 'product_certifications', COUNT(*) FROM public.product_certifications
UNION ALL SELECT 'certifications', COUNT(*) FROM public.certifications
UNION ALL SELECT 'cm_master_materials', COUNT(*) FROM public.cm_master_materials
UNION ALL SELECT 'csi_sections', COUNT(*) FROM public.csi_sections
ORDER BY table_name;
```
**Result:**
| Table | Rows |
|-------|------|
| vendors | 81 |
| products | 200 |
| product_knowledge | 146 |
| product_certifications | 404 |
| certifications | 21 |
| cm_master_materials | 123 |
| csi_sections | 6,428 |

---

### Query 4: TEST CASE 1 - 8" Masonry Block
```sql
SELECT 
    v.name as vendor,
    p.model_name as product,
    p.family_label,
    p.csi_section_code
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE p.family_label = 'cmu_blocks'
AND (p.model_name ILIKE '%20cm%' 
     OR p.model_name ILIKE '%8%' 
     OR p.model_name ILIKE '%standard%')
ORDER BY v.name
LIMIT 15;
```
**MCP Call:**
```json
{
  "tool": "mcp_supabase_execute_sql",
  "project_id": "dtxsieykjcvspzbsrrln",
  "query": "SELECT v.name as vendor..."
}
```
**Result:** 15 products from 3 vendors ✅

---

### Query 5: TEST CASE 2 - BCI Floor Joists
```sql
SELECT 
    v.name as vendor,
    p.model_name as product,
    p.family_label,
    p.csi_section_code
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE v.name = 'Boise Cascade'
AND (p.model_name ILIKE '%BCI%' OR p.model_name ILIKE '%joist%')
ORDER BY p.model_name
LIMIT 10;
```
**Result:** 10 BCI joist products ✅

---

### Query 6: TEST CASE 3 - Stucco System
```sql
SELECT 
    v.name as vendor,
    p.model_name as product,
    p.family_label,
    p.csi_section_code
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE p.family_label IN ('stucco', 'eifs', 'plaster')
   OR v.name IN ('Sto Corp', 'DuROCK Alfacing International')
ORDER BY v.name
LIMIT 10;
```
**Result:** 10 stucco products from DuROCK ✅

---

### Query 7: TEST CASE 4 - Railings (Vendor Schema)
```sql
-- Public products returned 0, checking vendor schema
SELECT product_name, family_label
FROM century_railings.products_enriched
ORDER BY product_name
LIMIT 10;
```
**Result:** 10 railing products (in schema, pending distribution)

```sql
SELECT product_name, model_code, family_label
FROM baros_vision.products_enriched
ORDER BY product_name
LIMIT 10;
```
**Result:** 10 glass railing products ✅

---

### Query 8: TEST CASE 5 - LVL Stair Stringer
```sql
SELECT 
    v.name as vendor,
    p.model_name as product,
    p.family_label,
    p.csi_section_code
FROM public.products p
JOIN public.vendors v ON p.vendor_id = v.vendor_id
WHERE v.name = 'Boise Cascade'
AND (p.model_name ILIKE '%LVL%' 
     OR p.model_name ILIKE '%stair%' 
     OR p.model_name ILIKE '%stringer%')
ORDER BY p.model_name
LIMIT 10;
```
**Result:** 8 stair stringer products ✅

---

### Query 9: Products Per Vendor
```sql
SELECT v.name as vendor, COUNT(p.product_id) as products
FROM public.vendors v
LEFT JOIN public.products p ON v.vendor_id = p.vendor_id
WHERE v.vendor_id IN (91, 93, 96, 97, 98, 99, 100, 101, 102)
GROUP BY v.vendor_id, v.name
ORDER BY products DESC;
```
**Result:**
| Vendor | Products |
|--------|----------|
| Willamette Graystone | 61 |
| Sto Corp | 37 |
| Richvale York | 27 |
| Boehmers Block | 24 |
| Boise Cascade | 19 |
| DuROCK | 16 |
| Brampton Brick | 12 |
| Century Railings | 0 (schema only) |
| Baros Vision | 0 (schema only) |

---

### Query 10: CSI Codes for Masonry
```sql
SELECT section_code, section_label 
FROM public.csi_sections 
WHERE section_code LIKE '04%'
AND section_label ILIKE '%concrete%masonry%'
ORDER BY section_code;
```
**Result:** 042200 = Concrete Unit Masonry (CMU)

---

## 📊 QUERY SUMMARY

| Query # | Purpose | Tables Queried | Rows Returned |
|---------|---------|----------------|---------------|
| 1 | Session verify | cm_master_materials | 1 |
| 2 | Schema list | information_schema.tables | 11 |
| 3 | Table counts | 7 public tables | 7 |
| 4 | CMU blocks | products, vendors | 15 |
| 5 | BCI joists | products, vendors | 10 |
| 6 | Stucco | products, vendors | 10 |
| 7a | Railings (Century) | century_railings.products_enriched | 10 |
| 7b | Railings (Baros) | baros_vision.products_enriched | 10 |
| 8 | LVL stringers | products, vendors | 8 |
| 9 | Vendor summary | vendors, products | 9 |
| 10 | CSI codes | csi_sections | 1 |

**Total Queries:** 11  
**Total MCP Tool Calls:** 11

---

## 🔍 SEARCH PATTERNS USED

| Pattern | Description | Example |
|---------|-------------|---------|
| `ILIKE '%term%'` | Case-insensitive partial match | `'%20cm%'` |
| `family_label = 'x'` | Exact family filter | `'cmu_blocks'` |
| `vendor_id IN (...)` | Filter specific vendors | `(97, 98, 99)` |
| `JOIN` | Link tables | `products JOIN vendors` |
| `UNION ALL` | Combine results | Multiple schema queries |

---

*End of Query Log*
