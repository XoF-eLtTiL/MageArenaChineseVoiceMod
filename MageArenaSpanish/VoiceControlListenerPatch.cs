using HarmonyLib;
using Recognissimo;
using Recognissimo.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MageArenaChinese.Patches.Voice
{
    /// <summary>
    /// 對 VoiceControlListener 的中文語音擴充 Patch
    /// </summary>
    [HarmonyPatch(typeof(VoiceControlListener))]
    internal class VoiceControlListenerPatch
    {
        // —— OnStartClient：啟動後等待 PlayerInventory 準備好，再追加中文詞彙 ——
        [HarmonyPatch("OnStartClient")]
        [HarmonyPrefix]
        private static void OnStartClient_Prefix(VoiceControlListener __instance)
        {
            __instance.StartCoroutine(CoWaitGetPlayer(__instance));
        }

        /// <summary>
        /// 等到 PlayerInventory 可用，再更新語音詞彙
        /// </summary>
        private static IEnumerator CoWaitGetPlayer(VoiceControlListener __instance)
        {
            // 最多等 5 秒，避免無限等待
            var timeout = Time.time + 5f;

            while (__instance.pi == null && Time.time < timeout)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    var parent = cam.transform?.parent;
                    if (parent != null && parent.TryGetComponent<PlayerInventory>(out var inv))
                    {
                        __instance.pi = inv;
                        break;
                    }
                }
                yield return null;
            }

            // 再等半秒讓組件穩定
            yield return null;
            yield return new WaitForSeconds(0.5f);

            AddToVocabularySafe(__instance);
        }

        /// <summary>
        /// 安全地更新辨識詞彙（先停止 → 加詞 → 重啟）
        /// </summary>
        private static void AddToVocabularySafe(VoiceControlListener __instance)
        {
            MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogInfo("正在載入中文語音詞彙…");

            var srField = AccessTools.Field(typeof(VoiceControlListener), "sr");
            var sr = srField.GetValue(__instance) as SpeechRecognizer;
            if (sr == null)
            {
                MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogWarning("無法取得 SpeechRecognizer（sr）。");
                return;
            }

            try
            {
                // 保險作法：先停，更新詞彙後再重啟
                sr.StopProcessing();

                // —— 基礎中文口令（同時保留英文對應，提升容錯） ——
                AddIfMissing(sr.Vocabulary, "鏡子");       // mirror
                AddIfMissing(sr.Vocabulary, "火球術");     // fireball
                AddIfMissing(sr.Vocabulary, "冰箭");       // freeze
                AddIfMissing(sr.Vocabulary, "入口");       // worm（入口）
                AddIfMissing(sr.Vocabulary, "出口");       // hole（出口）
                AddIfMissing(sr.Vocabulary, "魔法飛彈");   // magic missile

                // 英文備援
                AddIfMissing(sr.Vocabulary, "mirror");
                AddIfMissing(sr.Vocabulary, "fire");
                AddIfMissing(sr.Vocabulary, "ball");
                AddIfMissing(sr.Vocabulary, "freeze");
                AddIfMissing(sr.Vocabulary, "worm");
                AddIfMissing(sr.Vocabulary, "hole");
                AddIfMissing(sr.Vocabulary, "magic");
                AddIfMissing(sr.Vocabulary, "missle");    // 原作拼法
                AddIfMissing(sr.Vocabulary, "missile");   // 正確拼法也收一下

                // 西文/發音變體（可選）
                foreach (var term in _phoneticMap.Keys)
                    AddIfMissing(sr.Vocabulary, term);

                MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogInfo("中文語音詞彙已更新。");

                // 依原始類別是否提供 restartsr() 來決定重啟方式
                var m = AccessTools.Method(typeof(VoiceControlListener), "restartsr");
                if (m != null)
                    __instance.StartCoroutine((IEnumerator)m.Invoke(__instance, null));
                else
                    sr.StartProcessing();
            }
            catch (Exception e)
            {
                MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogError($"更新語音詞彙時發生例外：{e}");
            }
        }

        private static void AddIfMissing(ICollection<string> vocab, string term)
        {
            if (!vocab.Contains(term)) vocab.Add(term);
        }

        // —— tryresult：核心辨識邏輯，改成中文優先，仍保留英文詞 —— //
        [HarmonyPatch("tryresult")]
        [HarmonyPrefix]
        private static void TryResult_LogPrefix(string res)
        {
            if (!string.IsNullOrEmpty(res))
                MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogInfo($"[語音辨識] {res}");
        }

        [HarmonyPatch("tryresult")]
        [HarmonyPrefix]
        private static bool TryResult_Prefix(VoiceControlListener __instance, string res)
        {
            if (!string.IsNullOrWhiteSpace(res))
            {
                var input = res.Trim();

                // —— 直接施放型：以中文為主、英文為輔 —— //
                if (ContainsAny(input, "火球術", "火球", "fireball", "fire", "ball", "火", "球"))
                {
                    __instance.CastFireball();
                    return false;
                }
                else if (ContainsAny(input, "冰箭", "凍結", "freeze", "冰", "箭"))
                {
                    __instance.CastFrostBolt();
                    return false;
                }
                else if (ContainsAny(input, "入口", "worm"))
                {
                    __instance.CastWorm();
                    return false;
                }
                else if (ContainsAny(input, "出口", "hole"))
                {
                    __instance.CastHole();
                    return false;
                }
                else if (ContainsAny(input, "魔法飛彈", "魔法", "飛彈", "magic missile", "magic", "missle", "missile"))
                {
                    __instance.CastMagicMissle();
                    return false;
                }
                else if (ContainsAny(input, "鏡子", "mirror"))
                {
                    __instance.ActivateMirror();
                    return false;
                }

                // —— SpellPages 內的咒語（以顯示名稱比對） —— //
                if (ContainsAny(input, "閃現", "blink"))
                {
                    foreach (var page in __instance.SpellPages)
                    {
                        if (EqualsIgnoreCase(page.GetSpellName(), "閃現") ||
                            EqualsIgnoreCase(page.GetSpellName(), "blink"))
                        {
                            page.TryCastSpell();
                            return false;
                        }
                    }
                }

                if (ContainsAny(input, "黑暗", "dark"))
                {
                    foreach (var page in __instance.SpellPages)
                    {
                        if (EqualsIgnoreCase(page.GetSpellName(), "爆破") ||
                            EqualsIgnoreCase(page.GetSpellName(), "blast"))
                        {
                            page.TryCastSpell();
                            return false;
                        }
                    }
                }

                // 其他情況：若輸入剛好包含某個 SpellPages 的名稱就嘗試施放
                foreach (var page in __instance.SpellPages)
                {
                    var spellName = page.GetSpellName();
                    if (string.IsNullOrEmpty(spellName)) continue;

                    // 允許中英文名稱
                    if (input.IndexOf(spellName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        page.TryCastSpell();
                        return false;
                    }
                }

                // 已處理所有分支，不執行原方法
                return false;
            }

            // 若沒有輸入，沿用原本行為：停止 → 重啟
            var srField = AccessTools.Field(typeof(VoiceControlListener), "sr");
            var sr = srField.GetValue(__instance) as SpeechRecognizer;
            if (sr != null)
            {
                sr.StopProcessing();
                __instance.StartCoroutine((IEnumerator)AccessTools.Method(typeof(VoiceControlListener), "restartsr")
                    .Invoke(__instance, null));
            }
            return false;
        }

        private static bool ContainsAny(string src, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                if (src.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool EqualsIgnoreCase(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // —— 模型路徑攔截：改中文日誌，維持原本導向（如需改中文模型，請把路徑換成你的中文模型資料夾） —— //
        [HarmonyPatch(typeof(LanguageModel), MethodType.Constructor, new Type[] { typeof(string) })]
        [HarmonyPrefix]
        public static void LanguageModel_Ctor_Prefix(ref string path)
        {
            MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogWarning($"正在載入語音模型：{path}");
            string myPluginPath = MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Instance.Info.Location;
            string modDir = Path.GetDirectoryName(myPluginPath);

            // 目前仍導向英語模型，若你已放置中文模型，請改這行：
             string modPath = Path.Combine(modDir, "LanguageModels", "vosk-model-small-cn-0.22");
            //string modPath = Path.Combine(modDir, "LanguageModels", "vosk-model-small-en-us-0.15");

            path = modPath;
            MageArenaChineseVoiceMod.MageArenaChineseVoiceMod.Log.LogWarning($"模型路徑已改為：{path}");
        }

        // —— 發音/變體對應（可保留／可刪除） —— //
        private static readonly Dictionary<string, string> _phoneticMap = new Dictionary<string, string>
        {
            // 西語／誤聽對應 → 英文基詞
            { "fuegos", "fuego" },
            { "juego",  "fuego" },
            { "fuego",  "fuego" },
            { "fiar",   "fire"  },
            { "fayer",  "fire"  },
            { "bola",   "ball"  },
            { "bolas",  "ball"  },
            { "frees",  "freeze"},
            { "ease",   "freeze"},
            { "frieze", "freeze"},
            { "friz",   "freeze"},
            { "freese", "freeze"},
            { "entrada","worm"  },
            { "salida", "hole"  },
        };
    }
}
