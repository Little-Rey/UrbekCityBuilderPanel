using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Epic.OnlineServices;
using HarmonyLib;
using System.Collections;
using UnityEngine;

namespace ResourceDebugMod
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        // UI相关
        public Motor MotorInstance;
        public Control ControlInstance;
        private ConfigEntry<KeyboardShortcut> _toggleUiHotkey;

        public bool WindowOpen;
        private ResourceUI _resourceUi;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("资源调试 Mod 已加载。");

            // Harmony补丁
            var harmony = new Harmony(PluginInfo.GUID);
            harmony.PatchAll();

            // UI快捷键绑定 F2
            _toggleUiHotkey = Config.Bind("Hotkeys", "ToggleUI", new KeyboardShortcut(KeyCode.F2), "F2打开资源面板");
            // 实例化UI，把自己传进去
            _resourceUi = new ResourceUI(this);
        }

        void Update()
        {
            // 拿到Motor实例
            if (MotorInstance == null)
            {
                MotorInstance = Object.FindObjectOfType<Motor>();
            }
            if (ControlInstance == null)
            {
                ControlInstance = Object.FindObjectOfType<Control>();
            }
            // ========= F2 开关UI窗口 =========
            if (_toggleUiHotkey.Value.IsDown())
            {
                WindowOpen = !WindowOpen;
            }
        }

        private void OnGUI()
        {
            // 转发绘制给UI
            _resourceUi.Draw();
        }



        [HarmonyPatch(typeof(Motor), nameof(Motor.LoadPlayer))]
        public static class Patch_LoadPlayer
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                ResourceData.ClearGlobalVariableOverrides();
            }
        }

        [HarmonyPatch(typeof(Motor), "Update")]
        public static class Patch_MotorUpdate
        {
            [HarmonyPostfix]
            private static void Postfix(Motor __instance)
            {
                ResourceData.ReapplyGlobalVariableOverrides(__instance);
            }
        }

        public static class PluginInfo
        {
            public const string GUID = "cn.orrey";
            public const string Name = "资源调试";
            public const string Version = "1.0.0";
        }
    }
}