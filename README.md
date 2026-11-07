# ZhuyinToggle — 微軟注音繁簡快速切換工具

一款專為 Windows 微軟注音（Microsoft Bopomofo）設計的輕量級繁體／簡體中文切換工具。

不需要更換輸入法，也不必每次進入 Windows 設定。透過自訂快捷鍵，即可快速切換微軟注音的繁簡輸出模式。

**專案狀態：開發中（Development）**

> **尚不可用於切換輸出。** 開發版目前只包含系統匣、快捷鍵及設定介面骨架；實際輸出模式無法可靠判斷，切換功能刻意停用。請勿將建置產物視為正式版本。[測試紀錄](docs/ime-validation.md) 說明已知差異與驗證限制。

## 主要功能（規劃中）

- **一鍵切換繁簡**：預設 `Win + Alt + C`，支援自訂快捷鍵。
- **系統匣狀態顯示**：繁體模式顯示「繁」，簡體模式顯示「简」。
- **快捷鍵衝突檢查**：自動檢查快捷鍵是否已被其他程式占用。
- **開機自動啟動**：登入 Windows 後自動在背景執行，可自行停用。
- **輕量化設計**：不需常駐主視窗，不依賴 PowerToys。
- **簡單直覺**：支援滑鼠點擊圖示切換繁簡，以及右鍵開啟設定。

## 系統需求

Windows 11 x64

Microsoft Bopomofo（微軟注音輸入法）

## 使用方式

正式版本發布後，使用者可以從 GitHub Releases 下載程式，啟動後即可透過系統匣操作或快捷鍵切換繁簡。

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

目前自動化測試檢查快捷鍵資料規則及模擬註冊衝突、儲存失敗時的替換行為，不代表 Windows 全域註冊、設定錄製、自動啟動或注音切換通過實機驗收。GitHub Actions 的產物是實驗建置，不是 Release。

## 開源授權

本專案採用 MIT License 授權，歡迎自由使用、修改與貢獻。

---

ZhuyinToggle is a lightweight Windows utility for switching Microsoft Bopomofo between Traditional and Simplified Chinese. Currently under development.
