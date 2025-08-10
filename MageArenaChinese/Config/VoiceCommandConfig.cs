// Config/VoiceCommandConfig.cs
using BepInEx.Configuration;
using UnityEngine;

namespace MageArenaChineseVoice.Config
{
    public static class VoiceCommandConfig
    {
        // === 模型設定 ===
        /// <summary>模型語言（會設定到 Recognissimo 的 SystemLanguage）</summary>
        public static ConfigEntry<SystemLanguage> ModelLanguage;
        /// <summary>相對於插件資料夾的模型路徑（或絕對路徑）。例：LanguageModels/vosk-model-small-cn-0.22</summary>
        public static ConfigEntry<string> ModelRelativePath;

        // === 主咒語 ===
        public static ConfigEntry<string> FireballCommand;
        public static ConfigEntry<string> FrostBoltCommand;
        public static ConfigEntry<string> WormCommand;
        public static ConfigEntry<string> HoleCommand;
        public static ConfigEntry<string> MagicMissileCommand;
        public static ConfigEntry<string> MirrorCommand;

        // === 追加咒語（SpellPages）===
        public static ConfigEntry<string> RockCommand;
        public static ConfigEntry<string> WispCommand;
        public static ConfigEntry<string> BlastCommand;
        public static ConfigEntry<string> DivineCommand;
        public static ConfigEntry<string> BlinkCommand;
        public static ConfigEntry<string> ThunderboltCommand;

        public static void Init(ConfigFile config)
        {
            FireballCommand = config.Bind("Commands", "Fireball", "火球術 火球 火焰彈 火焰球",
                "中文口令：火球術（可用多種詞語觸發，空格分隔）");

            FrostBoltCommand = config.Bind("Commands", "FrostBolt", "冰凍 冰箭 冰槍 冰矛 冰彈",
                "中文口令：冰凍術（可用多種詞語觸發，空格分隔）");

            WormCommand = config.Bind("Commands", "Worm", "入口",
                "中文口令：入口（可用多種詞語觸發，空格分隔）");

            HoleCommand = config.Bind("Commands", "Hole", "出口",
                "中文口令：出口（可用多種詞語觸發，空格分隔）");

            MagicMissileCommand = config.Bind("Commands", "MagicMissile", "魔法飛彈 魔法 飛彈 魔彈 魔導彈 魔彈術",
                "中文口令：魔法飛彈（可用多種詞語觸發，空格分隔）");

            MirrorCommand = config.Bind("Commands", "Mirror", "魔鏡",
                "中文口令：鏡像（可用多種詞語觸發，空格分隔）");

            RockCommand = config.Bind("AdditionalCommands", "Rock", "巨石 石塊 岩石 大石頭",
                "中文口令：巨石（可用多種詞語觸發，空格分隔）");

            WispCommand = config.Bind("AdditionalCommands", "Wisp", "光靈 光球 鬼火 精靈",
                "中文口令：光靈（可用多種詞語觸發，空格分隔）");

            BlastCommand = config.Bind("AdditionalCommands", "Blast", "黑暗爆破 暗影衝擊 暗影爆 暗爆 黑暗衝擊",
                "中文口令：暗影衝擊（可用多種詞語觸發，空格分隔）");

            DivineCommand = config.Bind("AdditionalCommands", "Divine", "聖光 神聖光 明光 聖之光",
                "中文口令：聖光（可用多種詞語觸發，空格分隔）");

            BlinkCommand = config.Bind("AdditionalCommands", "Blink", "閃現 瞬移 瞬步 瞬間移動",
                "中文口令：閃現（可用多種詞語觸發，空格分隔）");

            ThunderboltCommand = config.Bind("AdditionalCommands", "Thunderbolt", "雷霆一擊 閃電 雷擊 雷電術",
                "中文口令：雷霆一擊（可用多種詞語觸發，空格分隔）");
        }
    }
}
