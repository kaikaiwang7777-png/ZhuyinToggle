# 跨程序焦點實驗（非正式產品）

這兩個獨立程序僅供可丟棄的專用互動桌面測試。TestHost 提供文字框、按鈕及唯讀狀態；FocusController 使用 Windows UI Automation 操作該宿主的焦點，不透過自訂指令讓宿主代為切換。程式不支援操作記事本、Chrome 或任意應用程式。

目前實驗未能安全完成焦點往返，請勿作為切換工具使用或無人值守執行。控制器在使用者明確觸發後會修改目前使用者的微軟注音 REG_SZ；關閉時僅在目前值符合實驗預期時嘗試還原原值。比較與還原不是原子操作，可能存在競態，不能保證攔截所有外部寫入。請勿在重要內容或尚未完成組字的情境使用。

本機編譯：`dotnet build tools/CrossProcess/TestHost -c Release` 及 `dotnet build tools/CrossProcess/FocusController -c Release`。控制器需與 TestHost.exe 位於同一目錄，並以 `--focus` 或 `--write-only` 明確選擇模式；Win+Alt+C 執行一次實驗。此機制和結果詳見專案的驗證紀錄。

宿主可觀察的 IMM 訊息與設定檔狀態不能完整證明所有 TSF 候選字或組字活動均已結束；本實驗也無法保證處理使用者輸入的所有競態。
