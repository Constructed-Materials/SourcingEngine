using Amazon.CDK;

namespace SourcingEngine.BomExtraction.Cdk;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var app = new App();

        _ = new BomExtractionLambdaStack(app, "BomExtractionLambdaStack", new StackProps
        {
            Env = new Amazon.CDK.Environment
            {
                Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
                Region = "us-east-2",
            },
            Description = "BOM Extraction Lambda — triggered by Amazon MQ RabbitMQ, extracts BOMs via Bedrock",
        });

        app.Synth();
    }
}
