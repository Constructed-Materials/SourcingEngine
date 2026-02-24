using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using Constructs;

namespace SourcingEngine.BomExtraction.Cdk;

/// <summary>
/// CDK Stack for the BOM Extraction Lambda function.
///
/// Pre-existing resources (NOT created by this stack):
///   - Amazon MQ RabbitMQ broker (us-east-2)
///   - RabbitMQ queues/exchanges (bom-extraction-queue, etc.)
///   - Secrets Manager secret with broker credentials
///   - (Optional) VPC with private subnets — only needed if broker is NOT publicly accessible
///
/// Resources created:
///   - ECR Repository (for Lambda container image)
///   - Lambda Function (container image, optionally VPC-attached)
///   - IAM policies (Bedrock, S3, Secrets Manager)
///   - Event source mapping (Amazon MQ → Lambda)
///   - (If VPC) Security group for Lambda ENIs
/// </summary>
public class BomExtractionLambdaStack : Stack
{
    public BomExtractionLambdaStack(Construct scope, string id, IStackProps? props = null)
        : base(scope, id, props)
    {
        // ================================================================
        // Context values — pass via cdk.json or --context flags
        // ================================================================
        var brokerArn = (string)this.Node.TryGetContext("brokerArn")
            ?? "arn:aws:mq:us-east-2:866934058848:broker:cm-app-queue:b-24d11402-33d4-43b1-91a8-4725eb95eade";
        var brokerHost = (string)this.Node.TryGetContext("brokerHost")
            ?? "b-24d11402-33d4-43b1-91a8-4725eb95eade.mq.us-east-2.on.aws";
        var secretArn = (string)this.Node.TryGetContext("brokerSecretArn")
            ?? "";
        var vpcId = (string)this.Node.TryGetContext("vpcId")
            ?? "";
        var queueName = (string)this.Node.TryGetContext("queueName")
            ?? "bom-extraction-queue";

        var useVpc = !string.IsNullOrEmpty(vpcId);

        // ================================================================
        // (Optional) Lookup existing VPC — only when broker is private
        // If your MQ broker is publicly accessible, skip VPC entirely.
        // ================================================================
        IVpc? vpc = null;
        SecurityGroup? lambdaSg = null;

        if (useVpc)
        {
            vpc = Vpc.FromLookup(this, "BrokerVpc", new VpcLookupOptions
            {
                VpcId = vpcId,
            });

            lambdaSg = new SecurityGroup(this, "LambdaSg", new SecurityGroupProps
            {
                Vpc = vpc,
                Description = "Security group for BOM Extraction Lambda ENIs",
                AllowAllOutbound = true,
            });
        }

        // ================================================================
        // ECR Repository — created by deploy script, imported here
        // ================================================================
        var ecrRepo = Repository.FromRepositoryName(this, "BomExtractionLambdaRepo", "bom-extraction-lambda");

        // ================================================================
        // Secrets Manager secret (existing — imported by ARN)
        // ================================================================
        ISecret brokerSecret = null!;
        if (!string.IsNullOrEmpty(secretArn))
        {
            brokerSecret = Secret.FromSecretCompleteArn(this, "BrokerSecret", secretArn);
        }

        // ================================================================
        // IAM Role for the Lambda function
        // ================================================================
        var lambdaRole = new Role(this, "LambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Execution role for BOM Extraction Lambda",
            ManagedPolicies = new[]
            {
                // Basic execution (CloudWatch Logs). VPC access role added conditionally below.
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"),
            },
        });

