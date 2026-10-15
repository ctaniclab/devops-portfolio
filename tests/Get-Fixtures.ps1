$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot 'fixtures'
New-Item -ItemType Directory -Force $destination | Out-Null
$commit = 'f158a64acf2a0e46ac3bd699bc7a5a8da6c40d26'
$hashes = @{
    'example-2013.ost' = 'b18ff390d0c22273029ee580ab6025976af69a4f84e276d1b1c01a988650853d'
    'dist-list.pst' = 'c86841da106036b5abe5a2141dc7644cbb2bf8b504873515eb35a2efeb8c28ac'
}
foreach ($name in @('example-2013.ost','dist-list.pst')) {
    $path = Join-Path $destination $name
    Invoke-WebRequest "https://raw.githubusercontent.com/rjohnsondev/java-libpst/$commit/src/test/resources/$name" -OutFile $path
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$name]) {
        throw "Не совпала контрольная сумма тестового файла $name"
    }
}
Write-Host "Тестовые архивы сохранены в $destination"
