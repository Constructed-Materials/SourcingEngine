namespace SourcingEngine.Core.Services;

/// <summary>
/// Expands input terms to include synonyms for broader search matching
/// </summary>
public interface ISynonymExpander
{
    /// <summary>
    /// Get all synonyms for the given term
    /// </summary>
    /// <param name="term">Input term</param>
    /// <returns>List of synonyms including the original term</returns>
    List<string> GetSynonyms(string term);
    
    /// <summary>
    /// Expand all terms in input text to include synonyms
    /// </summary>
    /// <param name="input">Input text</param>
    /// <returns>List of all expanded terms</returns>
    List<string> ExpandTerms(string input);
    
    /// <summary>
    /// Extract keywords from input text
    /// </summary>
    /// <param name="input">Input text</param>
    /// <returns>List of extracted keywords</returns>
    List<string> ExtractKeywords(string input);
}
