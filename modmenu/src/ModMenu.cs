// Mod Menu's API, for MONOLITH. Dorsth was here.
//
// MONOLITH replaces kestrel's Mod Menu (https://github.com/kestrel-straftat/mod-menu, MIT). Mods talk to Mod Menu through
// the functions below: they look for it by its plugin id and call it by name, so this file carries the same id, the same
// assembly name (ModMenu) and the same public types, namespaces and members as Mod Menu 1.2.0. Nothing here draws:
// it records what each mod asks for, and MONOLITH's "Other mods" page shows it. No asset bundle, so it also loads on
// macOS ARM64, where Mod Menu's bundle has no Metal shaders.
// The public API follows Mod Menu's source (MIT License, Copyright (c) 2025 kestrel); its text is in LICENSE-ModMenu.txt.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using ModMenu.Behaviours.OptionList;
using ModMenu.Behaviours.OptionList.Dummies;
using ModMenu.Behaviours.OptionList.ValueControllers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[assembly: AssemblyTitle("ModMenu")]
[assembly: AssemblyDescription("Mod Menu's API for MONOLITH: the settings pages of other mods show in MONOLITH's menu.")]
[assembly: AssemblyVersion("0.0.0.0")]

namespace ModMenu
{
    [BepInPlugin("kestrel.straftat.modmenu", "Mod Menu (MONOLITH)", "1.2.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        /// <summary>Read by MONOLITH: this is the stand-in, so content builders run in MONOLITH's menu.</summary>
        public const bool Monolith = true;

        private void Awake()
        {
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            Logger.LogInfo("Mod Menu's API is provided by MONOLITH: the Mod Menu pages of other mods show in MONOLITH's menu (F2).");
        }
    }

    internal static class Owner
    {
        private static readonly Dictionary<Assembly, PluginInfo> Cache = new Dictionary<Assembly, PluginInfo>();

        // as Mod Menu: the first loaded plugin whose class is in the calling assembly
        internal static PluginInfo Of(Assembly asm)
        {
            PluginInfo found;
            if (Cache.TryGetValue(asm, out found)) return found;
            found = null;
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance != null && info.Instance.GetType().Assembly == asm) { found = info; break; }
            }
            Cache[asm] = found;
            return found;
        }
    }

    /// <summary>For MONOLITH: what a mod's content builder adds to its page, as plain data, so MONOLITH needs none of
    /// these types. Each row is a dictionary: "kind" is config (one of the mod's own settings, "index" says which, in
    /// the order Mod Menu lists them), header, text, button or value.</summary>
    public static class Pages
    {
        public static bool Has(string guid) => ModMenu.Api.ModMenuCustomisation.Builders.ContainsKey(guid);

        /// <summary>Runs the builder the way Mod Menu does: on a list that already holds the mod's own setting rows
        /// (configRows of them, headers included), so Insert positions land where they do in Mod Menu.</summary>
        public static List<Dictionary<string, object>> Run(string guid, int configRows)
        {
            var rows = new List<Dictionary<string, object>>();
            Action<ModMenu.Api.OptionListContext> builder;
            if (!ModMenu.Api.ModMenuCustomisation.Builders.TryGetValue(guid, out builder)) return rows;

            var root = new GameObject("MONOLITH page " + guid) { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            var config = new Dictionary<Transform, int>();
            for (int i = 0; i < configRows; i++)
            {
                var p = new GameObject("config " + i); p.transform.SetParent(root.transform, false);
                config[p.transform] = i;
            }
            var ctx = new ModMenu.Api.OptionListContext(root.transform);
            try { builder(ctx); }
            catch (Exception e) { Debug.LogWarning("[Mod Menu] the page of " + guid + " could not be made: " + e); }

            Func<string> info = () => !string.IsNullOrEmpty(ctx.InfoDescription) ? ctx.InfoDescription : ctx.InfoTitle;
            foreach (Transform child in root.transform)
            {
                int index;
                if (config.TryGetValue(child, out index)) { rows.Add(new Dictionary<string, object> { { "kind", "config" }, { "index", index } }); continue; }
                var item = child.GetComponent<OptionListItem>();
                if (item == null) continue;
                GameObject go = child.gameObject;
                var row = new Dictionary<string, object>
                {
                    { "label", item.NameText }, { "active", (Func<bool>)(() => go != null && go.activeSelf) },
                    { "hover", (Action)item.Hovered }, { "unhover", (Action)item.Unhovered }, { "info", info },
                };
                if (item is TextDummy) row["kind"] = ((TextDummy)item).Header ? "header" : "text";
                else if (item is ButtonDummy)
                {
                    var b = (ButtonDummy)item;
                    row["kind"] = "button";
                    row["button"] = (Func<string>)(() => b.ButtonText);
                    row["click"] = (Action)(() => b.button.onClick.Invoke());
                }
                else if (item is BoxedValueController)
                {
                    var v = (BoxedValueController)item;
                    row["kind"] = "value";
                    row["type"] = v.ValueType; row["get"] = v.getter; row["set"] = v.setter; row["default"] = v.DefaultValue;
                    var s = item as NumericSliderValueController;
                    if (s != null && s.slider != null) { row["min"] = s.slider.minValue; row["max"] = s.slider.maxValue; }
                }
                else continue;
                rows.Add(row);
            }
            return rows;
        }
    }
}

