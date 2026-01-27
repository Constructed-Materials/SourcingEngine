namespace SourcingEngine.Core.Services;

/// <summary>
/// Bidirectional size calculator that converts between imperial and metric units
/// </summary>
public interface ISizeCalculator
{
    /// <summary>
    /// Parse input text and return all size variants for searching.
    /// Supports bidirectional conversion: imperial→metric and metric→imperial.
    /// </summary>
    /// <param name="input">Input text containing size (e.g., "8 inch", "20cm", "200mm")</param>
    /// <returns>List of all size variants for ILIKE matching</returns>
    List<string> GetSizeVariants(string input);
    
    /// <summary>
    /// Extract size value and unit from input text
    /// </summary>
    /// <param name="input">Input text</param>
    /// <returns>Tuple of (value, unit) or null if no size found</returns>
    (double Value, string Unit)? ExtractSize(string input);
}
