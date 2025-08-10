using System;
using BepInEx;
using UnityEngine;
using HarmonyLib;
using Recognissimo.Components;
using BepInEx.Logging;

namespace MageArenaChineseVoiceMod
{
    /// <summary>
    /// Mage Arena 中文語音指令 Mod
    /// - 載入後：自動將常用中文詞彙加入辨識詞庫
    /// - 攔截 tryresult：以中文關鍵詞優先觸發各法術
    /// </summary>
    [BepInPlugin("chinese.mage.arena", "Chinese Mod", "1.0.0")]
    public class MageArenaChineseVoiceMod : BaseUnityPlugin
    {
        internal static MageArenaChineseVoiceMod Instance { get; private set; }
        internal static ManualLogSource Log => Instance._log;

        private static Harmony _harmony;
        private ManualLogSource _log;

        private void Awake()
        {
            _log = base.Logger;
            Instance = this;

            _harmony = new Harmony("Chinese.mage.arena");
            _harmony.PatchAll();

            Log.LogInfo("中文語音指令模組已載入");
        }

        // =========================
        // Harmony Patches 本體
        // =========================
        [HarmonyPatch(typeof(VoiceControlListener))]
        private static class VoiceControlListenerPatches
        {
            /// <summary>
            /// OnStartClient 後，嘗試把中文關鍵詞加入到 SpeechRecognizer 的詞庫中
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch("OnStartClient")]
            private static void OnStartClient_Postfix(VoiceControlListener __instance)
            {
                try
                {
                    var sr = __instance.GetComponent<SpeechRecognizer>();
                    if (sr == null)
                    {
                        Log.LogWarning("找不到 SpeechRecognizer 元件，無法加入中文詞彙。");
                        return;
                    }

                    // 建議：先停止 → 加詞 → 再啟動，以避免個別版本在處理中改詞庫會失敗
                    sr.StopProcessing();

                    // —— 常用中文口令（同時附上常見同義字） ——
                    AddIfMissing(sr, "鏡子");        // mirror
                    AddIfMissing(sr, "火球術");      // fireball
                    AddIfMissing(sr, "火球");
                    AddIfMissing(sr, "冰箭");        // freeze
                    AddIfMissing(sr, "凍結");
                    AddIfMissing(sr, "入口");        // worm（入口）
                    AddIfMissing(sr, "出口");        // hole（出口）
                    AddIfMissing(sr, "魔法飛彈");    // magic missile
                    AddIfMissing(sr, "魔法");
                    AddIfMissing(sr, "飛彈");

                    // 也可加上英文備援（可留可刪，看你需求）
                    AddIfMissing(sr, "mirror");
                    AddIfMissing(sr, "fireball");
                    AddIfMissing(sr, "freeze");
                    AddIfMissing(sr, "worm");
                    AddIfMissing(sr, "hole");
                    AddIfMissing(sr, "magic");
                    AddIfMissing(sr, "missile");
                    AddIfMissing(sr, "missle"); // 原作常見拼法

                    sr.StartProcessing();
                    Log.LogInfo("已將中文口令加入語音詞庫。");
                }
                catch (Exception e)
                {
                    Log.LogError($"加入中文詞彙時發生錯誤：{e}");
                }
            }

            /// <summary>
            /// 攔截 tryresult：中文關鍵詞優先，比對到就直接施法並中止原邏輯
            /// </summary>
            [HarmonyPrefix]
            [HarmonyPatch("tryresult")]
            private static bool TryResult_Prefix(VoiceControlListener __instance, string res)
            {
                if (string.IsNullOrWhiteSpace(res))
                {
                    // 沒有內容就讓原本行為處理（回傳 true 呼叫原方法）
                    return true;
                }

                // 為降低誤判，先做簡單正規化
                var input = res.Trim();

                // —— 直接施放型（以中文為主，保留英文備援） ——
                if (ContainsAny(input, "火球術", "火球", "fireball", "fire", "ball", "火", "球"))
                {
                    __instance.CastFireball();
                    return false;
                }

                if (ContainsAny(input, "冰箭", "凍結", "freeze", "冰", "箭"))
                {
                    __instance.CastFrostBolt();
                    return false;
                }

                if (ContainsAny(input, "入口", "worm"))
                {
                    __instance.CastWorm();
                    return false;
                }

                if (ContainsAny(input, "出口", "hole"))
                {
                    __instance.CastHole();
                    return false;
                }

                if (ContainsAny(input, "魔法飛彈", "魔法", "飛彈", "magic missile", "magic", "missile", "missle"))
                {
                    __instance.CastMagicMissle();
                    return false;
                }

                if (ContainsAny(input, "鏡子", "mirror"))
                {
                    __instance.ActivateMirror();
                    return false;
                }

                // —— 若上面沒命中，放行給原邏輯（例如其它語言、或遊戲原生的英文關鍵詞） ——
                return true;
            }

            // 工具：把詞加進 Vocabulary（避免重複加入）
            private static void AddIfMissing(SpeechRecognizer sr, string term)
            {
                if (sr?.Vocabulary == null || string.IsNullOrEmpty(term)) return;
                if (!sr.Vocabulary.Contains(term)) sr.Vocabulary.Add(term);
            }

            // 工具：大小寫不敏感 ContainsAny
            private static bool ContainsAny(string src, params string[] keys)
            {
                foreach (var k in keys)
                {
                    if (string.IsNullOrEmpty(k)) continue;
                    if (src.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }
        }
    }
}
