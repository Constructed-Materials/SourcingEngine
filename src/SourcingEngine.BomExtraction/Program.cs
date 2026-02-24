using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SourcingEngine.BomExtraction.Configuration;
using SourcingEngine.BomExtraction.Parsing;
using SourcingEngine.BomExtraction.Services;

namespace SourcingEngine.BomExtraction;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: SourcingEngine.BomExtraction <file-or-folder> [--model <model-id>]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Supported formats: .pdf, .csv, .xlsx, .xls, .doc, .docx, .html, .txt, .md");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  SourcingEngine.BomExtraction estimate.csv");
            Console.Error.WriteLine("  SourcingEngine.BomExtraction ./bom_files/");
            Console.Error.WriteLine("  SourcingEngine.BomExtraction estimate.pdf --model amazon.nova-lite-v1:0");
            return 1;
        }

        var inputPath = args[0];
        string? modelOverride = null;

        // Parse optional --model flag
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] is "--model" or "-m")
            {
                modelOverride = args[i + 1];
                break;
            }
        }

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.Configure<BomExtractionSettings>(
                    context.Configuration.GetSection(BomExtractionSettings.SectionName));

                // Apply model override if specified
                if (modelOverride != null)
                {
                    services.PostConfigure<BomExtractionSettings>(s => s.ModelId = modelOverride);
                }

                services.AddSingleton<JsonResponseParser>();
                services.AddSingleton<IBomExtractionService, BomExtractionService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        var service = host.Services.GetRequiredService<IBomExtractionService>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        var filePaths = ResolveFilePaths(inputPath);
        if (filePaths.Count == 0)
        {
            Console.Error.WriteLine($"No supported BOM files found at: {inputPath}");
            return 1;
        }

        logger.LogInformation("Processing {Count} file(s)...", filePaths.Count);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        var exitCode = 0;

        foreach (var filePath in filePaths)
        {
            try
            {
                var result = await service.ExtractAsync(filePath);

                var json = JsonSerializer.Serialize(result, jsonOptions);
                Console.WriteLine(json);

                if (result.Warnings.Count > 0)
                {
                    foreach (var warning in result.Warnings)
                        logger.LogWarning("{Warning}", warning);
                }

                logger.LogInformation(
                    "Extracted {Count} items from {File} ({InputTokens}in/{OutputTokens}out)",
                    result.ItemCount, Path.GetFileName(filePath),
                    result.InputTokens, result.OutputTokens);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process {File}", filePath);
                exitCode = 1;
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Resolve the input path to a list of supported file paths.
    /// Accepts a single file or a directory (non-recursive).
    /// </summary>
    internal static List<string> ResolveFilePaths(string inputPath)
    {
        if (File.Exists(inputPath))
        {
            return [Path.GetFullPath(inputPath)];
        }

        if (Directory.Exists(inputPath))
        {
            var supported = Models.SupportedFileFormatExtensions.AllSupportedExtensions;
            return Directory.EnumerateFiles(inputPath)
                .Where(f => supported.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .OrderBy(f => f)
                .ToList();
        }

        return [];
    }
}
