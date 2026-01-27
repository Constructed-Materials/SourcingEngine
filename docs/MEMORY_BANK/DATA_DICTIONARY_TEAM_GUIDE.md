# 📖 DATA DICTIONARY - Team Guide
> **Simple explanation of every table and schema**  
> Last Updated: 2026-01-18

---

## 🗂️ SCHEMAS OVERVIEW

| Schema | Purpose | For Who |
|--------|---------|---------|
| `public` | Main business tables - products, vendors, knowledge | Everyone |
| `kawneer` | Deep Kawneer-specific intelligence | Track B |
| `richvale` | Deep Richvale York intelligence | Track B |
| `brampton_brick` | Deep Brampton Brick intelligence | Track B |
| `boehmers` | Deep Boehmers intelligence | Track B |
| `willamette_graystone` | Deep Willamette Graystone intelligence | Track B |
| `sto` | Deep Sto Corp intelligence | Track B |
| `durock` | Deep DuROCK intelligence | Track B |
| `universal_masonry_knowledge` | CMU industry standards & reference data | Everyone |
| `archive` | Backup of old tables (DO NOT USE) | None |

---

## 📋 PUBLIC SCHEMA - CORE TABLES

### 🏢 **vendors**
> **What:** List of all manufacturers/suppliers  
> **Contains:** Company name, website, contact, location, capabilities  
> **Relationships:** → products, vendor_warehouses, vendor_capabilities

### 📦 **products**
> **What:** Main product catalog - all products from all vendors  
> **Contains:** Product name, vendor, family type, CSI code  
> **Relationships:** → vendors, cm_master_materials, csi_sections

### 🌳 **cm_master_materials**
> **What:** Material taxonomy - 119 material families (backbone of system)  
> **Contains:** Family labels like "cmu_blocks", "windows", "doors"  
> **Relationships:** → products, universal_knowledge_index

### 📐 **csi_sections**
> **What:** Construction Specifications Institute codes  
> **Contains:** Section codes like "04 22 00" (CMU), "08 44 00" (Windows)  
> **Relationships:** → products

---

## 📋 PUBLIC SCHEMA - KNOWLEDGE TABLES

### 🧠 **product_knowledge**
> **What:** Deep intelligence about each product  
> **Contains:** use_cases, specifications, ideal_applications, environmental_data (JSONB)  
> **Relationships:** → products

### 🎨 **product_finishes**
> **What:** Colors and finish options for products  
> **Contains:** Color name, category, tier (standard/premium), price tier  
> **Relationships:** → products

### 🔄 **product_alternatives**
> **What:** Alternative products to suggest  
> **Contains:** Original product, alternative product, reason, cost comparison  
> **Relationships:** → products

### 🔗 **product_relationships**
> **What:** Links between products in OUR database  
> **Contains:** Product A, Product B, relationship type (upgrade/downgrade/compatible)  
> **Relationships:** → products (both sides)

---

## 📋 PUBLIC SCHEMA - ATTRIBUTES & STANDARDS

### 📊 **product_attribute_values**
> **What:** Specific attribute values for each product  
> **Contains:** Product, attribute key, value (e.g., "fire_rating" = "2 hours")  
> **Relationships:** → products, attributes_dictionary_full_v1_1

### 📚 **attributes_dictionary_full_v1_1**
> **What:** Master list of all possible attributes  
> **Contains:** Attribute key, name, data type, units  
> **Relationships:** → product_attribute_values

### 🏅 **certifications**
> **What:** Master list of certifications/standards  
> **Contains:** Certification name, type (e.g., "CCMPA Member", "CSA A165.1")  
> **Relationships:** → product_certifications

### 🎖️ **product_certifications**
> **What:** Which products have which certifications  
> **Contains:** Product ID, Certification ID  
> **Relationships:** → products, certifications

### 📏 **standards_catalog**
> **What:** Master list of test standards (ASTM, CSA)  
> **Contains:** Standard code, name, description  
> **Relationships:** → product_standard_claims

### ✅ **product_standard_claims**
> **What:** Which products meet which test standards  
> **Contains:** Product ID, Standard ID, claim details  
> **Relationships:** → products, standards_catalog

---

## 📋 PUBLIC SCHEMA - OPTIONS & FINISHES

### 🎨 **product_finish_options**
> **What:** Available finish configurations for products  
> **Contains:** Product, finish type, is_standard, lead_time  
> **Relationships:** → products, vendors

---

## 📋 PUBLIC SCHEMA - PROJECTS & DETECTION

### 🏗️ **projects**
> **What:** Construction projects we're analyzing  
> **Contains:** Project name, location, type  
> **Relationships:** → plan_documents, detected_materials

### 📄 **plan_documents**
> **What:** Uploaded plan files for a project  
> **Contains:** File name, upload date, processing status  
> **Relationships:** → projects

### 🔍 **detected_materials**
> **What:** Materials detected from plan documents  
> **Contains:** What was found, which document, matched product  
> **Relationships:** → projects, plan_documents, products, cm_master_materials

