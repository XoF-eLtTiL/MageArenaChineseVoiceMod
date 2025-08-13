using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MageArenaChineseVoice.Patches
{
    /// <summary>
    /// 全域 NRE 防護（Patch 版本）：
    /// - 掃描所有 MonoBehaviour，對常見生命周期方法（Update/LateUpdate/FixedUpdate/Start/Awake/OnGUI）裝上 Finalizer
    /// - Finalizer 只吞 NullReferenceException；其他例外照常丟出，避免遮蔽真正問題
    /// - 遇到 NRE 時，將該元件短暫禁用（冷卻秒數可調），到時自動恢復，避免每幀噴錯造成卡頓
    /// - 建議搭配你的「施法佇列（LateUpdate 執行）」使用，效果更穩
    /// </summary>
    public static class GlobalNreGuardPatch
    {
        // ===== 可調參數（可由 Apply 傳入覆寫）=====
        private static bool _enabled = true;     // 總開關
        private static float _cooldownSec = 0.8f;     // 出錯後暫停時間
        private static bool _logDetail = true;     // 是否記錄詳細日誌
        private static int _maxPatches = 10_000;   // 安全上限（避免極端情況）
        private static string[] _hookMethods = new[] { "Update", "LateUpdate", "FixedUpdate", "Start", "Awake", "OnGUI" };

        // ===== 內部狀態 =====
        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static readonly Dictionary<int, float> _cooldownUntil = new();
        private static readonly object _lock = new();

        // 用於啟動協程的輔助 Runner（必要，因為這不是 BepInEx Plugin 類）
        private class NreGuardRunner : MonoBehaviour
        {
            private static NreGuardRunner _inst;
            public static NreGuardRunner Ensure()
            {
                if (_inst != null) return _inst;
                var go = new GameObject("MA_GlobalNreGuard_Runner");
                GameObject.DontDestroyOnLoad(go);
                _inst = go.AddComponent<NreGuardRunner>();
                return _inst;
            }
        }

        /// <summary>
        /// 套用全域 NRE 防護。你可以在主插件 Awake 中呼叫。
        /// </summary>
        /// <param name="harmony">Harmony 實例（可傳 null，則自建）</param>
        /// <param name="enabled">總開關</param>
        /// <param name="cooldownSec">禁用冷卻秒數</param>
        /// <param name="logDetail">是否詳細列印</param>
        /// <param name="hookMethods">要守護的方法名清單（預設：Update/LateUpdate/FixedUpdate/Start/Awake/OnGUI）</param>
        /// <param name="maxPatches">最多裝幾個 Patch（安全上限）</param>
        /// <param name="logger">可傳入你的 LogSource；若為 null 則自建</param>
        public static void Apply(
            Harmony harmony = null,
            bool enabled = true,
            float cooldownSec = 0.8f,
            bool logDetail = true,
            string[] hookMethods = null,
            int maxPatches = 10_000,
            ManualLogSource logger = null)
        {
            _enabled = enabled;
            _cooldownSec = Mathf.Max(0f, cooldownSec);
            _logDetail = logDetail;
            _maxPatches = Mathf.Max(1, maxPatches);
            if (hookMethods != null && hookMethods.Length > 0)
                _hookMethods = hookMethods;

            _log = logger ?? BepInEx.Logging.Logger.CreateLogSource("MA-GlobalNreGuardPatch");
            _harmony ??= (harmony ?? new Harmony("com.xofelttil.MA.GlobalNreGuardPatch"));

            // 確保 Runner 存在以便啟動協程
            NreGuardRunner.Ensure();

            if (!_enabled)
            {
                _log.LogInfo("[GlobalNreGuardPatch] Disabled (not installing patches).");
                return;
            }

            try
            {
                int total = InstallGlobalFinalizers();
                _log.LogInfo($"[GlobalNreGuardPatch] Installed finalizers on {total} method(s). Cooldown={_cooldownSec:0.###}s");
            }
            catch (Exception ex)
            {
                _log.LogError($"[GlobalNreGuardPatch] Install failed: {ex}");
            }
        }

        // ====== 動態掃描並安裝 Finalizer ======
        private static int InstallGlobalFinalizers()
        {
            int patched = 0;

            static bool SkipAssembly(Assembly a)
            {
                var n = a.GetName().Name ?? "";
                if (n.StartsWith("System") || n.StartsWith("mscorlib") || n.StartsWith("netstandard")) return true;
                if (n.StartsWith("Unity") || n.StartsWith("UnityEngine") || n.StartsWith("UnityEditor")) return true;
                if (n.StartsWith("BepInEx") || n.StartsWith("mono")) return true;
                if (n.StartsWith("Harmony") || n.StartsWith("0Harmony")) return true;
                return false;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (SkipAssembly(asm)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (!typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                    if (t.IsAbstract) continue;

                    foreach (var mname in _hookMethods)
                    {
                        MethodInfo m;
                        try { m = AccessTools.Method(t, mname, Type.EmptyTypes); }
                        catch { continue; }

                        if (m == null) continue;
                        if (m.GetParameters().Length != 0) continue;
                        if (m.ReturnType != typeof(void)) continue;

                        try
                        {
                            var finalizer = new HarmonyMethod(typeof(GlobalNreGuardPatch)
                                .GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic));

                            // ✅ 新 API：用 CreateProcessor + Patch
                            var processor = _harmony.CreateProcessor(m);
                            processor.AddFinalizer(finalizer);
                            processor.Patch();

                            patched++;
                            if (patched >= _maxPatches) return patched;
                        }
                        catch (Exception ex)
                        {
                            if (_logDetail)
                                _log?.LogWarning($"[GlobalNreGuardPatch] Patch fail {t.FullName}.{mname}: {ex.Message}");
                        }
                    }
                }
            }
            return patched;
        }


        /// <summary>
        /// Finalizer：只吞 NullReferenceException；其餘原樣丟出。
        /// 若為 NRE，暫時禁用該 Behaviour，並排程在冷卻後重新啟用。
        /// </summary>
        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (!_enabled) return __exception;

            if (__exception is NullReferenceException)
            {
                var comp = __instance as Behaviour; // 大多數腳本皆為 Behaviour
                if (comp != null)
                {
                    var now = Time.realtimeSinceStartup;
                    var id = comp.GetInstanceID();

                    bool cooling = false;
                    lock (_lock)
                    {
                        if (_cooldownUntil.TryGetValue(id, out var until) && until > now)
                            cooling = true;
                        else
                            _cooldownUntil[id] = now + _cooldownSec;
                    }

                    if (!cooling && comp.isActiveAndEnabled)
                    {
                        if (_logDetail)
                            _log?.LogWarning($"[GlobalNreGuardPatch] NRE swallowed: {GetPath(comp.transform)} -> disable {_cooldownSec:0.###}s");

                        // 啟動協程在冷卻後恢復
                        NreGuardRunner.Ensure().StartCoroutine(ReEnableSoon(comp, _cooldownSec));
                        comp.enabled = false;
                    }

                    // 吞掉 NRE，避免每幀刷錯
                    return null;
                }
                else
                {
                    // 非 Behaviour 的少見情況，仍吞掉 NRE（避免刷屏）
                    if (_logDetail)
                        _log?.LogWarning("[GlobalNreGuardPatch] NRE swallowed on non-Behaviour instance.");
                    return null;
                }
            }

            // 其他例外：維持原樣，方便定位真正問題
            return __exception;
        }

        private static IEnumerator ReEnableSoon(Behaviour b, float sec)
        {
            yield return new WaitForSeconds(sec);
            if (b == null) yield break;

            // 檢查是否仍在冷卻（可能期間又多次出錯延長了）
            var id = b.GetInstanceID();
            var now = Time.realtimeSinceStartup;
            lock (_lock)
            {
                if (_cooldownUntil.TryGetValue(id, out var until) && until > now)
                {
                    var remain = until - now;
                    NreGuardRunner.Ensure().StartCoroutine(ReEnableSoon(b, remain));
                    yield break;
                }
            }

            // 嘗試恢復
            try
            {
                b.enabled = true;
                if (_logDetail)
                    _log?.LogInfo($"[GlobalNreGuardPatch] Re-enabled: {GetPath((b as Component)?.transform)}");
            }
            catch (Exception e)
            {
                _log?.LogWarning($"[GlobalNreGuardPatch] Re-enable failed: {e.Message}");
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "<null>";
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.parent.name + "/" + path; }
            return path;
        }

        internal static void Apply(object harmony, bool enabled, float cooldownSec, bool logDetail, object hookMethods, int maxPatches, ManualLogSource logger)
        {
            throw new NotImplementedException();
        }
    }
}
