$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$output = Join-Path $root 'bin'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compileArguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/langversion:5',
    "/out:$output\CodexAccountDock.exe", "/win32icon:$root\assets\app.ico", "/win32manifest:$root\app.manifest",
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.Net.Http.dll', '/reference:System.Security.dll', '/reference:System.Management.dll',
    "/resource:$root\assets\background.jpg,CodexAccountDock.Background.jpg",
    "$root\Program.cs", "$root\DpiSupport.cs", "$root\DesktopPet.cs", "$root\Dashboard.cs", "$root\GlassBackdrop.cs", "$root\DailyNews.cs", "$root\ThemeArt.cs", "$root\ThemeSelection.cs")
$compileArguments += @("$root\SidePanels.cs", "$root\StaggerPlan.cs", "$root\AppearanceOptions.cs", "$root\ReferenceSurface.cs", "$root\NewsFeed.cs", "$root\ResetFeed.cs", "$root\ResetReminder.cs", "$root\PlanInputs.cs", "$root\PlanVisualControls.cs")
$compileArguments += "$root\AiShop.cs"
$compileArguments += "$root\CliSetup.cs"
$compileArguments += "$root\PublicFeedTransport.cs"
$compileArguments += "$root\NativeFeedHttp.cs"
$compileArguments += @("$root\DynamicBackground.cs", "$root\MediaVideoDecoder.cs")
$compileArguments += "$root\BackgroundDiagnostics.cs"
$compileArguments += "$root\AppTypography.cs"
$compileArguments += "$root\AccountUsageClient.cs"
$compileArguments += "$root\IpQueryClient.cs"
$compileArguments += @("$root\AccountOnlineTools.cs", "$root\ModelEvidence.cs", "$root\AccountToolsDiagnostics.cs")
$compileArguments += @("$root\AccountToolsCanvas.cs", "$root\AccountToolsView.cs")
foreach($season in @('heritage','spring','summer','autumn','winter')) { $compileArguments += "/resource:$root\assets\$season.png,CodexAccountDock.$season.png" }
$compileArguments += "/resource:$root\assets\v3-dragon.png,CodexAccountDock.Dragon.png"
$compileArguments += "/resource:$root\assets\v3-wordmark.png,CodexAccountDock.V3Wordmark.png"
$compileArguments += @("$root\WorkspaceTools.cs", "/reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll")
& $compiler $compileArguments
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Host "Built $output\CodexAccountDock.exe"

