# EasyLoco

* [English](README.md)
* [正體中文](README.zh-hant-TW.md)

自訂 VRChat avatar 的移動動作 —— 待機姿勢、睡覺、AFK —— 不需要手動編輯 animator controller。
透過 Modular Avatar 安裝。

## 功能

* **待機姿勢** —— 更換站立、蹲下、趴下的待機動畫，並可登錄多組姿勢，在遊戲中從 expression menu
  隨時切換。
* **睡覺** —— 趴下時可切換睡覺姿勢。依頭部方向在三個動畫之間混合（面朝上、面朝下、側躺），
  方向由實際的頭部追蹤偵測。睡覺時仍可移動，因此還是能爬行。站起來時會自動解除。
* **AFK** —— 依姿勢分開的 AFK 動畫，站立、蹲下、趴下各自有進入 / 循環 / 結束三段動畫。

每個動畫都內建預設值，安裝後即可直接使用。

## 使用方式

1. 選擇你的 avatar，點選 `GameObject -> Add EasyLoco Component`。
2. 在 Inspector 設定想要的動畫。
3. 按下 **Build Modular Avatar**。
4. 將產生的 `GeneratedEasyLocoMA` prefab 拖曳到你的 avatar 上。

建置會產生一個獨立的 prefab，放到 avatar 底下任何位置即可安裝。重新建置會直接覆寫同一個 prefab，
因此已經使用它的 avatar 會自動更新，不需要重新拖曳。

完整說明請見 [Getting Started](Documentation~/getting-started.md)。

## 安裝方式

### VCC

1. 開啟 VRChat Creator Companion。
2. 加入 Pue-Tsua Workshop 的 VPM listing。
3. 在 VCC 開啟你的 avatar project。
4. 將 `EasyLoco` 加入 project。

### Unity Package Manager

1. 開啟 Unity Package Manager。
2. 點選 `+`。
3. 選擇 `Add package from git URL...`。
4. 輸入 `https://github.com/puetsua/VRCEasyLoco.git`。
