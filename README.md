# ZhuyinToggle — 微軟注音繁簡設定快捷切換工具

一款針對 Windows 微軟注音（Microsoft Bopomofo）繁簡設定的受限開發版工具，**不是已驗證的即時無感繁簡輸出切換器**。

不需要更換輸入法；透過快捷鍵可修改注音設定值，但**不保證目前輸入視窗立即改變實際輸出**。

**專案狀態：開發中（Development）**

> **受限開發版，尚未通過正式驗收。** 登錄設定值與實際輸出可能不一致，圖示一律顯示灰色未知。正常離開輸入欄位再返回可能有助更新，但不保證生效；程式不會強制更改其他程式的焦點。請實際組字確認，必要時使用 Windows 原生設定。請勿將此版視為正式版本。[測試紀錄](docs/cross-process-validation.md) 說明驗證限制。

## 主要功能（規劃中）

- **修改繁簡設定值**：預設 `Win + Alt + C`，支援自訂快捷鍵；不保證即時改變輸出。
- **系統匣狀態顯示**：實際輸出無法可靠確認時顯示灰底「?」；設定視窗另顯示登錄設定值。
- **快捷鍵衝突檢查**：自動檢查快捷鍵是否已被其他程式占用。
- **開機自動啟動**：登入 Windows 後自動在背景執行，可自行停用。
- **輕量化設計**：不需常駐主視窗，不依賴 PowerToys。
- **滑鼠操作**：支援點擊圖示修改設定，以及右鍵開啟選單。

## 系統需求

Windows 11 x64

Microsoft Bopomofo（微軟注音輸入法）

## 使用方式

開發版啟動後於通知區域常駐。左鍵或 `Win + Alt + C` 切換設定值，右鍵開啟選單，雙擊開啟設定視窗。請以注音實際組字確認輸出；若尚未更新，可正常離開輸入欄位並返回，再次確認。此操作不保證生效，必要時使用 Windows 設定；不要將設定回讀視為實際輸出的證明。

目前尚未發布可供下載的正式版本。

## 開發技術

- C# / .NET 10
- Windows Forms
- Windows API（全域快捷鍵）
- Windows Registry（微軟注音設定）

## 建置開發版

需要 Windows 11 x64 與 .NET 10 SDK：

```powershell
dotnet build src/ZhuyinToggle/ZhuyinToggle.csproj -c Release
dotnet run --project tests/ZhuyinToggle.Tests/ZhuyinToggle.Tests.csproj -c Release
dotnet publish src/ZhuyinToggle/ZhuyinToggle.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

目前自動化測試涵蓋快捷鍵及注音設定服務的模擬錯誤，不代表正式程式切換、同步或登入啟動通過實機驗收。GitHub Actions 的產物是實驗建置，不是 Release。

程式以登錄變更通知監測外部設定；監測失敗時每五秒重試。因已觀察設定與輸出不一致，圖示保留未知狀態。另有獨立 UI Automation 概念驗證，不屬於正式切換路徑，見[測試紀錄](docs/ime-validation.md)。

## 選擇性診斷

結束已執行的程式後，以 `ZhuyinToggle.exe --diagnostics` 啟動才會產生本機 JSONL 紀錄；不加參數則不啟用。紀錄位於 `%LOCALAPPDATA%\ZhuyinToggle\diagnostics`，包含 Windows 使用者 SID、工作階段、父程序名稱、快捷鍵 ID、登錄設定值與時間，可能包含私人資訊，**請勿上傳或提交公開倉庫**。同一台電腦上的不同程序、診斷程序及有／無訊息視窗可能影響排程，時間僅供對照。延遲取樣若看到值與預期不同，不能僅憑此判定是其他程序覆寫；必須排除下一次自己的操作，並用 ProcMon 等工具確認寫入者。

## 開源授權

本專案採用 MIT License 授權，歡迎自由使用、修改與貢獻。

---

ZhuyinToggle is a lightweight Windows utility for switching Microsoft Bopomofo between Traditional and Simplified Chinese. Currently under development.
