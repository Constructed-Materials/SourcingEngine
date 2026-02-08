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

    private static readonly Regex FeetPattern = new(
        @"(\d+(?:\.\d+)?)\s*(?:ft|feet|foot|')\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MeterPattern = new(
        @"(\d+(?:\.\d+)?)\s*(?:meter|metre|meters|metres|m)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqFtPattern = new(
        @"(\d+(?:\.\d+)?)\s*(?:sqft|sq\s*ft|sf)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SqMPattern = new(
        @"(\d+(?:\.\d+)?)\s*(?:sqm|sq\s*m|m2|m²)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Conversion constants
    private const double InchesToCm = 2.54;
    private const double InchesToMm = 25.4;
    private const double CmToInches = 1 / InchesToCm;
    private const double MmToInches = 1 / InchesToMm;
    private const double FeetToMeters = 0.3048;
    private const double MetersToFeet = 1 / FeetToMeters;
    private const double SqFtToSqM = 0.092903;
    private const double SqMToSqFt = 1 / SqFtToSqM;

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

            case "ft":
            case "feet":
            case "foot":
            case "'":
                AddFeetVariants(variants, value);
                AddMeterVariantsFromFeet(variants, value);
                break;

            case "m":
            case "meter":
            case "metre":
                AddMeterVariants(variants, value);
                AddFeetVariantsFromMeters(variants, value);
                break;

            case "sqft":
            case "sf":
                AddSqFtVariants(variants, value);
                AddSqMVariantsFromSqFt(variants, value);
                break;

            case "sqm":
            case "m2":
                AddSqMVariants(variants, value);
                AddSqFtVariantsFromSqM(variants, value);
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
        
        // Try feet
        var feetMatch = FeetPattern.Match(input);
        if (feetMatch.Success)
        {
            return (double.Parse(feetMatch.Groups[1].Value), "ft");
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

        // Try meters
        var meterMatch = MeterPattern.Match(input);
        if (meterMatch.Success)
        {
            return (double.Parse(meterMatch.Groups[1].Value), "m");
        }

        // Try square feet
        var sqftMatch = SqFtPattern.Match(input);
        if (sqftMatch.Success)
        {
            return (double.Parse(sqftMatch.Groups[1].Value), "sqft");
        }

        // Try square meters
        var sqmMatch = SqMPattern.Match(input);
        if (sqmMatch.Success)
        {
            return (double.Parse(sqmMatch.Groups[1].Value), "sqm");
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

    #region Feet/Meter Variants

    private static void AddFeetVariants(HashSet<string> variants, double feet)
    {
        var rounded = (int)Math.Round(feet);
        variants.Add($"{rounded}ft");
        variants.Add($"{rounded} ft");
        variants.Add($"{rounded} feet");
        variants.Add($"{rounded}'");

        // Add fractional if not whole
        if (Math.Abs(Math.Round(feet, 1) - rounded) > 0.01)
        {
            variants.Add($"{Math.Round(feet, 1)}ft");
            variants.Add($"{Math.Round(feet, 1)} ft");
        }
    }

    private static void AddMeterVariants(HashSet<string> variants, double meters)
    {
        var rounded = Math.Round(meters, 1);
        var wholeNumber = (int)Math.Round(meters);

        if (Math.Abs(rounded - wholeNumber) < 0.01)
        {
            variants.Add($"{wholeNumber}m");
            variants.Add($"{wholeNumber} m");
            variants.Add($"{wholeNumber} meter");
        }
        else
        {
            variants.Add($"{rounded}m");
            variants.Add($"{rounded} m");
            variants.Add($"{rounded} meter");
        }
    }

    private static void AddMeterVariantsFromFeet(HashSet<string> variants, double feet)
    {
        var meters = Math.Round(feet * FeetToMeters, 1);
        AddMeterVariants(variants, meters);
    }

    private static void AddFeetVariantsFromMeters(HashSet<string> variants, double meters)
    {
        var feet = Math.Round(meters * MetersToFeet);
        AddFeetVariants(variants, feet);
    }

    #endregion

    #region Square Feet/Meter Variants

    private static void AddSqFtVariants(HashSet<string> variants, double sqft)
    {
        var rounded = (int)Math.Round(sqft);
        variants.Add($"{rounded} sqft");
        variants.Add($"{rounded} sq ft");
        variants.Add($"{rounded} sf");
    }

    private static void AddSqMVariants(HashSet<string> variants, double sqm)
    {
        var rounded = Math.Round(sqm, 1);
        var wholeNumber = (int)Math.Round(sqm);

        if (Math.Abs(rounded - wholeNumber) < 0.01)
        {
            variants.Add($"{wholeNumber} sqm");
            variants.Add($"{wholeNumber} sq m");
            variants.Add($"{wholeNumber} m²");
        }
        else
        {
            variants.Add($"{rounded} sqm");
            variants.Add($"{rounded} sq m");
            variants.Add($"{rounded} m²");
        }
    }

    private static void AddSqMVariantsFromSqFt(HashSet<string> variants, double sqft)
    {
        var sqm = Math.Round(sqft * SqFtToSqM, 1);
        AddSqMVariants(variants, sqm);
    }

    private static void AddSqFtVariantsFromSqM(HashSet<string> variants, double sqm)
    {
        var sqft = Math.Round(sqm * SqMToSqFt);
        AddSqFtVariants(variants, sqft);
    }

    #endregion
}
