// Patches/SetUpModelProviderPatch.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MageArenaChineseVoice.Config;
using Recognissimo.Components;
using UnityEngine;

namespace MageArenaChineseVoice.Patches
{
    [HarmonyPatch(typeof(SetUpModelProvider), "Setup")]
    public static class SetUpModelProviderPatch
    {
        private static ManualLogSource _log;

        // �ڭ̦ۤv�إ�/�޲z�� Provider �|�a�o�ӼаO�A�קK���ƻP��K����
        private const string ProviderTag = "MA-CN-ExternalModelProvider";

        [HarmonyPrefix]
        public static bool Prefix(SetUpModelProvider __instance)
        {
            _log ??= BepInEx.Logging.Logger.CreateLogSource("MA-CN-SetupProvider");

            try
            {
                // === 1) Ū���]�w ===
                string configured = VoiceCommandConfig.ModelRelativePath?.Value ?? string.Empty;
                var lang = VoiceCommandConfig.ModelLanguage?.Value ?? SystemLanguage.Chinese;

                // ���]�w�G������^���
                if (string.IsNullOrWhiteSpace(configured))
                {
                    _log.LogInfo("[Setup] Model.RelativePath is empty �� use game's original provider.");
                    return true; // ���d�I
                }

                // === 2) �ѪR�ҫ���ڸ��|�]�۹�/���ﳣ�i�^ ===
                // �̧ǹ��աGBepInEx �����Ƨ��B�C���ڥؿ��B�ثe DLL �Ҧb��Ƨ�
                var candidates = new List<string>();

                // ������|�����ΡF�_�h�~�զX���
                if (Path.IsPathRooted(configured))
                {
                    candidates.Add(configured);
                }
                else
                {
                    // BepInEx �����Ƨ�
                    if (!string.IsNullOrEmpty(Paths.PluginPath))
                        candidates.Add(Path.Combine(Paths.PluginPath, configured));

                    // �C���ڥؿ�
                    if (!string.IsNullOrEmpty(Paths.GameRootPath))
                        candidates.Add(Path.Combine(Paths.GameRootPath, configured));

                    // �ثe DLL �Ҧb��Ƨ�
                    string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(pluginDir))
                        candidates.Add(Path.Combine(pluginDir, configured));
                }

                // �h�����ƻP���X�k�r��
                candidates = candidates
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(NormalizePath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (candidates.Count == 0)
                {
                    _log.LogWarning("[Setup] No valid path candidates can be built. Fallback to original.");
                    return true;
                }

                // ���Ĥ@�Ӧs�b����Ƨ�
                string modelPath = candidates.FirstOrDefault(Directory.Exists);
                if (string.IsNullOrEmpty(modelPath))
                {
                    _log.LogWarning($"[Setup] None of candidate model paths exist: {string.Join(" | ", candidates)} �� fallback to original.");
                    return true;
                }

                // === 3) ²�椺�e���ˡ]Vosk �`�����c/�ɮס^ ===
                // ���קK�L���Y��A�u�����ˡG�s�b model.conf �άO��Ƨ�/�ɮצܤ֭Y�z
                bool looksLikeModel =
                    File.Exists(Path.Combine(modelPath, "model.conf")) ||
                    Directory.EnumerateFileSystemEntries(modelPath).Any();

                if (!looksLikeModel)
                {
                    _log.LogWarning($"[Setup] Path looks empty or not a model folder: {modelPath} �� fallback to original.");
                    return true;
                }

                // === 4) ��/�мg Provider ===
                var go = __instance.gameObject;
                if (go == null)
                {
                    _log.LogWarning("[Setup] Target GameObject is null. Fallback to original.");
                    return true;
                }

                // ���ը��o�J�� Provider�]�u�B�z�ڭ̪��Ϊš^
                var provider = go.GetComponent<StreamingAssetsLanguageModelProvider>();
                if (provider == null)
                {
                    provider = go.AddComponent<StreamingAssetsLanguageModelProvider>();
                    TagComponent(provider, ProviderTag);
                    _log.LogDebug("[Setup] Added new StreamingAssetsLanguageModelProvider (tagged).");
                }
                else
                {
                    // �p�G���O�ڭ̼аO�إߪ��]�S���Y�A���мg�ݩʡA���D�� Destroy�A���C IL2CPP ���I
                    _log.LogDebug("[Setup] Reusing existing StreamingAssetsLanguageModelProvider.");
                }

                provider.language = lang;
                provider.languageModels = new List<StreamingAssetsLanguageModel>
                {
                    new StreamingAssetsLanguageModel
                    {
                        language = lang,
                        path = modelPath
                    }
                };

                // === 5) �j�w�� SpeechRecognizer ===
                var speechRecognizer = __instance.GetComponent<SpeechRecognizer>();
                if (speechRecognizer == null)
                {
                    _log.LogWarning("[Setup] SpeechRecognizer not found on target GameObject. Let original handle.");
                    return true; // �S�����Ѿ��N��^���
                }

                // �����ڭ̪� Provider
                speechRecognizer.LanguageModelProvider = provider;

                // === 6) �O���̲׸�T���d�I ===
                _log.LogInfo($"[Setup] Using external language model: \"{modelPath}\" (lang={lang})");
                _log.LogDebug($"[Setup] Candidates tried: {string.Join(" | ", candidates)}");
                return false; // �d�I��� Setup�]�ڭ̤w�t�m�����^
            }
            catch (Exception ex)
            {
                // ���N���~����^��͡A�קK�d��
                _log?.LogError($"[Setup] Exception �� fallback to original. {ex}");
                return true;
            }
        }

        // === �u��G���|���W�� ===
        private static string NormalizePath(string path)
        {
            try
            {
                // �Τ@���|���j�šF�������ݱ׽u
                var full = Path.GetFullPath(path);
                if (full.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                    full.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                return full;
            }
            catch
            {
                return path;
            }
        }

        // === �u��G�����󰵤@��²�� Tag�]���v�T�C���^===
        private static void TagComponent(Component c, string tag)
        {
            if (c == null) return;
            // �Q�� hideFlags ����ܦb Hierarchy�F�� name ��������
            try
            {
                c.hideFlags |= HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
                var t = c as UnityEngine.Object;
                if (t != null && !t.name.Contains(tag))
                {
                    t.name = $"{t.name} [{tag}]";
                }
            }
            catch
            {
                // �����аO����
            }
        }
    }
}
