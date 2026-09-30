#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore TalumeDoc.sln --locked-mode
dotnet test TalumeDoc.sln --no-restore --configuration Release
npm ci
npm run build
npm test -- --runInBand
bash scripts/publish.sh
npx cdk synth
