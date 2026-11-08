# 原生輸入設定更新：呼叫堆疊與 ETW 可行性

## 證據範圍

專案原始擷取位置保存 `ime-capture.CSV`，有時間、程序、PID、操作、路徑、結果和 Detail，**沒有呼叫堆疊欄位**。專案工作區遞迴搜尋及 Downloads、Desktop 目錄直接搜尋沒有找到 PML；不能排除其他位置存在 PML。先前畫面顯示擷取由虛擬記憶體支撐，且使用者當時另存 CSV；不能因此推斷不存在任何其他保存副本。現有 CSV 無法回答寫入目標值的呼叫堆疊、DLL 呼叫路徑或 32 次保存是否由同一函式完成；同一程序與相近時間只能證明事件關聯，不能替代堆疊。

本機 CSV 的確有 `ctfmon.exe` 查詢目標值，該組原生切換的第二次目標寫入後約 2.469 秒出現一次查詢；該時間點沒有與一筆已精確標記的注音組字輸出對齊，不能認定是即時生效的因果事件。原始日誌只保留本機，不上傳。

## 成功／失敗路徑：現有紀錄與限制

| 路徑 | 已有證據 | 未能分離或確認 |
|---|---|---|
| A：原生設定頁 | 相同一組 CSV 中 `SystemSettings.exe` 成功將目標 REG_SZ 寫為 1、再寫為 0；其他歷史原生頁測試觀察到記事本實際繁簡輸出 | CSV 操作與歷史輸出觀察**不是同一條已逐事件關聯的記錄**；頁面操作存在視窗／輸入焦點變化，無法歸因於單一通知、CPSS 寫入或其他機制 |
| B：產品快捷鍵 | 歷史受控紀錄中，快捷鍵進入產品切換且寫入及延遲回讀成功；記事本未離開前景時立即與約 19 秒後仍為繁體 | 沒有與 A 同一套 ETW／呼叫堆疊同步擷取；Registry notification 不等於 IME 更新 |

因此可確認差異是**原生頁有批次保存及視窗操作、產品只寫目標設定且可保持記事本前景**，而不是已確定其內部機制不同。先前比較的 32 個值詳見 [原生設定順序](native-settings-sequence.md)，不應因同批出現就盲目重寫。

## 本機 ETW 提供者及可行性

`logman query providers` 在本機列出：

| Provider | 唯讀 metadata 觀察 | 限制 |
|---|---|---|
| `Microsoft-Windows-IME-TCTIP` | 三個事件，任務含 `IME_InvokeCommand`；資源為微軟注音 IME 模組 | 事件名稱**不證明**包含繁簡設定通知或可重現的 API |
| `Microsoft-Windows-IME-TCCORE` | 任務包含打字、候選字、轉換及解碼 | 內容可能涉及輸入行為；不可假設日誌無私人資訊 |
| `Microsoft-Windows-TSF-msctf` | keyword 含 `ALPC`、`TSFEvent`、`OOPIME`；任務含 `ReceiveALPCMessages`、`ProcessALPCMessage`、設定檔及文件焦點事件 | 事件名稱僅提供追蹤候選，不能自行證明特定訊息與本設定相關 |
| `Microsoft-Windows-COMRuntime`、`Microsoft-Windows-RPC` | 系統可列舉 | 全系統範圍可能很廣，應待窄範圍線索支持後才考慮 |

使用單一 IME-TCTIP provider 的短暫 `logman create trace` 權限探測，回傳 **Access is denied（5）**；工作階段未建立，後續查詢為不存在，沒有留下追蹤。此環境的非提升程序無法啟動該工作階段；依 [Microsoft ETW 控制工作階段文件](https://learn.microsoft.com/en-us/windows/win32/etw/controlling-event-tracing-sessions)，常見允許對象包括提升權限的管理員或已授權的 Performance Log Users。未變更使用者群組、ACL 或系統安全設定，也未繞過 UAC。

下一次若可透過正常 Windows UAC 授權，優先使用短時、容量上限、僅 IME-TCTIP 與必要的 TSF ALPC／OOPIME keyword 的受控追蹤，記錄 A／B 的單調時間及前景／焦點與實際注音輸出，分析時按程序及時間縮小。提供者可能是全系統範圍；即使只後處理特定 PID，原始 ETL 仍可能含其他程序或輸入資訊，必須保留私人本機、不上傳並於完成後正常停止工作階段。須先確認事件 schema 與追蹤權限；若不能定位輸出選項對應事件，不得宣稱已發現通知或 COM／IPC 機制。

## 本輪結論

本輪找到了可篩選的 Windows ETW 觀察目標及權限阻礙，**沒有找到已歸因的更新通知、可安全重現的 C# 候選或新的同步 A／B 實機輸出結果**。因此沒有建立候選原型或修改正式切換服務。這不代表專案停止，也不代表所有可行的系統介面不存在；保持 Draft、灰色未知及不發布正式版。