        // VPC execution role — only needed when Lambda is VPC-attached
        if (useVpc)
        {
            lambdaRole.AddManagedPolicy(
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaVPCAccessExecutionRole"));
        }

        // Bedrock — invoke model (foundation models + cross-region inference profiles)
        // The us.* inference profile routes to ANY US region, so we must allow all regions.
        lambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "BedrockInvoke",
            Effect = Effect.ALLOW,
            Actions = new[] { "bedrock:InvokeModel", "bedrock:InvokeModelWithResponseStream" },
            Resources = new[]
            {
                // Foundation models — any region (cross-region profile picks the region)
                "arn:aws:bedrock:*::foundation-model/amazon.nova-pro-v1:0",
                // Cross-region inference profiles
                $"arn:aws:bedrock:us-east-2:{Aws.ACCOUNT_ID}:inference-profile/us.amazon.nova-pro-v1:0",
            },
        }));

        // S3 — download BOM files from presigned URLs (or direct s3:// paths)
        lambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "S3BomAccess",
            Effect = Effect.ALLOW,
            Actions = new[] { "s3:GetObject" },
            Resources = new[] { "arn:aws:s3:::*/*" }, // Tighten to specific buckets in production
        }));

        // Secrets Manager — retrieve broker credentials
        if (brokerSecret != null)
        {
            brokerSecret.GrantRead(lambdaRole);
        }

        // Amazon MQ — required for event source mapping
        lambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "MQAccess",
            Effect = Effect.ALLOW,
            Actions = new[]
            {
                "mq:DescribeBroker",
                "ec2:CreateNetworkInterface",
                "ec2:DescribeNetworkInterfaces",
                "ec2:DescribeVpcs",
                "ec2:DeleteNetworkInterface",
                "ec2:DescribeSubnets",
                "ec2:DescribeSecurityGroups",
            },
            Resources = new[] { "*" },
        }));

        // ================================================================
        // Lambda Function (container image)
        // ================================================================
        var function = new DockerImageFunction(this, "BomExtractionLambda", new DockerImageFunctionProps
        {
            FunctionName = "bom-extraction-dotnet",
            Description = "Extracts BOM line items from documents using Bedrock Converse API",
            Code = DockerImageCode.FromEcr(ecrRepo, new EcrImageCodeProps
            {
                TagOrDigest = "latest",
            }),
            MemorySize = 512,
            Timeout = Duration.Seconds(180),
            Role = lambdaRole,
            // VPC config — only when broker is private
            Vpc = vpc,
            VpcSubnets = useVpc ? new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS } : null,
            SecurityGroups = lambdaSg != null ? new[] { lambdaSg } : null,
            Environment = new Dictionary<string, string>
            {
                ["BomExtraction__Region"] = "us-east-2",
                ["BomExtraction__ModelId"] = "us.amazon.nova-pro-v1:0",
                ["BomExtraction__MaxTokens"] = "5000",
                ["BomExtraction__Temperature"] = "0",
                ["BomExtraction__TimeoutSeconds"] = "120",
                ["Lambda__BrokerHost"] = brokerHost,
                ["Lambda__BrokerPort"] = "5671",
                ["Lambda__BrokerSecretArn"] = secretArn,
                ["Lambda__ResultExchange"] = "bom.extraction",
                ["Lambda__ResultRoutingKey"] = "extract.result",
                ["Lambda__TempDirectory"] = "/tmp/bom-extraction",
            },
        });

        // Explicit log group with retention
        _ = new LogGroup(this, "LambdaLogGroup", new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{function.FunctionName}",
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        // ================================================================
        // Event Source Mapping: Amazon MQ (RabbitMQ) → Lambda
        // CDK L2 doesn't have a dedicated RabbitMQ event source construct,
        // so we use CfnResource (L1) for full control.
        // ================================================================
        var eventSourceMapping = new CfnResource(this, "MqEventSourceMapping", new CfnResourceProps
        {
            Type = "AWS::Lambda::EventSourceMapping",
            Properties = new Dictionary<string, object>
            {
                ["EventSourceArn"] = brokerArn,
                ["FunctionName"] = function.FunctionArn,
                ["Queues"] = new[] { queueName },
                ["BatchSize"] = 1,
                ["Enabled"] = true,
                ["SourceAccessConfigurations"] = string.IsNullOrEmpty(secretArn)
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new Dictionary<string, string>
                        {
                            ["Type"] = "BASIC_AUTH",
                            ["URI"] = secretArn,
                        },
                    },
            },
        });

        // ================================================================
        // Outputs
        // ================================================================
        _ = new CfnOutput(this, "LambdaFunctionArn", new CfnOutputProps
        {
            Value = function.FunctionArn,
            Description = "ARN of the BOM Extraction Lambda function",
        });

        _ = new CfnOutput(this, "EcrRepositoryUri", new CfnOutputProps
        {
            Value = $"{Aws.ACCOUNT_ID}.dkr.ecr.{Aws.REGION}.amazonaws.com/bom-extraction-lambda",
            Description = "ECR repository URI for pushing container images",
        });
    }
}
