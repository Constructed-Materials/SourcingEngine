# Multi-stage build for .NET 9 Lambda container image
# Build context should be the SourcingEngine repo root (for project references)

# ============================================================
# Stage 1: Build & Publish
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first for layer caching of NuGet restore
COPY src/SourcingEngine.Common/SourcingEngine.Common.csproj src/SourcingEngine.Common/
COPY src/SourcingEngine.Core/SourcingEngine.Core.csproj src/SourcingEngine.Core/
COPY src/SourcingEngine.Data/SourcingEngine.Data.csproj src/SourcingEngine.Data/
COPY src/SourcingEngine.Search.Lambda/SourcingEngine.Search.Lambda.csproj src/SourcingEngine.Search.Lambda/

RUN dotnet restore src/SourcingEngine.Search.Lambda/SourcingEngine.Search.Lambda.csproj \
    --runtime linux-x64

# Copy all source files
COPY src/SourcingEngine.Common/ src/SourcingEngine.Common/
COPY src/SourcingEngine.Core/ src/SourcingEngine.Core/
COPY src/SourcingEngine.Data/ src/SourcingEngine.Data/
COPY src/SourcingEngine.Search.Lambda/ src/SourcingEngine.Search.Lambda/

# Publish optimised for Lambda (ReadyToRun requires explicit RID)
RUN dotnet publish src/SourcingEngine.Search.Lambda/SourcingEngine.Search.Lambda.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --output /app/publish \
    --no-restore \
    --self-contained false

# ============================================================
# Stage 2: Lambda Runtime
# Uses AWS .NET 9 base image with Lambda RIC + RIE (local testing)
# ============================================================
FROM public.ecr.aws/lambda/dotnet:9 AS runtime

# Copy published output to Lambda task root
COPY --from=build /app/publish ${LAMBDA_TASK_ROOT}

# Set the handler: Assembly::Namespace.Class::Method
CMD ["SourcingEngine.Search.Lambda::SourcingEngine.Search.Lambda.Function::FunctionHandler"]
