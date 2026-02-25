using System.Text.Json.Serialization;
using SourcingEngine.Common.Models;

namespace SourcingEngine.Core.Models;

/// <summary>
/// Request to the SourcingEngine search pipeline.
/// Contains a full BOM extraction result to be processed.
/// </summary>
public record SourcingRequest
{
    /// <summary>
    /// The complete extraction result from the BOM extraction service.
    /// Each item in <see cref="ExtractionResultMessage.Items"/> is searched independently.
    /// </summary>
    public required ExtractionResultMessage ExtractionResult { get; init; }
}
