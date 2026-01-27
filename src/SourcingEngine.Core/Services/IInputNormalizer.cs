using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Input normalizer that combines size calculation and synonym expansion
/// </summary>
public interface IInputNormalizer
{
    /// <summary>
    /// Normalize BOM input text into searchable components
    /// </summary>
    /// <param name="bomText">Raw BOM line item text</param>
    /// <returns>Normalized BOM item with keywords, sizes, and synonyms</returns>
    BomItem Normalize(string bomText);
}
