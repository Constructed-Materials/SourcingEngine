-- Migration: Resize product embeddings from 768 to 1024 dimensions
-- For: AWS Bedrock Titan Text Embeddings V2 (1024-dim)
-- Created: 2026-02-21
-- IMPORTANT: Back up the products table before running!
--   CREATE TABLE public.products_backup_002 AS SELECT * FROM public.products;

-- ============================================================
-- SAFETY: Verify backup exists before proceeding
-- ============================================================
-- SELECT count(*) FROM public.products_backup_002;  -- Should match products count

-- ============================================================
-- Step 1: Drop the existing HNSW index (cannot alter vector dimension with index present)
-- ============================================================
DROP INDEX IF EXISTS products_embedding_hnsw_idx;

-- ============================================================
-- Step 2: Drop and recreate the embedding column with new dimension
-- ALTER COLUMN ... TYPE vector(1024) doesn't work with pgvector;
-- must drop and recreate.
-- ============================================================
ALTER TABLE public.products DROP COLUMN IF EXISTS embedding;
ALTER TABLE public.products ADD COLUMN embedding vector(1024);

-- ============================================================
-- Step 3: Clear embedding metadata (force full regeneration)
-- ============================================================
UPDATE public.products
SET embedding_updated_at = NULL
WHERE embedding_updated_at IS NOT NULL;

-- ============================================================
-- Step 4: Drop and recreate the semantic search RPC function
-- with the new vector dimension
-- ============================================================
DROP FUNCTION IF EXISTS match_products_semantic(vector, double precision, integer);

CREATE OR REPLACE FUNCTION match_products_semantic(
    query_embedding vector(1024),
    match_threshold double precision DEFAULT 0.5,
    match_count integer DEFAULT 10
)
RETURNS TABLE (
    product_id uuid,
    vendor_name text,
    model_name character varying,
    family_label character varying,
    description text,
    use_cases text,
    specifications text,
    embedding_text text,
    similarity double precision
)
LANGUAGE sql STABLE
AS $$
    SELECT 
        p.product_id,
        v.name AS vendor_name,
        p.model_name,
        p.family_label,
        pk.description,
        pk.use_cases::text,
        pk.specifications::text,
        p.embedding_text,
        1 - (p.embedding <=> query_embedding) AS similarity
    FROM public.products p
    JOIN public.vendors v ON p.vendor_id = v.vendor_id
    LEFT JOIN public.product_knowledge pk ON p.product_id = pk.product_id
    WHERE p.embedding IS NOT NULL
        AND p.is_active = true
        AND 1 - (p.embedding <=> query_embedding) > match_threshold
    ORDER BY p.embedding <=> query_embedding
    LIMIT match_count;
$$;

-- ============================================================
-- Step 5: Recreate HNSW index with new dimension
-- Same m=16, ef_construction=64 settings as original
-- ============================================================
CREATE INDEX IF NOT EXISTS products_embedding_hnsw_idx 
ON public.products 
USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 64);

-- ============================================================
-- Post-migration notes:
-- 1. Run the embedding generation command to re-embed all products:
--    dotnet run --project src/SourcingEngine.Console -- generate-embeddings
-- 2. Verify embeddings were generated:
--    SELECT count(*) FROM public.products WHERE embedding IS NOT NULL;
-- 3. Update SemanticSearch.SimilarityThreshold in appsettings.json if needed
--    (Titan V2 1024-dim may produce different similarity distributions)
-- ============================================================
