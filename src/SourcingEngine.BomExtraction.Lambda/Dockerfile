# Multi-stage build for .NET 9 Lambda container image
# Build context should be the SourcingEngine repo root (for project references)

# ============================================================
# Stage 1: Build & Publish
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first for layer caching of NuGet restore
COPY src/SourcingEngine.BomExtraction/SourcingEngine.BomExtraction.csproj src/SourcingEngine.BomExtraction/
COPY src/SourcingEngine.BomExtraction.Lambda/SourcingEngine.BomExtraction.Lambda.csproj src/SourcingEngine.BomExtraction.Lambda/

RUN dotnet restore src/SourcingEngine.BomExtraction.Lambda/SourcingEngine.BomExtraction.Lambda.csproj \
    --runtime linux-x64

# Copy all source files
COPY src/SourcingEngine.BomExtraction/ src/SourcingEngine.BomExtraction/
COPY src/SourcingEngine.BomExtraction.Lambda/ src/SourcingEngine.BomExtraction.Lambda/

# Publish optimised for Lambda (ReadyToRun requires explicit RID)
RUN dotnet publish src/SourcingEngine.BomExtraction.Lambda/SourcingEngine.BomExtraction.Lambda.csproj \
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
CMD ["SourcingEngine.BomExtraction.Lambda::SourcingEngine.BomExtraction.Lambda.Function::FunctionHandler"]
