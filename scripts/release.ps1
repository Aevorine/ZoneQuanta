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

function Remove-WorkspaceArtifact([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($root.Path).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw '拒绝删除工作区以外的路径'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

function Invoke-Native([scriptblock]$Cmd) {
    & $Cmd
    if ($LASTEXITCODE -ne 0) { throw "命令失败 ($LASTEXITCODE)：$Cmd" }
}

(Get-Content $proj -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" | Set-Content $proj -NoNewline

Remove-WorkspaceArtifact $dist
New-Item -ItemType Directory -Path $dist | Out-Null

$obj = Join-Path $root 'src\ZoneQuanta\obj'
Remove-WorkspaceArtifact $obj
$out = Join-Path $dist 'build'
Invoke-Native { dotnet publish $proj -c Release -o $out -p:Flavor=full -p:PublishReadyToRun=true -nologo -v q }
Move-Item (Join-Path $out 'ZoneQuanta.exe') (Join-Path $dist 'ZoneQuanta.exe')
Remove-WorkspaceArtifact $out

$file = Join-Path $dist 'ZoneQuanta.exe'
$entry = [ordered]@{
    name   = 'ZoneQuanta.exe'
    size   = (Get-Item $file).Length
    sha256 = (Get-FileHash $file -Algorithm SHA256).Hash
}
$assets = [ordered]@{ lite = $entry; full = $entry }

$payload = [ordered]@{ version = $Version; notes = $Notes; assets = $assets } | ConvertTo-Json -Compress -Depth 5

$password = (Get-Content (Join-Path $SecretDir 'update-signing-key-password.txt') -Raw).Trim()
$ec = [System.Security.Cryptography.ECDsa]::Create()
$ec.ImportEncryptedPkcs8PrivateKey($password, [IO.File]::ReadAllBytes((Join-Path $SecretDir 'update-signing-key.p8')), [ref]$null)
$sig = [Convert]::ToBase64String($ec.SignData([Text.Encoding]::UTF8.GetBytes($payload), [System.Security.Cryptography.HashAlgorithmName]::SHA256))
$password = $null

[ordered]@{ payload = $payload; sig = $sig } | ConvertTo-Json -Compress | Set-Content (Join-Path $dist 'update.json') -Encoding utf8 -NoNewline

if ($SkipPublish) { Write-Output "构建完成（未发布）：$dist"; return }

$files = @('ZoneQuanta.exe', 'update.json') | ForEach-Object { Join-Path $dist $_ }
$body = if ($Notes) { $Notes } else { "ZoneQuanta $tag" }
$notesFile = Join-Path $dist 'release-notes.md'
$body | Set-Content -LiteralPath $notesFile -Encoding utf8
Invoke-Native { gh release create $tag @files --repo $repo --title "ZoneQuanta $tag" --notes-file $notesFile --latest }
Remove-Item -LiteralPath $notesFile

$releases = gh release list --repo $repo --limit 100 --json tagName,publishedAt | ConvertFrom-Json | Sort-Object publishedAt -Descending
$releases | Select-Object -Skip $Keep | ForEach-Object {
    $oldTag = $_.tagName
    Invoke-Native { gh release delete $oldTag --repo $repo --yes --cleanup-tag }
}

# Keep the current package locally; remove obsolete build outputs.
foreach ($d in 'bin', 'obj') {
    $p = Join-Path $root "src\ZoneQuanta\$d"
    Remove-WorkspaceArtifact $p
}
Write-Output "已发布 $tag，保留最近 $Keep 个版本"
