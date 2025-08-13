// Patches/VoiceControlListenerPatch.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using Dissonance;
using HarmonyLib;
using MageArenaChineseVoice.Config;
using Recognissimo;
using Recognissimo.Components;
using UnityEngine;

namespace MageArenaChineseVoice.Patches
{
    [HarmonyPatch(typeof(VoiceControlListener))]
    public static class VoiceControlListenerPatch
    {
        // ====== 主要/追加指令映射 ======
        private static Dictionary<string[], Action<VoiceControlListener>> commandMap;     // main spells: 多關鍵詞 -> action
        private static Dictionary<string, string[]> additionalCommandMap;                 // extra spells: spellId -> 多關鍵詞

        // 正規化後（去空白/標點、小寫）
        private static List<(string[] keys, Action<VoiceControlListener> action)> commandListNormalized;
        private static Dictionary<string, string[]> additionalMapNormalized;

        // ====== 快速匹配相關 ======
        private static FastKeywordMatcher _mainMatcher;
        private static FastKeywordMatcher _extraMatcher;
        private static Dictionary<string, Action<VoiceControlListener>> _mainKw2Action;   // kw -> action
        private static Dictionary<string, string> _extraKw2SpellId;                       // kw -> spellId
        private static readonly Dictionary<string, int> _spellPriority = new()
        {
            ["thunderbolt"] = 0,
            ["blink"] = 1,
            ["divine"] = 2,
            ["blast"] = 3,
            ["rock"] = 4,
            ["wisp"] = 5
        };

        // 去抖狀態（Partial）
        private static string _lastHitKw = null;
        private static float _lastHitTs = 0f;
        private const float PARTIAL_REPEAT_WINDOW = 0.30f; // 300ms

        // ====== 勝出鎖（誰先施放，另一方立刻放棄）======
        private static float _lastWinnerTs = -999f;
        private const float WINNER_LATCH_WINDOW = 0.050f; // 50ms 競速鎖

        // ====== 時序設定（讀自 Config）======
        private static float _castCooldownSec;
        private static float _startupWaitSec;
        private static float _resetStopWaitSec;
        private static float _restartWaitSec;
        private static float _monitorIntervalSec;

        // 觸發節流
        private static float _lastCastTs;

        // Debug
        private static ManualLogSource _log;
        private static bool _dbgEnabled;
        private static bool _dbgPartial;
        private static bool _dbgFinal;
        private static bool _dbgDecision;
        private static bool _dbgDumpVocab;

        // 反射存取
        private static readonly AccessTools.FieldRef<VoiceControlListener, SpeechRecognizer> srRef =
            AccessTools.FieldRefAccess<VoiceControlListener, SpeechRecognizer>("sr");

        private static readonly AccessTools.FieldRef<VoiceControlListener, VoiceBroadcastTrigger> vbtRef =
            AccessTools.FieldRefAccess<VoiceControlListener, VoiceBroadcastTrigger>("vbt");

        private static readonly MethodInfo restartsrMethod =
            AccessTools.Method(typeof(VoiceControlListener), "restartsr");

