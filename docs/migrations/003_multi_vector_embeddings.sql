-- Migration 003: Multi-vector embeddings
-- Splits single embedding/embedding_text into 3 purpose-specific vectors:
--   embedding_description (Vector A, weight 0.6): material + product + description + certifications
--   embedding_specs       (Vector B, weight 0.3): technical specifications
--   embedding_enrichment  (Vector C, weight 0.1): enrichment / use-case context
--
-- Two-phase search: HNSW retrieval on embedding_description, then C# re-scoring with all 3.
-- Run this migration, then backfill via EmbeddingGenerationService (MissingOnly = true).

BEGIN;

-- 1. Add new vector columns
ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS embedding_description vector(1024),
    ADD COLUMN IF NOT EXISTS embedding_specs       vector(1024),
    ADD COLUMN IF NOT EXISTS embedding_enrichment  vector(1024);

-- 2. Add new text columns (debugging / reproducibility)
ALTER TABLE public.products
    ADD COLUMN IF NOT EXISTS embedding_text_description text,
    ADD COLUMN IF NOT EXISTS embedding_text_specs       text,
    ADD COLUMN IF NOT EXISTS embedding_text_enrichment  text;

-- 3. Create HNSW indexes (description is the primary retrieval vector)
CREATE INDEX IF NOT EXISTS idx_products_embedding_description
    ON public.products
    USING hnsw (embedding_description vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

CREATE INDEX IF NOT EXISTS idx_products_embedding_specs
    ON public.products
    USING hnsw (embedding_specs vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

CREATE INDEX IF NOT EXISTS idx_products_embedding_enrichment
    ON public.products
    USING hnsw (embedding_enrichment vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- 4. Drop old single-vector column, text, and HNSW index
DROP INDEX IF EXISTS idx_products_embedding;
ALTER TABLE public.products DROP COLUMN IF EXISTS embedding;
ALTER TABLE public.products DROP COLUMN IF EXISTS embedding_text;

-- 5. Drop old RPC function (no longer needed — search is in C#)
DROP FUNCTION IF EXISTS match_products_semantic(vector, float, int);

COMMIT;
