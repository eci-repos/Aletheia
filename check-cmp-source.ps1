# check-cmp-source.ps1
# Diagnoses Copilot retrieval issues for the CMP RFP documents.
# Requires Docker Desktop running with the aletheia-postgres container up.
#
# Usage:  .\check-cmp-source.ps1
# Adjust $DbUser / $DbPassword / $DbName below if you overrode POSTGRES_* in .env.

$ErrorActionPreference = 'Stop'

$Container   = 'aletheia-postgres'
$DbUser      = 'aletheia'
$DbName      = 'aletheia'
$DbPassword  = 'aletheia'

if (-not (docker ps --format '{{.Names}}' | Select-String -SimpleMatch -Quiet $Container)) {
    Write-Host "ERROR: container '$Container' is not running." -ForegroundColor Red
    Write-Host 'Start it with:  docker compose up -d postgres'
    exit 1
}

function Invoke-Query([string]$Title, [string]$Sql) {
    Write-Host ''
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    docker exec -e "PGPASSWORD=$DbPassword" $Container psql -U $DbUser -d $DbName -c $Sql
}

Invoke-Query 'Q1: CMP chunks in embeddings (vector index)' @"
SELECT e.source_id, m.file_name, COUNT(*) AS chunk_count
FROM embeddings e
LEFT JOIN file_metadata m ON m.file_id = e.source_id
WHERE e.content ILIKE '%CMP%'
GROUP BY e.source_id, m.file_name
ORDER BY chunk_count DESC;
"@

Invoke-Query 'Q2: per-source 2022 vs 2026 mentions' @"
SELECT e.source_id, m.file_name,
       COUNT(*) FILTER (WHERE e.content ILIKE '%2022%') AS mentions_2022,
       COUNT(*) FILTER (WHERE e.content ILIKE '%2026%') AS mentions_2026
FROM embeddings e
LEFT JOIN file_metadata m ON m.file_id = e.source_id
WHERE e.content ILIKE '%CMP%'
GROUP BY e.source_id, m.file_name
ORDER BY m.file_name;
"@

Invoke-Query 'Q3: CMP rows in file_metadata' @"
SELECT file_id, file_name, version, content_type, size_bytes, uploaded_at
FROM file_metadata
WHERE file_name ILIKE '%CMP%'
ORDER BY uploaded_at;
"@

Invoke-Query 'Q4: embeddings populated? total chunks per source' @"
SELECT COUNT(*) AS total_chunks, COUNT(DISTINCT source_id) AS distinct_sources
FROM embeddings;
"@

Invoke-Query 'Q5: chunk counts for every source that has embeddings' @"
SELECT e.source_id, m.file_name, COUNT(*) AS chunk_count
FROM embeddings e
LEFT JOIN file_metadata m ON m.file_id = e.source_id
GROUP BY e.source_id, m.file_name
ORDER BY m.file_name NULLS LAST, e.source_id;
"@

Invoke-Query 'Q6: wiki pages mentioning CMP (WRAGS)' @"
SELECT id, title, status, source_ids, primary_source_id,
       LEFT(summary, 200) AS summary_excerpt,
       updated_at
FROM wiki_pages
WHERE title ILIKE '%CMP%' OR summary ILIKE '%CMP%'
ORDER BY updated_at;
"@

Invoke-Query 'Q7: taxonomy tags mentioning CMP/RFP and their sources' @"
SELECT t.name AS tag, t.category_id, tts.source_id, fm.file_name
FROM taxonomy_tags t
LEFT JOIN taxonomy_tag_sources tts ON tts.tag_id = t.id
LEFT JOIN file_metadata fm ON fm.file_id = tts.source_id
WHERE t.name ILIKE '%cmp%' OR t.name ILIKE '%rfp%'
ORDER BY t.name, fm.file_name;
"@

Write-Host ''
Write-Host 'Key check: compare the file_ids in Q3 (current documents) with the source_ids in Q5 (documents that have vector chunks).'
Write-Host 'If a file_id in Q3 has NO row in Q5, that document was uploaded/registered but never vector-indexed -> index drift (re-ingest needed).'
