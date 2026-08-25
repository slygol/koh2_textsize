using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KoH2.LargerText
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class LargerTextPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ru.sly.koh2.largertext.popup";
        public const string PluginName = "KoH2 Larger Popup Text";
        public const string PluginVersion = "1.3.0";

        private sealed class TextScalerWorker : MonoBehaviour
        {
            private void Awake()
            {
                if (instance != null)
                    instance.Logger.LogInfo("Independent TextScalerWorker is active.");
            }

            private void Update()
            {
                if (instance != null)
                    instance.TryScan();
            }
        }

        private sealed class TmpTextState
        {
            public TMP_Text Text;
            public float FontSize;
            public float FontSizeMin;
            public float FontSizeMax;
        }

        private sealed class LegacyTextState
        {
            public Text Text;
            public int FontSize;
        }

        private readonly Dictionary<int, TmpTextState> tmpStates = new Dictionary<int, TmpTextState>();
        private readonly Dictionary<int, LegacyTextState> legacyStates = new Dictionary<int, LegacyTextState>();

        private ConfigEntry<float> scale;
        private ConfigEntry<float> scanInterval;
        private ConfigEntry<bool> globalMode;
        private ConfigEntry<bool> popupOnly;
        private ConfigEntry<bool> skipHyperText;
        private ConfigEntry<string> popupMarkers;

        private string[] markers = Array.Empty<string>();
        private static LargerTextPlugin instance;
        private Harmony harmony;
        private GameObject workerObject;
        private float nextScanAt;
        private int scanNumber;
        private bool scanInProgress;
        private int hookApplications;

        private void Awake()
        {
            instance = this;

            scale = Config.Bind(
                "General",
                "Scale",
                1.25f,
                "Text scale multiplier. 1.25 means 125 percent.");

            scanInterval = Config.Bind(
                "General",
                "ScanIntervalSeconds",
                0.5f,
                "How often the plugin searches for newly opened UI windows.");

            globalMode = Config.Bind(
                "V2Scope",
                "GlobalMode",
                true,
                "Enlarge every active text component. Disable the regular LargerText data mod when this is true.");

            popupOnly = Config.Bind(
                "Scope",
                "PopupOnly",
                true,
                "When true, only UI objects whose hierarchy matches PopupMarkers are changed.");

            skipHyperText = Config.Bind(
                "Scope",
                "SkipHyperText",
                true,
                "Skip HyperText already enlarged by the regular LargerText data mod.");

            popupMarkers = Config.Bind(
                "Scope",
                "PopupMarkers",
                "popup,dialog,message,warning,confirmation,confirm,query,systemwindow,networkerror,invitecode,fallback",
                "Comma-separated, case-insensitive fragments matched against UI object names and their parents.");

            RebuildMarkers();
            popupMarkers.SettingChanged += delegate { RebuildMarkers(); };

            CreateIndependentWorker();
            InstallRuntimeHooks();
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Logger.LogInfo(
                globalMode.Value
                    ? "Global mode is active. Disable the regular LargerText data mod to avoid double scaling."
                    : "Compatibility popup-filter mode is active.");
            Logger.LogInfo("Scanner is attached to Canvas.willRenderCanvases.");
        }

        private void CreateIndependentWorker()
        {
            workerObject = new GameObject("KoH2.LargerText.Worker");
            workerObject.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(workerObject);
            workerObject.AddComponent<TextScalerWorker>();
        }

        private void InstallRuntimeHooks()
        {
            try
            {
                harmony = new Harmony(PluginGuid);
                var uiTextOnEnable = AccessTools.Method(typeof(UIText), "OnEnable");
                var uiTextSetText = AccessTools.Method(
                    typeof(UIText),
                    "SetText",
                    new[] { typeof(TMP_Text), typeof(string) });
                var tmpOnEnable = AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable");

                var uiTextOnEnablePostfix = new HarmonyMethod(
                    typeof(LargerTextPlugin),
                    nameof(UITextOnEnablePostfix));
                var uiTextSetTextPostfix = new HarmonyMethod(
                    typeof(LargerTextPlugin),
                    nameof(UITextSetTextPostfix));
                var tmpOnEnablePostfix = new HarmonyMethod(
                    typeof(LargerTextPlugin),
                    nameof(TmpOnEnablePostfix));

                if (uiTextOnEnable != null)
                    harmony.Patch(uiTextOnEnable, postfix: uiTextOnEnablePostfix);
                if (uiTextSetText != null)
                    harmony.Patch(uiTextSetText, postfix: uiTextSetTextPostfix);
                if (tmpOnEnable != null)
                    harmony.Patch(tmpOnEnable, postfix: tmpOnEnablePostfix);

                Logger.LogInfo(
                    "Runtime hooks installed: UIText.OnEnable=" + (uiTextOnEnable != null) +
                    ", UIText.SetText=" + (uiTextSetText != null) +
                    ", TextMeshProUGUI.OnEnable=" + (tmpOnEnable != null) + ".");
            }
            catch (Exception exception)
            {
                Logger.LogError("Failed to install runtime text hooks: " + exception);
            }
        }

        private static void UITextOnEnablePostfix(UIText __instance)
        {
            if (instance != null && __instance != null)
                instance.ApplyHookedText(__instance.GetComponent<TMP_Text>(), "UIText.OnEnable");
        }

        private static void UITextSetTextPostfix(TMP_Text component)
        {
            if (instance != null)
                instance.ApplyHookedText(component, "UIText.SetText");
        }

        private static void TmpOnEnablePostfix(TextMeshProUGUI __instance)
        {
            if (instance != null)
                instance.ApplyHookedText(__instance, "TextMeshProUGUI.OnEnable");
        }

        private void ApplyHookedText(TMP_Text text, string source)
        {
            if (!IsLiveSceneObject(text) || !IsTarget(text.transform))
                return;

            bool isNew = RegisterAndApply(text, Mathf.Clamp(scale.Value, 1f, 3f));
            if (isNew && hookApplications++ < 8)
            {
                Logger.LogInfo(
                    "Hook scaled text via " + source + ": " + GetObjectPath(text.transform) +
                    " -> " + text.fontSize.ToString("0.##"));
            }
        }

        private static string GetObjectPath(Transform transform)
        {
            string path = transform == null ? "<null>" : transform.name;
            for (Transform current = transform?.parent; current != null; current = current.parent)
                path = current.name + "/" + path;
            return path;
        }

        private void RebuildMarkers()
        {
            string raw = popupMarkers == null ? string.Empty : popupMarkers.Value;
            string[] values = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();

            foreach (string value in values)
            {
                string marker = value.Trim().ToLowerInvariant();
                if (marker.Length > 0)
                    result.Add(marker);
            }

            markers = result.ToArray();
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            harmony?.UnpatchSelf();
            if (workerObject != null)
                UnityEngine.Object.Destroy(workerObject);
            if (ReferenceEquals(instance, this))
                instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            nextScanAt = 0f;
        }

        private void OnWillRenderCanvases()
        {
            TryScan();
        }

        // Kept as a second trigger for Unity configurations where no Canvas event is raised.
        private void Update()
        {
            TryScan();
        }

        private void TryScan()
        {
            if (scanInProgress || Time.realtimeSinceStartup < nextScanAt)
                return;

            nextScanAt = Time.realtimeSinceStartup + Mathf.Clamp(scanInterval.Value, 0.1f, 10f);
            scanInProgress = true;

            try
            {
                ScaleNewText();
            }
            catch (Exception exception)
            {
                Logger.LogError("Text scan failed: " + exception);
            }
            finally
            {
                scanInProgress = false;
            }
        }

        private void ScaleNewText()
        {
            float multiplier = Mathf.Clamp(scale.Value, 1f, 3f);
            int newTmp = 0;
            int newLegacy = 0;
            scanNumber++;

            TMP_Text[] tmpTexts = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
            Text[] legacyTexts = UnityEngine.Object.FindObjectsOfType<Text>();

            if (scanNumber <= 3)
            {
                Logger.LogInfo(
                    "Text scan #" + scanNumber + ": active TMP=" + tmpTexts.Length +
                    ", active legacy=" + legacyTexts.Length + ".");
            }

            foreach (TMP_Text text in tmpTexts)
            {
                if (!IsLiveSceneObject(text) || !IsTarget(text.transform))
                    continue;

                if (RegisterAndApply(text, multiplier))
                    newTmp++;
            }

            foreach (Text text in legacyTexts)
            {
                if (!IsLiveSceneObject(text) || !IsTarget(text.transform))
                    continue;

                if (text.fontSize <= 0)
                    continue;

                int id = text.GetInstanceID();
                LegacyTextState state;

                if (!legacyStates.TryGetValue(id, out state) || !ReferenceEquals(state.Text, text))
                {
                    state = new LegacyTextState
                    {
                        Text = text,
                        FontSize = text.fontSize
                    };
                    legacyStates[id] = state;
                    newLegacy++;
                }

                int desiredSize = Mathf.RoundToInt(state.FontSize * multiplier);
                if (text.fontSize != desiredSize)
                    text.fontSize = desiredSize;
            }

            if (newTmp > 0 || newLegacy > 0)
                Logger.LogInfo("Scaled newly active text components: TMP=" + newTmp + ", legacy=" + newLegacy + ".");
        }

        private bool RegisterAndApply(TMP_Text text, float multiplier)
        {
            if (text == null || text.fontSize <= 0f)
                return false;

            int id = text.GetInstanceID();
            TmpTextState state;
            bool isNew = !tmpStates.TryGetValue(id, out state) || !ReferenceEquals(state.Text, text);

            if (isNew)
            {
                state = new TmpTextState
                {
                    Text = text,
                    FontSize = text.fontSize,
                    FontSizeMin = text.fontSizeMin,
                    FontSizeMax = text.fontSizeMax
                };
                tmpStates[id] = state;
            }

            bool changed = SetIfDifferent(text.fontSize, state.FontSize * multiplier, value => text.fontSize = value);

            if (text.enableAutoSizing)
            {
                if (state.FontSizeMin > 0f)
                    changed |= SetIfDifferent(text.fontSizeMin, state.FontSizeMin * multiplier, value => text.fontSizeMin = value);
                if (state.FontSizeMax > 0f)
                    changed |= SetIfDifferent(text.fontSizeMax, state.FontSizeMax * multiplier, value => text.fontSizeMax = value);
            }

            if (changed)
            {
                text.SetLayoutDirty();
                text.SetVerticesDirty();
            }

            return isNew;
        }

        private static bool SetIfDifferent(float current, float desired, Action<float> setter)
        {
            if (Mathf.Abs(current - desired) < 0.01f)
                return false;

            setter(desired);
            return true;
        }

        private static bool IsLiveSceneObject(Component component)
        {
            return component != null &&
                   component.gameObject != null &&
                   component.gameObject.scene.IsValid() &&
                   component.gameObject.activeInHierarchy;
        }

        private bool IsTarget(Transform transform)
        {
            if (globalMode.Value)
                return true;

            if (!popupOnly.Value)
                return !skipHyperText.Value || !HierarchyContains(transform, "hypertext");

            bool popupMatch = false;
            bool hyperTextMatch = false;

            for (Transform current = transform; current != null; current = current.parent)
            {
                string objectName = current.name.ToLowerInvariant();

                if (objectName.Contains("hypertext"))
                    hyperTextMatch = true;

                for (int i = 0; i < markers.Length; i++)
                {
                    if (objectName.Contains(markers[i]))
                    {
                        popupMatch = true;
                        break;
                    }
                }
            }

            if (skipHyperText.Value && hyperTextMatch)
                return false;

            return popupMatch;
        }

        private static bool HierarchyContains(Transform transform, string marker)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
