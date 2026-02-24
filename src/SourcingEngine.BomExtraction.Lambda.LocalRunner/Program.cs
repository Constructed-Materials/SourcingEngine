// BOM Extraction Lambda — Local Debug Runner
// Usage:
//   dotnet run --project src/SourcingEngine.BomExtraction.Lambda.LocalRunner
//   dotnet run --project src/SourcingEngine.BomExtraction.Lambda.LocalRunner -- --event path/to/event.json

await LocalRunner.RunAsync(args);
