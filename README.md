# Achroma — 互動劇場裝置

一個多媒體互動裝置作品，結合地板投影、牆面投影與四位玩家的實體互動，帶領觀眾走過一段色彩失落與重建的故事。

## 專案資訊

- **Unity 版本**：6000.0.23f1（URP）
- **主場景**：`Assets/Scenes/Achroma.unity`
- **套件來源**：Unity Package Manager（含 Keijiro scoped registry）

## 故事結構

| 階段    | 內容                           |
| ------- | ------------------------------ |
| Story 1 | 開場影片 — 世界失去色彩        |
| Game 1  | 玩家撿拾散落的色彩能量瓶       |
| Story 2 | 過場影片                       |
| Game 2  | 玩家合作塗色，修復城市         |
| Story 3 | 過場影片                       |
| Game 3  | 對抗 Boss，阻止陰影吞噬城市    |
| Story 4 | 結尾影片 — 色彩回歸            |

## 系統架構

```plaintext
TouchDesigner (玩家追蹤)
    └─ OSC /table → Unity (port 10000)

Unity
    ├─ Wall Camera  → 牆面投影（故事影片 / 遊戲畫面）
    └─ Floor Camera → 地板投影（玩家位置 / 遊戲互動區域）

Unity
    └─ OSC /cue/{id}/start → QLab (port 53000)
```

### 玩家顏色對應（slot 0–3）

| Slot | 顏色 |
| ---- | ---- |
| 0    | 紅   |
| 1    | 藍   |
| 2    | 綠   |
| 3    | 黃   |

## 主要腳本

| 腳本                      | 說明                                          |
| ------------------------- | --------------------------------------------- |
| `TDAchromaFlowManager`    | 全局狀態機，管理 Story/Game 切換與淡場過渡    |
| `AchromaScreenManager`    | 管理牆/地板畫面的 CanvasGroup 切換            |
| `AchromaAudioManager`     | 音效管理，支援 QLab 和 Unity Native 兩種模式  |
| `TDTableReceiverBase`     | 接收 TouchDesigner OSC，輸出玩家座標          |
| `AchromaGame1Controller`  | 色彩能量瓶收集遊戲                            |
| `AchromaGame2Controller`  | 城市塗色遊戲（UV 多邊形區域判定）             |
| `AchromaGame3Controller`  | Boss 戰：修洞 / 躲影子 / 反擊                |
| `ColoringFloorRenderer`   | Game 2 地板動態上色渲染                       |
| `Game3FloorRenderer`      | Game 3 地板破洞與修復渲染                     |

## 設定步驟

### 1. 開啟專案

1. 以 Unity Hub 使用 `6000.0.23f1` 開啟專案
2. 等待 Unity 完成 Import 與 Library 建置
3. 開啟主場景 `Assets/Scenes/Achroma.unity`

影片檔案使用 Git LFS 儲存，clone 後請執行：

```bash
git lfs install
git lfs pull
```

### 2. QLab 音效

在 `AchromaAudioManager` Inspector 中：

- 將 `Mode` 設為 `QLab`
- 填入 QLab 電腦的 IP 與 Port（預設 `127.0.0.1:53000`，同台電腦）
- 每個 Cue 欄位填入對應的 **QLab Cue Number**（不是 Cue Name）
- 不需要的 Cue 欄位留空即可，不會送出 OSC

QLab 側設定：Workspace Settings → OSC → 勾選 **Use OSC Controls**。

### 3. TouchDesigner 追蹤

在 `TDTableReceiverBase` Inspector 中：

- OSC Address：`/table`
- Fallback Port：`10000`
- Arena Width / Height：依現場地板投影尺寸設定（單位：公尺）

### 4. 偵錯模式

在 `TDAchromaFlowManager` Inspector 中可勾選 **Use Initial Debug State**，直接從指定的 Game 或 Story 狀態開始。

執行期間熱鍵：

| 按鍵    | 動作                               |
| ------- | ---------------------------------- |
| `Space` | 在 Story 狀態跳過到下一個 Game     |
| `0`     | 強制結束目前 Game，進入下一段 Story |

## 套件相依

| 套件                        | 用途                          |
| --------------------------- | ----------------------------- |
| `jp.keijiro.osc-jack`       | OSC 通訊（TouchDesigner、QLab） |
| TextMeshPro                 | UI 文字                       |
| 2D Progress Bar Toolkit     | Game 1 / 2 進度條             |
| MicroLight MicroBar         | Game 3 Boss 血條              |
| Universal Render Pipeline   | 渲染管線                      |

## Repository 說明

- `Library/`、`Temp/`、`Obj/` 等 Unity 產生的資料夾已列入 `.gitignore`
- 影片檔（`*.mp4`）使用 **Git LFS** 儲存，不佔用一般 git 物件空間
- Source assets、Scenes、Scripts、ProjectSettings 均納入版本控制
