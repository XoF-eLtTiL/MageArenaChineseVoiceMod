
# 🪄 Mage Arena 繁體中文語音模組

⚠️ **警告**：本模組仍在測試階段，可能存在錯誤或不穩定的情況。  
如遇到任何問題，請回報以便修正。  

⚠️ **注意**：  
- **僅支援中文口令**，英文原始口令已移除。  
- 無法與 **BlackMagic API 系列模組** 一同使用，通用版本仍在開發中。  

---

## ✨ 功能特色

- 🗣 **完整繁體中文語音施法**：直接用中文喊招式就能施放法術。  
- 🔄 **自動繁簡轉換**：可選擇在讀取設定時將繁體詞轉成簡體，提升 Vosk 模型辨識率（不修改原檔案）。  
- 🧩 **多同義詞觸發**：每個法術可設定多個同義詞，提升辨識成功率。  
- 🔠 **自動單字擴充**：可選擇將純中文詞拆成單字加入詞庫（命中率更高，誤觸多可關閉）。  
- 🪄 **可擴充外部法術**：支援 `SpellBindings` 配置外部模組的法術綁定。  

---

## 🗣 內建法術中文口令

| 法術（原始名稱）     | 中文口令（空格分隔多個詞）                           |
|--------------------|--------------------------------------------------|
| Fireball           | 火球 爆裂 大爆炸                            |
| Frost Bolt         | 冰凍 冰槍 凍住           |
| Worm（入口）    | 入口 芝麻                                              |
| hole（出口）    | 出口 開門                                              |
| Magic Missile      | 魔法飛彈 魔彈 飛彈 魔法彈 魔法                  |
| Mirror             | 魔鏡                                              |
| Rock               | 巨石 岩石 大石                                |
| Wisp               | 鬼火 精靈 光靈 靈火                                  |
| Dark Blast         | 爆破 黑暗 衝擊 衝擊 暗影 波動              |
| Divine Light       | 聖光 光明 奇蹟 治療 治癒                            |
| Blink              | 閃現 瞬移 傳送                                              |
| Thunderbolt        | 雷霆一擊 閃電 雷擊 霹靂 雷電                        |

> 💡 每個口令的詞語用空格分隔，說出其中任意一個詞即可觸發。

---

## 🔧 Config 主要設定

| 參數                              | 預設值 | 說明 |
|----------------------------------|--------|------|
| `Model.Language`                 | `Chinese` | 辨識語言（傳給 Recognissimo，例如 Chinese、Russian、English）。 |
| `Model.RelativePath`             | `LanguageModels/vosk-model-small-cn-0.22` | 模型資料夾相對於 DLL 的路徑或絕對路徑。 |
| `Behavior.ConvertTraditionalToSimplified` | `true` | 是否將設定中的繁體詞轉成簡體（程式內部，檔案不變）。 |
| `Behavior.EnableSingleCharExpansion`      | `true` | 是否將純中文詞額外拆成單字加入詞庫（可提升命中率）。 |
| `Modules.SpellBindings`          | *(空)* | 外部模組法術綁定設定，格式見下方。 |

---

## 🆕 新增額外模組咒語

如果安裝了其他法術模組，可以在 `config.cfg` 內新增對應的中文口令，格式如下：  

```ini
[Modules]
SpellBindings = spellid1=關鍵詞1 關鍵詞2|spellid2=關鍵詞3 關鍵詞4
````

* `spellid`：外部法術的 **唯一識別名稱**（必須與 `ISpellCommand.GetSpellName()` 回傳值一致，通常為小寫英文字母）。
* `關鍵詞`：觸發該法術的**中文口令**，可設定多個，並以空格分隔。
* 多個法術之間用 `|` 分隔。

**範例**：

```ini
[Modules]
SpellBindings = blackrain=黑雨 黑色風暴|summonimp=小惡魔 召喚小鬼
```

這樣：

* 說「黑雨」或「黑色風暴」會觸發 `blackrain` 法術。
* 說「小惡魔」或「召喚小鬼」會觸發 `summonimp` 法術。


