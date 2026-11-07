# ZhuyinToggle — 微軟注音繁簡快速切換工具

一款專為 Windows 微軟注音（Microsoft Bopomofo）設計的輕量級繁體／簡體中文切換工具。

不需要更換輸入法，也不必每次進入 Windows 設定。透過自訂快捷鍵，即可快速切換微軟注音的繁簡輸出模式。

**專案狀態：開發中（Development）**

> **實驗性開發版，尚未通過正式驗收。** 已整合設定讀寫及快捷鍵，但登錄設定值不保證等於微軟注音的即時輸出；程式會分別標示設定值與「實際輸出未知」。請勿將此版視為正式版本。[測試紀錄](docs/ime-validation.md) 說明已知差異與驗證限制。

## 主要功能（規劃中）

- **一鍵切換繁簡**：預設 `Win + Alt + C`，支援自訂快捷鍵。
- **系統匣設定顯示**：設定值為繁體顯示藍底「繁」，簡體顯示綠底「简」，讀取失敗顯示灰底「?」。圖示不是實際輸出的獨立驗證。
- **快捷鍵衝突檢查**：自動檢查快捷鍵是否已被其他程式占用。
- **開機自動啟動**：登入 Windows 後自動在背景執行，可自行停用。
- **輕量化設計**：不需常駐主視窗，不依賴 PowerToys。
- **簡單直覺**：支援滑鼠點擊圖示切換繁簡，以及右鍵開啟設定。

## 系統需求

Windows 11 x64

Microsoft Bopomofo（微軟注音輸入法）

## 使用方式

開發版啟動後於通知區域常駐。左鍵或 `Win + Alt + C` 切換設定，右鍵開啟選單，雙擊開啟設定視窗；請以注音實際組字確認輸出。開發版未經完整驗收，若輸出與設定值不一致請以 Windows 設定為準。

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

圖示呈現的是登錄設定，不宣稱實際輸出已驗證。程式以登錄變更通知監測外部設定；監測失敗時每五秒重試。另有獨立 UI Automation 概念驗證，不屬於正式切換路徑，見[測試紀錄](docs/ime-validation.md)。

## 開源授權

本專案採用 MIT License 授權，歡迎自由使用、修改與貢獻。

---

ZhuyinToggle is a lightweight Windows utility for switching Microsoft Bopomofo between Traditional and Simplified Chinese. Currently under development.
