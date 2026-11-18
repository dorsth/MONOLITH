#if !DORSTH_BUNDLE   // ezCrosshair's own window: MONOLITH has its menu instead
// ezCrosshair - a STRAFTAT mod (BepInEx 5). Dorsth was here.
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EzCrosshair
{
    // A text field in the menu (every one: the search at the top, find a player, the hex codes), in FieldStyle: darker than the window, a thin edge that lights up while
    // typing. It's a normal text field (click to place the cursor, drag, double-click or shift+arrows to select,
    // ctrl+a/c/v/x). A field made with a key keeps its focus and cursor when the page is built again under it.
    internal sealed class TextBox : TMP_InputField
    {
        // the same field as the STRAFTH menu's; this menu doesn't use every option
#pragma warning disable CS0649
        public sealed class Options
        {
            public string Text = "";
            // shown in front and not part of the text, like the "#" of a hex code
            public string Prefix;
            // a magnifier in front, like the search box
            public bool Icon;
            public string Placeholder;
            public int Limit;
            // turns a typed or pasted character into what goes in, or '\0' to drop it
            public Func<char, char> Filter;
            public Action<string> Changed;
            public Action<string> Left;
            public float Size = 18f;
            // keeps its focus when the page is built again under it
            public string Key;
            // takes the focus right away, so you can type without clicking it first
            public bool Focus;
        }
#pragma warning restore CS0649

        private Image edge;
        private TextMeshProUGUI hint;
        private string key;
        private int pendingCaret = -1, caretFrames;
        private string pendingText;

        // the field that had the focus last, so a page built again can give it back
        private static string focusKey;
        private static int focusCaret, focusFrame = -10;

        // the wheel over the field scrolls the page, like anywhere else on it
        public override void OnScroll(PointerEventData e)
        {
            if (transform.parent != null) ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, e, ExecuteEvents.scrollHandler);
        }

        // The cursor: solid while you type or move it, blinking at the usual Windows pace once you stop.
        private string blinkText;
        private int blinkPos = -1;
        private float lastInput;
        private bool caretShown = true;

        private void Blink()
        {
            if (text != blinkText || stringPosition != blinkPos)
            {
                blinkText = text;
                blinkPos = stringPosition;
                lastInput = Time.unscaledTime;
            }
            float idle = Time.unscaledTime - lastInput;
            // on for 0.53 s, off for 0.53 s, starting over (on) with every key or cursor move
            bool show = idle % 1.06f < 0.53f;
            if (show == caretShown) return;
            caretShown = show;
            var c = Ui.Txt;
            c.a = show ? 1f : 0f;
            caretColor = c;
        }

        // the last frame any field was being typed in (escape there stops typing, it doesn't close the menu)
        public static int LastTypingFrame { get; private set; } = -10;

        protected override void LateUpdate()
        {
            base.LateUpdate();
            // the cursor goes back where it was once the field has the focus, and stays there the next few frames
            // (taking the focus moves it), until something is typed
            if (pendingCaret >= 0 && isFocused)
            {
                if (text != pendingText || caretFrames <= 0) pendingCaret = -1;
                else
                {
                    int c = Mathf.Clamp(pendingCaret, 0, text.Length);
                    stringPosition = selectionStringAnchorPosition = selectionStringFocusPosition = c;
                    caretFrames--;
                }
            }
            if (isFocused) Blink();
            if (isFocused)
            {
                LastTypingFrame = Time.frameCount;
                if (key != null)
                {
                    focusKey = key;
                    focusCaret = stringPosition;
                    focusFrame = Time.frameCount;
                }
            }
        }

        public static TextBox Make(Transform parent, float x, float y, float w, float h, Options o)
        {
            var bg = Ui.Panel(parent, FieldStyle.Fill, Ui.RoundSmall);
            bg.raycastTarget = true;
            Ui.At(bg.rectTransform, x, y, w, h);
            var edge = Ui.Edge(bg, FieldStyle.EdgeIdle);
            edge.raycastTarget = false;

            float left = 12f;
            if (o.Icon)
            {
                var glass = Ui.Node("icon", bg.transform).gameObject.AddComponent<RawImage>();
                glass.texture = FieldStyle.Icon();
                glass.color = Ui.Mix(0.5f);
                glass.raycastTarget = false;
                Ui.At(glass.rectTransform, 12f, (h - 18f) / 2f, 18f, 18f);
                left = 40f;
            }
            if (!string.IsNullOrEmpty(o.Prefix))
            {
                var pre = Ui.Label(bg.transform, o.Prefix, o.Size, Ui.Txt, TextAlignmentOptions.Left);
                pre.enableWordWrapping = false;
                pre.raycastTarget = false;
                pre.characterSpacing = 0f;
                Ui.Fill(pre.rectTransform);
                // measured without the margin, so the text starts right after it
                pre.margin = Vector4.zero;
                float pw = pre.GetPreferredValues(o.Prefix).x;
                pre.margin = new Vector4(left, 0f, 0f, 0f);
                left += pw + 2f;
            }

            var area = Ui.Node("area", bg.transform);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(left, 0f);
            area.offsetMax = new Vector2(-10f, 0f);
            area.gameObject.AddComponent<RectMask2D>();
            // middle by the font's line height, not the letters': the text doesn't move as you type
            var text = Ui.Label(area, "", o.Size, Ui.Txt, TextAlignmentOptions.Left);
            text.enableWordWrapping = false;
            text.richText = false;
            Ui.Fill(text.rectTransform);
            text.margin = Vector4.zero;
            TextMeshProUGUI hint = null;
            if (!string.IsNullOrEmpty(o.Placeholder))
            {
                hint = Ui.Label(area, o.Placeholder, o.Size, Ui.Mix(0.45f), TextAlignmentOptions.Left);
                hint.enableWordWrapping = false;
                hint.raycastTarget = false;
                Ui.Fill(hint.rectTransform);
                hint.margin = Vector4.zero;
            }

            // set up while inactive, so the field starts with its parts in place
            var go = bg.gameObject;
            go.SetActive(false);
            var f = go.AddComponent<TextBox>();
            f.edge = edge;
            f.hint = hint;
            f.key = o.Key;
            f.textViewport = area;
            f.textComponent = text;
            f.placeholder = hint;
            f.targetGraphic = bg;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            f.lineType = LineType.SingleLine;
            f.contentType = ContentType.Custom;
            f.characterValidation = CharacterValidation.None;
            f.characterLimit = o.Limit;
            f.richText = false;
            f.restoreOriginalTextOnEscape = o.Key == null;
            f.onFocusSelectAll = o.Key == null;
            if (o.Filter != null) { var filter = o.Filter; f.onValidateInput = (s, i, c) => filter(c); }
            f.customCaretColor = true;
            f.caretColor = Ui.Txt;
            f.caretWidth = 2;
            // blinking is done here (Blink), not by the field
            f.caretBlinkRate = 0f;
            f.selectionColor = Ui.A(Ui.Txt, 0.35f);
            f.SetTextWithoutNotify(o.Text ?? "");
            if (o.Changed != null) f.onValueChanged.AddListener(v => o.Changed(v));
            // enter (or escape) ends typing too, like a click elsewhere
            if (o.Left != null) f.onEndEdit.AddListener(v => o.Left(v));
            // the hint goes once you click in (only its color: the field switches it on and off by itself)
            f.onSelect.AddListener(v => { f.edge.color = FieldStyle.EdgeTyping; if (f.hint != null) f.hint.alpha = 0f; });
            f.onDeselect.AddListener(v =>
            {
                f.edge.color = FieldStyle.EdgeIdle;
                if (f.hint != null) f.hint.alpha = 1f;
                if (o.Left != null) o.Left(f.text);
            });
            go.SetActive(true);

            // typing in this field when the page was built again: carry on where the cursor was
            bool carryOn = o.Key != null && o.Key == focusKey && Time.frameCount - focusFrame <= 2;
            if (carryOn || o.Focus)
            {
                f.pendingCaret = carryOn ? focusCaret : f.text.Length;
                f.pendingText = f.text;
                f.caretFrames = 4;
                f.Select();
                f.ActivateInputField();
            }
            return f;
        }
    }
}
#endif
