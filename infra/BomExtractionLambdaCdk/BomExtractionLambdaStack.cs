using Amazon.CDK;
using Amazon.CDK.AWS.EC2;
using Amazon.CDK.AWS.ECR;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.SecretsManager;
using Amazon.CDK.CustomResources;
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

        // ================================================================
        // SourcingEngine Search Lambda
        // Triggered by bom-extraction-result-queue, runs product search
        // for each BOM item, publishes to sourcing.engine exchange.
        // ================================================================

        var sourcingQueueName = (string)this.Node.TryGetContext("sourcingQueueName")
            ?? "bom-extraction-result-queue";
        var sourcingDbSecretArn = (string)this.Node.TryGetContext("sourcingDbSecretArn")
            ?? "";

        // ECR Repository — created by deploy script, imported here
        var sourcingEcrRepo = Repository.FromRepositoryName(this, "SourcingEngineLambdaRepo", "sourcing-engine-lambda");

        // IAM Role
        var sourcingLambdaRole = new Role(this, "SourcingEngineLambdaRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
            Description = "Execution role for SourcingEngine Search Lambda",
            ManagedPolicies = new[]
            {
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole"),
            },
        });

        if (useVpc)
        {
            sourcingLambdaRole.AddManagedPolicy(
                ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaVPCAccessExecutionRole"));
        }

        // Bedrock — embedding + parsing models
        sourcingLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "BedrockInvokeSourcing",
            Effect = Effect.ALLOW,
            Actions = new[] { "bedrock:InvokeModel", "bedrock:InvokeModelWithResponseStream" },
            Resources = new[]
            {
                "arn:aws:bedrock:*::foundation-model/amazon.titan-embed-text-v2:0",
                "arn:aws:bedrock:*::foundation-model/amazon.nova-lite-v1:0",
                $"arn:aws:bedrock:us-east-2:{Aws.ACCOUNT_ID}:inference-profile/us.amazon.nova-lite-v1:0",
            },
        }));

        // Secrets Manager — broker credentials
        if (brokerSecret != null)
        {
            brokerSecret.GrantRead(sourcingLambdaRole);
        }

        // Secrets Manager — database connection string
        if (!string.IsNullOrEmpty(sourcingDbSecretArn))
        {
            var dbSecret = Secret.FromSecretCompleteArn(this, "SourcingDbSecret", sourcingDbSecretArn);
            dbSecret.GrantRead(sourcingLambdaRole);
        }

        // Amazon MQ — event source mapping
        sourcingLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "MQAccessSourcing",
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

        // Lambda Function
        var sourcingFunction = new DockerImageFunction(this, "SourcingEngineLambda", new DockerImageFunctionProps
        {
            FunctionName = "sourcing-engine-dotnet",
            Description = "Searches product catalog for BOM items, triggered by extraction results queue",
            Code = DockerImageCode.FromEcr(sourcingEcrRepo, new EcrImageCodeProps
            {
                TagOrDigest = "latest",
            }),
            MemorySize = 1024,
            Timeout = Duration.Seconds(300),
            Role = sourcingLambdaRole,
            Vpc = vpc,
            VpcSubnets = useVpc ? new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS } : null,
            SecurityGroups = lambdaSg != null ? new[] { lambdaSg } : null,
            Environment = new Dictionary<string, string>
            {
                ["SemanticSearch__Enabled"] = "true",
                ["SemanticSearch__MatchCount"] = "5",
                ["SemanticSearch__SimilarityThreshold"] = "0.4",
                ["SemanticSearch__EnableSpecReRanking"] = "true",
                ["SemanticSearch__ReRankerSemanticWeight"] = "0.6",
                ["SemanticSearch__ReRankerSpecWeight"] = "0.4",
                ["Bedrock__Enabled"] = "true",
                ["Bedrock__Region"] = "us-east-2",
                ["Bedrock__EmbeddingModelId"] = "amazon.titan-embed-text-v2:0",
                ["Bedrock__EmbeddingDimension"] = "1024",
                ["Bedrock__EmbeddingNormalize"] = "true",
                ["Bedrock__ParsingModelId"] = "us.amazon.nova-lite-v1:0",
                ["Bedrock__ParsingMaxTokens"] = "500",
                ["Bedrock__ParsingTemperature"] = "0.1",
                ["Bedrock__TimeoutSeconds"] = "30",
                ["Bedrock__MaxConcurrentEmbeddings"] = "5",
                ["Lambda__BrokerHost"] = brokerHost,
                ["Lambda__BrokerPort"] = "5671",
                ["Lambda__BrokerSecretArn"] = secretArn,
                ["Lambda__BrokerUseSsl"] = "true",
                ["Lambda__DatabaseSecretArn"] = sourcingDbSecretArn,
                ["Lambda__ResultExchange"] = "sourcing.engine",
                ["Lambda__ResultRoutingKey"] = "search.result",
                ["Lambda__ZeroResultRoutingKey"] = "search.zero-result",
            },
        });

        // Log group
        _ = new LogGroup(this, "SourcingEngineLambdaLogGroup", new LogGroupProps
        {
            LogGroupName = $"/aws/lambda/{sourcingFunction.FunctionName}",
            Retention = RetentionDays.TWO_WEEKS,
            RemovalPolicy = RemovalPolicy.DESTROY,
        });

        // Event Source Mapping: Amazon MQ (RabbitMQ) → SourcingEngine Lambda
        _ = new CfnResource(this, "SourcingMqEventSourceMapping", new CfnResourceProps
        {
            Type = "AWS::Lambda::EventSourceMapping",
            Properties = new Dictionary<string, object>
            {
                ["EventSourceArn"] = brokerArn,
                ["FunctionName"] = sourcingFunction.FunctionArn,
                ["Queues"] = new[] { sourcingQueueName },
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

        // Outputs
        _ = new CfnOutput(this, "SourcingEngineLambdaArn", new CfnOutputProps
        {
            Value = sourcingFunction.FunctionArn,
            Description = "ARN of the SourcingEngine Search Lambda function",
        });

        _ = new CfnOutput(this, "SourcingEngineEcrUri", new CfnOutputProps
        {
            Value = $"{Aws.ACCOUNT_ID}.dkr.ecr.{Aws.REGION}.amazonaws.com/sourcing-engine-lambda",
            Description = "ECR repository URI for pushing SourcingEngine Search Lambda images",
        });

        // ================================================================
        // RabbitMQ Topology — Custom Resource
        //
        // Provisions exchanges, queues, bindings, and policies on the
        // Amazon MQ broker via the RabbitMQ Management HTTP API.
        // Runs automatically during cdk deploy. Safe to re-run (idempotent).
        // On stack deletion, topology is preserved (queues not removed).
        // ================================================================
        if (!string.IsNullOrEmpty(secretArn))
        {
            var topologyHandler = new Function(this, "RabbitMqTopologyHandler", new FunctionProps
            {
                Runtime = Runtime.PYTHON_3_12,
                Handler = "index.on_event",
                Code = Code.FromAsset("lambda/rabbitmq-topology"),
                Timeout = Duration.Seconds(60),
                MemorySize = 128,
                Description = "Provisions RabbitMQ topology via Management API (Custom Resource)",
                Vpc = vpc,
                VpcSubnets = useVpc ? new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS } : null,
                SecurityGroups = lambdaSg != null ? new[] { lambdaSg } : null,
            });

            brokerSecret?.GrantRead(topologyHandler);

            var topologyProvider = new Provider(this, "RabbitMqTopologyProvider", new ProviderProps
            {
                OnEventHandler = topologyHandler,
            });

            // Full topology definition — both bom.extraction + sourcing.engine
            // Exchanges: PUT is idempotent (re-creating existing ones is safe)
            // Queues: only NEW queues listed (existing ones left untouched)
            // Bindings: idempotent for same (source, dest, routing_key)
            // Policies: used to attach DLX to existing bom-extraction-result-queue
            var topologyJson = @"{
  ""exchanges"": [
    { ""name"": ""bom.extraction"",     ""type"": ""direct"", ""durable"": true },
    { ""name"": ""bom.extraction.dlx"", ""type"": ""direct"", ""durable"": true },
    { ""name"": ""sourcing.engine"",    ""type"": ""direct"", ""durable"": true },
    { ""name"": ""sourcing.engine.dlx"",""type"": ""direct"", ""durable"": true }
  ],
  ""queues"": [
    { ""name"": ""sourcing-engine-search-results-queue"",      ""durable"": true },
    { ""name"": ""sourcing-engine-search-zero-results-queue"", ""durable"": true },
    { ""name"": ""sourcing-engine-poison-queue"",              ""durable"": true }
  ],
  ""bindings"": [
    { ""source"": ""sourcing.engine"",     ""destination"": ""sourcing-engine-search-results-queue"",      ""routing_key"": ""search.result"" },
    { ""source"": ""sourcing.engine"",     ""destination"": ""sourcing-engine-search-zero-results-queue"", ""routing_key"": ""search.zero-result"" },
    { ""source"": ""sourcing.engine.dlx"", ""destination"": ""sourcing-engine-poison-queue"",              ""routing_key"": ""search.poison"" }
  ],
  ""policies"": [
    {
      ""name"": ""sourcing-dlx-on-extraction-results"",
      ""pattern"": ""^bom-extraction-result-queue$"",
      ""definition"": {
        ""dead-letter-exchange"": ""sourcing.engine.dlx"",
        ""dead-letter-routing-key"": ""search.poison""
      },
      ""apply_to"": ""queues"",
      ""priority"": 10
    }
  ]
}";

            _ = new CustomResource(this, "RabbitMqTopology", new CustomResourceProps
            {
                ServiceToken = topologyProvider.ServiceToken,
                Properties = new Dictionary<string, object>
                {
                    ["BrokerHost"] = brokerHost,
                    ["SecretArn"] = secretArn,
                    ["Region"] = Aws.REGION,
                    ["Topology"] = topologyJson,
                },
            });
        }
    }
}
