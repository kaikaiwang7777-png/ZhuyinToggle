> **歷史紀錄：**本文件描述特定版本與當時的研究決策，不代表目前發布狀態。現行方案見 [最終實作](final-implementation.zh-TW.md)，驗收範圍見 [v0.1.0 驗收](release-acceptance-v0.1.0.zh-TW.md)。

# 原生微軟注音設定作業：追蹤重分析

## 範圍與假說

假說：原生設定頁保存繁簡選項時，除了設定本身，還有額外的 CPSS 寫入或其他操作，使既有輸入環境採用新設定。原始 ProcMon CSV 僅存在本機私人工作區，不提交；本紀錄只摘錄設定名稱、相對時序及去識別化結論。既有擷取為一次使用者操作的繁→簡→繁，不能作為重複實驗或因果證據；不同環境、Windows 更新及未捕捉的 IPC 均可能改變結果。

## 同一擷取中的兩次保存

以 `SystemSettings.exe` 成功寫入 `Enable Simplified Chinese Output` 的時間為各次 0 ms：

| 證據 | 繁→簡 | 簡→繁 |
|---|---:|---:|
| 目標 `REG_SZ` 寫入 | `0x00000001` | `0x00000000` |
| 目標之前 CPSS `Enable New Phrase Learning` 寫入 | 約 −24.7 ms，值 1 | 約 −48.7 ms，值 1 |
| 目標之前 CPSS `Enable Personal Regulating` 寫入 | 約 −18.98 ms，值 1 | 約 −35.87 ms，值 1 |
| 目標之前 CPSS `Fixed Candidate Order.New Phonetic` 寫入 | 約 −12.88 ms，值 0 | 約 −24.52 ms，值 0 |
| 目標之前 CPSS `Enable User Defined Phrases` 寫入 | 約 −6.94 ms，值 1 | 約 −10.76 ms，值 1 |

兩個窄時間窗各有 32 個相關 `RegSetValue` 路徑，路徑集合相同。根據 CSV 匯出的 Detail，只有目標繁簡值的資料不同；其餘顯示的資料相同。**大型 `REG_BINARY` 的 Detail 是截斷預覽，不能據此證明完整二進位內容相同。** 這支持原生頁可能以批次儲存多個設定的假說，但既有追蹤沒有單獨的因果對照，不能證明任何相同值的重寫是必要通知，也不能排除其他未顯示的內部操作。

在本機唯讀查詢中，`HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\CPSS\\UserPolicy\\IME\\Bopomofo` 只有上述四個設定項；它們各自的 `LegacyProjection` 為 1，`StaticMigrationPath` 指向 `Software\\Microsoft\\IME\\15.0\\IMETC`，`LegacyKeyName` 對應同名設定。本機該 CPSS policy 分支未列出 `Enable Simplified Chinese Output`。這是**所查分支中的觀察**，不是證明系統其他路徑或未公開介面不存在；也不代表應該複製或改寫這四個不相關設定。

本 CSV 中 `ctfmon.exe` 對目標值有查詢，該組切換後首次列出的查詢距第二次目標寫入約 2.469 秒。這些查詢沒有提供造成輸出改變的因果或完整通知路徑；ProcMon 不觀測所有 COM 呼叫、記憶體或 IPC，不能從未出現某事件推論事件不存在。原生頁及原型測試曾改變前景／輸入焦點，必須與產品保持前景的條件分開比較。

## 與產品的已知差異及驗收關卡

產品 `ImeModeService` 在一般情況只修改目標值、回讀並以 Registry notification 更新設定顯示，不會執行原生頁的整批設定保存；按快捷鍵時輸入視窗通常仍在前景。原生頁執行了整批保存，並伴隨視窗操作。這些是**同時存在的差異，不是已確定的根因**。

目前沒有證據支持某個單一 CPSS 值是觸發器，也未找到針對本輸出選項的已記載、可跨程序即時更新的公開 API；因此不寫入其他 CPSS／IME 值、不猜測性廣播、不宣稱已有候選原型或修復。下一步應先確認 Windows 版本與相同使用者／互動工作階段，建立不變更輸入法身分的原生頁實際輸出控制組，再以**唯讀、可歸因且隱私受控**的證據縮小到可能的通知或 COM 介面；若找到有支持證據的候選，再建立獨立最小 C# 原型及與產品相同的前景條件對照。

官方文件可用於辨識[Microsoft Bopomofo 的完整輸入設定檔識別碼](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/windows-language-pack-default-values?view=windows-11)及 [TSF 設定檔查詢](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-getactiveprofile)；它們不證明可控制此輸出選項。核心驗收仍需實際注音組字、記事本十次往返、Chrome、非干擾與狀態圖示一致；所有項目在本次重分析中均為 NOT TESTED。保持 Draft，不發布正式版。