---

## 📋 PUBLIC SCHEMA - EXTRACTION (Track A)

### 🤖 **extracted_models**
> **What:** Products extracted by AI from vendor catalogs  
> **Contains:** Model name, vendor, extraction confidence  
> **Relationships:** → vendors, cm_master_materials

### 📝 **product_attributes_staging**
> **What:** Staging area for extracted attributes (not yet verified)  
> **Contains:** Attribute data waiting for review  
> **Relationships:** → extracted_models

---

## 📋 PUBLIC SCHEMA - SHIPPING & LOGISTICS

### 🏭 **vendor_warehouses**
> **What:** Vendor warehouse locations  
> **Contains:** Address, capabilities, inventory  
> **Relationships:** → vendors

### 🚚 **shipping_carriers**
> **What:** Shipping company information  
> **Contains:** Carrier name, services, coverage

### 📦 **shipping_services**
> **What:** Types of shipping services available  
> **Contains:** Service name, speed, cost tier

### 🔗 **vendor_shipping_compatibility**
> **What:** Which vendors work with which shipping services  
> **Relationships:** → vendors, shipping_services

### 💰 **vendor_sku**
> **What:** SKU and pricing data (for future e-commerce)  
> **Contains:** SKU, price, stock level - CURRENTLY EMPTY  
> **Relationships:** → vendors, products

---

## 📋 PUBLIC SCHEMA - INDEX

### 📇 **universal_knowledge_index**
> **What:** Index linking material families to universal knowledge tables  
> **Contains:** Table name, family_label, category  
> **Relationships:** → cm_master_materials

---

## 🏭 VENDOR SCHEMAS (Kawneer Pattern)

Each vendor schema (kawneer, richvale, brampton_brick, boehmers, willamette_graystone, sto, durock) has:

### **products_enriched**
> **What:** Deep product data with JSONB fields  
> **Contains:** use_when, dont_use_when, best_for, technical_specs, performance_data, sustainability

### **product_finishes**
> **What:** Vendor-specific color/finish options  
> **Contains:** Colors, categories, pricing tiers

### **product_alternatives**
> **What:** Vendor-specific alternatives  
> **Contains:** Similar products from same vendor

### **assembly_knowledge**
> **What:** How to install/assemble products  
> **Contains:** Installation steps, requirements

### **detail_drawings** (some vendors)
> **What:** CAD/technical drawings  
> **Contains:** Drawing references, file links

---

## 📚 UNIVERSAL_MASONRY_KNOWLEDGE SCHEMA

Reference data that applies to ALL CMU vendors:

| Table | What It Contains |
|-------|------------------|
| `cmu_terminology` | Definitions (stretcher, header, bed joint, etc.) |
| `cmu_standard_dimensions` | Standard CMU sizes |
| `cmu_unit_types` | Types of blocks (standard, lintel, bond beam) |
| `cmu_modular_coursing` | Brick-to-block coursing tables |
| `obc_fire_resistance_table` | Ontario Building Code fire ratings |
| `csa_a165_four_facet_system` | Canadian CMU classification standard |
| `ccmpa_cmu_gwp_per_block` | Carbon footprint per block |
| `ccmpa_lca_results` | Life cycle assessment data |
| `insulation_terminology` | R-value, U-value definitions |
| `insulation_k_values` | Thermal conductivity of materials |

---

## 🔗 KEY RELATIONSHIPS DIAGRAM

```
                    ┌─────────────────┐
                    │ cm_master_      │
                    │ materials       │
                    │ (119 families)  │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
              ▼              ▼              ▼
       ┌──────────┐   ┌──────────┐   ┌──────────────────┐
       │ vendors  │   │ products │   │ universal_       │
       │          │◄──│          │   │ knowledge_index  │
       └────┬─────┘   └────┬─────┘   └──────────────────┘
            │              │
            │              ├──────────────────────┐
            │              │                      │
            ▼              ▼                      ▼
     ┌───────────┐  ┌──────────────┐      ┌──────────────┐
     │ vendor_   │  │ product_     │      │ product_     │
     │ warehouses│  │ knowledge    │      │ finishes     │
     └───────────┘  └──────────────┘      └──────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │ product_     │
                    │ certifications│
                    └──────┬───────┘
                           │
                           ▼
                    ┌──────────────┐
                    │ certifications│
                    └──────────────┘
```

---

## ❓ QUICK REFERENCE

**Q: I want to add a new vendor?**
→ Insert into `public.vendors`

**Q: I want to add products for that vendor?**
→ Create schema `vendor_name.*`, add to `products_enriched`, then distribute to `public.products`

**Q: I want to add colors for a product?**
→ Insert into `vendor_name.product_finishes`, then distribute to `public.product_finishes`

**Q: I want to add universal CMU knowledge?**
→ Insert into `universal_masonry_knowledge.*`

**Q: Where do I find what a material family means?**
→ Query `cm_master_materials`

---

*End of Data Dictionary*
