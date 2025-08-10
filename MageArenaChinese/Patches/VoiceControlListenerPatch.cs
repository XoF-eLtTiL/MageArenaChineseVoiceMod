// Patches/VoiceControlListenerPatch.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
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
        // 指令映射（主要咒語）
        private static Dictionary<string[], Action<VoiceControlListener>> commandMap;
        // 追加咒語映射：SpellPages 名稱 -> 關鍵詞陣列
        private static Dictionary<string, string[]> additionalCommandMap;

        private static readonly AccessTools.FieldRef<VoiceControlListener, SpeechRecognizer> srRef =
            AccessTools.FieldRefAccess<VoiceControlListener, SpeechRecognizer>("sr");

        private static readonly AccessTools.FieldRef<VoiceControlListener, VoiceBroadcastTrigger> vbtRef =
            AccessTools.FieldRefAccess<VoiceControlListener, VoiceBroadcastTrigger>("vbt");

        private static readonly MethodInfo restartsrMethod =
            AccessTools.Method(typeof(VoiceControlListener), "restartsr");

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AwakePostfix(VoiceControlListener __instance)
        {
            // 讀取本插件的 Config（請確認 GUID 與你的 BepInPlugin 一致）
            var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos.Values
                .FirstOrDefault(p => p.Metadata.GUID == "com.xofelttil.MageArenaChineseVoice");
            if (plugin != null)
            {
                VoiceCommandConfig.Init(plugin.Instance.Config);

                commandMap = new Dictionary<string[], Action<VoiceControlListener>>()
                {
                    { VoiceCommandConfig.FireballCommand.Value.Split(' '), v => v.CastFireball() },
                    { VoiceCommandConfig.FrostBoltCommand.Value.Split(' '), v => v.CastFrostBolt() },
                    { VoiceCommandConfig.WormCommand.Value.Split(' '), v => v.CastWorm() },
                    { VoiceCommandConfig.HoleCommand.Value.Split(' '), v => v.CastHole() },
                    { VoiceCommandConfig.MagicMissileCommand.Value.Split(' '), v => v.CastMagicMissle() },
                    { VoiceCommandConfig.MirrorCommand.Value.Split(' '), v => v.ActivateMirror() }
                };

                additionalCommandMap = new Dictionary<string, string[]>
                {
                    { "rock",        VoiceCommandConfig.RockCommand.Value.Split(' ') },
                    { "wisp",        VoiceCommandConfig.WispCommand.Value.Split(' ') },
                    { "blast",       VoiceCommandConfig.BlastCommand.Value.Split(' ') },
                    { "divine",      VoiceCommandConfig.DivineCommand.Value.Split(' ') },
                    { "blink",       VoiceCommandConfig.BlinkCommand.Value.Split(' ') },
                    { "thunderbolt", VoiceCommandConfig.ThunderboltCommand.Value.Split(' ') }
                };
            }
        }

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
                if (Camera.main.transform.parent != null &&
                    Camera.main.transform.parent.TryGetComponent<PlayerInventory>(out var playerInventory))
                {
                    instance.pi = playerInventory;
                }
                yield return null;
            }

            instance.GetComponent<SetUpModelProvider>().Setup();
            yield return null;

            srRef(instance) = instance.GetComponent<SpeechRecognizer>();
            instance.SpellPages = new List<ISpellCommand>();

            // 收集所有 ISpellCommand
            foreach (var mb in instance.gameObject.GetComponents<MonoBehaviour>())
                if (mb is ISpellCommand sc && sc != null)
                    instance.SpellPages.Add(sc);

            var recognizer = srRef(instance);

            // 加入字彙
            AddSpellsToVocabulary(recognizer);

            // 綁定結果回呼
            recognizer.ResultReady.AddListener(res => instance.tryresult(res.text));

            yield return new WaitForSeconds(1f);
            instance.GetComponent<SpeechRecognizer>().StartProcessing();

            // 監控麥克風狀態，必要時安全重啟
            while (instance && instance.isActiveAndEnabled)
            {
                yield return new WaitForSeconds(30f);
                var vbt = vbtRef(instance);
                if (!vbt.IsTransmitting)
                {
                    if (recognizer != null && recognizer.State != SpeechProcessorState.Inactive)
                    {
                        recognizer.StopProcessing();
                        instance.StartCoroutine((IEnumerator)restartsrMethod.Invoke(instance, null));
                    }
                }
            }
        }

        private static void AddSpellsToVocabulary(SpeechRecognizer recognizer)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        [HarmonyPatch("tryresult")]
        [HarmonyPrefix]
        private static bool TryResultPrefix(VoiceControlListener __instance, string res)
        {
            if (string.IsNullOrWhiteSpace(res))
                return false;

            var text = res.Trim().ToLowerInvariant();

            // 主要咒語：命中即觸發並重啟辨識器
            foreach (var command in commandMap)
            {
                if (command.Key.Any(k => !string.IsNullOrWhiteSpace(k) && text.Contains(k.ToLowerInvariant())))
                {
                    command.Value(__instance);
                    srRef(__instance).StopProcessing();
                    __instance.StartCoroutine((IEnumerator)restartsrMethod.Invoke(__instance, null));
                    return false;
                }
            }

            // 追加咒語頁：命中即施放並重啟辨識器
            foreach (var pair in additionalCommandMap)
            {
                if (pair.Value.Any(k => !string.IsNullOrWhiteSpace(k) && text.Contains(k.ToLowerInvariant())))
                {
                    var spell = __instance.SpellPages.FirstOrDefault(s =>
                        s != null && s.GetSpellName() == pair.Key);

                    spell?.TryCastSpell();
                    srRef(__instance).StopProcessing();
                    __instance.StartCoroutine((IEnumerator)restartsrMethod.Invoke(__instance, null));
                    return false;
                }
            }

            // 未命中：維持原行為，重啟辨識器
            srRef(__instance).StopProcessing();
            __instance.StartCoroutine((IEnumerator)restartsrMethod.Invoke(__instance, null));
            return false;
        }

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
            yield return new WaitForSeconds(0.5f);

            UnityEngine.Object.Destroy(recognizer);
            srRef(instance) = instance.gameObject.AddComponent<SpeechRecognizer>();
            var recognizerNew = srRef(instance);

            // 重新指向同一個 Provider 與音源
            recognizerNew.LanguageModelProvider = instance.GetComponent<StreamingAssetsLanguageModelProvider>();
            recognizerNew.SpeechSource = instance.GetComponent<DissonanceSpeechSource>();
            recognizerNew.Vocabulary = new List<string>();

            // 重建 SpellPages
            instance.SpellPages = new List<ISpellCommand>();
            foreach (var mb in instance.gameObject.GetComponents<MonoBehaviour>())
                if (mb is ISpellCommand sc && sc != null)
                    instance.SpellPages.Add(sc);

            // 重新加入字彙與監聽
            AddSpellsToVocabulary(recognizerNew);
            recognizerNew.ResultReady.AddListener(res => instance.tryresult(res.text));

            yield return new WaitForSeconds(0.1f);
            recognizerNew.StartProcessing();
        }

        // 安全重啟：等待 Inactive 再啟動
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
    }
}
