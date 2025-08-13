// Config/VoiceCommandConfig.cs
using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MageArenaChineseVoice.Config
{
    public static class VoiceCommandConfig
    {
        // === 模型設定 ===
        public static ConfigEntry<SystemLanguage> ModelLanguage;
        public static ConfigEntry<string> ModelRelativePath;

        // === 行為調整 ===
        public static ConfigEntry<bool> ConvertTraditionalToSimplified;
        public static ConfigEntry<bool> EnableSingleCharExpansion;
        public static ConfigEntry<DetectionMode> DetectMode; // 新增：原生/模組命中策略

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

        // === 額外模組咒語（如 BlackMagic API 的 SpellPages）===
        // 格式：spellId=關鍵詞1 關鍵詞2|spellId2=關鍵詞...
        public static ConfigEntry<string> ModuleSpellBindings;

        // === 時序 / 效能參數（秒）===
        public static ConfigEntry<float> CastCooldownSec;
        public static ConfigEntry<float> StartupWaitSec;
        public static ConfigEntry<float> ResetStopWaitSec;
        public static ConfigEntry<float> RestartWaitSec;
        public static ConfigEntry<float> MonitorIntervalSec;

        // === Debug 選項 ===
        public static ConfigEntry<bool> DebugEnabled;
        public static ConfigEntry<bool> DebugLogPartial;
        public static ConfigEntry<bool> DebugLogFinal;
        public static ConfigEntry<bool> DebugLogDecision;
        public static ConfigEntry<bool> DebugDumpVocabulary;
        public static ConfigEntry<bool> DebugDumpVocabularyToFile; // 新增：輸出檔案

        // ===== 暴露給外部（已套用繁→簡＋可選單字拆解＋正規化）=====
        public static string FireballExpanded => ExpandForUse(FireballCommand?.Value);
        public static string FrostBoltExpanded => ExpandForUse(FrostBoltCommand?.Value);
        public static string WormExpanded => ExpandForUse(WormCommand?.Value);
        public static string HoleExpanded => ExpandForUse(HoleCommand?.Value);
        public static string MagicMissileExpanded => ExpandForUse(MagicMissileCommand?.Value);
        public static string MirrorExpanded => ExpandForUse(MirrorCommand?.Value);

        public static string RockExpanded => ExpandForUse(RockCommand?.Value);
        public static string WispExpanded => ExpandForUse(WispCommand?.Value);
        public static string BlastExpanded => ExpandForUse(BlastCommand?.Value);
        public static string DivineExpanded => ExpandForUse(DivineCommand?.Value);
        public static string BlinkExpanded => ExpandForUse(BlinkCommand?.Value);
        public static string ThunderboltExpanded => ExpandForUse(ThunderboltCommand?.Value);

        // === 對外 tokens（方便外部直接使用陣列） ===
        public static string[] FireballTokens => GetTokens(FireballExpanded);
        public static string[] FrostBoltTokens => GetTokens(FrostBoltExpanded);
        public static string[] WormTokens => GetTokens(WormExpanded);
        public static string[] HoleTokens => GetTokens(HoleExpanded);
        public static string[] MagicMissileTokens => GetTokens(MagicMissileExpanded);
        public static string[] MirrorTokens => GetTokens(MirrorExpanded);

        public static string[] RockTokens => GetTokens(RockExpanded);
        public static string[] WispTokens => GetTokens(WispExpanded);
        public static string[] BlastTokens => GetTokens(BlastExpanded);
        public static string[] DivineTokens => GetTokens(DivineExpanded);
        public static string[] BlinkTokens => GetTokens(BlinkExpanded);
        public static string[] ThunderboltTokens => GetTokens(ThunderboltExpanded);

        // 模組咒語解析後快取： spellId -> tokens[]
        private static Dictionary<string, string[]> _moduleSpellTokenMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        // ====== 繁→簡字庫 ======
        private static Dictionary<char, char> _t2s;
        private static bool _mapBuilt;

        // 插件 DLL 根目錄（用於解析相對路徑）
        private static string _pluginDir;

        public enum DetectionMode
        {
            // Any：原生或模組任一命中即觸發（你需求的「只要一方觸發成功就先執行」）
            Any = 0,
            // All：原生與模組必須同時命中才觸發（較嚴格）
            All = 1
        }

        public static void Init(ConfigFile config)
        {
            // 解析插件資料夾
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                _pluginDir = Path.GetDirectoryName(asm.Location)?.Replace('\\', '/');
            }
            catch { _pluginDir = Directory.GetCurrentDirectory().Replace('\\', '/'); }

            // === 模型設定 ===
            ModelLanguage = config.Bind("Model", "Language", SystemLanguage.Chinese,
                "辨識語言（會傳給 Recognissimo/Vosk）。例如 Chinese、Russian、English。");
            ModelRelativePath = config.Bind("Model", "RelativePath", "LanguageModels/vosk-model-small-cn-0.22",
                "模型資料夾相對於插件 DLL 的路徑（或絕對路徑）。建議使用輕量版本以降低記憶體佔用。");

            // === 行為調整 ===
            ConvertTraditionalToSimplified = config.Bind("Behavior", "ConvertTraditionalToSimplified", true,
                "是否將 Config 的繁體詞在讀取時自動轉為簡體（僅程式內生效，不改檔案）。");
            EnableSingleCharExpansion = config.Bind("Behavior", "EnableSingleCharExpansion", false,
                "是否將純中文詞額外拆成單字加入（可提升命中；若誤觸多，建議關閉）。");
            DetectMode = config.Bind("Behavior", "DetectionMode", DetectionMode.Any,
                "命中策略：Any=原生或模組任一命中即觸發；All=兩者皆命中才觸發。");

            // === 詞表（中文）===
            FireballCommand = BindCommand(config, "Commands", "Fireball",
                "火球 爆裂 大爆炸",
                "中文口令：火球術（空格分隔多個同義詞）");

            FrostBoltCommand = BindCommand(config, "Commands", "FrostBolt",
                "冰凍 冰槍 凍住 ",
                "中文口令：冰凍術（空格分隔多個同義詞）");

            WormCommand = BindCommand(config, "Commands", "Worm",
                "入口 芝麻",
                "中文口令：入口（空格分隔多個同義詞）");

            HoleCommand = BindCommand(config, "Commands", "Hole",
                "出口 開門",
                "中文口令：出口（空格分隔多個同義詞）");

            MagicMissileCommand = BindCommand(config, "Commands", "MagicMissile",
                "魔法飛彈 魔彈 飛彈 魔法彈 魔法",
                "中文口令：魔法飛彈（空格分隔多個同義詞）");

            MirrorCommand = BindCommand(config, "Commands", "Mirror",
                "魔鏡",
                "中文口令：魔鏡（空格分隔多個同義詞）");

            // === 追加咒語（SpellPages）===
            RockCommand = BindCommand(config, "AdditionalCommands", "Rock",
                "巨石 岩石 大石",
                "中文口令：巨石（空格分隔多個同義詞）");

            WispCommand = BindCommand(config, "AdditionalCommands", "Wisp",
                "鬼火 精靈 光靈 靈火",
                "中文口令：光靈（空格分隔多個同義詞）");

            BlastCommand = BindCommand(config, "AdditionalCommands", "Blast",
                "爆破 黑暗 衝擊 衝擊 暗影 波動",
                "中文口令：暗影衝擊（空格分隔多個同義詞）");

            DivineCommand = BindCommand(config, "AdditionalCommands", "Divine",
                "聖光 光明 奇蹟 治療 治癒",
                "中文口令：聖光（空格分隔多個同義詞）");

            BlinkCommand = BindCommand(config, "AdditionalCommands", "Blink",
                "閃現 瞬移 傳送",
                "中文口令：閃現（空格分隔多個同義詞）");

            ThunderboltCommand = BindCommand(config, "AdditionalCommands", "Thunderbolt",
                "雷霆一擊 閃電 雷擊 霹靂 雷電",
                "中文口令：雷霆一擊（空格分隔多個同義詞）");

            // 額外模組咒語
            ModuleSpellBindings = config.Bind(
                "Modules",
                "SpellBindings",
                "",
                "為外部模組新增法術口令綁定。格式：spellId=關鍵詞1 關鍵詞2|spellId2=關鍵詞...\n" +
                "左邊為 ISpellCommand.GetSpellName() 的返回值（建議小寫），右邊為空格分隔的觸發詞。\n" +
                "例：blackrain=黑雨 黑色風暴|summonimp=小惡魔 召喚小鬼"
            );
            TryHookSettingChanged(ModuleSpellBindings);

            // === 時序參數（秒） ===
            CastCooldownSec = config.Bind("Timing", "CastCooldownSec", 0.15f, "命中後的冷卻時間（秒），避免 partial/final 重複觸發。");
            StartupWaitSec = config.Bind("Timing", "StartupWaitSec", 0.05f, "初次啟動識別前的等待（秒）。");
            ResetStopWaitSec = config.Bind("Timing", "ResetStopWaitSec", 0.01f, "Reset 時 StopProcessing 後等待（秒）。");
            RestartWaitSec = config.Bind("Timing", "RestartWaitSec", 0.00f, "Reset 結束後 StartProcessing 前等待（秒）。");
            MonitorIntervalSec = config.Bind("Timing", "MonitorIntervalSec", 120.0f, "麥克風狀態檢查間隔（秒）。");

            // === Debug ===
            DebugEnabled = config.Bind("Debug", "Enabled", false, "是否啟用 Debug 記錄。");
            DebugLogPartial = config.Bind("Debug", "LogPartial", false, "是否記錄 Partial 結果。");
            DebugLogFinal = config.Bind("Debug", "LogFinal", true, "是否記錄 Final 結果。");
            DebugLogDecision = config.Bind("Debug", "LogDecision", true, "是否記錄匹配/施法決策過程。");
            DebugDumpVocabulary = config.Bind("Debug", "DumpVocabulary", true, "啟動時是否輸出 Vocabulary 清單（到 Console）。");
            DebugDumpVocabularyToFile = config.Bind("Debug", "DumpVocabularyToFile", false, "啟動時是否將 Vocabulary 另存檔到 BepInEx/Config/。");

            // === 最後建詞庫 ===
            BuildT2SMap();
            RebuildModuleSpellMap();
            RefreshVocabularyCache();

            if (DebugEnabled.Value)
            {
                UnityEngine.Debug.Log($"[VoiceCommandConfig] DebugEnabled = true");
                UnityEngine.Debug.Log($"[VoiceCommandConfig] Language = {ModelLanguage.Value}");
                UnityEngine.Debug.Log($"[VoiceCommandConfig] ModelPath = {GetResolvedModelPath()}");
                UnityEngine.Debug.Log($"[VoiceCommandConfig] DetectionMode = {DetectMode.Value}");

                var vocab = BuildVocabularyPreview();
                UnityEngine.Debug.Log($"[VoiceCommandConfig] Vocabulary total tokens = {vocab.TotalCount}, unique = {vocab.UniqueCount}, modules = {vocab.ModuleEntryCount}");

                if (DebugDumpVocabulary.Value)
                {
                    UnityEngine.Debug.Log($"[VoiceCommandConfig] === Vocabulary (preview) ===\n{string.Join(", ", vocab.AllTokens.Take(120))}{(vocab.UniqueCount > 120 ? " ..." : "")}");
                }

                if (DebugDumpVocabularyToFile.Value)
                {
                    try
                    {
                        var outPath = Path.Combine(Paths.ConfigPath, "MageArena.voice.vocab.txt").Replace('\\', '/');
                        File.WriteAllLines(outPath, vocab.AllTokens.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
                        UnityEngine.Debug.Log($"[VoiceCommandConfig] Vocabulary written: {outPath}");
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning($"[VoiceCommandConfig] Write vocab failed: {e}");
                    }
                }
            }
        }

        // 取得絕對模型路徑（解決相對路徑環境差異）
        public static string GetResolvedModelPath()
        {
            var raw = ModelRelativePath?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            if (Path.IsPathRooted(raw)) return NormalizePath(raw);

            // 相對於插件 DLL
            if (!string.IsNullOrEmpty(_pluginDir))
                return NormalizePath(Path.Combine(_pluginDir, raw));

            // 後備：目前工作目錄
            return NormalizePath(Path.GetFullPath(raw));
        }

        private static string NormalizePath(string p) => p.Replace('\\', '/');

        public static string[] GetTokens(string raw) => Tokenize(ExpandForUse(raw));

        // ===== 模組綁定解析 =====
        public static IReadOnlyDictionary<string, string[]> GetModuleSpellTokenMap() => _moduleSpellTokenMap;

        public static void RebuildModuleSpellMap()
        {
            _moduleSpellTokenMap.Clear();
            var raw = ModuleSpellBindings?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return;

            int ok = 0, bad = 0;
            foreach (var part in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2)
                {
                    bad++;
                    continue;
                }
                var id = kv[0].Trim();
                var syn = kv[1];

                if (string.IsNullOrWhiteSpace(id))
                {
                    bad++;
                    continue;
                }

                var expanded = ExpandForUse(syn);
                var toks = Tokenize(expanded);
                if (toks.Length == 0)
                {
                    bad++;
                    continue;
                }

                if (!_moduleSpellTokenMap.ContainsKey(id))
                {
                    _moduleSpellTokenMap[id] = toks;
                    ok++;
                }
            }

            if (DebugEnabled.Value)
                UnityEngine.Debug.Log($"[VoiceCommandConfig] ModuleSpellBindings parsed: ok={ok}, bad={bad}, totalIds={_moduleSpellTokenMap.Count}");
        }

        // ===== Vocabulary 預覽（提供 Debug 與外部需求）=====
        public struct VocabPreview
        {
            public int TotalCount;
            public int UniqueCount;
            public int ModuleEntryCount;
            public List<string> AllTokens;
        }

        public static VocabPreview BuildVocabularyPreview()
        {
            var all = new List<string>(128);
            void addRange(IEnumerable<string> xs) { if (xs != null) all.AddRange(xs); }

            addRange(FireballTokens);
            addRange(FrostBoltTokens);
            addRange(WormTokens);
            addRange(HoleTokens);
            addRange(MagicMissileTokens);
            addRange(MirrorTokens);

            addRange(RockTokens);
            addRange(WispTokens);
            addRange(BlastTokens);
            addRange(DivineTokens);
            addRange(BlinkTokens);
            addRange(ThunderboltTokens);

            int before = all.Count;
            var unique = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);

            foreach (var kv in _moduleSpellTokenMap)
                foreach (var t in kv.Value)
                    unique.Add(t);

            return new VocabPreview
            {
                TotalCount = before,
                UniqueCount = unique.Count,
                ModuleEntryCount = _moduleSpellTokenMap.Count,
                AllTokens = unique.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        // 外部可一次取回所有同義詞集合（含模組）
        public static Dictionary<string, string[]> GetAllSynonymSets()
        {
            var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Fireball"] = FireballTokens,
                ["FrostBolt"] = FrostBoltTokens,
                ["Worm"] = WormTokens,
                ["Hole"] = HoleTokens,
                ["MagicMissile"] = MagicMissileTokens,
                ["Mirror"] = MirrorTokens,
                ["Rock"] = RockTokens,
                ["Wisp"] = WispTokens,
                ["Blast"] = BlastTokens,
                ["Divine"] = DivineTokens,
                ["Blink"] = BlinkTokens,
                ["Thunderbolt"] = ThunderboltTokens
            };

            foreach (var kv in _moduleSpellTokenMap)
                dict[kv.Key] = kv.Value;

            return dict;
        }

        // ====== 繁→簡（單向）字庫 ======
        private static void BuildT2SMap()
        {
            if (_mapBuilt && _t2s != null) return;
            _mapBuilt = true;

            const string pairs =
                "術术 彈弹 鏡镜 聖圣 電电 擊击 閃闪 門门 槍枪 凍冻 靈灵 飛飞 間间 衝冲 開开 治治 癒愈 雷雷 魔魔 火火 冰冰 光光 爆爆 岩岩 石石 大大 黑黑 暗暗 影影 波波 動动 奇奇 蹟迹 " +
                "芝芝 麻麻 出出 口口 進进 入入 出出 口口 巨巨 石石 精精 靈灵 靈灵 暗暗 影影 聖圣 光光 明明 奇奇 蹟迹 治治 癒愈 閃闪 現现 瞬瞬 移移 傳传 送送 雷雷 霆霆 閃闪 電电 擊击 霹霹 靂雳";


            _t2s = new Dictionary<char, char>(1024);

            foreach (var token in pairs.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length == 2)
                {
                    var trad = token[0];
                    var simp = token[1];
                    if (!_t2s.ContainsKey(trad)) _t2s.Add(trad, simp);
                }
            }
        }

        private static string ConvertT2S(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (!(_mapBuilt && _t2s != null)) BuildT2SMap();

            var arr = s.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
                if (_t2s.TryGetValue(arr[i], out var m)) arr[i] = m;

            return new string(arr);
        }

        // ====== 主正規化流程：繁→簡（可關） + 去重 +（可選）單字拆解 + 移除標點/全形半形統一 ======
        private static string ExpandForUse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var tokens = TokenizeRaw(raw)
                .Select(NormalizeForMatch)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (ConvertTraditionalToSimplified?.Value == true)
            {
                for (int i = 0; i < tokens.Count; i++)
                    tokens[i] = ConvertT2S(tokens[i]);
            }

            var set = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);

            if (EnableSingleCharExpansion?.Value == true)
            {
                foreach (var tok in tokens)
                {
                    if (IsAllCjk(tok))
                        foreach (var ch in tok) set.Add(ch.ToString());
                }
            }

            return string.Join(" ", set.Where(s2 => !string.IsNullOrWhiteSpace(s2)));
        }

        // === 小工具 ===

        // 允許中英空白分隔；也支援以多個空白分隔
        private static string[] TokenizeRaw(string raw)
        {
            return raw
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToArray();
        }

        public static string[] Tokenize(string expanded)
        {
            if (string.IsNullOrWhiteSpace(expanded)) return Array.Empty<string>();
            return expanded
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        // 將字串正規化用於比對：去標點、全形→半形、去空白
        public static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            // 全形→半形
            s = ToHalfWidth(s);

            // 移除常見中英標點
            s = RemovePunctuation(s);

            // 去空白與大小寫不敏感
            return s.Trim();
        }

        private static string ToHalfWidth(string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                // 全形空白
                if (ch == 0x3000) { sb.Append(' '); continue; }
                // 全形可見字元範圍
                if (ch >= 0xFF01 && ch <= 0xFF5E)
                {
                    sb.Append((char)(ch - 0xFEE0));
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static readonly char[] _punct =
        {
            // 英文標點
            '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '<', '>', '/', '\\', '|', '-', '_', '+', '=', '*', '&', '^', '%', '$', '#', '@', '~', 
            // 空白類
            '\t', '\r', '\n',
            // 中文標點
            '。','，','、','；','：','？','！','「','」','『','』','（','）','《','》','〈','〉','—','－','～','…','【','】','．','‧','｜','＼','／'
        };

        private static string RemovePunctuation(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var arr = s.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
                if (_punct.Contains(arr[i])) arr[i] = ' ';
            return new string(arr);
        }

        private static bool IsAllCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s)
                if (!(c >= 0x4E00 && c <= 0x9FFF)) return false;
            return true;
        }

        private static ConfigEntry<string> BindCommand(ConfigFile config, string section, string key, string defaultValue, string description)
        {
            var entry = config.Bind(section, key, defaultValue, description);
            _commandEntries.Add(entry);
            TryHookSettingChanged(entry);
            return entry;
        }

        private static void RefreshVocabularyCache()
        {
            // 重新生成所有同義詞集合，外部可調用
            var vocab = BuildVocabularyPreview();
            if (DebugEnabled.Value)
            {
                UnityEngine.Debug.Log($"[VoiceCommandConfig] Vocabulary refreshed: total={vocab.TotalCount}, unique={vocab.UniqueCount}");
            }
        }

        private static void TryHookSettingChanged<T>(ConfigEntry<T> entry)
        {
            try
            {
                entry.SettingChanged += (_, __) =>
                {
                    if (entry is ConfigEntry<string> strEntry)
                    {
                        if (_commandEntries.Contains(strEntry))
                        {
                            RefreshVocabularyCache();
                        }
                    }
                    else if (entry == (object)ModelRelativePath)
                    {
                        if (DebugEnabled.Value)
                            UnityEngine.Debug.Log($"[VoiceCommandConfig] Model path changed -> {GetResolvedModelPath()}");
                    }
                    else if (entry == (object)ModuleSpellBindings)
                    {
                        RebuildModuleSpellMap();
                        RefreshVocabularyCache();
                    }
                };
            }
            catch { /* BepInEx 5 無事件忽略 */ }
        }

        private static readonly List<ConfigEntry<string>> _commandEntries = new List<ConfigEntry<string>>();
    }
}