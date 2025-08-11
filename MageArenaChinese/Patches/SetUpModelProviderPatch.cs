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

        // 我們自己建立/管理的 Provider 會帶這個標記，避免重複與方便辨識
        private const string ProviderTag = "MA-CN-ExternalModelProvider";

        [HarmonyPrefix]
        public static bool Prefix(SetUpModelProvider __instance)
        {
            _log ??= BepInEx.Logging.Logger.CreateLogSource("MA-CN-SetupProvider");

            try
            {
                // === 1) 讀取設定 ===
                string configured = VoiceCommandConfig.ModelRelativePath?.Value ?? string.Empty;
                var lang = VoiceCommandConfig.ModelLanguage?.Value ?? SystemLanguage.Chinese;

                // 未設定：直接交回原生
                if (string.IsNullOrWhiteSpace(configured))
                {
                    _log.LogInfo("[Setup] Model.RelativePath is empty → use game's original provider.");
                    return true; // 不攔截
                }

                // === 2) 解析模型實際路徑（相對/絕對都可） ===
                // 依序嘗試：BepInEx 插件資料夾、遊戲根目錄、目前 DLL 所在資料夾
                var candidates = new List<string>();

                // 絕對路徑直接用；否則才組合基準
                if (Path.IsPathRooted(configured))
                {
                    candidates.Add(configured);
                }
                else
                {
                    // BepInEx 插件資料夾
                    if (!string.IsNullOrEmpty(Paths.PluginPath))
                        candidates.Add(Path.Combine(Paths.PluginPath, configured));

                    // 遊戲根目錄
                    if (!string.IsNullOrEmpty(Paths.GameRootPath))
                        candidates.Add(Path.Combine(Paths.GameRootPath, configured));

                    // 目前 DLL 所在資料夾
                    string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(pluginDir))
                        candidates.Add(Path.Combine(pluginDir, configured));
                }

                // 去除重複與不合法字串
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

                // 取第一個存在的資料夾
                string modelPath = candidates.FirstOrDefault(Directory.Exists);
                if (string.IsNullOrEmpty(modelPath))
                {
                    _log.LogWarning($"[Setup] None of candidate model paths exist: {string.Join(" | ", candidates)} → fallback to original.");
                    return true;
                }

                // === 3) 簡單內容健檢（Vosk 常見結構/檔案） ===
                // 為避免過度嚴格，只做輕檢：存在 model.conf 或是資料夾/檔案至少若干
                bool looksLikeModel =
                    File.Exists(Path.Combine(modelPath, "model.conf")) ||
                    Directory.EnumerateFileSystemEntries(modelPath).Any();

                if (!looksLikeModel)
                {
                    _log.LogWarning($"[Setup] Path looks empty or not a model folder: {modelPath} → fallback to original.");
                    return true;
                }

                // === 4) 掛/覆寫 Provider ===
                var go = __instance.gameObject;
                if (go == null)
                {
                    _log.LogWarning("[Setup] Target GameObject is null. Fallback to original.");
                    return true;
                }

                // 嘗試取得既有 Provider（只處理我們的或空）
                var provider = go.GetComponent<StreamingAssetsLanguageModelProvider>();
                if (provider == null)
                {
                    provider = go.AddComponent<StreamingAssetsLanguageModelProvider>();
                    TagComponent(provider, ProviderTag);
                    _log.LogDebug("[Setup] Added new StreamingAssetsLanguageModelProvider (tagged).");
                }
                else
                {
                    // 如果不是我們標記建立的也沒關係，僅覆寫屬性，不主動 Destroy，降低 IL2CPP 風險
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

                // === 5) 綁定到 SpeechRecognizer ===
                var speechRecognizer = __instance.GetComponent<SpeechRecognizer>();
                if (speechRecognizer == null)
                {
                    _log.LogWarning("[Setup] SpeechRecognizer not found on target GameObject. Let original handle.");
                    return true; // 沒有辨識器就放回原生
                }

                // 指派我們的 Provider
                speechRecognizer.LanguageModelProvider = provider;

                // === 6) 記錄最終資訊並攔截 ===
                _log.LogInfo($"[Setup] Using external language model: \"{modelPath}\" (lang={lang})");
                _log.LogDebug($"[Setup] Candidates tried: {string.Join(" | ", candidates)}");
                return false; // 攔截原生 Setup（我們已配置完成）
            }
            catch (Exception ex)
            {
                // 任意錯誤都放回原生，避免卡死
                _log?.LogError($"[Setup] Exception → fallback to original. {ex}");
                return true;
            }
        }

        // === 工具：路徑正規化 ===
        private static string NormalizePath(string path)
        {
            try
            {
                // 統一路徑分隔符；移除尾端斜線
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

        // === 工具：替元件做一個簡易 Tag（不影響遊戲）===
        private static void TagComponent(Component c, string tag)
        {
            if (c == null) return;
            // 利用 hideFlags 不顯示在 Hierarchy；用 name 附註標籤
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
                // 忽略標記失敗
            }
        }
    }
}
