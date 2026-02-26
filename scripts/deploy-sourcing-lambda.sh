#!/usr/bin/env bash
# ============================================================================
# deploy-sourcing-lambda.sh — Build, push, and deploy the SourcingEngine
#                              Search Lambda
#
# Usage:
#   ./deploy-sourcing-lambda.sh                    # Build + push image only
#   ./deploy-sourcing-lambda.sh --deploy           # Build + push + CDK deploy
#   ./deploy-sourcing-lambda.sh --synth            # CDK synth only (dry run)
#
# Prerequisites:
#   - AWS CLI configured (aws sts get-caller-identity works)
#   - Docker running
#   - .NET 9 SDK installed
#   - AWS CDK CLI installed (npm install -g aws-cdk)
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LAMBDA_DIR="$REPO_ROOT/src/SourcingEngine.Search.Lambda"
CDK_DIR="$REPO_ROOT/infra/BomExtractionLambdaCdk"

# Defaults
AWS_REGION="${AWS_REGION:-us-east-2}"
ECR_REPO_NAME="sourcing-engine-lambda"
IMAGE_TAG="${IMAGE_TAG:-latest}"
DEPLOY=false
SYNTH_ONLY=false

# Parse args
for arg in "$@"; do
    case $arg in
        --deploy)  DEPLOY=true ;;
        --synth)   SYNTH_ONLY=true ;;
        --help|-h) echo "Usage: $0 [--deploy|--synth]"; exit 0 ;;
    esac
done

echo "=== SourcingEngine Search Lambda Deployment ==="
echo "Region:    $AWS_REGION"
echo "ECR Repo:  $ECR_REPO_NAME"
echo "Image Tag: $IMAGE_TAG"
echo

# Get AWS account ID
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
ECR_URI="$ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO_NAME"

echo "Account: $ACCOUNT_ID"
echo "ECR URI: $ECR_URI"
echo

# --- CDK Synth only ---
if $SYNTH_ONLY; then
    echo "=== CDK Synth (dry run) ==="
    cd "$CDK_DIR"
    cdk synth
    echo "=== Synth complete ==="
    exit 0
fi

# --- Step 1: Docker build (always target linux/amd64 for Lambda) ---
echo "=== Step 1: Building Docker image ==="
docker build \
    --platform linux/amd64 \
    --provenance=false \
    -t "$ECR_REPO_NAME:$IMAGE_TAG" \
    -f "$LAMBDA_DIR/Dockerfile" \
    "$REPO_ROOT"

echo "Docker image built: $ECR_REPO_NAME:$IMAGE_TAG"
echo

# --- Step 2: ECR login + push ---
echo "=== Step 2: Pushing to ECR ==="
aws ecr get-login-password --region "$AWS_REGION" \
    | docker login --username AWS --password-stdin "$ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com"

# Create repo if it doesn't exist
aws ecr describe-repositories --repository-names "$ECR_REPO_NAME" --region "$AWS_REGION" 2>/dev/null \
    || aws ecr create-repository --repository-name "$ECR_REPO_NAME" --region "$AWS_REGION"

docker tag "$ECR_REPO_NAME:$IMAGE_TAG" "$ECR_URI:$IMAGE_TAG"
docker push "$ECR_URI:$IMAGE_TAG"

echo "Image pushed: $ECR_URI:$IMAGE_TAG"
echo

# --- Step 3: CDK Deploy (optional) ---
if $DEPLOY; then
    echo "=== Step 3: CDK Deploy ==="
    cd "$CDK_DIR"
    cdk deploy --require-approval broadening
    echo "=== Deployment complete ==="
else
    echo "=== Image pushed. Run with --deploy to also deploy CDK stack ==="
    echo "Or manually: cd infra/BomExtractionLambdaCdk && cdk deploy"
fi

# --- Step 4: Update Lambda to use new image ---
if $DEPLOY; then
    echo "=== Step 4: Updating Lambda function image ==="
    aws lambda update-function-code \
        --function-name sourcing-engine-dotnet \
        --image-uri "$ECR_URI:$IMAGE_TAG" \
        --region "$AWS_REGION"
    echo "Lambda function updated with new image."
fi

echo
echo "=== Done ==="