namespace ModMenu.Api
{
    public static class ModMenuCustomisation
    {
        // keyed by plugin guid (read by MONOLITH, same names as in Mod Menu)
        internal static Dictionary<string, Action<OptionListContext>> Builders { get; } = new Dictionary<string, Action<OptionListContext>>();
        internal static Dictionary<string, Sprite> Icons { get; } = new Dictionary<string, Sprite>();
        internal static Dictionary<string, string> Descriptions { get; } = new Dictionary<string, string>();
        internal static HashSet<ConfigEntryBase> HiddenConfigEntries { get; } = new HashSet<ConfigEntryBase>();

        /// <summary>Registers a content builder that will be run while the option list for the current plugin's config file is made</summary>
        public static void RegisterContentBuilder(Action<OptionListContext> builder) => Add(Builders, Assembly.GetCallingAssembly(), builder, "RegisterContentBuilder");

        /// <summary>Hides the specified <see cref="ConfigEntryBase"/></summary>
        public static void HideEntry(ConfigEntryBase entry) => HiddenConfigEntries.Add(entry);

        /// <summary>Hides the specified <see cref="ConfigEntryBase"/>s</summary>
        public static void HideEntries(IEnumerable<ConfigEntryBase> entries) { foreach (var entry in entries) HiddenConfigEntries.Add(entry); }

        /// <summary>Sets the icon of the current plugin</summary>
        public static void SetPluginIcon(Sprite icon) => Add(Icons, Assembly.GetCallingAssembly(), icon, "SetPluginIcon");

        /// <summary>Sets the description of the current plugin</summary>
        public static void SetPluginDescription(string description) => Add(Descriptions, Assembly.GetCallingAssembly(), description, "SetPluginDescription");

        private static void Add<T>(Dictionary<string, T> to, Assembly caller, T value, string what)
        {
            PluginInfo info = Owner.Of(caller);
            if (info == null) throw new InvalidOperationException(what + " must be called from an assembly containing a BepInEx plugin.");
            if (to.ContainsKey(info.Metadata.GUID)) throw new InvalidOperationException(what + " can only be called once per assembly.");
            to.Add(info.Metadata.GUID, value);
        }
    }

    public partial class OptionListContext
    {
        /// <summary>The root transform of the option list</summary>
        public Transform Root { get; }
        /// <summary>For MONOLITH: what the mod put in the info panel last.</summary>
        internal string InfoTitle, InfoSubtitle, InfoDescription; internal Action ResetAction;

        internal OptionListContext(Transform root) { Root = root; }

        public void SetInfoPanelContents(string title, string subtitle, string description) { InfoTitle = title; InfoSubtitle = subtitle; InfoDescription = description; }
        public void ClearInfoPanelContents() { InfoTitle = InfoSubtitle = InfoDescription = null; }
        public void ShowInfoPanelResetButton(Action resetAction) { ResetAction = resetAction; }

        private T Make<T>(string name) where T : OptionListItem
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            var label = new GameObject("name"); label.transform.SetParent(go.transform, false);
            T item = go.AddComponent<T>();
            item.nameText = label.AddComponent<TextMeshProUGUI>();
            return item;
        }

