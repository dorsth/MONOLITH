// TrueSens - a STRAFTAT mod (BepInEx 5). Dorsth was here.
using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrueSens
{
    // The game's sensitivity settings (controls) become TrueSens' while it's on:
    //   Mouse Sensitivity                    -> cm/360
    //   Aiming Mouse Sensitivity             -> ADS multiplier
    //   Sniper Rifle Aim Mouse Sensitivity   -> scoped multiplier
    //   Two Axis Sensitivity (switch)        -> a smiley and a reminder to enter your DPI
    //   Horizontal Sensitivity               -> mouse DPI (no slider, type it in)
    //   Vertical Sensitivity                 -> the game sensitivity that comes out (no slider, just shown)
    // Numbers you can click and type in are underlined. Turned off, everything goes back to the game's own settings,
    // untouched.
    internal static class SettingsMenu
    {
        internal enum Kind { None, Cm, Ads, Scoped, Dpi, Sens }

        private static AccessTools.FieldRef<DisplayLoadValues, bool> sensFlag, adsFlag, scopedFlag, twoAxisFlag, xFlag, yFlag;
        private static readonly List<Takeover> live = new List<Takeover>();
        private static readonly List<SwitchTakeover> switches = new List<SwitchTakeover>();

        internal static void Install(Harmony harmony)
        {
            sensFlag = AccessTools.FieldRefAccess<DisplayLoadValues, bool>("mouseSensitivity");
            adsFlag = AccessTools.FieldRefAccess<DisplayLoadValues, bool>("mouseAimSensitivity");
            scopedFlag = AccessTools.FieldRefAccess<DisplayLoadValues, bool>("mouseAimScopeSensitivity");
            twoAxisFlag = AccessTools.FieldRefAccess<DisplayLoadValues, bool>("toggleTwoAxis");
            xFlag = AccessTools.FieldRefAccess<DisplayLoadValues, bool>("mouseXSensitivity");
            yFlag = AccessTools.FieldRefAccess<DisplayLoadValues, bool>("mouseYSensitivity");
            harmony.Patch(AccessTools.Method(typeof(DisplayLoadValues), "Start"), new HarmonyMethod(typeof(SettingsMenu), nameof(StartPrefix)));
            // a click on a slider's number is for typing, not for the slider under it
            var press = new HarmonyMethod(typeof(SettingsMenu), nameof(PressPrefix));
            var drag = new HarmonyMethod(typeof(SettingsMenu), nameof(DragPrefix));
            harmony.Patch(AccessTools.Method(typeof(Slider), "OnPointerDown", new[] { typeof(PointerEventData) }), press);
            harmony.Patch(AccessTools.Method(typeof(Slider), "OnInitializePotentialDrag", new[] { typeof(PointerEventData) }), drag);
            harmony.Patch(AccessTools.Method(typeof(Slider), "OnDrag", new[] { typeof(PointerEventData) }), drag);
        }

        // the slider whose number the current press started on (it stays still until the button comes up)
        private static Slider pressedNumber;

        private static bool PressPrefix(Slider __instance, PointerEventData eventData)
        {
            pressedNumber = null;
            try
            {
                if (eventData == null || !Plugin.On.Value) return true;
                var t = __instance.GetComponent<Takeover>();
                if (t == null || !t.OverNumber(eventData.position)) return true;
                pressedNumber = __instance;
                return false;
            }
            catch { return true; }
        }

        private static bool DragPrefix(Slider __instance)
        {
            if (pressedNumber == null) return true;
            if (!Input.GetMouseButton(0)) { pressedNumber = null; return true; }
            return pressedNumber != __instance;
        }

        private static Kind KindOf(DisplayLoadValues d)
        {
            if (sensFlag(d)) return Kind.Cm;
            if (adsFlag(d)) return Kind.Ads;
            if (scopedFlag(d)) return Kind.Scoped;
            if (xFlag(d)) return Kind.Dpi;
            if (yFlag(d)) return Kind.Sens;
            return Kind.None;
        }

        // The game loads each setting's saved value when its menu first shows. These get TrueSens' values instead
        // (the game's loader is skipped while TrueSens is on, it would set them back).
        private static bool StartPrefix(DisplayLoadValues __instance)
        {
            try
            {
                if (twoAxisFlag(__instance))
                {
                    var toggle = __instance.GetComponent<Toggle>();
                    if (toggle == null) return true;
                    var sw = __instance.gameObject.GetComponent<SwitchTakeover>() ?? __instance.gameObject.AddComponent<SwitchTakeover>();
                    sw.Init(toggle);
                    return true;
                }
                Kind k = KindOf(__instance);
                if (k == Kind.None) return true;
                var slider = __instance.GetComponent<Slider>();
                if (slider == null) return true;
                var t = __instance.gameObject.GetComponent<Takeover>() ?? __instance.gameObject.AddComponent<Takeover>();
                t.Init(k, slider);
                return !Plugin.On.Value;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Sensitivity setting left as it is: " + e.Message);
                return true;
            }
        }

        internal static void RefreshAll()
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i] == null) { live.RemoveAt(i); continue; }
                live[i].Refresh();
            }
            for (int i = switches.Count - 1; i >= 0; i--)
            {
                if (switches[i] == null) { switches.RemoveAt(i); continue; }
                switches[i].Refresh();
            }
        }

        internal static void Add(Takeover t) { if (!live.Contains(t)) live.Add(t); }
        internal static void Remove(Takeover t) { live.Remove(t); }
        internal static void Add(SwitchTakeover t) { if (!switches.Contains(t)) switches.Add(t); }
        internal static void Remove(SwitchTakeover t) { switches.Remove(t); }

        internal static string Fmt(float v, string f) { return v.ToString(f, CultureInfo.InvariantCulture); }

        // A control's own label: the nearest text above it that matches
        internal static TextMeshProUGUI FindLabel(Transform from, Func<string, bool> match)
        {
            int depth = 0;
            for (Transform t = from; t != null && depth < 5; t = t.parent, depth++)
            {
                foreach (var tmp in t.GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (!string.IsNullOrEmpty(tmp.text) && match(tmp.text.ToLowerInvariant())) return tmp;
            }
            return null;
        }
    }

    // One of the game's sensitivity sliders, as a TrueSens setting.
    internal sealed class Takeover : MonoBehaviour
    {
        private SettingsMenu.Kind kind;
        private Slider slider;
        private TextMeshProUGUI label, value;
        private Behaviour gameText;
        private string labelBefore;
        private float minBefore, maxBefore;
        private bool wholeBefore, applied, ready, busy;
        private TypeIn typing;
        // the slider's own pictures (track, fill, handle), hidden for the settings that have no slider
        private readonly List<Graphic> hidden = new List<Graphic>();
        // where the label and number were, for the two settings that are laid out again without a slider
        private Vector2 labelPos, labelSize, valuePos, valueSize;
        private bool moved;
        private TextAlignmentOptions alignBefore;

        // The other panels have the label at 27.9 and the number at 17.6 (from the left, -13.7 and 148.5) over a slider at 0.
        // Without a slider the pair sits lower, in the middle of the panel, the same in both panels.
        private const float LabelX = -13.7f, ValueX = 148.5f, LabelY = 17.1f, ValueY = 14.6f;

        private bool HasSlider { get { return kind == SettingsMenu.Kind.Cm || kind == SettingsMenu.Kind.Ads || kind == SettingsMenu.Kind.Scoped; } }
        private bool Typeable { get { return kind != SettingsMenu.Kind.Sens; } }

        public void Init(SettingsMenu.Kind k, Slider s)
        {
            if (ready) return;
            kind = k;
            slider = s;
            minBefore = s.minValue;
            maxBefore = s.maxValue;
            wholeBefore = s.wholeNumbers;
            var ctv = s.GetComponentInParent<ChangeTextValue>();
            if (ctv != null)
            {
                gameText = ctv;
                value = Traverse.Create(ctv).Field("textMesh").GetValue<TextMeshProUGUI>() ?? ctv.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            label = SettingsMenu.FindLabel(s.transform.parent, IsLabel);
            if (label != null) labelBefore = label.text;
            s.onValueChanged.AddListener(Slid);
            if (value != null && Typeable)
            {
                value.raycastTarget = true;
                typing = value.gameObject.AddComponent<TypeIn>();
                typing.Setup(value, kind == SettingsMenu.Kind.Dpi ? "0123456789" : "0123456789.", ShowValue, TypedValue);
            }
            ready = true;
            SettingsMenu.Add(this);
            Refresh();
        }

        private void OnDestroy() { SettingsMenu.Remove(this); }

        public bool OverNumber(Vector2 screen) { return typing != null && typing.Over(screen); }

        private bool IsLabel(string s)
        {
            if (!s.Contains("sensitivity")) return false;
            switch (kind)
            {
                case SettingsMenu.Kind.Cm: return s.Contains("mouse") && !s.Contains("aim") && !s.Contains("sniper");
                case SettingsMenu.Kind.Ads: return s.Contains("aim") && !s.Contains("sniper");
                case SettingsMenu.Kind.Scoped: return s.Contains("sniper") || s.Contains("scope");
                case SettingsMenu.Kind.Dpi: return s.Contains("horizontal");
                default: return s.Contains("vertical");
            }
        }

        private string Title()
        {
            switch (kind)
            {
                case SettingsMenu.Kind.Cm: return "cm/360";
                case SettingsMenu.Kind.Ads: return "ADS Multiplier";
                case SettingsMenu.Kind.Scoped: return "Scoped Multiplier";
                case SettingsMenu.Kind.Dpi: return "Mouse DPI";
                default: return "Game Sensitivity";
            }
        }

        // Shows TrueSens' setting while it's on, the game's own while it's off.
        public void Refresh()
        {
            if (!ready || slider == null) return;
            // changing the slider's range moves its value and tells everyone: not a change you made
            busy = true;
            try
            {
                if (Plugin.On.Value) Apply();
                else Revert();
            }
            finally { busy = false; }
        }

        private void Apply()
        {
            if (!applied)
            {
                for (int i = 0; i < slider.onValueChanged.GetPersistentEventCount(); i++)
                    slider.onValueChanged.SetPersistentListenerState(i, UnityEventCallState.Off);
                if (gameText != null) gameText.enabled = false;
                applied = true;
            }
            if (HasSlider)
            {
                slider.wholeNumbers = false;
                if (kind == SettingsMenu.Kind.Cm) { slider.minValue = Plugin.MinCm; slider.maxValue = 100f; }
                else { slider.minValue = Plugin.MinMult; slider.maxValue = Plugin.MaxMult; }
                slider.SetValueWithoutNotify(Current());
            }
            else if (hidden.Count == 0)
            {
                // a typed number or one that's only shown: no slider (its pictures hidden, the number stays)
                slider.interactable = false;
                foreach (var g in slider.GetComponentsInChildren<Graphic>(true))
                    if (g.enabled && !(g is TMP_Text)) { g.enabled = false; hidden.Add(g); }
            }
            if (!HasSlider && !moved && label != null && value != null)
            {
                moved = true;
                var l = label.rectTransform;
                var v = value.rectTransform;
                labelPos = l.anchoredPosition; labelSize = l.sizeDelta;
                valuePos = v.anchoredPosition; valueSize = v.sizeDelta;
                l.anchoredPosition = new Vector2(LabelX, LabelY);
                l.sizeDelta = new Vector2(302.5f, 19.9f);
                v.anchoredPosition = new Vector2(ValueX, ValueY);
                v.sizeDelta = new Vector2(50.3f, 28.6f);
                alignBefore = value.alignment;
                if (kind == SettingsMenu.Kind.Dpi)
                {
                    // the DPI right after its label instead of at the far right, so it reads as one line
                    value.alignment = TextAlignmentOptions.MidlineLeft;
                    var lp = l.localPosition;
                    float labelLeft = lp.x - l.pivot.x * l.rect.width;
                    float textW = label.GetPreferredValues(Title()).x;
                    var vp = v.localPosition;
                    v.localPosition = new Vector3(labelLeft + textW + 14f + v.pivot.x * v.rect.width, vp.y, vp.z);
                }
            }
            if (label != null) label.text = Title();
            // not while you're typing a new one in
            if (value != null && (typing == null || !typing.Editing)) value.text = ShowValue();
        }

        private void Revert()
        {
            if (!applied) return;
            applied = false;
            for (int i = 0; i < slider.onValueChanged.GetPersistentEventCount(); i++)
                slider.onValueChanged.SetPersistentListenerState(i, UnityEventCallState.RuntimeOnly);
            foreach (var g in hidden) if (g != null) g.enabled = true;
            hidden.Clear();
            if (moved)
            {
                moved = false;
                label.rectTransform.anchoredPosition = labelPos; label.rectTransform.sizeDelta = labelSize;
                value.rectTransform.anchoredPosition = valuePos; value.rectTransform.sizeDelta = valueSize;
                value.alignment = alignBefore;
            }
            slider.interactable = true;
            slider.minValue = minBefore;
            slider.maxValue = maxBefore;
            slider.wholeNumbers = wholeBefore;
            slider.SetValueWithoutNotify(PlayerPrefs.GetFloat(PrefKey(), slider.value));
            if (label != null && labelBefore != null) label.text = labelBefore;
            if (gameText != null)
            {
                gameText.enabled = true;
                ((ChangeTextValue)gameText).ChangeIntValue(slider);
            }
        }

        private string PrefKey()
        {
            switch (kind)
            {
                case SettingsMenu.Kind.Cm: return "mouseSensitivity";
                case SettingsMenu.Kind.Ads: return "mouseAimSensitivity";
                case SettingsMenu.Kind.Scoped: return "mouseAimScopeSensitivity";
                case SettingsMenu.Kind.Dpi: return "mouseXSensitivity";
                default: return "mouseYSensitivity";
            }
        }

        private float Current()
        {
            switch (kind)
            {
                case SettingsMenu.Kind.Cm: return Plugin.Cm360.Value;
                case SettingsMenu.Kind.Ads: return Plugin.AdsMult.Value;
                case SettingsMenu.Kind.Scoped: return Plugin.ScopedMult.Value;
                case SettingsMenu.Kind.Dpi: return Plugin.Dpi.Value;
                default: return Plugin.HipSens();
            }
        }

        private void Set(float v)
        {
            switch (kind)
            {
                case SettingsMenu.Kind.Cm: Plugin.Cm360.Value = Mathf.Clamp(Mathf.Round(v * 10f) / 10f, Plugin.MinCm, Plugin.MaxCm); break;
                case SettingsMenu.Kind.Ads: Plugin.AdsMult.Value = Mathf.Clamp(Mathf.Round(v * 100f) / 100f, Plugin.MinMult, Plugin.MaxMult); break;
                case SettingsMenu.Kind.Scoped: Plugin.ScopedMult.Value = Mathf.Clamp(Mathf.Round(v * 100f) / 100f, Plugin.MinMult, Plugin.MaxMult); break;
                case SettingsMenu.Kind.Dpi: Plugin.Dpi.Value = Mathf.Clamp(Mathf.RoundToInt(v), 100, 32000); break;
            }
        }

        private void Slid(float v)
        {
            if (busy || !applied || !HasSlider) return;
            Set(v);
        }

        // underlined when you can click it and type
        private string ShowValue()
        {
            string s;
            switch (kind)
            {
                case SettingsMenu.Kind.Cm: s = SettingsMenu.Fmt(Plugin.Cm360.Value, "0.0"); break;
                case SettingsMenu.Kind.Dpi: s = Plugin.Dpi.Value.ToString(CultureInfo.InvariantCulture); break;
                case SettingsMenu.Kind.Sens: return SettingsMenu.Fmt(Plugin.HipSens(), "0.###");
                default: s = SettingsMenu.Fmt(Current(), "0.00"); break;
            }
            return "<u>" + s + "</u>";
        }

        private bool TypedValue(string text)
        {
            float v;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            Set(v);
            return true;
        }
    }

    // The two axis switch does nothing while TrueSens is on, so it's a little note instead: a smiley where the tick goes,
    // and a reminder to set your DPI. Back to the game's switch when TrueSens is off.
    internal sealed class SwitchTakeover : MonoBehaviour
    {
        private const string Note = "You must set your DPI below";
        private static readonly Color Box = Color.black, Face = new Color32(0xb5, 0xf7, 0xb7, 0xff); // STRAFTH's mint
        private Toggle toggle;
        private TextMeshProUGUI label, smiley;
        private string labelBefore;
        private Selectable.Transition transitionBefore;
        private Color boxBefore;
        private bool tickWasOn, applied, ready;

        public void Init(Toggle t)
        {
            if (ready) return;
            toggle = t;
            label = SettingsMenu.FindLabel(t.transform, s => s.Contains("axis"));
            if (label != null) labelBefore = label.text;
            ready = true;
            SettingsMenu.Add(this);
            Refresh();
        }

        private void OnDestroy() { SettingsMenu.Remove(this); }

        public void Refresh()
        {
            if (!ready || toggle == null) return;
            if (Plugin.On.Value)
            {
                if (!applied)
                {
                    applied = true;
                    transitionBefore = toggle.transition;
                    toggle.transition = Selectable.Transition.None;
                    toggle.interactable = false;
                    if (toggle.graphic != null) { tickWasOn = toggle.graphic.enabled; toggle.graphic.enabled = false; }
                    if (toggle.targetGraphic != null) { boxBefore = toggle.targetGraphic.color; toggle.targetGraphic.color = Box; }
                    MakeSmiley();
                }
                if (smiley != null) smiley.gameObject.SetActive(true);
                if (label != null) label.text = Note;
            }
            else if (applied)
            {
                applied = false;
                toggle.interactable = true;
                toggle.transition = transitionBefore;
                if (toggle.graphic != null) toggle.graphic.enabled = tickWasOn || toggle.graphic.enabled;
                if (toggle.targetGraphic != null) toggle.targetGraphic.color = boxBefore;
                if (smiley != null) smiley.gameObject.SetActive(false);
                if (label != null && labelBefore != null) label.text = labelBefore;
                if (Settings.Instance != null) toggle.SetIsOnWithoutNotify(Settings.Instance.toggleTwoAxis);
            }
        }

        // a smiley filling the box, in the label's font and color
        private void MakeSmiley()
        {
            if (smiley != null) return;
            RectTransform box = toggle.targetGraphic != null ? toggle.targetGraphic.rectTransform : (RectTransform)toggle.transform;
            var go = new GameObject("TrueSens Smiley", typeof(RectTransform));
            go.transform.SetParent(box, false);
            smiley = go.AddComponent<TextMeshProUGUI>();
            if (label != null) { smiley.font = label.font; smiley.fontSharedMaterial = label.fontSharedMaterial; smiley.fontStyle = label.fontStyle; }
            smiley.color = Face;
            // a filled smiley face when the font has one, a ":)" turned on its side when it doesn't
            bool face = smiley.font != null && smiley.font.HasCharacter('\u263B', true, true);
            smiley.text = face ? "\u263B" : ":)";
            smiley.alignment = TextAlignmentOptions.Center;
            smiley.enableWordWrapping = false;
            smiley.enableAutoSizing = false;
            smiley.fontSize = 22f;
            smiley.raycastTarget = false;
            var rt = smiley.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            // a little bigger than the box, and a little up: the glyph sits low in its line
            rt.offsetMin = new Vector2(-8f, -1f);
            rt.offsetMax = new Vector2(8f, 7f);
            if (!face) rt.localEulerAngles = new Vector3(0f, 0f, -90f);
        }
    }

    // A number you can click and type in: enter or clicking away keeps it, escape leaves it as it was. It looks for clicks
    // on itself every frame, because the game's menu can have something see-through drawn over the numbers that would
    // catch a normal click first.
    internal sealed class TypeIn : MonoBehaviour
    {
        private TextMeshProUGUI text;
        private string allowed, typed = "";
        private Func<string> show;
        private Func<string, bool> commit;
        private bool editing, fresh, autoSized;

        public void Setup(TextMeshProUGUI t, string chars, Func<string> shown, Func<string, bool> typedIn)
        {
            text = t;
            allowed = chars;
            show = shown;
            commit = typedIn;
        }

        public bool Editing { get { return editing; } }

        private bool Over() { return Over(Input.mousePosition); }

        public bool Over(Vector2 screen)
        {
            if (text == null || !text.isActiveAndEnabled) return false;
            var canvas = text.canvas;
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            // a little room around the number, it's small
            var rt = text.rectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, cam, out local)) return false;
            Rect r = rt.rect;
            float w = Mathf.Max(r.width, text.preferredWidth);
            return local.y >= r.yMin - 6f && local.y <= r.yMax + 6f && local.x >= r.xMax - w - 10f && local.x <= r.xMax + 10f
                || r.Contains(local);
        }

        private void Update()
        {
            if (text == null || !text.isActiveAndEnabled || !Plugin.On.Value) { if (editing) Finish(false); return; }
            bool click = Input.GetMouseButtonDown(0);
            if (!editing)
            {
                // starts with the number as it is, selected: the first key you type replaces it
                if (click && Over()) { editing = true; fresh = true; typed = ""; HoldSize(); Draw(); }
                return;
            }
            if (click && !Over()) { Finish(true); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Finish(false); return; }
            string keys = Input.inputString;
            if (keys.Length == 0) return;
            foreach (char c in keys)
            {
                if (fresh && c != '\n' && c != '\r') { fresh = false; typed = ""; }
                if (c == '\b') { if (typed.Length > 0) typed = typed.Substring(0, typed.Length - 1); }
                else if (c == '\n' || c == '\r') { Finish(true); return; }
                else if (c == ',' && allowed.IndexOf('.') >= 0 && typed.IndexOf('.') < 0) typed += ".";
                else if (allowed.IndexOf(c) >= 0 && typed.Length < 7 && !(c == '.' && typed.IndexOf('.') >= 0)) typed += c;
            }
            Draw();
        }

        // Drawn like the number always is (underlined, same size and place), with a soft highlight behind it while
        // you type. No blinking line: it took room and made the number jump.
        private void Draw()
        {
            string mark = "<mark=#" + ColorUtility.ToHtmlStringRGB(text.color) + "40>";
            text.text = fresh ? mark + show() + "</mark>" : mark + "<u>" + (typed.Length > 0 ? typed : "\u00A0") + "</u></mark>";
        }

        // the number keeps the size it has now while you type (a shorter or longer number would resize it)
        private void HoldSize()
        {
            if (!text.enableAutoSizing) return;
            float size = text.fontSize;
            text.enableAutoSizing = false;
            text.fontSize = size;
            autoSized = true;
        }

        private void Finish(bool keep)
        {
            editing = false;
            if (keep && !fresh && typed.Length > 0) commit(typed);
            fresh = false;
            if (autoSized) { autoSized = false; text.enableAutoSizing = true; }
            text.text = show();
        }

        private void OnDisable() { if (editing) Finish(false); }
    }
}
