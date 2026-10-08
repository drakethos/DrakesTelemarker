using System.Globalization;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DrakesTelemarker
{
    internal static class TelemarkerClearDialog
    {
        private const int TryOpenFrames = 240;

        private static GameObject? _root;
        private static Text? _questionText;
        private static Button? _buttonYes;
        private static Button? _buttonNo;
        private static bool _inputBlocked;
        private static bool _pendingAll;
        private static int _pendingSlot;
        private static string _lockedWorldKey = "";

        internal static void RegisterForGuiRebuild()
        {
            GUIManager.OnCustomGUIAvailable += () => TearDownPanel();
        }

        internal static void RequestConfirmation(DrakesTelemarkerPlugin plugin, bool clearAll, int slot1Based)
        {
            if (TryOpenNow(plugin, clearAll, slot1Based))
                return;

            plugin.StartCoroutine(CoRetryOpen(plugin, clearAll, slot1Based));
        }

        private static System.Collections.IEnumerator CoRetryOpen(DrakesTelemarkerPlugin plugin, bool clearAll,
            int slot1Based)
        {
            for (int i = 0; i < TryOpenFrames; i++)
            {
                if (TryOpenNow(plugin, clearAll, slot1Based))
                    yield break;

                yield return null;
            }

            DrakesTelemarkerPlugin.Notify("Confirmation UI is not ready yet. Try again in a moment.");
        }

        internal static bool TryOpenNow(DrakesTelemarkerPlugin plugin, bool clearAll, int slot1Based)
        {
            GUIManager? gui = GUIManager.Instance;
            if (gui == null || !GUIManager.CustomGUIFront)
                return false;

            EnsurePanelBuilt(gui);
            if (_root == null || _questionText == null || _buttonYes == null || _buttonNo == null)
                return false;

            _pendingAll = clearAll;
            _pendingSlot = slot1Based;
            _lockedWorldKey = DrakesTelemarkerPlugin.SanitizeWorldKey(DrakesTelemarkerPlugin.GetWorldKey());
            _questionText.text = clearAll
                ? "Are you sure you want to clear ALL saved marks for this world?"
                : $"Are you sure you want to clear mark {slot1Based.ToString(CultureInfo.InvariantCulture)}?";

            WireButtonsOnce(plugin);

            _root.SetActive(true);
            if (!_inputBlocked)
            {
                GUIManager.BlockInput(true);
                _inputBlocked = true;
            }

            return true;
        }

        private static void WireButtonsOnce(DrakesTelemarkerPlugin plugin)
        {
            _buttonYes!.onClick.RemoveAllListeners();
            _buttonNo!.onClick.RemoveAllListeners();
            _buttonYes.onClick.AddListener(() => OnYes(plugin));
            _buttonNo.onClick.AddListener(Close);
        }

        private static void OnYes(DrakesTelemarkerPlugin plugin)
        {
            string nowKey = DrakesTelemarkerPlugin.SanitizeWorldKey(DrakesTelemarkerPlugin.GetWorldKey());
            if (!string.Equals(_lockedWorldKey, nowKey, System.StringComparison.Ordinal))
            {
                DrakesTelemarkerPlugin.Notify(
                    "World changed before confirmation — nothing was cleared. Run telemark clear … again.");
                Close();
                return;
            }

            if (_pendingAll)
            {
                if (!plugin.TryClearAllSlots(out int n, out string err))
                    DrakesTelemarkerPlugin.Notify(string.IsNullOrEmpty(err) ? "Nothing to clear." : err);
                else
                    DrakesTelemarkerPlugin.Notify($"Cleared all {n} marks for this world.");
            }
            else
            {
                if (!plugin.TryClearSlot(_pendingSlot, out string err))
                    DrakesTelemarkerPlugin.Notify(
                        string.IsNullOrEmpty(err)
                            ? $"Mark {_pendingSlot.ToString(CultureInfo.InvariantCulture)} was already empty."
                            : err);
                else
                    DrakesTelemarkerPlugin.Notify(
                        $"Cleared mark {_pendingSlot.ToString(CultureInfo.InvariantCulture)}.");
            }

            Close();
        }

        private static void Close()
        {
            if (_root != null)
                _root.SetActive(false);

            if (_inputBlocked)
            {
                GUIManager.BlockInput(false);
                _inputBlocked = false;
            }
        }

        private static void TearDownPanel()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            _questionText = null;
            _buttonYes = null;
            _buttonNo = null;

            if (_inputBlocked)
            {
                try
                {
                    GUIManager.BlockInput(false);
                }
                catch
                {
                    // ignore — scene may be unloading
                }

                _inputBlocked = false;
            }
        }

        private static void EnsurePanelBuilt(GUIManager gui)
        {
            if (_root != null)
                return;

            Transform parent = GUIManager.CustomGUIFront.transform;
            _root = gui.CreateWoodpanel(
                parent: parent,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: 380,
                height: 200,
                draggable: false);

            gui.CreateText(
                text: DrakesTelemarkerPlugin.ModName,
                parent: _root.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -36f),
                font: gui.AveriaSerifBold,
                fontSize: 18,
                color: gui.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: 340,
                height: 28,
                addContentSizeFitter: false);

            GameObject qGo = gui.CreateText(
                text: "",
                parent: _root.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, 12f),
                font: gui.AveriaSerifBold,
                fontSize: 15,
                color: Color.white,
                outline: true,
                outlineColor: Color.black,
                width: 340,
                height: 70,
                addContentSizeFitter: false);
            _questionText = qGo.GetComponent<Text>();
            _questionText.alignment = TextAnchor.MiddleCenter;
            _questionText.horizontalOverflow = HorizontalWrapMode.Wrap;

            _buttonYes = gui.CreateButton(
                    text: "Yes",
                    parent: _root.transform,
                    anchorMin: new Vector2(0.5f, 0f),
                    anchorMax: new Vector2(0.5f, 0f),
                    position: new Vector2(-70f, 36f),
                    width: 110f,
                    height: 32f)
                .GetComponent<Button>();

            _buttonNo = gui.CreateButton(
                    text: "No",
                    parent: _root.transform,
                    anchorMin: new Vector2(0.5f, 0f),
                    anchorMax: new Vector2(0.5f, 0f),
                    position: new Vector2(70f, 36f),
                    width: 110f,
                    height: 32f)
                .GetComponent<Button>();

            _root.SetActive(false);
        }
    }
}