        // ================== Awake：初始化 ==================
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AwakePostfix(VoiceControlListener __instance)
        {
            var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos.Values
                .FirstOrDefault(p => p.Metadata.GUID == "com.xofelttil.MageArenaChineseVoice");
            if (plugin == null) return;

            VoiceCommandConfig.Init(plugin.Instance.Config);

            // 時序
            _castCooldownSec = Mathf.Max(0f, VoiceCommandConfig.CastCooldownSec.Value);
            _startupWaitSec = Mathf.Max(0f, VoiceCommandConfig.StartupWaitSec.Value);
            _resetStopWaitSec = Mathf.Max(0f, VoiceCommandConfig.ResetStopWaitSec.Value);
            _restartWaitSec = Mathf.Max(0f, VoiceCommandConfig.RestartWaitSec.Value);
            _monitorIntervalSec = Mathf.Max(0.25f, VoiceCommandConfig.MonitorIntervalSec.Value);

            // Debug
            _dbgEnabled = VoiceCommandConfig.DebugEnabled.Value;
            _dbgPartial = VoiceCommandConfig.DebugLogPartial.Value;
            _dbgFinal = VoiceCommandConfig.DebugLogFinal.Value;
            _dbgDecision = VoiceCommandConfig.DebugLogDecision.Value;
            _dbgDumpVocab = VoiceCommandConfig.DebugDumpVocabulary.Value;

            if (_dbgEnabled && _log == null)
                _log = BepInEx.Logging.Logger.CreateLogSource("MageArenaChineseVoice");

            // 主要咒語映射
            commandMap = new Dictionary<string[], Action<VoiceControlListener>>()
            {
                { VoiceCommandConfig.GetTokens(VoiceCommandConfig.FireballExpanded),      v => v.CastFireball() },
                { VoiceCommandConfig.GetTokens(VoiceCommandConfig.FrostBoltExpanded),     v => v.CastFrostBolt() },
                { VoiceCommandConfig.GetTokens(VoiceCommandConfig.WormExpanded),          v => v.CastWorm() },
                { VoiceCommandConfig.GetTokens(VoiceCommandConfig.HoleExpanded),          v => v.CastHole() },
                { VoiceCommandConfig.GetTokens(VoiceCommandConfig.MagicMissileExpanded),  v => v.CastMagicMissle() },
                { VoiceCommandConfig.GetTokens(VoiceCommandConfig.MirrorExpanded),        v => v.ActivateMirror() }
            };

            // 追加咒語映射
            additionalCommandMap = new Dictionary<string, string[]>
            {
                { "rock",        VoiceCommandConfig.GetTokens(VoiceCommandConfig.RockExpanded) },
                { "wisp",        VoiceCommandConfig.GetTokens(VoiceCommandConfig.WispExpanded) },
                { "blast",       VoiceCommandConfig.GetTokens(VoiceCommandConfig.BlastExpanded) },
                { "divine",      VoiceCommandConfig.GetTokens(VoiceCommandConfig.DivineExpanded) },
                { "blink",       VoiceCommandConfig.GetTokens(VoiceCommandConfig.BlinkExpanded) },
                { "thunderbolt", VoiceCommandConfig.GetTokens(VoiceCommandConfig.ThunderboltExpanded) }
            };

            // 合併額外模組咒語（由 .cfg 提供）
            foreach (var kv in ParseExtraModuleBindings(VoiceCommandConfig.ModuleSpellBindings.Value))
            {
                if (additionalCommandMap.TryGetValue(kv.Key, out var existing))
                    additionalCommandMap[kv.Key] = existing.Concat(kv.Value).Distinct().ToArray();
                else
                    additionalCommandMap[kv.Key] = kv.Value;
            }

            // 正規化
            commandListNormalized = commandMap
                .Select(kv => (kv.Key.Select(NormalizeForMatch).ToArray(), kv.Value))
                .ToList();

            additionalMapNormalized = additionalCommandMap
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.Select(NormalizeForMatch).ToArray()
                );

            // 快速匹配器 & 反查
            _mainKw2Action = new Dictionary<string, Action<VoiceControlListener>>(StringComparer.Ordinal);
            foreach (var (keys, act) in commandListNormalized)
                foreach (var k in keys)
                    if (!string.IsNullOrEmpty(k)) _mainKw2Action[k] = act;

            _extraKw2SpellId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in additionalMapNormalized)
                foreach (var k in kv.Value)
                    if (!string.IsNullOrEmpty(k)) _extraKw2SpellId[k] = kv.Key;

