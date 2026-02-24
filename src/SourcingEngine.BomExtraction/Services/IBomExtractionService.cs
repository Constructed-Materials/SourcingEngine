using SourcingEngine.BomExtraction.Models;

namespace SourcingEngine.BomExtraction.Services;

/// <summary>
/// Service for extracting BOM line items from documents using AWS Bedrock.
/// </summary>
public interface IBomExtractionService
{
    /// <summary>
    /// Extract BOM line items from a document file.
    /// The file is sent directly to the Bedrock model via the Converse API DocumentBlock.
    /// </summary>
    /// <param name="filePath">Path to the BOM document (PDF, CSV, XLSX, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result containing parsed line items and metadata.</returns>
    Task<ExtractionResult> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
