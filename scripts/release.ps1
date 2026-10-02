param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Notes = '',
    [string]$SecretDir = $env:ZONEQUANTA_SECRET_DIR,
    [int]$Keep = 2,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
if (-not $SecretDir) { throw '请设置环境变量 ZONEQUANTA_SECRET_DIR 或传入 -SecretDir（签名密钥所在目录）' }
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj = Join-Path $root 'src\ZoneQuanta\ZoneQuanta.csproj'
$dist = Join-Path $root 'dist'
$repo = 'Aevorine/ZoneQuanta'
$tag = "v$Version"

function Invoke-Native([scriptblock]$Cmd) {
    & $Cmd
    if ($LASTEXITCODE -ne 0) { throw "命令失败 ($LASTEXITCODE)：$Cmd" }
}

(Get-Content $proj -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" | Set-Content $proj -NoNewline

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

foreach ($flavor in 'lite', 'full') {
    $obj = Join-Path $root 'src\ZoneQuanta\obj'
    if (Test-Path $obj) { Remove-Item $obj -Recurse -Force }
    $out = Join-Path $dist "build-$flavor"
    Invoke-Native { dotnet publish $proj -c Release -o $out -p:Flavor=$flavor -p:PublishReadyToRun=true -nologo -v q }
    $name = if ($flavor -eq 'full') { 'ZoneQuanta-standalone.exe' } else { 'ZoneQuanta.exe' }
    Move-Item (Join-Path $out 'ZoneQuanta.exe') (Join-Path $dist $name)
    Remove-Item $out -Recurse -Force
}

$assets = [ordered]@{}
foreach ($pair in @(@('lite', 'ZoneQuanta.exe'), @('full', 'ZoneQuanta-standalone.exe'))) {
    $file = Join-Path $dist $pair[1]
    $assets[$pair[0]] = [ordered]@{
        name   = $pair[1]
        size   = (Get-Item $file).Length
        sha256 = (Get-FileHash $file -Algorithm SHA256).Hash
    }
}

$payload = [ordered]@{ version = $Version; notes = $Notes; assets = $assets } | ConvertTo-Json -Compress -Depth 5

$password = (Get-Content (Join-Path $SecretDir 'update-signing-key-password.txt') -Raw).Trim()
$ec = [System.Security.Cryptography.ECDsa]::Create()
$ec.ImportEncryptedPkcs8PrivateKey($password, [IO.File]::ReadAllBytes((Join-Path $SecretDir 'update-signing-key.p8')), [ref]$null)
$sig = [Convert]::ToBase64String($ec.SignData([Text.Encoding]::UTF8.GetBytes($payload), [System.Security.Cryptography.HashAlgorithmName]::SHA256))
$password = $null

[ordered]@{ payload = $payload; sig = $sig } | ConvertTo-Json -Compress | Set-Content (Join-Path $dist 'update.json') -Encoding utf8 -NoNewline

if ($SkipPublish) { Write-Output "构建完成（未发布）：$dist"; return }

$files = @('ZoneQuanta.exe', 'ZoneQuanta-standalone.exe', 'update.json') | ForEach-Object { Join-Path $dist $_ }
$body = if ($Notes) { $Notes } else { "ZoneQuanta $tag" }
Invoke-Native { gh release create $tag @files --repo $repo --title "ZoneQuanta $tag" --notes $body --latest }

$releases = gh release list --repo $repo --limit 100 --json tagName,publishedAt | ConvertFrom-Json | Sort-Object publishedAt -Descending
$releases | Select-Object -Skip $Keep | ForEach-Object {
    gh release delete $_.tagName --repo $repo --yes --cleanup-tag
}

Remove-Item $dist -Recurse -Force
foreach ($d in 'bin', 'obj') {
    $p = Join-Path $root "src\ZoneQuanta\$d"
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
Write-Output "已发布 $tag，保留最近 $Keep 个版本"
