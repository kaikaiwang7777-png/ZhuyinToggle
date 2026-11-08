# 開發歷程與證據回顧

本回顧按已留存的提交、歷史文件及 Owner 回報整理；不同輪次不是同一實驗。正式驗收範圍見 [v0.1.0 驗收](release-acceptance-v0.1.0.zh-TW.md)。

## 1. 需求
- **目的／條件**：在 Windows 微軟注音內切換繁簡，減少反覆進入設定；預設 Win+Alt+C，可自訂熱鍵及使用系統匣「繁／简」。
- **結果／證據**：需求不等於把既有文字轉換。最終圖示只表示設定值，見[最終實作](final-implementation.zh-TW.md)。
- **決策／後續**：保留實際輸出與設定值分開驗收。

## 2. 最初手動與原型
- **目的／條件**：檢查原始 REG_SZ 命令是否可影響輸出。Owner 回報互動 PowerShell 成功，但當時未保存完整輸出證據；後續 Run 對話框觀察到一次繁→簡→繁（[34b3067](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/34b30674e392264108b70e6ada7ab65e14eb0bb2)），另有獨立 C# 原型成功觀察（[4350f02](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/4350f022d2a811f190187caf8d1d1618ba1f0f19)）。
- **結果／限制**：三者啟動方式、輪次及證據不相同，不能合成完整因果實驗。測試腳本有 Read-Host 與 finally 還原；沒有時序證據不能斷言歷史觀察因提前還原所致。
- **決策／後續**：繼續對照，未否定 Owner 觀察。詳[歷史驗證](ime-validation.md)。

## 3. 直接寫入
- **目的／條件**：產品直接寫設定並回讀。**結果**：部分實測中寫入及回讀成功，但記事本仍輸出舊模式；命令退出碼、設定、實際輸出及圖示是不同證據。
- **決策／修正**：當時以未知圖示避免誤導，並持續診斷；不能只由寫入成功宣布輸出通過。[c2bedca](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/c2bedca6fd87abd0cae8ebae8526fd938cf8b102)。

## 4. 焦點與原生設定
- **目的／條件**：研究設定如何被現有輸入情境採用。自建 WinForms 控制項焦點往返出現成功；跨程序 UIA 因狀態判定與副作用未證明安全；原生設定頁曾使記事本實際輸出更新。ProcMon 和 ETW 記錄相關設定與事件，但並未完整證明內部 API、通知或 IPC 因果。
- **決策／限制**：本程序控制項焦點、第三方控制項焦點與頂層視窗啟用是不同操作；缺乏追蹤事件不代表系統機制不存在。跨程序控制項焦點路線停止，不把它當成最終方案。[跨程序記錄](cross-process-validation.md)、[原生通知](native-notification-investigation.md)、[時序](native-settings-sequence.md)、[692acd7](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/692acd7)。

## 5. 執行來源分歧
- **目的／條件**：比較簡體實際輸出期間桌面與背景的原始查詢。**結果**：曾觀察桌面來源讀到 1、Codex 背景來源讀到 0（[96d4270](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/96d427004e13bf8b0d7acd7e322651e7789539c5)）。
- **限制／修正**：先前僅憑背景值判定桌面設定的結論需要重查；不因此證明沙箱、MSIX、虛擬化或 HKCU 映射，也不解釋所有舊失敗。

## 6. 命令後端
- **目的／條件**：比較無視窗 reg.exe 與 PowerShell 候選（[2ffd9ff](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/2ffd9ff)、[fa61959](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/fa61959f520d3b8d59f0d5d2fa03f3f0b648a6d0)）。
- **結果／限制**：PowerShell 受控輪次寫入、查詢成功但輸出仍為繁體；不能把工具名稱當已確認根因，也不能否定其他條件下的歷史成功。[驗證記錄](ime-validation.md)。
- **決策**：保留結果，不在沒有實際輸出證據時宣稱成功。

## 7. 獨立程式與人工驗收
- **目的／條件**：Owner 要求建立獨立 `manual-build`，不引用舊產品服務、熱鍵協調器或設定檔；由 Codex 建置打包、Owner 實測。[588fd79](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/588fd7978853bf224d5deb4903b5f4f3d83051fb)。
- **限制／決策**：隔離舊程式碼不是證明舊程式碼造成失敗。建置成功不是實機功能成功。

## 8. 關鍵觀察與選案
- **條件／結果**：Owner 回報從工作列手動操作可改變實際輸出；單靠熱鍵寫入未立即改變；熱鍵後正常切離再返回，實際輸出更新。
- **決策／限制**：因此選用設定寫入加頂層視窗啟用往返（[5f4af9f](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/5f4af9f06dfb8d48ab24ebe2f2c0dad0f7d3069c)），不宣稱已證明內部機制或所有情境成功。

## 9. 返回修正與封版
- **結果／條件**：Owner 觀察到輸出變化，但自動返回不穩定；當時記錄顯示提前取消，不能單憑它確定原因。修正版將 NULL 視為限時過渡，區分前景身份、受控返回與通知設定（[f1d351b](https://github.com/kaikaiwang7777-png/ZhuyinToggle/commit/f1d351ba72e3b6912cc7567fbc7f9d0e99ae621c)）。
- **決策**：Owner 後續確認最新交付版符合其需求，接受約 0.5～1 秒及短暫跳動，批准封版；詳[驗收紀錄](release-acceptance-v0.1.0.zh-TW.md)。

## 10. 經驗與未解問題

一次只改一項變數；先保住成功基準再逐步簡化；不要把測試工具限制當產品失敗，也不要混用不同執行環境讀值。大量追蹤不等於具備關鍵證據。工程方案可先滿足已確認的使用情境，不必等待完整破解內部機制；但最終成功也不能反向證明早期猜測正確。內部何時重新採用設定、背景／桌面讀值底層差異及其他應用程式相容性仍待研究。
