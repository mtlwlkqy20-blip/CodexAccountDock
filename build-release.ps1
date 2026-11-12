param([ValidateSet('Standard','Pet')][string]$Edition='Standard')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $MyInvocation.MyCommand.Path))
$releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ('release\'+$Edition)))
$portableRoot = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot 'portable'))
$qaRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot ('qa\installer\'+$Edition)))
$version = '3.0.0'
$compilerCandidates = @(
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
    'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $compiler) {
    throw 'Windows .NET Framework C# compiler was not found.'
}

$requiredInputs = @(
    'Program.cs',
    'DpiSupport.cs',
    'DesktopPet.cs',
    'app.manifest',
    'Dashboard.cs',
    'GlassBackdrop.cs',
    'DailyNews.cs',
    'NewsFeed.cs',
    'ResetFeed.cs',
    'ResetReminder.cs',
    'PlanInputs.cs',
    'PlanVisualControls.cs',
    'AiShop.cs',
    'CliSetup.cs',
    'PublicFeedTransport.cs',
    'NativeFeedHttp.cs',
    'DynamicBackground.cs',
    'MediaVideoDecoder.cs',
    'BackgroundDiagnostics.cs',
    'AppTypography.cs',
    'AccountUsageClient.cs',
    'IpQueryClient.cs',
    'AccountOnlineTools.cs',
    'AccountToolsCanvas.cs',
    'AccountToolsView.cs',
    'ModelEvidence.cs', 'WorkspaceTools.cs',
    'AccountToolsDiagnostics.cs',
    'ThemeArt.cs',
    'ThemeSelection.cs',
    'assets\background.jpg',
    'Installer.cs',
    'LICENSE',
    'README.md',
    'assets\app.ico'
)
foreach ($relative in $requiredInputs) {
    $candidate = Join-Path $projectRoot $relative
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Required build input is missing: $candidate"
    }
}

