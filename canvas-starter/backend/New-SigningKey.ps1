$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot '.keys'
$key = Join-Path $directory 'IrsLeap.Plugins.snk'
if (Test-Path $key) {
    Write-Output "Keeping existing signing key: $key"
    return
}
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$parameters = [System.Security.Cryptography.CspParameters]::new()
$parameters.Flags = [System.Security.Cryptography.CspProviderFlags]::CreateEphemeralKey
$rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048, $parameters)
try {
    $rsa.PersistKeyInCsp = $false
    [System.IO.File]::WriteAllBytes($key, $rsa.ExportCspBlob($true))
    Write-Output "Created local, gitignored signing key: $key"
} finally {
    $rsa.Dispose()
}