        private static TComp Part<TComp>(OptionListItem item, string name) where TComp : Component
        {
            var go = new GameObject(name); go.transform.SetParent(item.transform, false);
            return go.AddComponent<TComp>();
        }

        private T PrependItem<T>(T item) where T : OptionListItem { item.transform.SetAsFirstSibling(); return item; }

        private T InsertItem<T>(int position, T item) where T : OptionListItem
        {
            var active = new List<Transform>();
            foreach (Transform child in Root) if (child.gameObject.activeSelf) active.Add(child);
            if (active.Count > 0) item.transform.SetSiblingIndex(active[Math.Max(0, Math.Min(position, active.Count - 1))].GetSiblingIndex());
            return item;
        }

        private T Value<T, TValue>(string nameText, Func<TValue> getter, Action<TValue> setter) where T : BoxedValueController
        {
            T c = Make<T>(nameText);
            c.SetupFromValues(getter, setter);
            c.NameText = nameText;
            return c;
        }

        // ---- text and buttons
        public TextDummy AppendHeader(string text) { var t = Make<TextDummy>("header"); t.Header = true; t.Text = text; return t; }
        public TextDummy PrependHeader(string text) => PrependItem(AppendHeader(text));
        public TextDummy InsertHeader(int position, string text) => InsertItem(position, AppendHeader(text));

        public TextDummy AppendTextBox(string text) { var t = Make<TextDummy>("text"); t.Text = text; return t; }
        public TextDummy PrependTextBox(string text) => PrependItem(AppendTextBox(text));
        public TextDummy InsertTextBox(int position, string text) => InsertItem(position, AppendTextBox(text));

        public ButtonDummy AppendButton(string nameText, string buttonText, Action onClick)
        {
            var b = Make<ButtonDummy>("button");
            b.button = Part<Button>(b, "button");
            b.buttonTextMeshProText = Part<TextMeshProUGUI>(b, "label");
            if (onClick != null) b.button.onClick.AddListener(() => onClick());
            b.NameText = nameText; b.ButtonText = buttonText;
            return b;
        }
        public ButtonDummy PrependButton(string nameText, string buttonText, Action onClick) => PrependItem(AppendButton(nameText, buttonText, onClick));
        public ButtonDummy InsertButton(int position, string nameText, string buttonText, Action onClick) => InsertItem(position, AppendButton(nameText, buttonText, onClick));

        // ---- values
        public BoolValueController AppendCheckbox(string nameText, Func<bool> getter, Action<bool> setter) { var c = Value<BoolValueController, bool>(nameText, getter, setter); c.toggle = Part<Toggle>(c, "toggle"); return c; }
        public BoolValueController PrependCheckbox(string nameText, Func<bool> getter, Action<bool> setter) => PrependItem(AppendCheckbox(nameText, getter, setter));
        public BoolValueController InsertCheckbox(int position, string nameText, Func<bool> getter, Action<bool> setter) => InsertItem(position, AppendCheckbox(nameText, getter, setter));

        public StringValueController AppendStringInput(string nameText, Func<string> getter, Action<string> setter) { var c = Value<StringValueController, string>(nameText, getter, setter); c.inputField = Part<TMP_InputField>(c, "input"); return c; }
        public StringValueController PrependStringInput(string nameText, Func<string> getter, Action<string> setter) => PrependItem(AppendStringInput(nameText, getter, setter));
        public StringValueController InsertStringInput(int position, string nameText, Func<string> getter, Action<string> setter) => InsertItem(position, AppendStringInput(nameText, getter, setter));

        public ColorValueController AppendColorInput(string nameText, Func<Color> getter, Action<Color> setter) { var c = Value<ColorValueController, Color>(nameText, getter, setter); c.inputField = Part<TMP_InputField>(c, "input"); c.preview = Part<Image>(c, "preview"); return c; }
        public ColorValueController PrependColorInput(string nameText, Func<Color> getter, Action<Color> setter) => PrependItem(AppendColorInput(nameText, getter, setter));
        public ColorValueController InsertColorInput(int position, string nameText, Func<Color> getter, Action<Color> setter) => InsertItem(position, AppendColorInput(nameText, getter, setter));

