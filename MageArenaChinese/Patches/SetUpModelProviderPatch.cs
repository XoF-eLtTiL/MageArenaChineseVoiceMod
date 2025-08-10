// Patches/SetUpModelProviderPatch.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using MageArenaChineseVoice.Config;
using Recognissimo.Components;
using UnityEngine;

namespace MageArenaChineseVoice.Patches
{
    [HarmonyPatch(typeof(SetUpModelProvider), "Setup")]
    public static class SetUpModelProviderPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(SetUpModelProvider __instance)
        {
            // 由插件 DLL 所在位置推導模型目錄
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // 允許使用絕對路徑；否則相對於插件資料夾
            string configured = VoiceCommandConfig.ModelRelativePath?.Value ?? string.Empty;
            string modelPath = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(pluginDir ?? string.Empty, configured);

            // 建立並配置 Provider（用插件路徑，不走 StreamingAssets）
            var provider = __instance.gameObject.AddComponent<StreamingAssetsLanguageModelProvider>();
            provider.language = VoiceCommandConfig.ModelLanguage?.Value ?? SystemLanguage.Chinese;
            provider.languageModels = new List<StreamingAssetsLanguageModel>
            {
                new StreamingAssetsLanguageModel
                {
                    language = provider.language,
                    path = modelPath
                }
            };

            // 套用到辨識器
            var speechRecognizer = __instance.GetComponent<SpeechRecognizer>();
            speechRecognizer.LanguageModelProvider = provider;

            return false; // 攔截原始 Setup
        }
    }
}
