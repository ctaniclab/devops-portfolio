param([ValidateSet('win-x64','win-x86')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish src/MailArchive/MailArchive.csproj -c Release -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "dist/$Runtime"
    if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась с ошибкой.' }
    Copy-Item README.md "dist/$Runtime/README.md"
    Copy-Item VERIFICATION.md "dist/$Runtime/VERIFICATION.md"
    Copy-Item LICENSE.md "dist/$Runtime/LICENSE.md"
    Copy-Item THIRD-PARTY-NOTICES.md "dist/$Runtime/THIRD-PARTY-NOTICES.md"
    New-Item -ItemType Directory -Force "dist/$Runtime/licenses" | Out-Null
    Copy-Item vendor/XstReader/LICENSE-MS-PL.md "dist/$Runtime/licenses/"
    Copy-Item vendor/XstReader/LICENSE-LGPL-3.0.txt "dist/$Runtime/licenses/"
    Copy-Item vendor/XstReader/LICENSE-GPL-3.0.txt "dist/$Runtime/licenses/"
    Copy-Item vendor/XstReader/LICENSE-DOTNET.txt "dist/$Runtime/licenses/"
    Copy-Item vendor/XstReader/NOTICES-DOTNET.txt "dist/$Runtime/licenses/"
    Compress-Archive -Path "dist/$Runtime/*" -DestinationPath "dist/MailArchive-$Runtime.zip" -Force
    Write-Host "Готово: dist/MailArchive-$Runtime.zip"
} finally { Pop-Location }