        public KeyboardShortcutValueController AppendKeyboardShortcutInput(string nameText, Func<KeyboardShortcut> getter, Action<KeyboardShortcut> setter) { var c = Value<KeyboardShortcutValueController, KeyboardShortcut>(nameText, getter, setter); c.buttonText = Part<TextMeshProUGUI>(c, "key"); return c; }
        public KeyboardShortcutValueController PrependKeyboardShortcutInput(string nameText, Func<KeyboardShortcut> getter, Action<KeyboardShortcut> setter) => PrependItem(AppendKeyboardShortcutInput(nameText, getter, setter));
        public KeyboardShortcutValueController InsertKeyboardShortcutInput(int position, string nameText, Func<KeyboardShortcut> getter, Action<KeyboardShortcut> setter) => InsertItem(position, AppendKeyboardShortcutInput(nameText, getter, setter));

        public KeyCodeValueController AppendKeyCodeInput(string nameText, Func<KeyCode> getter, Action<KeyCode> setter) { var c = Value<KeyCodeValueController, KeyCode>(nameText, getter, setter); c.buttonText = Part<TextMeshProUGUI>(c, "key"); return c; }
        public KeyCodeValueController PrependKeyCodeInput(string nameText, Func<KeyCode> getter, Action<KeyCode> setter) => PrependItem(AppendKeyCodeInput(nameText, getter, setter));
        public KeyCodeValueController InsertKeyCodeInput(int position, string nameText, Func<KeyCode> getter, Action<KeyCode> setter) => InsertItem(position, AppendKeyCodeInput(nameText, getter, setter));

        public QuaternionValueController AppendQuaternionInput(string nameText, Func<Quaternion> getter, Action<Quaternion> setter) { var c = Value<QuaternionValueController, Quaternion>(nameText, getter, setter); c.xInputField = Part<TMP_InputField>(c, "x"); c.yInputField = Part<TMP_InputField>(c, "y"); c.zInputField = Part<TMP_InputField>(c, "z"); c.wInputField = Part<TMP_InputField>(c, "w"); return c; }
        public QuaternionValueController PrependQuaternionInput(string nameText, Func<Quaternion> getter, Action<Quaternion> setter) => PrependItem(AppendQuaternionInput(nameText, getter, setter));
        public QuaternionValueController InsertQuaternionInput(int position, string nameText, Func<Quaternion> getter, Action<Quaternion> setter) => InsertItem(position, AppendQuaternionInput(nameText, getter, setter));

        public Vector2ValueController AppendVector2Input(string nameText, Func<Vector2> getter, Action<Vector2> setter) { var c = Value<Vector2ValueController, Vector2>(nameText, getter, setter); c.xInputField = Part<TMP_InputField>(c, "x"); c.yInputField = Part<TMP_InputField>(c, "y"); return c; }
        public Vector2ValueController PrependVector2Input(string nameText, Func<Vector2> getter, Action<Vector2> setter) => PrependItem(AppendVector2Input(nameText, getter, setter));
        public Vector2ValueController InsertVector2Input(int position, string nameText, Func<Vector2> getter, Action<Vector2> setter) => InsertItem(position, AppendVector2Input(nameText, getter, setter));

        public Vector3ValueController AppendVector3Input(string nameText, Func<Vector3> getter, Action<Vector3> setter) { var c = Value<Vector3ValueController, Vector3>(nameText, getter, setter); c.xInputField = Part<TMP_InputField>(c, "x"); c.yInputField = Part<TMP_InputField>(c, "y"); c.zInputField = Part<TMP_InputField>(c, "z"); return c; }
        public Vector3ValueController PrependVector3Input(string nameText, Func<Vector3> getter, Action<Vector3> setter) => PrependItem(AppendVector3Input(nameText, getter, setter));
        public Vector3ValueController InsertVector3Input(int position, string nameText, Func<Vector3> getter, Action<Vector3> setter) => InsertItem(position, AppendVector3Input(nameText, getter, setter));

