using SourcingEngine.Core.Models;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Input normalizer implementation
/// </summary>
public class InputNormalizer : IInputNormalizer
{
    private readonly ISizeCalculator _sizeCalculator;
    private readonly ISynonymExpander _synonymExpander;

    public InputNormalizer(ISizeCalculator sizeCalculator, ISynonymExpander synonymExpander)
    {
        _sizeCalculator = sizeCalculator;
        _synonymExpander = synonymExpander;
    }

    public BomItem Normalize(string bomText)
    {
        var keywords = _synonymExpander.ExtractKeywords(bomText);
        var sizeVariants = _sizeCalculator.GetSizeVariants(bomText);
        var expandedTerms = _synonymExpander.ExpandTerms(bomText);
        
        return new BomItem
        {
            RawText = bomText,
            Keywords = keywords,
            SizeVariants = sizeVariants,
            Synonyms = expandedTerms
        };
    }
}
