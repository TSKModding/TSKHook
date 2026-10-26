# TSKHook

## [English](README.md)

星騎士mod (DMM Game Player版)

## 功能

1. 更改遊戲速度 (更快的Ready GO不好玩嗎?^^)
   ![3x speed](./img/3x.gif)
2. 內置遊戲截圖
3. FPS設定
4. [中文翻譯](Translation.md)（可選簡繁轉換）
5. 遊戲視窗大小設定
6. 更改圖鑑放大縮小倍率
7. UI 文字與圖片翻譯，支援獨立資源更新及回退
8. 可選文字擷取及本機翻譯預覽

## 需求

1. Windows 10或以上
2. 星騎士 DMM Game Player版本

## 安裝方法

下載[Release](https://github.com/TSKModding/TSKHook/releases)
並解壓縮至您的星騎士安裝位置 `C:\Users\<username>\Twinkle_StarKnightsX`

遊戲內設定需安裝 [IL2CPP ConfigurationManager](https://github.com/anosu/BepInEx.ConfigurationManager/releases) 的 ConfigurationManager.dll 及 BepInEx.KeyboardShortcut.dll。UI 資源由 [TSKHook-UITranslations](https://github.com/Azure-Sora/TSKHook-UITranslations) 維護。

## 設定

已安裝設定管理器時，按 F1 開啟設定；未安裝時則重新讀取設定。亦可關閉遊戲後編輯 `BepInEx/config` 中產生的設定檔。舊 `plugins/config.json` 僅匯入一次；Maintenance 可重新讀取設定、檢查、重載及回退資源包。

| 欄位        | 預設值  | 說明                         |
|-----------|------|----------------------------|
| General/SpeedStep | 0.5  | 加快/減慢遊戲速度 (每按一下)           |
| General/FPS | 60   | 更改FPS值, 只會在遊戲開啟60FPS的情況下生效 |
| Translation/Enabled | true | 閣啟/關閉中文翻譯功能                |
| General/WindowWidth | 1280 | 遊戲視窗寬度                     |
| General/WindowHeight | 720  | 遊戲視窗高度                     |
| General/CharacterZoom | 1.0  | 角色立繪放大縮小倍率                 |
| Translation/UIEnabled | true | 開啟 UI 翻譯 |
| Translation/ImagesEnabled | true | 開啟 UI 圖片替換 |
| Translation/ChineseScript | Original | 保持原譯文，或轉換為簡體／繁體 |
| Capture/Enabled | false | 儲存擷取文字至 `plugins/TSKHook/ui_capture` |
| Updates/Enabled | true | 僅在遊戲啟動後檢查一次文字／圖片包；失敗時保留本機資源 |
| Updates/LocalPreview | false | 讀取 `plugins/TSKHook/ui_preview.jsonl` 並暫停公開更新 |

## 綁定鍵

| 按鍵   | 類型 | 說明                                       |
|------|----|------------------------------------------|
| F1   | 設定 | 已安裝管理器時開啟設定，否則重新讀取設定                    |
| F5   | 靜止 | 將遊戲暫時凍結                                  |
| F6   | 重設 | 將遊戲重設至正常速度                               | 
| F7   | 減少 | 減慢遊戲速度，視乎您的 `General/SpeedStep` 設定                   |
| F8   | 增加 | 加快遊戲速度，視乎您的 `General/SpeedStep` 設定                   |
| F10  | 翻譯 | 清除劇情快取並重載 UI 翻譯                                   |
| F11  | 翻譯 | 開啟/關閉中文翻譯功能                              |
| F12  | 截圖 | 截取當前的遊戲畫面至`C:\Users\<username>\Pictures` |
| Ctrl | 跳過 | 按Ctrl能跳過文字，就像Galgame的Ctrl skip功能         |

## 貢獻

您可以PR一些有用的功能，例如戰鬥紀錄、360滿體提示之類，除了修改戰鬥數值

## 免責聲明

使用修改程式進行遊戲，均屬違規行為，可能導致帳號被封鎖。

本人概不負責。