        public Vector4ValueController AppendVector4Input(string nameText, Func<Vector4> getter, Action<Vector4> setter) { var c = Value<Vector4ValueController, Vector4>(nameText, getter, setter); c.xInputField = Part<TMP_InputField>(c, "x"); c.yInputField = Part<TMP_InputField>(c, "y"); c.zInputField = Part<TMP_InputField>(c, "z"); c.wInputField = Part<TMP_InputField>(c, "w"); return c; }
        public Vector4ValueController PrependVector4Input(string nameText, Func<Vector4> getter, Action<Vector4> setter) => PrependItem(AppendVector4Input(nameText, getter, setter));
        public Vector4ValueController InsertVector4Input(int position, string nameText, Func<Vector4> getter, Action<Vector4> setter) => InsertItem(position, AppendVector4Input(nameText, getter, setter));

        public DropdownValueController AppendDropdown<T>(string nameText, Func<T> getter, Action<T> setter) where T : Enum { var c = Value<DropdownValueController, T>(nameText, getter, setter); c.dropdown = Part<TMP_Dropdown>(c, "dropdown"); return c; }
        public DropdownValueController PrependDropdown<T>(string nameText, Func<T> getter, Action<T> setter) where T : Enum => PrependItem(AppendDropdown(nameText, getter, setter));
        public DropdownValueController InsertDropdown<T>(int position, string nameText, Func<T> getter, Action<T> setter) where T : Enum => InsertItem(position, AppendDropdown(nameText, getter, setter));

        public NumericInputFieldValueController AppendNumericInputField<T>(string nameText, Func<T> getter, Action<T> setter)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
        { var c = Value<NumericInputFieldValueController, T>(nameText, getter, setter); c.inputField = Part<TMP_InputField>(c, "input"); return c; }
        public NumericInputFieldValueController PrependNumericInputField<T>(string nameText, Func<T> getter, Action<T> setter)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable => PrependItem(AppendNumericInputField(nameText, getter, setter));
        public NumericInputFieldValueController InsertNumericInputField<T>(int position, string nameText, Func<T> getter, Action<T> setter)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable => InsertItem(position, AppendNumericInputField(nameText, getter, setter));

        public NumericSliderValueController AppendNumericSlider<T>(string nameText, Func<T> getter, Action<T> setter, float minValue, float maxValue)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable
        {
            if (minValue >= maxValue) throw new ArgumentException("minValue must be < maxValue");
            var c = Value<NumericSliderValueController, T>(nameText, getter, setter);
            c.slider = Part<Slider>(c, "slider"); c.inputField = Part<TMP_InputField>(c, "input");
            c.slider.minValue = minValue; c.slider.maxValue = maxValue;
            return c;
        }
        public NumericSliderValueController PrependNumericSlider<T>(string nameText, Func<T> getter, Action<T> setter, float minValue, float maxValue)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable => PrependItem(AppendNumericSlider(nameText, getter, setter, minValue, maxValue));
        public NumericSliderValueController InsertNumericSlider<T>(int position, string nameText, Func<T> getter, Action<T> setter, float minValue, float maxValue)
            where T : struct, IComparable, IComparable<T>, IConvertible, IEquatable<T>, IFormattable => InsertItem(position, AppendNumericSlider(nameText, getter, setter, minValue, maxValue));
    }
}

namespace ModMenu.Behaviours.OptionList
{
    public class OptionListItem : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public delegate void ItemHoverHandler();

        public TextMeshProUGUI nameText;

        public string NameText { get => nameText != null ? nameText.text : ""; set { if (nameText != null) nameText.text = value; } }

        public event ItemHoverHandler OnItemHovered;
        public event ItemHoverHandler OnItemUnhovered;

        /// <summary>MONOLITH calls these when its cursor comes onto or leaves the row.</summary>
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) => OnItemHovered?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) => OnItemUnhovered?.Invoke();
        internal void Hovered() => OnItemHovered?.Invoke();
        internal void Unhovered() => OnItemUnhovered?.Invoke();
    }
}

namespace ModMenu.Behaviours.OptionList.Dummies
{
    public class ButtonDummy : OptionListItem
    {
        public TextMeshProUGUI buttonTextMeshProText;
        public Button button;
        public string ButtonText { get => buttonTextMeshProText != null ? buttonTextMeshProText.text : ""; set { if (buttonTextMeshProText != null) buttonTextMeshProText.text = value; } }
    }

