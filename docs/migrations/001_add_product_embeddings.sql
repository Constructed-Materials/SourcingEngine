-- Migration: Add semantic search embedding support to public.products
-- Created: 2026-02-05
-- Model: nomic-embed-text (768 dimensions)

-- Enable pgvector extension (if not already enabled)
CREATE EXTENSION IF NOT EXISTS vector;

-- Add embedding column (768 dimensions for nomic-embed-text)
ALTER TABLE public.products 
ADD COLUMN IF NOT EXISTS embedding vector(768);

-- Add embedding_text column for debugging and regeneration
ALTER TABLE public.products 
ADD COLUMN IF NOT EXISTS embedding_text text;

-- Add timestamp for tracking embedding updates
ALTER TABLE public.products 
ADD COLUMN IF NOT EXISTS embedding_updated_at timestamp with time zone;

-- Create HNSW index for fast vector similarity search
-- Using cosine distance which is best for semantic similarity
-- m=16: connections per layer, ef_construction=64: build-time search depth
CREATE INDEX IF NOT EXISTS products_embedding_hnsw_idx 
ON public.products 
USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 64);

-- Create RPC function for semantic product search
-- Returns products sorted by semantic similarity to query embedding
CREATE OR REPLACE FUNCTION match_products_semantic(
    query_embedding vector(768),
    match_threshold float DEFAULT 0.5,
    match_count int DEFAULT 10
)
RETURNS TABLE (
    id int,
    sku varchar,
    name varchar,
    description text,
    product_type varchar,
    specifications jsonb,
    embedding_text text,
    similarity float
)
LANGUAGE sql STABLE
AS $$
    SELECT 
        p.id,
        p.sku,
        p.name,
        p.description,
        p.product_type,
        p.specifications,
        p.embedding_text,
        1 - (p.embedding <=> query_embedding) AS similarity
    FROM public.products p
    WHERE p.embedding IS NOT NULL
        AND p.is_active = true
        AND 1 - (p.embedding <=> query_embedding) > match_threshold
    ORDER BY p.embedding <=> query_embedding
    LIMIT match_count;
$$;

-- Grant execute permission on the function (optional, adjust as needed)
-- GRANT EXECUTE ON FUNCTION match_products_semantic TO authenticated;
