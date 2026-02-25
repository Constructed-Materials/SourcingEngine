using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SourcingEngine.Common.Models;

namespace SourcingEngine.BomExtraction.Parsing;

/// <summary>
/// Parses raw LLM JSON responses into validated <see cref="BomLineItem"/> lists.
/// Handles markdown fences, numeric sanitization, and partial/wrapped responses.
/// Ported from Python BomDataExtractor's <c>_extract_json</c> and <c>_validate_items</c>.
/// </summary>
public class JsonResponseParser
{
    private readonly ILogger<JsonResponseParser> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public JsonResponseParser(ILogger<JsonResponseParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parse the raw LLM response text into validated BOM line items.
    /// </summary>
    /// <param name="response">Raw text from the Bedrock model.</param>
    /// <returns>List of validated <see cref="BomLineItem"/> objects.</returns>
    /// <exception cref="BomParsingException">If no valid JSON array can be extracted.</exception>
    public List<BomLineItem> Parse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new BomParsingException("LLM returned an empty response.");

        var rawItems = ExtractJsonArray(response);
        return ValidateItems(rawItems);
    }

    /// <summary>
    /// Extract a JSON array of dictionaries from the LLM response.
    /// Handles markdown fences, numeric quirks, and wrapped objects.
    /// </summary>
    internal static List<Dictionary<string, JsonElement>> ExtractJsonArray(string response)
    {
        // Strip markdown code fences (```json ... ``` or ``` ... ```)
        var cleaned = Regex.Replace(response, @"```\w*\s*", "").Trim();

        // Attempt 1: direct parse (with optional numeric sanitization)
        var result = TryParse(cleaned);
        if (result != null) return result;

        // Attempt 2: find array boundaries [...]
        var match = Regex.Match(cleaned, @"\[.*\]", RegexOptions.Singleline);
        if (match.Success)
        {
            result = TryParse(match.Value);
            if (result != null) return result;
        }

        throw new BomParsingException(
            $"Could not extract valid JSON array from LLM response. " +
            $"Response starts with: {response[..Math.Min(response.Length, 200)]}");
    }

    /// <summary>
    /// Try to parse the candidate string as a JSON array or a wrapper object with an "items" key.
    /// Falls back to numeric sanitization if the first attempt fails.
    /// </summary>
    private static List<Dictionary<string, JsonElement>>? TryParse(string candidate)
    {
        var parsed = TryDeserialize(candidate);
        if (parsed != null) return parsed;

        // Retry with numeric sanitization (thousands separators, trailing dots)
        var sanitized = SanitizeNumericLiterals(candidate);
        if (sanitized != candidate)
        {
            parsed = TryDeserialize(sanitized);
            if (parsed != null) return parsed;
        }

        return null;
    }

    /// <summary>
    /// Attempt JSON deserialization as an array or as a wrapper object with "items" key.
    /// </summary>
    private static List<Dictionary<string, JsonElement>>? TryDeserialize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return DeserializeArray(doc.RootElement);
            }

            // Handle {"items": [...]} wrapper
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("items", out var itemsElement) &&
                itemsElement.ValueKind == JsonValueKind.Array)
            {
                return DeserializeArray(itemsElement);
            }
        }
        catch (JsonException)
        {
            // Intentionally swallowed — caller will try sanitization or fallback
        }

        return null;
    }

    private static List<Dictionary<string, JsonElement>> DeserializeArray(JsonElement arrayElement)
    {
        var result = new List<Dictionary<string, JsonElement>>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var dict = new Dictionary<string, JsonElement>();
                foreach (var prop in item.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.Clone();
                }
                result.Add(dict);
            }
        }
        return result;
    }

    /// <summary>
    /// Repair common non-JSON numeric formats emitted by LLMs.
    /// Removes thousands separators (50,000 → 50000) and trailing decimal dots (50000. → 50000).
    /// </summary>
    internal static string SanitizeNumericLiterals(string text)
    {
        // Remove thousands separators from unquoted numeric values
        text = Regex.Replace(
            text,
            @"([:\[,]\s*)(-?\d{1,3}(?:,\d{3})+)(\.\d+|\.)?(\s*[,}\]])",
            m =>
            {
                var prefix = m.Groups[1].Value;
                var number = m.Groups[2].Value.Replace(",", "");
                var decimals = m.Groups[3].Value;
                var suffix = m.Groups[4].Value;

                // Strip bare trailing dot (e.g. "50000.")
                if (decimals == ".") decimals = "";

                return prefix + number + decimals + suffix;
            });

        // Remove trailing decimal dots (e.g. 50000.)
        text = Regex.Replace(
            text,
            @"([:\[,]\s*)(-?\d+)\.(\s*[,}\]])",
            "$1$2$3");

        return text;
    }

    /// <summary>
    /// Validate raw JSON dictionaries and convert to <see cref="BomLineItem"/> models.
    /// Items that fail validation are logged and skipped.
    /// </summary>
    internal List<BomLineItem> ValidateItems(List<Dictionary<string, JsonElement>> rawItems)
    {
        var items = new List<BomLineItem>();

        for (var i = 0; i < rawItems.Count; i++)
        {
            try
            {
                var raw = rawItems[i];
                var item = new BomLineItem
                {
                    BomItem = GetStringValue(raw, "bom_item", "bomItem"),
                    Spec = GetStringValue(raw, "spec"),
                    Quantity = GetDoubleValue(raw, "quantity"),
                    AdditionalData = GetAdditionalData(raw, "additional_data", "additionalData"),
                };

                if (!string.IsNullOrWhiteSpace(item.BomItem) && !string.IsNullOrWhiteSpace(item.Spec))
                {
                    items.Add(item);
                }
                else
                {
                    _logger.LogWarning("Item {Index} skipped — missing bom_item or spec: {Raw}",
                        i, JsonSerializer.Serialize(raw));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Item {Index} validation failed", i);
            }
        }

        return items;
    }

    /// <summary>
    /// Get a string value from the dict, trying multiple key names (for camelCase/snake_case tolerance).
    /// </summary>
    private static string GetStringValue(Dictionary<string, JsonElement> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (dict.TryGetValue(key, out var element))
            {
                return element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.ToString();
            }
        }
        return string.Empty;
    }

    /// <summary>
    /// Get a nullable double value from the dict.
    /// </summary>
    private static double? GetDoubleValue(Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String when double.TryParse(element.GetString(), out var d) => d,
            _ => null
        };
    }

    /// <summary>
    /// Extract the additional_data dictionary, trying multiple key names.
    /// </summary>
    private static Dictionary<string, object?> GetAdditionalData(
        Dictionary<string, JsonElement> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (dict.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.Object)
            {
                return ConvertJsonObject(element);
            }
        }
        return new Dictionary<string, object?>();
    }

    /// <summary>
    /// Convert a JsonElement object to a Dictionary of primitive values.
    /// </summary>
    private static Dictionary<string, object?> ConvertJsonObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.ToString()
            };
        }
        return result;
    }
}
