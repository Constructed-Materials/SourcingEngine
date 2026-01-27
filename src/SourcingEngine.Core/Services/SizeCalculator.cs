using System.Text.RegularExpressions;

namespace SourcingEngine.Core.Services;

/// <summary>
/// Bidirectional size calculator supporting imperial ↔ metric conversion
/// </summary>
public class SizeCalculator : ISizeCalculator
{
    // Regex patterns for different size formats
    private static readonly Regex ImperialPattern = new(
        @"(\d+(?:\.\d+)?)\s*(?:""|inch|in\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex CentimeterPattern = new(
        @"(\d+(?:\.\d+)?)\s*cm\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    private static readonly Regex MillimeterPattern = new(
        @"(\d+(?:\.\d+)?)\s*mm\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Conversion constants
    private const double InchesToCm = 2.54;
    private const double InchesToMm = 25.4;
    private const double CmToInches = 1 / InchesToCm;
    private const double MmToInches = 1 / InchesToMm;

    public List<string> GetSizeVariants(string input)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extracted = ExtractSize(input);
        
        if (extracted == null)
            return [];

        var (value, unit) = extracted.Value;
        
        switch (unit.ToLowerInvariant())
        {
            case "inch":
            case "in":
            case "\"":
                AddImperialVariants(variants, value);
                AddMetricVariantsFromInches(variants, value);
                break;
                
            case "cm":
                AddCmVariants(variants, value);
                AddImperialVariantsFromCm(variants, value);
                AddMmVariantsFromCm(variants, value);
                break;
                
            case "mm":
                AddMmVariants(variants, value);
                AddImperialVariantsFromMm(variants, value);
                AddCmVariantsFromMm(variants, value);
                break;
        }
        
        return [.. variants];
    }

    public (double Value, string Unit)? ExtractSize(string input)
    {
        // Try imperial first
        var imperialMatch = ImperialPattern.Match(input);
        if (imperialMatch.Success)
        {
            return (double.Parse(imperialMatch.Groups[1].Value), "inch");
        }
        
        // Try centimeters
        var cmMatch = CentimeterPattern.Match(input);
        if (cmMatch.Success)
        {
            return (double.Parse(cmMatch.Groups[1].Value), "cm");
        }
        
        // Try millimeters
        var mmMatch = MillimeterPattern.Match(input);
        if (mmMatch.Success)
        {
            return (double.Parse(mmMatch.Groups[1].Value), "mm");
        }
        
        return null;
    }

    #region Imperial Variants
    
    private static void AddImperialVariants(HashSet<string> variants, double inches)
    {
        var rounded = Math.Round(inches, 1);
        var wholeNumber = (int)Math.Round(inches);
        
        // Add various imperial formats
        variants.Add($"{wholeNumber}\"");
        variants.Add($"{wholeNumber} inch");
        variants.Add($"{wholeNumber} in");
        variants.Add($"{wholeNumber}-inch");
        
        // Handle fractional inches if not whole number
        if (Math.Abs(rounded - wholeNumber) > 0.01)
        {
            variants.Add($"{rounded}\"");
            variants.Add($"{rounded} inch");
        }
    }
    
    private static void AddImperialVariantsFromCm(HashSet<string> variants, double cm)
    {
        var inches = cm * CmToInches;
        AddImperialVariants(variants, inches);
    }
    
    private static void AddImperialVariantsFromMm(HashSet<string> variants, double mm)
    {
        var inches = mm * MmToInches;
        AddImperialVariants(variants, inches);
    }
    
    #endregion

    #region Metric Variants
    
    private static void AddMetricVariantsFromInches(HashSet<string> variants, double inches)
    {
        var cm = Math.Round(inches * InchesToCm);
        var mm = Math.Round(inches * InchesToMm);
        
        AddCmVariants(variants, cm);
        AddMmVariants(variants, mm);
    }
    
    private static void AddCmVariants(HashSet<string> variants, double cm)
    {
        var rounded = Math.Round(cm);
        variants.Add($"{rounded}cm");
        variants.Add($"{rounded} cm");
        variants.Add($"{rounded}-cm");
    }
    
    private static void AddMmVariants(HashSet<string> variants, double mm)
    {
        var rounded = Math.Round(mm);
        variants.Add($"{rounded}mm");
        variants.Add($"{rounded} mm");
        variants.Add($"{rounded}-mm");
    }
    
    private static void AddCmVariantsFromMm(HashSet<string> variants, double mm)
    {
        var cm = mm / 10;
        AddCmVariants(variants, cm);
    }
    
    private static void AddMmVariantsFromCm(HashSet<string> variants, double cm)
    {
        var mm = cm * 10;
        AddMmVariants(variants, mm);
    }
    
    #endregion
}
