# v0.1.0 最終實作與選案

採用「修改微軟注音繁簡設定＋受控的頂層視窗啟用往返」。Owner 已在其測試環境確認可用，接受約 0.5～1 秒等待與短暫跳動；這不表示微軟注音內部更新機制已完整解析。

## 來源及職責

正式產品來源：[受測版 manual-build](https://github.com/kaikaiwang7777-png/ZhuyinToggle/tree/f1d351ba72e3b6912cc7567fbc7f9d0e99ae621c/manual-build)，入口 `src/ZhuyinToggle/ZhuyinToggle.csproj`。舊 `src/` 為先前產品，`tools/` 為歷史實驗，不是本 EXE 的依賴。參照[固定版 Program.cs](https://github.com/kaikaiwang7777-png/ZhuyinToggle/blob/f1d351ba72e3b6912cc7567fbc7f9d0e99ae621c/manual-build/src/ZhuyinToggle/Program.cs)：`Program.Main` 管單實例；`TrayApplication` 管選單、通知與序列化切換；`HotkeyWindow` 使用 RegisterHotKey；`SettingsForm` 管獨立設定；`PowerShellCommands.Run` 執行命令；`WindowRoundTrip.Run` 嘗試視窗往返。設定位於 `%LOCALAPPDATA%\ZhuyinToggle-Manual`，登入啟動使用目前使用者 Run 項目。

PowerShell 路徑為 `%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe`，以 `-NoProfile -NonInteractive -EncodedCommand` 無視窗啟動，不自動回退。可讀腳本可從「查看最近一次命令」取得。以下是展開後的核心腳本形式，目標依操作選 `0x00000000` 或 `0x00000001`；實作在驗證失敗時停止，不建立缺失值：

```powershell
$ErrorActionPreference = 'Continue'
$before = & reg query "HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC" /v "Enable Simplified Chinese Output" 2>&1
$beforeExit = $LASTEXITCODE
$before | ForEach-Object { Write-Output $_ }
Write-Output ('ZT_BEFORE_EXIT=' + $beforeExit)
$entry = @($before | Where-Object { $_ -match '^\s*Enable Simplified Chinese Output\s+REG_SZ\s+0x0000000[01]\s*$' })
if ($beforeExit -ne 0 -or $entry.Count -ne 1) { Write-Output 'ZT_INVALID_BEFORE'; exit 23 }
& reg add "HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC" /v "Enable Simplified Chinese Output" /t REG_SZ /d "0x00000001" /f
$writeExit = $LASTEXITCODE
Write-Output ('ZT_WRITE_EXIT=' + $writeExit)
if ($writeExit -ne 0) { exit 24 }
$after = & reg query "HKCU\SOFTWARE\Microsoft\IME\15.0\IMETC" /v "Enable Simplified Chinese Output" 2>&1
$queryExit = $LASTEXITCODE
$after | ForEach-Object { Write-Output $_ }
Write-Output ('ZT_QUERY_EXIT=' + $queryExit)
if ($queryExit -ne 0) { exit 25 }
$result = @($after | Where-Object { $_ -match '^\s*Enable Simplified Chinese Output\s+REG_SZ\s+0x0000000[01]\s*$' })
if ($result.Count -ne 1) { exit 26 }
if ($result[0] -match '0x00000001\s*$') { Write-Output 'ZT_MODE=1' } else { Write-Output 'ZT_MODE=0' }
```

上例寫簡體；繁體只將 `reg add` 的 `/d` 值改為 `0x00000000`。查詢模式省略寫入段。執行逾時不自動重試。寫入退出碼、查詢退出碼、設定回讀及真實輸出是不同證據。

熱鍵一收到即記住前景 HWND/PID；設定寫入和回讀成功後等待按鍵釋放，再要求啟用 `Shell_TrayWnd`。NULL 前景是有上限的過渡，不直接當其他視窗。若使用者輸入或其他非零前景出現、原視窗失效或條件不安全，停止。僅在原視窗仍有效且前景是指定工作列、無偵測到外來輸入等條件下，嘗試一次返回並限時確認。非熱鍵入口只改設定，**不**啟用視窗往返。不能保證輸入控制項、選取或游標安全恢復。最近八筆操作可於設定查看，保留命令輸出與視窗 HWND/PID/類別，不收集視窗標題或文件內容；仍應在分享前檢查。

```text
熱鍵 → 保存原前景 → PowerShell 驗證／寫入／回讀
                         ├─失敗→記錄失敗，不往返
                         └─成功→安全條件、按鍵釋放→工作列啟用
                                      ├─取消／逾時→安全條件成立才嘗試一次返回
                                      └─到達→短暫等待→嘗試返回→確認或報未完成
```

圖示反映讀回設定而非獨立取得實際輸出。採用此法因 Owner 接受本機體感及跳動，而非證明此方案對所有視窗可靠。尚未釐清內部重新採用設定的事件，也未釐清歷史桌面與背景讀值分歧；未測程式、Windows 版本及組字中途情境不可推論通過。

## 建置及參考

`dotnet publish manual-build/src/ZhuyinToggle/ZhuyinToggle.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false`。CI 產物另行識別，正式附件使用受測 EXE，而非重新建置的檔案。

[參考腳本固定版本](https://github.com/aa846301/ime-tc-sc-switcher/blob/769eeec8e8b56c66faa350043c9fbed62e9e4cac/ChineseInputSwitcher_AHK2.0up.ahk)可核對其啟用工作列、等待、返回原視窗的思路；本實作依 Owner 提供之概念以 C# 實作及加入安全判斷，非將整支腳本納入產品。該固定版本的儲存庫樹未找到獨立 LICENSE 檔、GitHub license API 回 404；不以公開可讀推定授權，亦不宣稱已完成一切法律授權判定。參考腳本本身不等於本產品通過驗收。

[Microsoft SetForegroundWindow 文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)說明系統可能拒絕啟用；它不保證微軟注音輸出切換。[RegisterHotKey 文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)僅支持所述快捷鍵 API 行為。