            _mainMatcher = new FastKeywordMatcher(_mainKw2Action.Keys);
            _extraMatcher = new FastKeywordMatcher(_extraKw2SpellId.Keys);
        }

        // ================== 取代 waitgetplayer：擴充流程 ==================
        [HarmonyPatch("waitgetplayer")]
        [HarmonyPrefix]
        private static bool WaitGetPlayerPrefix(VoiceControlListener __instance, ref IEnumerator __result)
        {
            __result = ModifiedWaitGetPlayer(__instance);
            return false;
        }

        private static IEnumerator ModifiedWaitGetPlayer(VoiceControlListener instance)
        {
            while (instance.pi == null)
            {
                if (Camera.main && Camera.main.transform.parent != null &&
                    Camera.main.transform.parent.TryGetComponent<PlayerInventory>(out var playerInventory))
                    instance.pi = playerInventory;

                yield return null;
            }

            instance.GetComponent<SetUpModelProvider>().Setup();
            yield return null;

            srRef(instance) = instance.GetComponent<SpeechRecognizer>();
            instance.SpellPages = new List<ISpellCommand>();

            foreach (var mb in instance.gameObject.GetComponents<MonoBehaviour>())
                if (mb is ISpellCommand sc && sc != null)
                    instance.SpellPages.Add(sc);

            // SpellPages dump（確認實際 GetSpellName）
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[MageArenaChineseVoice] SpellPages dump:");
                foreach (var s in instance.SpellPages.Where(x => x != null))
                    sb.AppendLine($" • {s.GetSpellName()}  (type: {s.GetType().FullName})");
                if (_dbgEnabled) _log?.LogInfo(sb.ToString());
            }
            catch { /* ignore */ }

            var recognizer = srRef(instance);

            // 加入字彙（附加到原生 Vocabulary）
            AddSpellsToVocabulary(recognizer);

            if (_dbgEnabled && _dbgDumpVocab)
            {
                var dump = string.Join(" | ", recognizer.Vocabulary.Distinct().OrderBy(x => x));
                _log.LogInfo($"[Vocab Dump] Count={recognizer.Vocabulary.Count} :: {dump}");
            }

            // ===== Partial：模組先試（較快）；命中才吃，不命中不阻擋 =====
            recognizer.PartialResultReady.AddListener(p =>
            {
                var t = SafeGetText(p);
                if (_dbgEnabled && _dbgPartial && !string.IsNullOrWhiteSpace(t))
                    _log.LogInfo($"[Partial] {t}");

                if (!string.IsNullOrWhiteSpace(t))
                {
                    _lastMicActivityTs = Time.realtimeSinceStartup;   // ← 新增
                    TryMatchAndCast(instance, t, source: "partial");
                }
            });

            // ===== Final：原生與模組「同時偵測、先成功先贏」=====
            // 流程：先交給原生 tryresult -> 等到 EndOfFrame -> 模組補刀（若前者未在超短窗內成功）
            recognizer.ResultReady.AddListener(res =>
            {
                var text = SafeGetText(res);
                if (_dbgEnabled && _dbgFinal && !string.IsNullOrWhiteSpace(text))
                    _log.LogInfo($"[Final] {text}");

                if (!string.IsNullOrWhiteSpace(text))
                    _lastMicActivityTs = Time.realtimeSinceStartup;   // ← 新增

                // （依你版本：如果已移除原生，就只呼叫 TryMatchAndCast）
                TryMatchAndCast(instance, text, source: "final");
            });

            yield return new WaitForSeconds(_startupWaitSec);
            recognizer.StartProcessing();

            // 監控麥克風狀態，必要時安全重啟
            while (instance && instance.isActiveAndEnabled)
            {
                yield return new WaitForSeconds(_monitorIntervalSec);

                var sr  = srRef(instance);
                var vbt = vbtRef(instance);

                // 若 VBT 正在傳輸，也視為「有活動」
                if (vbt != null && vbt.IsTransmitting)
                    _lastMicActivityTs = Time.realtimeSinceStartup;

                // 只有當：
                //  1) SR 正在運行（Processing）
                //  2) 且「長時間沒有任何活動」（partial/final 也沒更新）
                //  3) 且與上次重啟已間隔一段時間
                // 才認定為 idle 卡死，進行重啟
                bool srBusy = (sr != null && sr.State == SpeechProcessorState.Processing);
                float idleFor = Time.realtimeSinceStartup - _lastMicActivityTs;
                bool debounceOk = (Time.realtimeSinceStartup - _lastRestartTs) >= RESTART_DEBOUNCE_SEC;

                if (srBusy && idleFor >= MIC_IDLE_TIMEOUT_SEC && debounceOk)
                {
                    if (_dbgEnabled && _dbgDecision)
                        _log.LogWarning($"[Monitor] IdleFor={idleFor:0.00}s (no partial/final). Restart recognizer.");

                    _lastRestartTs = Time.realtimeSinceStartup;

                    sr.StopProcessing();
                    instance.StartCoroutine((IEnumerator)restartsrMethod.Invoke(instance, null));
                }
            }
        }

        private static IEnumerator FinalRace(VoiceControlListener instance, string text)
        {
            // 先交給「原生」：有命中就會自己施放
            instance.tryresult(text);

            // 等到幀尾，給原生邏輯一點時間
            yield return new WaitForEndOfFrame();

            // 若在短窗內已有一方成功，放棄（先贏者鎖）
            if (Time.realtimeSinceStartup - _lastWinnerTs <= WINNER_LATCH_WINDOW)
            {
                if (_dbgEnabled && _dbgDecision)
                    _log.LogDebug("[Race] Native likely won; skip module.");
                yield break;
            }

            // 模組補刀（若還沒人成功）
            TryMatchAndCast(instance, text, source: "final-race");
        }

        private static void AddSpellsToVocabulary(SpeechRecognizer recognizer)
        {
            if (recognizer.Vocabulary == null)
                recognizer.Vocabulary = new List<string>();

            var seen = new HashSet<string>(recognizer.Vocabulary, StringComparer.OrdinalIgnoreCase);

            void add(string term)
            {
                if (string.IsNullOrWhiteSpace(term)) return;
                var t = term.Trim();
                if (seen.Add(t)) recognizer.Vocabulary.Add(t);
            }

            foreach (var pair in commandMap)
                foreach (var w in pair.Key) add(w);

            foreach (var pair in additionalCommandMap)
                foreach (var w in pair.Value) add(w);
        }

        // ============= 關鍵：不再攔截 tryresult，讓原生可「同步嘗試」 =============
        // （移除你先前的 TryResultPrefix；若有保留，請刪除或註解）
        // [HarmonyPatch("tryresult")] ...  <-- 不要再掛 Prefix 攔截

        // ================== 核心：快速匹配 + 去抖 + 勝出鎖 ==================
        private static bool TryMatchAndCast(VoiceControlListener instance, string raw, string source = "")
        {
            // 若剛剛另一方成功（贏者鎖），放棄
            if (Time.realtimeSinceStartup - _lastWinnerTs <= WINNER_LATCH_WINDOW)
                return false;

            // 冷卻（避免連發）
            if (Time.realtimeSinceStartup - _lastCastTs < _castCooldownSec)
                return false;

            var textNorm = NormalizeForMatch(raw);
            if (string.IsNullOrEmpty(textNorm))
                return false;

            // 一次掃描命中
            var mainHits = _mainMatcher?.MatchAll(textNorm);
            var extraHits = _extraMatcher?.MatchAll(textNorm);

            if ((mainHits == null || mainHits.Count == 0) && (extraHits == null || extraHits.Count == 0))
            {
                if (_dbgEnabled && _dbgDecision)
                    _log.LogDebug($"[Miss:{source}] \"{raw}\" => \"{textNorm}\"");
                return false;
            }

            // 決策：最長關鍵詞 > 優先度（extra）> 出現最靠後
            string chosenType = null; // "main" or "extra"
            string chosenKw = null;
            int chosenStart = -1, chosenEnd = -1;

            void Consider(string type, string kw, int s, int e, int priority = int.MaxValue)
            {
                if (chosenKw == null)
                {
                    chosenType = type; chosenKw = kw; chosenStart = s; chosenEnd = e;
                    return;
                }

                int curLen = chosenEnd - chosenStart + 1;
                int newLen = e - s + 1;

                if (newLen > curLen) { chosenType = type; chosenKw = kw; chosenStart = s; chosenEnd = e; return; }

                if (type == "extra" && chosenType == "extra")
                {
                    var oldPri = _spellPriority.TryGetValue(_extraKw2SpellId[chosenKw], out var p1) ? p1 : int.MaxValue;
                    var newPri = priority;
                    if (newPri < oldPri) { chosenType = type; chosenKw = kw; chosenStart = s; chosenEnd = e; return; }
                    if (newPri > oldPri) return;
                }

                if (e > chosenEnd) { chosenType = type; chosenKw = kw; chosenStart = s; chosenEnd = e; }
            }

            if (mainHits != null)
                foreach (var (kw, s, e) in mainHits)
                    Consider("main", kw, s, e);

            if (extraHits != null)
                foreach (var (kw, s, e) in extraHits)
                {
                    var sid = _extraKw2SpellId[kw];
                    var pri = _spellPriority.TryGetValue(sid, out var p) ? p : int.MaxValue;
                    Consider("extra", kw, s, e, pri);
                }

            // Partial 去抖：只允許「結尾命中」或「短時間連續同關鍵詞」
            bool isPartial = source.StartsWith("partial", StringComparison.OrdinalIgnoreCase);
            if (isPartial)
            {
                bool endsWithChosen = chosenEnd == textNorm.Length - 1;
                bool rapidRepeat = (chosenKw == _lastHitKw) &&
                                   (Time.realtimeSinceStartup - _lastHitTs <= PARTIAL_REPEAT_WINDOW);
                if (!endsWithChosen && !rapidRepeat)
                {
                    if (_dbgEnabled && _dbgDecision)
                        _log.LogDebug($"[Partial-skip] \"{raw}\" kw={chosenKw} end={chosenEnd} last={_lastHitKw}");
                    _lastHitKw = chosenKw;
                    _lastHitTs = Time.realtimeSinceStartup;
                    return false;
                }
            }

            // 若在執行前，贏者鎖已被另一方搶下（幀間競速），則放棄
            if (Time.realtimeSinceStartup - _lastWinnerTs <= WINNER_LATCH_WINDOW)
                return false;

            // 執行施放（並搶下贏者鎖）
            _lastWinnerTs = Time.realtimeSinceStartup;

            if (chosenType == "main")
            {
                if (_dbgEnabled && _dbgDecision)
                    _log.LogInfo($"[Hit:{source}] main -> {chosenKw} :: \"{raw}\"");

                if (_mainKw2Action.TryGetValue(chosenKw, out var act))
                    act(instance);
                else
                    return false;
            }
            else // extra
            {
                var spellId = _extraKw2SpellId[chosenKw];      // "rock"/"blink"/...
                var keyNorm = NormalizeId(spellId);

                var spell = instance.SpellPages.FirstOrDefault(s =>
                {
                    if (s == null) return false;
                    var norm = NormalizeId(s.GetSpellName());
                    return norm == keyNorm;
                });

                if (spell == null)
                {
                    if (_dbgEnabled && _dbgDecision)
                        _log.LogWarning($"[Extra] Spell page not found for key='{spellId}'. Check dump.");
                    return false;
                }

                if (_dbgEnabled && _dbgDecision)
                    _log.LogInfo($"[Hit:{source}] extra:{spellId} -> {chosenKw} :: \"{raw}\"");

                spell.TryCastSpell();
            }

            // 命中後：只記錄時間，不立即 Stop/Restart SR（維持低延遲）
            _lastCastTs = Time.realtimeSinceStartup;
            _lastHitKw = chosenKw;
            _lastHitTs = _lastCastTs;
            return true;
        }

        // ================== Reset/Restart：保留你的穩定流程 ==================
        [HarmonyPatch("resetmiclong")]
        [HarmonyPrefix]
        private static bool ResetMicLongPrefix(VoiceControlListener __instance, ref IEnumerator __result)
        {
            __result = ModifiedResetMicLong(__instance);
            return false;
        }

        private static IEnumerator ModifiedResetMicLong(VoiceControlListener instance)
        {
            var recognizer = srRef(instance);
            recognizer.StopProcessing();
            yield return new WaitForSeconds(VoiceCommandConfig.ResetStopWaitSec.Value);

            UnityEngine.Object.Destroy(recognizer);
            srRef(instance) = instance.gameObject.AddComponent<SpeechRecognizer>();
            var recognizerNew = srRef(instance);

            recognizerNew.LanguageModelProvider = instance.GetComponent<StreamingAssetsLanguageModelProvider>();
            recognizerNew.SpeechSource = instance.GetComponent<DissonanceSpeechSource>();

            if (recognizerNew.Vocabulary == null)
                recognizerNew.Vocabulary = new List<string>();

            // 重建 SpellPages
            instance.SpellPages = new List<ISpellCommand>();
            foreach (var mb in instance.gameObject.GetComponents<MonoBehaviour>())
                if (mb is ISpellCommand sc && sc != null)
                    instance.SpellPages.Add(sc);

            // 重新加入字彙與監聽
            AddSpellsToVocabulary(recognizerNew);

            recognizerNew.PartialResultReady.AddListener(p =>
            {
                var t = SafeGetText(p);
                if (_dbgEnabled && _dbgPartial && !string.IsNullOrWhiteSpace(t))
                    _log.LogInfo($"[Partial:reset] {t}");

                if (!string.IsNullOrWhiteSpace(t))
                    TryMatchAndCast(instance, t, source: "partial-reset");
            });

            recognizerNew.ResultReady.AddListener(r =>
            {
                var text = SafeGetText(r);
                if (_dbgEnabled && _dbgFinal && !string.IsNullOrWhiteSpace(text))
                    _log.LogInfo($"[Final:reset] {text}");

                // Reset 後也走競速流程
                instance.StartCoroutine(FinalRace(instance, text));
            });

            yield return new WaitForSeconds(VoiceCommandConfig.RestartWaitSec.Value);
            recognizerNew.StartProcessing();
        }

        [HarmonyPatch("restartsr")]
        [HarmonyPrefix]
        private static bool RestartSrPrefix(VoiceControlListener __instance, ref IEnumerator __result)
        {
            __result = SafeRestartSr(__instance);
            return false;
        }

        private static IEnumerator SafeRestartSr(VoiceControlListener instance)
        {
            var recognizer = srRef(instance);
            if (recognizer == null)
                yield break;

            while (recognizer.State != SpeechProcessorState.Inactive)
                yield return null;

            recognizer.StartProcessing();
        }

        // ================== 反射安全取文字 ==================
        private static string SafeGetText<T>(T obj)
        {
            if (obj == null) return string.Empty;
            var t = obj.GetType();

            var prop = t.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance)
                    ?? t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
                return prop.GetValue(obj)?.ToString() ?? string.Empty;

            var field = t.GetField("Text", BindingFlags.Public | BindingFlags.Instance)
                     ?? t.GetField("text", BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(obj)?.ToString() ?? string.Empty;

            return obj.ToString();
        }

        // === 正規化：去空白/標點，小寫（不做簡繁） ===
        private static string NormalizeForMatch(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (char.IsWhiteSpace(ch)) continue;
                if ((ch >= 0x4E00 && ch <= 0x9FFF) || char.IsLetterOrDigit(ch))
                    sb.Append(ch);
            }
            return sb.ToString().ToLowerInvariant();
        }

        // SpellName/ID 正規化：小寫、去空白/底線/破折號
        private static string NormalizeId(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s.Trim().ToLowerInvariant())
            {
                if (char.IsWhiteSpace(ch)) continue;
                if (ch == '_' || ch == '-') continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // 解析額外模組綁定："rock=巨石 石頭|blackrain=黑雨 黑色風暴|summonimp=小惡魔 召喚小鬼"
        private static Dictionary<string, string[]> ParseExtraModuleBindings(string raw)
        {
            var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return dict;

            var entries = raw.Split(new[] { '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var pair = entry.Split(new[] { '=', ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (pair.Length != 2) continue;

                var key = pair[0].Trim();
                var kws = pair[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim())
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray();
                if (key.Length == 0 || kws.Length == 0) continue;

                dict[key] = kws;
            }
            return dict;
        }

        // 追蹤最後一次偵測到語音活動的時間（partial/final 有字、或 VBT 傳輸）
        private static float _lastMicActivityTs;
        // restart 節流（避免連續重啟）
        private static float _lastRestartTs;
        private const float  MIC_IDLE_TIMEOUT_SEC = 5.0f;   // 超過這秒數都沒活動才重啟（可調）
        private const float  RESTART_DEBOUNCE_SEC = 2.0f;   // 重啟之間至少間隔（可調）
    }
}
