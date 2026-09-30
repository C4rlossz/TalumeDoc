$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "Falha ao restaurar ferramenta Lambda" }
New-Item -ItemType Directory -Force artifacts | Out-Null
dotnet lambda package --project-location src/TalumeDoc.Api --configuration Release --framework net10.0 --function-architecture x86_64 --msbuild-parameters "-m:1 -p:UseSharedCompilation=false -p:UseAppHost=false" --output-package artifacts/api.zip
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar API' }
dotnet publish -m:1 -p:UseSharedCompilation=false src/TalumeDoc.Worker -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o artifacts/worker
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar Worker' }
