param([switch]$Run)

# 實驗工具，不是正式產品。執行前請先閱讀 docs/ime-validation.md。
$ErrorActionPreference = 'Stop'
$path = 'SOFTWARE\Microsoft\IME\15.0\IMETC'
$name = 'Enable Simplified Chinese Output'
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, [bool]$Run)
if ($null -eq $key) { throw '找不到微軟注音登錄機碼。' }
try {
    if ($name -notin $key.GetValueNames()) { throw '找不到繁簡輸出設定值。' }
    $kind = $key.GetValueKind($name)
    $original = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    Write-Host "設定類型：$kind；目前設定值：$original"
    if (-not $Run) { Write-Host '僅讀取；如要執行互動式實驗，請參閱文件。'; return }
    if ($kind -ne [Microsoft.Win32.RegistryValueKind]::String -or $original -notin @('0x00000000', '0x00000001')) {
        throw '此實驗僅支援已觀察到的 REG_SZ 格式；未修改任何設定。'
    }
    $backup = Join-Path $env:TEMP ('ZhuyinToggle-ime-backup-' + [guid]::NewGuid().ToString('N') + '.json')
    @{ Path = $path; Name = $name; Kind = $kind.ToString(); Value = $original } |
        ConvertTo-Json | Set-Content -LiteralPath $backup -Encoding UTF8
    Write-Host "原值備份：$backup"
    $target = if ($original -eq '0x00000000') { '0x00000001' } else { '0x00000000' }
    try {
        $key.SetValue($name, $target, $kind)
        Write-Host "已寫入設定值 $target，這不代表實際輸出已改變。"
        Write-Host '請在記事本及瀏覽器使用微軟注音輸入可區分繁簡的詞語並記錄結果。'
        [void](Read-Host '完成後按 Enter 還原原值')
    } finally {
        $current = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($current -ceq $target -and $key.GetValueKind($name) -eq $kind) {
            $key.SetValue($name, $original, $kind)
            Write-Host "已還原：$($key.GetValue($name))；類型：$($key.GetValueKind($name))"
        } else {
            Write-Warning '設定已被其他程序變更，未覆寫；請檢查備份並手動決定是否還原。'
        }
    }
} finally { $key.Dispose() }
