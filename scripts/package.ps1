param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts'
$staging = Join-Path $artifactRoot ('package-' + [guid]::NewGuid().ToString('N'))
$appFolder = Join-Path $staging 'TimerManager'
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
Push-Location $repoRoot
try {
    & $Dotnet publish src/TimerManager.App/TimerManager.App.csproj -c Release -r win-x64 --self-contained true -o $appFolder
    if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $appFolder
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/portable-readme.txt') -Destination (Join-Path $appFolder 'README.txt')
    $noticeFolder = Join-Path $appFolder 'ThirdPartyNotices'
    New-Item -ItemType Directory -Path $noticeFolder -Force | Out-Null
    $assets = Get-Content (Join-Path $repoRoot 'src/TimerManager.App/obj/project.assets.json') -Raw | ConvertFrom-Json
    $packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object {
        [pscustomobject]@{ Path = $_.Value.path; Label = $_.Name -replace '/', '-' }
    })
    foreach ($download in $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies) {
        if ($download.name -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.') { continue }
        $version = $download.version.Trim('[', ']').Split(',')[0].Trim()
        $packages += [pscustomobject]@{ Path = $download.name.ToLowerInvariant() + '/' + $version; Label = $download.name + '-' + $version }
    }
    foreach ($package in $packages) {
        foreach ($packageRoot in $assets.packageFolders.PSObject.Properties.Name) {
            $packagePath = Join-Path $packageRoot $package.Path
            if (-not (Test-Path -LiteralPath $packagePath)) { continue }
            $destination = Join-Path $noticeFolder $package.Label
            $notices = Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(licen[sc]e|notice|third.party)' }
            if ($notices) {
                New-Item -ItemType Directory -Path $destination -Force | Out-Null
                $notices | Copy-Item -Destination $destination
            }
            break
        }
    }
    $archive = Join-Path $artifactRoot 'TimerManager-win-x64.zip'
    Compress-Archive -LiteralPath $appFolder -DestinationPath $archive -Force
    Get-FileHash -LiteralPath $archive -Algorithm SHA256 | ForEach-Object {
        "$($_.Hash.ToLowerInvariant())  TimerManager-win-x64.zip" | Set-Content -LiteralPath "$archive.sha256"
    }
    Write-Output "Package: $archive"
    Write-Output "Runnable folder: $appFolder"
}
finally { Pop-Location }