    public class TextDummy : OptionListItem
    {
        internal bool Header;
        public string Text { get => NameText; set => NameText = value; }
        public Color Color { get => nameText.color; set => nameText.color = value; }
        public TMP_Style Style { get => nameText.textStyle; set => nameText.textStyle = value; }
        public float FontSize { get => nameText.fontSize; set => nameText.fontSize = value; }
    }
}

namespace ModMenu.Behaviours.OptionList.ValueControllers
{
    /// <summary>An option list item that reads and writes a value through a getter and setter</summary>
    public abstract class BoxedValueController : OptionListItem
    {
        public Func<object> getter;
        public Action<object> setter;
        internal object DefaultValue;
        public Type ValueType { get; private set; }

        public abstract void UpdateAppearance();
        public virtual void ResetValue() => setter(DefaultValue);

        internal virtual void SetupFromValues<T>(Func<T> getter, Action<T> setter)
        {
            DefaultValue = default(T);
            ValueType = typeof(T);
            this.getter = () => getter();
            this.setter = value => setter((T)value);
        }
    }

    /// <summary>Typed versions of a <see cref="BoxedValueController"/>'s getter and setter</summary>
    public abstract class ValueController<T> : BoxedValueController
    {
        public T GetValue() => (T)getter();
        public void SetValue(T value) => setter(value);
    }

    public class BoolValueController : ValueController<bool> { public override void UpdateAppearance() { } public Toggle toggle; public void OnCheckboxValueChanged(bool value) => SetValue(value); }
    public class StringValueController : ValueController<string> { public override void UpdateAppearance() { } public TMP_InputField inputField; public void OnInputFieldEndEdit(string value) => SetValue(value); }
    public class ColorValueController : ValueController<Color> { public override void UpdateAppearance() { } public TMP_InputField inputField; public Image preview; public void OnInputFieldEndEdit(string value) { Color c; if (ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out c)) SetValue(c); } }
    public class DropdownValueController : BoxedValueController { public override void UpdateAppearance() { } public TMP_Dropdown dropdown; public void OnDropdownValueChanged(int index) { Array v = Enum.GetValues(ValueType); if (index >= 0 && index < v.Length) setter(v.GetValue(index)); } }
    public abstract class KeybindValueController<T> : ValueController<T> { public TextMeshProUGUI buttonText; public void OnRebindButtonPressed() { } public override void ResetValue() => base.ResetValue(); public override void UpdateAppearance() { } }
    public class KeyCodeValueController : KeybindValueController<KeyCode> { }
    public class KeyboardShortcutValueController : KeybindValueController<KeyboardShortcut> { }
    public class NumericInputFieldValueController : BoxedValueController { public override void UpdateAppearance() { } public TMP_InputField inputField; public void OnInputFieldEndEdit(string value) { try { setter(Convert.ChangeType(value, ValueType, System.Globalization.CultureInfo.InvariantCulture)); } catch { } } }
    public class NumericSliderValueController : BoxedValueController
    {
        public override void UpdateAppearance() { }
        public Slider slider; public TMP_InputField inputField;
        public void OnSliderValueChanged(float value) { try { setter(Convert.ChangeType(value, ValueType, System.Globalization.CultureInfo.InvariantCulture)); } catch { } }
        public void OnInputFieldEndEdit(string value) { try { setter(Convert.ChangeType(value, ValueType, System.Globalization.CultureInfo.InvariantCulture)); } catch { } }
    }
    public class Vector2ValueController : ValueController<Vector2> { public override void UpdateAppearance() { } public TMP_InputField xInputField, yInputField; public void OnInputFieldEndEdit() { } }
    public class Vector3ValueController : ValueController<Vector3> { public override void UpdateAppearance() { } public TMP_InputField xInputField, yInputField, zInputField; public void OnInputFieldEndEdit() { } }
    public class Vector4ValueController : ValueController<Vector4> { public override void UpdateAppearance() { } public TMP_InputField xInputField, yInputField, zInputField, wInputField; public void OnInputFieldEndEdit() { } }
    public class QuaternionValueController : ValueController<Quaternion> { public override void UpdateAppearance() { } public TMP_InputField xInputField, yInputField, zInputField, wInputField; public void OnInputFieldEndEdit() { } }
}
