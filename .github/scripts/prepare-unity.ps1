$ErrorActionPreference = 'Stop'
$env:UNITY_CLI_CHANNEL = 'beta'
Invoke-RestMethod https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | Invoke-Expression

$unityBin = Join-Path $env:LOCALAPPDATA 'Unity\bin'
Add-Content -Path $env:GITHUB_PATH -Value $unityBin
$env:PATH = "$unityBin;$env:PATH"
unity --version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($env:UNITY_LICENSE) {
    $licenseFile = Join-Path $env:RUNNER_TEMP 'kern-ci.ulf'
    [System.IO.File]::WriteAllText($licenseFile, $env:UNITY_LICENSE)
    unity license activate --file $licenseFile
} elseif ($env:UNITY_SERIAL) {
    unity license activate --serial $env:UNITY_SERIAL
} else {
    Write-Error 'Unity validation requires UNITY_LICENSE or UNITY_SERIAL in repository Actions secrets.'
    exit 1
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
