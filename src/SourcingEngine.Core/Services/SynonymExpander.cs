using System.Text.RegularExpressions;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Synonym expander with predefined construction material synonyms
/// </summary>
public class SynonymExpander : ISynonymExpander
{
    /// <summary>
    /// Predefined synonym dictionary for construction materials
    /// </summary>
    private static readonly Dictionary<string, string[]> SynonymDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // Masonry
        ["masonry block"] = ["cmu", "concrete block", "masonry unit", "block", "concrete masonry"],
        ["cmu"] = ["masonry block", "concrete block", "masonry unit", "block", "concrete masonry"],
        ["concrete block"] = ["cmu", "masonry block", "masonry unit", "block"],
        ["block"] = ["cmu", "masonry block", "concrete block"],
        
        // Floor systems
        ["floor truss"] = ["joist", "i-joist", "bci", "floor joist", "engineered joist"],
        ["floor joist"] = ["joist", "i-joist", "bci", "floor truss", "engineered joist"],
        ["joist"] = ["i-joist", "bci", "floor joist", "floor truss"],
        ["i-joist"] = ["joist", "bci", "floor joist", "floor truss"],
        ["bci"] = ["joist", "i-joist", "floor joist", "floor truss"],
        ["wood floor"] = ["floor truss", "floor joist", "engineered wood floor"],
        
        // Stucco/EIFS
        ["stucco"] = ["eifs", "plaster", "stucco system", "exterior insulation"],
        ["eifs"] = ["stucco", "exterior insulation finish system", "synthetic stucco"],
        ["plaster"] = ["stucco", "render", "cement plaster"],
        
        // Railings
        ["railing"] = ["guardrail", "handrail", "balustrade", "baluster", "rail"],
        ["guardrail"] = ["railing", "safety rail", "barrier"],
        ["handrail"] = ["railing", "hand rail", "grab rail"],
        ["balustrade"] = ["railing", "baluster", "spindle"],
        ["exterior railing"] = ["ext railing", "outdoor railing", "deck railing"],
        ["ext railing"] = ["exterior railing", "outdoor railing", "deck railing", "railing"],
        
        // Stairs
        ["stair"] = ["stringer", "staircase", "stairs", "step"],
        ["stairs"] = ["stair", "stringer", "staircase", "step"],
        ["stringer"] = ["stair stringer", "stair", "lvl stringer"],
        ["lvl"] = ["laminated veneer lumber", "lvl beam", "lvl stringer"],
        ["wood stair"] = ["stair", "wood stairs", "stringer", "lvl stair"],
        
        // Curtain wall
        ["curtain wall"] = ["curtainwall", "glazed wall", "glass wall", "storefront"],
        ["storefront"] = ["curtain wall", "glass front", "shop front"],
        
        // General
        ["aluminum"] = ["aluminium", "alum"],
        ["aluminium"] = ["aluminum", "alum"],
        ["wood"] = ["lumber", "timber", "engineered wood"],
        ["lumber"] = ["wood", "timber"],
        ["engineered wood"] = ["wood", "lvl", "glulam", "i-joist"],
    };

    // Words to exclude from keyword extraction
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
        "of", "with", "by", "from", "as", "is", "was", "are", "were", "been",
        "be", "have", "has", "had", "do", "does", "did", "will", "would",
        "could", "should", "may", "might", "must", "shall", "can", "need",
        "per", "sf", "lf", "ea", "each", "sqft", "sq", "ft", "linear"
    };

    public List<string> GetSynonyms(string term)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { term };
        
        // Direct match
        if (SynonymDictionary.TryGetValue(term.ToLowerInvariant(), out var synonyms))
        {
            foreach (var syn in synonyms)
            {
                result.Add(syn);
            }
        }
        
        // Check if term is part of a multi-word key
        foreach (var kvp in SynonymDictionary)
        {
            if (kvp.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Any(v => v.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(kvp.Key);
                foreach (var syn in kvp.Value)
                {
                    result.Add(syn);
                }
            }
        }
        
        return [.. result];
    }

    public List<string> ExpandTerms(string input)
    {
        var keywords = ExtractKeywords(input);
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Add all original keywords
        foreach (var keyword in keywords)
        {
            expanded.Add(keyword);
        }
        
        // Check for multi-word matches first
        var lowerInput = input.ToLowerInvariant();
        foreach (var kvp in SynonymDictionary)
        {
            if (lowerInput.Contains(kvp.Key))
            {
                expanded.Add(kvp.Key);
                foreach (var syn in kvp.Value)
                {
                    expanded.Add(syn);
                }
            }
        }
        
        // Then expand individual keywords
        foreach (var keyword in keywords)
        {
            var synonyms = GetSynonyms(keyword);
            foreach (var syn in synonyms)
            {
                expanded.Add(syn);
            }
        }
        
        return [.. expanded];
    }

    public List<string> ExtractKeywords(string input)
    {
        // Remove special characters except quotes (for sizes)
        var cleaned = Regex.Replace(input, @"[^\w\s""']", " ");
        
        // Split by whitespace
        var words = cleaned.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        
        // Filter out stop words and very short words
        var keywords = words
            .Where(w => w.Length > 1)
            .Where(w => !StopWords.Contains(w))
            .Where(w => !Regex.IsMatch(w, @"^\d+$")) // Exclude pure numbers (sizes handled separately)
            .Select(w => w.Trim('"', '\''))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        return keywords;
    }
}
