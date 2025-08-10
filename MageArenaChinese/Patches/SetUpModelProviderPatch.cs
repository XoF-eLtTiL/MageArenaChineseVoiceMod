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
            // �Ѵ��� DLL �Ҧb��m���ɼҫ��ؿ�
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // ���\�ϥε�����|�F�_�h�۹�󴡥��Ƨ�
            string configured = VoiceCommandConfig.ModelRelativePath?.Value ?? string.Empty;
            string modelPath = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(pluginDir ?? string.Empty, configured);

            // �إߨðt�m Provider�]�δ�����|�A���� StreamingAssets�^
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

            // �M�Ψ���Ѿ�
            var speechRecognizer = __instance.GetComponent<SpeechRecognizer>();
            speechRecognizer.LanguageModelProvider = provider;

            return false; // �d�I��l Setup
        }
    }
}
