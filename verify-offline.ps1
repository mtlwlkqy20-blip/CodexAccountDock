param([string]$Executable)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
if(-not $Executable){$Executable=Join-Path $taskRoot 'release\portable\CodexAccountDock.exe'}
$taskExe=(Resolve-Path -LiteralPath $Executable).Path
$taskQa=Join-Path $taskRoot 'qa\offline-current'
New-Item -ItemType Directory -Path $taskQa -Force | Out-Null
$taskCopy=Join-Path $taskQa 'CodexAccountDock.exe'
Copy-Item -LiteralPath $taskExe -Destination $taskCopy -Force
$taskBin=Split-Path -Parent $taskExe
foreach($library in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','runtimes')) {if(Test-Path -LiteralPath (Join-Path $taskBin $library)){Copy-Item -LiteralPath (Join-Path $taskBin $library) -Destination $taskQa -Recurse -Force}}
$taskResults=@()
$taskFlags=@('--self-test','--cli-setup-test','--cli-setup-preview','--appearance-preview','--refresh-feedback-test','--pro-plan-test','--dpi-test','--hotkey-test','--account-usage-test','--shop-test','--shop-ui-test','--news-feed-test','--reset-feed-test','--daily-offline-test','--daily-responsive-test','--daily-dock-test','--reset-reminder-test','--ip-query-test','--stagger-test','--stagger-ui-test','--plan-input-test','--expiry-datetime-test','--outer-border-test','--render-test')
foreach($taskFlag in $taskFlags){
    $taskInfo=New-Object System.Diagnostics.ProcessStartInfo
    $taskInfo.FileName=$taskCopy
    $taskInfo.Arguments=$taskFlag+' --preview-theme=custom --detailed-preview'
    $taskInfo.WorkingDirectory=$taskQa
    $taskInfo.UseShellExecute=$false
    $taskInfo.CreateNoWindow=$true
    $taskInfo.WindowStyle=[System.Diagnostics.ProcessWindowStyle]::Hidden
    $taskInfo.RedirectStandardOutput=$true
    $taskInfo.RedirectStandardError=$true
    $taskProcess=New-Object System.Diagnostics.Process
    $taskProcess.StartInfo=$taskInfo
    try{
        if(-not $taskProcess.Start()){throw ('Cannot start '+$taskFlag)}
        $taskOutput=$taskProcess.StandardOutput.ReadToEndAsync()
        $taskError=$taskProcess.StandardError.ReadToEndAsync()
        if(-not $taskProcess.WaitForExit(45000)){
            $taskProcess.Kill()
            throw ('Offline test deadline: '+$taskFlag)
        }
        $taskStdout=$taskOutput.Result
        $taskStderr=$taskError.Result
        $taskCode=$taskProcess.ExitCode
        $taskName=$taskFlag.TrimStart('-')
        [System.IO.File]::WriteAllText((Join-Path $taskQa ($taskName+'.log')),$taskStdout+$taskStderr,(New-Object System.Text.UTF8Encoding($false)))
        $taskResults+=@{test=$taskFlag;exit_code=$taskCode;output=$taskStdout.Trim();error=$taskStderr.Trim()}
        $taskProgress=@{program_sha256=(Get-FileHash -LiteralPath $taskCopy -Algorithm SHA256).Hash;complete=($taskResults.Count -eq $taskFlags.Count);tests=$taskResults}
        [System.IO.File]::WriteAllText((Join-Path $taskQa 'verification.json'),($taskProgress|ConvertTo-Json -Depth 5),(New-Object System.Text.UTF8Encoding($false)))
        Write-Output ($taskFlag+' EXIT='+$taskCode+' '+$taskStdout.Trim())
        if($taskCode -ne 0){throw ('Offline test failed: '+$taskFlag+' '+$taskStderr)}
    }finally{$taskProcess.Dispose()}
}
$taskSummary=@{program_sha256=(Get-FileHash -LiteralPath $taskCopy -Algorithm SHA256).Hash;complete=$true;tests=$taskResults}
[System.IO.File]::WriteAllText((Join-Path $taskQa 'verification.json'),($taskSummary|ConvertTo-Json -Depth 5),(New-Object System.Text.UTF8Encoding($false)))
Write-Output ('OFFLINE_SUITE_OK tests='+$taskResults.Count+' SHA256='+$taskSummary.program_sha256)