function Assert-SafeBuildDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate).TrimEnd('\')
    $expectedFull = [System.IO.Path]::GetFullPath($Expected).TrimEnd('\')
    $projectPrefix = $projectRoot.TrimEnd('\') + '\'
    if ($candidateFull -ne $expectedFull -or
        -not $candidateFull.StartsWith($projectPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing unsafe build cleanup path: $candidateFull"
    }

    $relative = $candidateFull.Substring($projectPrefix.Length)
    $current = $projectRoot
    $projectItem = Get-Item -LiteralPath $projectRoot -Force
    if (($projectItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing cleanup because the project root is a reparse point: $projectRoot"
    }
    foreach ($part in $relative.Split('\')) {
        if (-not $part) { continue }
        $current = Join-Path $current $part
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (-not $item.PSIsContainer) {
                throw "Refusing cleanup because a directory path is a file: $current"
            }
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing cleanup through a reparse point: $current"
            }
        }
    }

    if (Test-Path -LiteralPath $candidateFull) {
        $nestedReparsePoint = Get-ChildItem -LiteralPath $candidateFull -Force -Recurse |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($nestedReparsePoint) {
            throw "Refusing recursive cleanup because it contains a reparse point: $($nestedReparsePoint.FullName)"
        }
    }
}

Assert-SafeBuildDirectory -Candidate $releaseRoot -Expected (Join-Path $projectRoot ('release\'+$Edition))
Assert-SafeBuildDirectory -Candidate $qaRoot -Expected (Join-Path $projectRoot ('qa\installer\'+$Edition))

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
if (Test-Path -LiteralPath $qaRoot) {
    Remove-Item -LiteralPath $qaRoot -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $portableRoot 'assets') -Force | Out-Null
New-Item -ItemType Directory -Path $qaRoot -Force | Out-Null

$mainExe = Join-Path $portableRoot 'CodexAccountDock.exe'
$mainArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/langversion:5',
    "/out:$mainExe",
    "/win32icon:$(Join-Path $projectRoot 'assets\app.ico')",
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:System.Net.Http.dll',
    '/reference:System.Security.dll',
    '/reference:System.Management.dll',
    (Join-Path $projectRoot 'Program.cs'),
    (Join-Path $projectRoot 'DpiSupport.cs'),
    (Join-Path $projectRoot 'DesktopPet.cs'),
    (Join-Path $projectRoot 'Dashboard.cs'),
    (Join-Path $projectRoot 'GlassBackdrop.cs'),
    (Join-Path $projectRoot 'DailyNews.cs'),
    (Join-Path $projectRoot 'ThemeArt.cs'),
    (Join-Path $projectRoot 'ThemeSelection.cs'),
    (Join-Path $projectRoot 'AppTypography.cs'),
    (Join-Path $projectRoot 'AccountUsageClient.cs'),
    (Join-Path $projectRoot 'IpQueryClient.cs'),
    "/resource:$(Join-Path $projectRoot 'assets\background.jpg'),CodexAccountDock.Background.jpg"
)
$mainArguments += @("$projectRoot\SidePanels.cs", "$projectRoot\StaggerPlan.cs", "$projectRoot\AppearanceOptions.cs", "$projectRoot\ReferenceSurface.cs", "$projectRoot\NewsFeed.cs", "$projectRoot\ResetFeed.cs", "$projectRoot\ResetReminder.cs", "$projectRoot\PlanInputs.cs", "$projectRoot\PlanVisualControls.cs")
$mainArguments += "$projectRoot\AiShop.cs"
$mainArguments += "$projectRoot\CliSetup.cs"
$mainArguments += "$projectRoot\PublicFeedTransport.cs"
$mainArguments += "$projectRoot\NativeFeedHttp.cs"
$mainArguments += @("$projectRoot\DynamicBackground.cs", "$projectRoot\MediaVideoDecoder.cs")
$mainArguments += "$projectRoot\BackgroundDiagnostics.cs"
$mainArguments += @("$projectRoot\AccountOnlineTools.cs", "$projectRoot\AccountToolsCanvas.cs", "$projectRoot\AccountToolsView.cs", "$projectRoot\ModelEvidence.cs", "$projectRoot\AccountToolsDiagnostics.cs")
foreach($season in @('heritage','spring','summer','autumn','winter')) { $mainArguments += "/resource:$projectRoot\assets\$season.png,CodexAccountDock.$season.png" }
if ($Edition -eq 'Standard') { $mainArguments += '/define:NO_DESKTOP_PET' }
$mainArguments += @("/resource:$projectRoot\assets\v3-dragon.png,CodexAccountDock.Dragon.png","/resource:$projectRoot\assets\v3-wordmark.png,CodexAccountDock.V3Wordmark.png")
$mainArguments += @("$projectRoot\WorkspaceTools.cs", "/reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll")
& $compiler $mainArguments
if ($LASTEXITCODE -ne 0) {
    throw "Application build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $portableRoot 'LICENSE')
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $portableRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\app.ico') -Destination (Join-Path $portableRoot 'assets\app.ico')


$portableFiles = Get-ChildItem -LiteralPath $portableRoot -File -Recurse
$relativePortableFiles = @($portableFiles | ForEach-Object {
    $_.FullName.Substring($portableRoot.Length + 1).Replace('\', '/')
} | Sort-Object)
$expectedPortableFiles = @('assets/app.ico', 'CodexAccountDock.exe', 'LICENSE', 'README.md')
$expectedPortableFiles = $expectedPortableFiles | Sort-Object
if (Compare-Object -ReferenceObject $expectedPortableFiles -DifferenceObject $relativePortableFiles) {
    throw "Portable payload allowlist validation failed: $($relativePortableFiles -join ', ')"
}

if ($relativePortableFiles | Where-Object { $_ -match '(^|/)(data|backup|backups)(/|$)|auth\.json$|\.bak$' }) {
    throw 'Portable payload contains account data, authentication material, or a backup.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$portableZip = Join-Path $releaseRoot "CodexAccountDock-v$version-$Edition-portable.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $portableRoot,
    $portableZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

$setupExe = Join-Path $releaseRoot "CodexAccountDock-v$version-$Edition-Setup.exe"
$setupArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/langversion:5',
    '/main:CodexAccountDock.Setup.Program',
    "/out:$setupExe",
    "/win32icon:$(Join-Path $projectRoot 'assets\app.ico')",
    "/resource:$portableZip,CodexAccountDock.Payload.zip,private",
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    (Join-Path $projectRoot 'Installer.cs')
)
& $compiler $setupArguments
if ($LASTEXITCODE -ne 0) {
    throw "Setup build failed with exit code $LASTEXITCODE"
}

$packageTestProcess = Start-Process -FilePath $setupExe -ArgumentList '--package-test' -WindowStyle Hidden -Wait -PassThru
if ($packageTestProcess.ExitCode -ne 0) {
    throw "Embedded package validation failed with exit code $($packageTestProcess.ExitCode)"
}

$testInstall = Join-Path $qaRoot 'install-test'
New-Item -ItemType Directory -Path $testInstall -Force | Out-Null
$quotedTestInstall = '"' + $testInstall.Replace('"', '\"') + '"'
$installTestProcess = Start-Process -FilePath $setupExe -ArgumentList @('--install-test', $quotedTestInstall) -WindowStyle Hidden -Wait -PassThru
if ($installTestProcess.ExitCode -ne 0) {
    throw "Noninteractive installer test failed with exit code $($installTestProcess.ExitCode)"
}

$testFiles = Get-ChildItem -LiteralPath $testInstall -File -Recurse
$relativeTestFiles = @($testFiles | ForEach-Object {
    $_.FullName.Substring($testInstall.Length + 1).Replace('\', '/')
} | Sort-Object)
if (Compare-Object -ReferenceObject $expectedPortableFiles -DifferenceObject $relativeTestFiles) {
    throw "Installer extracted an unexpected file set: $($relativeTestFiles -join ', ')"
}
if (Test-Path -LiteralPath (Join-Path $testInstall 'data')) {
    throw 'Installer test unexpectedly created a data directory.'
}
if (Get-ChildItem -LiteralPath $testInstall -File -Recurse | Where-Object { $_.Name -match 'auth\.json|\.bak$' }) {
    throw 'Installer test unexpectedly extracted authentication material or a backup.'
}

$portableProgramHash = (Get-FileHash -LiteralPath $mainExe -Algorithm SHA256).Hash
$installedProgramHash = (Get-FileHash -LiteralPath (Join-Path $testInstall 'CodexAccountDock.exe') -Algorithm SHA256).Hash
if ($portableProgramHash -ne $installedProgramHash) {
    throw 'Installed program hash does not match the portable program hash.'
}

$fixtureMarker = 'synthetic-qa-no-credentials'
$dataFixtureDirectory = Join-Path $testInstall 'data'
$backupFixtureDirectory = Join-Path $testInstall 'backups'
New-Item -ItemType Directory -Path $dataFixtureDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $backupFixtureDirectory -Force | Out-Null
[System.IO.File]::WriteAllText((Join-Path $dataFixtureDirectory 'qa-fixture.txt'), $fixtureMarker)
[System.IO.File]::WriteAllText((Join-Path $backupFixtureDirectory 'qa-backup.txt'), $fixtureMarker)

$upgradeTestProcess = Start-Process -FilePath $setupExe -ArgumentList @('--upgrade-test', $quotedTestInstall) -WindowStyle Hidden -Wait -PassThru
if ($upgradeTestProcess.ExitCode -ne 0) {
    throw "Upgrade preservation test failed with exit code $($upgradeTestProcess.ExitCode)"
}
if ((Get-Content -LiteralPath (Join-Path $dataFixtureDirectory 'qa-fixture.txt') -Raw) -ne $fixtureMarker -or
    (Get-Content -LiteralPath (Join-Path $backupFixtureDirectory 'qa-backup.txt') -Raw) -ne $fixtureMarker) {
    throw 'Upgrade test modified or removed the synthetic data fixtures.'
}
$upgradedProgramHash = (Get-FileHash -LiteralPath (Join-Path $testInstall 'CodexAccountDock.exe') -Algorithm SHA256).Hash
if ($upgradedProgramHash -ne $portableProgramHash) {
    throw 'Upgraded program hash does not match the portable program hash.'
}

$manifestPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$manifestLines = @(
    "$(Get-FileHash -LiteralPath $setupExe -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $(Split-Path -Leaf $setupExe)",
    "$(Get-FileHash -LiteralPath $portableZip -Algorithm SHA256 | Select-Object -ExpandProperty Hash)  $(Split-Path -Leaf $portableZip)"
)
[System.IO.File]::WriteAllLines($manifestPath, $manifestLines, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Built release: $releaseRoot"
Write-Host "Installer QA passed: exact allowlist, no account data, program SHA256 $portableProgramHash"

