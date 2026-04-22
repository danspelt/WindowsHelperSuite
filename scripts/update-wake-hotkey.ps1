$tick = [char]0x0060  # backtick character
$path = Join-Path $env:APPDATA 'WindowsHelperSuite\settings.json'
$json = Get-Content $path -Raw | ConvertFrom-Json

$refresh = $json.hotkeys.bindings | Where-Object { $_.actionName -eq 'WriterRefresh' }
if ($refresh) { $refresh.gesture = 'Ctrl+Shift+R' }

$wake = $json.hotkeys.bindings | Where-Object { $_.actionName -eq 'WakeWriter' }
if ($wake) {
    $wake.gesture = "$tick"
    $wake.enabled = $true
} else {
    $newBinding = [PSCustomObject]@{ actionName = 'WakeWriter'; gesture = "$tick"; enabled = $true }
    $json.hotkeys.bindings = @($json.hotkeys.bindings) + $newBinding
}

$json | ConvertTo-Json -Depth 20 | Set-Content $path -Encoding UTF8

Write-Host 'Updated bindings:'
$json.hotkeys.bindings |
    Where-Object { $_.actionName -in @('WriterRefresh','WakeWriter') } |
    Format-Table actionName, gesture, enabled
