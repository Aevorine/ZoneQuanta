param(
    [string]$SecretDir = $env:ZONEQUANTA_SECRET_DIR
)

$ErrorActionPreference = 'Stop'
if (-not $SecretDir) { throw '请设置环境变量 ZONEQUANTA_SECRET_DIR 或传入 -SecretDir（密钥存放目录，必须在仓库之外）' }
$keyFile = Join-Path $SecretDir 'update-signing-key.p8'
$passFile = Join-Path $SecretDir 'update-signing-key-password.txt'
$pubFile = Join-Path $PSScriptRoot '..\src\ZoneQuanta\Core\Update\UpdateKey.cs'

if (Test-Path $keyFile) { throw "密钥已存在：$keyFile（不覆盖）" }
New-Item -ItemType Directory -Force -Path $SecretDir | Out-Null

$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$password = [Convert]::ToBase64String($bytes)

$ec = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256'))
$pbe = New-Object System.Security.Cryptography.PbeParameters ([System.Security.Cryptography.PbeEncryptionAlgorithm]::Aes256Cbc), ([System.Security.Cryptography.HashAlgorithmName]::SHA256), 200000
[IO.File]::WriteAllBytes($keyFile, $ec.ExportEncryptedPkcs8PrivateKey($password, $pbe))
Set-Content -Path $passFile -Value $password -NoNewline -Encoding ascii

$pub = [Convert]::ToBase64String($ec.ExportSubjectPublicKeyInfo())
$src = @"
namespace ZoneQuanta.Core.Update;

internal static class UpdateKey
{
    public const string PublicKeyBase64 = "$pub";
}
"@
Set-Content -Path $pubFile -Value $src -Encoding utf8
Write-Output "OK public key -> $pubFile"
