#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet tool restore
mkdir -p artifacts
dotnet lambda package --project-location src/TalumeDoc.Api --configuration Release --framework net10.0 --function-architecture x86_64 --msbuild-parameters "-m:1 -p:UseSharedCompilation=false -p:UseAppHost=false" --output-package artifacts/api.zip
dotnet publish -m:1 -p:UseSharedCompilation=false src/TalumeDoc.Worker -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o artifacts/worker
