# EasyLoco

* [English](README.md)
* [正體中文](README.zh-hant-TW.md)

自訂 VRChat avatar 的移動動作 —— 靜止姿勢、睡覺、AFK —— 不需要手動編輯 animator controller。
透過 Modular Avatar 安裝。

## 功能

* **靜止姿勢** —— 更換站立、蹲下、趴下的靜止動畫，並可登錄多組姿勢，在遊戲中從 expression menu
  隨時切換。
* **睡覺**（*獨立模組，有自己的按鈕與 prefab*）—— 睡覺子選單內含兩個開關：*Sleep Loco*
  在趴下時切換睡覺姿勢（依頭部方向在三個動畫之間混合——臉朝上、臉朝下、側躺，由實際的頭部追蹤
  偵測；睡覺時仍可移動，因此還是能爬行），*Feet Lock* 則把雙腳鎖定到姿勢上，避免站立的雙腳把
  姿勢拉走。兩者在站起來時都會自動解除。睡覺是疊加在 avatar 既有的 base locomotion 之上，因此
  不論有沒有安裝 EasyLoco 的其他功能，都可以只裝睡覺。
* **AFK** —— 依姿勢分開的 AFK 動畫，站立、蹲下、趴下各自有進入 / 循環 / 結束三段動畫。
* **多語言** —— 支援 English 與正體中文，可從元件最上方的「語言」下拉選單切換，設定屬於使用者本身，
  會跨 project 記住。靜止姿勢的名稱會一起帶進遊戲內的選單：切換語言時，內建的名稱會自動換成新語言。
  一旦你自己改名、加了一列、或把某一列換成自己的動畫，那個 stance 就不再自動更新——名稱從此是你的。
  這是逐 stance 判斷的，所以改了站立的姿勢，蹲下和趴下還是會跟著語言走。睡覺選單的項目目前兩種語言
  都是英文。

每個動畫都內建預設值，安裝後即可直接使用。

## 使用方式

1. 選擇你的 avatar，點選 `GameObject -> Add EasyLoco Component`。
2. 在 Inspector 設定想要的動畫。
3. 按下 **建置 Modular Avatar**。

建置會產生一個獨立的 `GeneratedEasyLocoMA` prefab，並自動安裝到你的 avatar 上。重新建置會直接覆寫
同一個 prefab，已安裝的實例也會跟著更新。你也可以手動把這個 prefab 拖到其他相似的 avatar 上重複使用。

睡覺是獨立模組，**建置 Modular Avatar** 不會處理它。請用「模組 - 睡覺動畫」區塊最上方的
**建置並附加睡覺動作**：它會用你設定的動畫產生睡覺 prefab 並疊加到 avatar 上。裝好之後
同一個按鈕會變成 **移除睡覺動作**，可以再把它移除；改了動畫要更新的話，先移除再重新建置。

睡覺會裝在 `GeneratedEasyLocoMA` 底下，讓 EasyLoco 安裝的東西集中在同一個物件下，選單也會掛在
`EasyLoco` 底下。若 avatar 從未建置過主 prefab，就沒有這個物件，睡覺會直接裝在 descriptor 旁邊、
選單掛在根選單——所以就算完全沒裝 EasyLoco 的其他功能，睡覺一樣能用。如果是先裝睡覺、之後才建置主
prefab，記得再按一次睡覺按鈕，它才會移到正確位置。

由於它是以「新增子物件」的形式掛在 `GeneratedEasyLocoMA` 實例上，重新建置該 prefab 不會弄丟它，但它
也不會跟著 prefab 走：把 `GeneratedEasyLocoMA.prefab` 拖到其他 avatar 只會帶過去移動功能，不含睡覺。
需要的話請另外把 `EasyLocoSleep.prefab` 一起拖過去。

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
