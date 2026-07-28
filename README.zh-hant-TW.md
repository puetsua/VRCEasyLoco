# EasyLoco

* [English](README.md)
* [正體中文](README.zh-hant-TW.md)

自訂 VRChat avatar 的移動動作 —— 待機姿勢、睡覺、AFK —— 不需要手動編輯 animator controller。
透過 Modular Avatar 安裝。

## 功能

* **待機姿勢** —— 更換站立、蹲下、趴下的待機動畫，並可登錄多組姿勢，在遊戲中從 expression menu
  隨時切換。
* **睡覺**（*選用功能，在 Inspector 用一個勾選框開關*）—— 睡覺子選單內含兩個開關：*Sleep Loco*
  在趴下時切換睡覺姿勢（依頭部方向在三個動畫之間混合——面朝上、面朝下、側躺，由實際的頭部追蹤
  偵測；睡覺時仍可移動，因此還是能爬行），*Feet Lock* 則把雙腳鎖定到姿勢上，避免站立的雙腳把
  姿勢拉走。兩者在站起來時都會自動解除。睡覺會單獨產生一個 prefab，因此也可以只把睡覺功能安裝
  到其他 avatar 上。
* **AFK** —— 依姿勢分開的 AFK 動畫，站立、蹲下、趴下各自有進入 / 循環 / 結束三段動畫。

每個動畫都內建預設值，安裝後即可直接使用。

## 使用方式

1. 選擇你的 avatar，點選 `GameObject -> Add EasyLoco Component`。
2. 在 Inspector 設定想要的動畫。
3. 按下 **Build Modular Avatar**。

建置會產生一個獨立的 `GeneratedEasyLocoMA` prefab，並自動安裝到你的 avatar 上。重新建置會直接覆寫
同一個 prefab，已安裝的實例也會跟著更新。你也可以手動把這個 prefab 拖到其他相似的 avatar 上重複使用。

睡覺也可以單獨安裝：睡覺區塊最下方的 **Add Sleeping Only** 會只用你設定的動畫產生睡覺 prefab 並裝到
avatar 上，不會動到移動、待機姿勢與 AFK。此時睡覺選單會掛在 avatar 的根選單；之後再按
**Build Modular Avatar** 會一併處理睡覺，並取代這個按鈕裝上的內容。

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
