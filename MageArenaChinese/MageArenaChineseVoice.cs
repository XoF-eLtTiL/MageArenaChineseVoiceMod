using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MageArenaChineseVoice.Config;
using MageArenaChineseVoice.Patches; // ← 放有 GlobalNreGuardPatch 的命名空間

namespace MageArenaChineseVoice
{
    [BepInPlugin("com.xofelttil.MageArenaChineseVoice", "MageArenaChineseVoice", "2.0.0")]
    public class MageArenaChineseVoice : BaseUnityPlugin
    {
        private Harmony _harmony;
        internal static ManualLogSource Log; // 不覆蓋 BaseUnityPlugin.Logger，避免用 new

        private void Awake()
        {
            Log = Logger;

            // 讀取/建立 .cfg（含你原本的語音設定）
            VoiceCommandConfig.Init(Config);

            // 建立 Harmony 實例
            _harmony = new Harmony("com.xofelttil.MageArenaChineseVoice");

            // 先套你的所有補丁（VoiceControlListenerPatch 等）
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            // 再套「全域 NRE 防護 Patch」（Harmony 2.x 新 API：CreateProcessor().Patch() 版本）
            GlobalNreGuardPatch.Apply(
                harmony: _harmony,
                enabled: true,            // 可改成從 Config 讀
                cooldownSec: 0.8f,        // NRE 後暫停該元件 0.8s
                logDetail: true,          // 顯示詳細 Log
                hookMethods: null,        // 預設守護 Update/LateUpdate/FixedUpdate/Start/Awake/OnGUI
                maxPatches: 20000,        // 安全上限
                logger: Logger
            );

            Logger.LogInfo("MageArenaChineseVoice loaded (NRE guard enabled).");
        }
    }
}
