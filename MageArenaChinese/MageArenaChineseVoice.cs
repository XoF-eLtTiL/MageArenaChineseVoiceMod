using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MageArenaChineseVoice.Config;

namespace MageArenaChineseVoice;

[BepInPlugin("com.xofelttil.MageArenaChineseVoice", "MageArenaChineseVoice", "2.0.0")]
public class MageArenaChineseVoice : BaseUnityPlugin
{
    private readonly Harmony harmony = new Harmony("com.xofelttil.MageArenaChineseVoice");
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        Logger = base.Logger;
        VoiceCommandConfig.Init(Config);
        harmony.PatchAll();
        Logger.LogInfo("MageArenaChineseVoice loaded!");
    }
    
    

}
