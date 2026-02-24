namespace SourcingEngine.BomExtraction.Parsing;

/// <summary>
/// Thrown when the LLM response cannot be parsed into valid BOM line items.
/// </summary>
public class BomParsingException : Exception
{
    public BomParsingException(string message)
        : base(message)
    {
    }

    public BomParsingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
