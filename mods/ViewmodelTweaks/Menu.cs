// ViewmodelTweaks' page in Mod Menu (kestrel): the weapons you added, each with its own settings, and a + to add one.
// Only touched when Mod Menu is installed; without it the settings are in the config file as usual.

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using ModMenu.Api;
using ModMenu.Behaviours.OptionList;
using ModMenu.Behaviours.OptionList.Dummies;
using ModMenu.Behaviours.OptionList.ValueControllers;
using UnityEngine;

namespace ViewmodelTweaks
{
    /// <summary>
    /// Which rows of the page show (no Mod Menu types here, so this runs without it too). Mod Menu's search can switch
    /// rows back on, so the ones of unused weapons are switched off again each frame while the page is open.
    /// </summary>
    internal static class Menu
    {
        internal static readonly List<KeyValuePair<Slot, List<GameObject>>> Rows = new List<KeyValuePair<Slot, List<GameObject>>>();
        internal static GameObject AddRow;

        internal static void Register() => Page.Register();
        internal static void HideOld(ConfigEntryBase[] entries) => Page.HideOld(entries);

        internal static void Keep()
        {
            if (Rows.Count == 0) return;
            List<GameObject> firstRow = Rows[0].Value;
            GameObject first = firstRow.Count > 0 ? firstRow[0] : null;
            if (first == null || !first.transform.parent || !first.transform.parent.gameObject.activeInHierarchy) return;
            foreach (var row in Rows)
                if (!row.Key.Used) foreach (GameObject go in row.Value) if (go != null && go.activeSelf) go.SetActive(false);
            if (AddRow != null && AddRow.activeSelf == ViewmodelTweaksPlugin.Slots.All(s => s.Used)) AddRow.SetActive(!AddRow.activeSelf);
        }

        internal static void Refresh()
        {
            foreach (var row in Rows)
                foreach (GameObject go in row.Value) if (go != null && go.activeSelf != row.Key.Used) go.SetActive(row.Key.Used);
            if (AddRow != null) AddRow.SetActive(!ViewmodelTweaksPlugin.Slots.All(s => s.Used));
        }
    }

    internal static class Page
    {
        internal static void Register()
        {
            ModMenuCustomisation.HideEntries(ViewmodelTweaksPlugin.Slots.SelectMany(s => s.Entries));
            ModMenuCustomisation.SetPluginDescription("Viewmodel FOV, position, sway, bob and arms. Below: weapons with their own values; every other weapon uses the general ones.");
            ModMenuCustomisation.RegisterContentBuilder(Build);
            // Mod Menu skips the custom page and says "No options found" when a mod has no visible config entries,
            // and every entry here is hidden behind the page, so tell it this mod does have options.
            Type mod = HarmonyLib.AccessTools.TypeByName("ModMenu.Mods.Mod");
            info = HarmonyLib.AccessTools.Field(mod, "info");
            guid = HarmonyLib.AccessTools.Field(info.FieldType, "guid");
            new HarmonyLib.Harmony(ViewmodelTweaksPlugin.Guid + ".menu").Patch(HarmonyLib.AccessTools.PropertyGetter(mod, "HasAnyConfigs"),
                postfix: new HarmonyLib.HarmonyMethod(typeof(Page), nameof(HasOptions)));
        }

        private static System.Reflection.FieldInfo info, guid;

        private static void HasOptions(object __instance, ref bool __result)
        {
            if (!__result && (string)guid.GetValue(info.GetValue(__instance)) == ViewmodelTweaksPlugin.Guid) __result = true;
        }

        internal static void HideOld(ConfigEntryBase[] entries) => ModMenuCustomisation.HideEntries(entries);

        private static void Build(OptionListContext c)
        {
            Menu.Rows.Clear();
            foreach (Slot s in ViewmodelTweaksPlugin.Slots)
            {
                Slot slot = s;
                var rows = new List<GameObject>();
                var refresh = new List<Action>();   // the controllers' redraws, not the controllers: no Mod Menu type in a captured field
                TextDummy header = c.AppendHeader(Weapons.Label(slot.Weapon.Value));
                rows.Add(header.gameObject);
                // kept as object: a Mod Menu type in a field the lambda below captures made every type search in this file
                // fail when Mod Menu is not installed (HarmonyX logged it at each AccessTools.TypeByName)
                object head = header;
                DropdownValueController pick = c.AppendDropdown<Weapon>("Weapon", () => slot.Weapon.Value, v =>
                {
                    slot.Weapon.Value = v;
                    if (v != Weapon.None) ((TextDummy)head).Text = Weapons.Label(v);
                    ViewmodelTweaksPlugin.Changed();
                    Menu.Refresh();
                });
                rows.Add(pick.gameObject); refresh.Add(pick.UpdateAppearance);
                refresh.Add(Slider(c, rows, "Viewmodel FOV", slot.Fov, 30f, 120f).UpdateAppearance);
                refresh.Add(Slider(c, rows, "Viewmodel X Offset", slot.X, -5f, 5f).UpdateAppearance);
                refresh.Add(Slider(c, rows, "Viewmodel Y Offset", slot.Y, -5f, 5f).UpdateAppearance);
                refresh.Add(Slider(c, rows, "Viewmodel Z Offset", slot.Z, -5f, 5f).UpdateAppearance);
                BoolValueController arms = c.AppendCheckbox("Invisible Arms", () => slot.NoArms.Value, v => { slot.NoArms.Value = v; ViewmodelTweaksPlugin.Changed(); });
                rows.Add(arms.gameObject); refresh.Add(arms.UpdateAppearance);
                ButtonDummy remove = c.AppendButton("", "remove", () =>
                {
                    slot.Weapon.Value = Weapon.None;
                    ViewmodelTweaksPlugin.Changed();
                    Menu.Refresh();
                });
                rows.Add(remove.gameObject);
                // adding one: the first weapon not added yet, from the defaults
                Menu.Rows.Add(new KeyValuePair<Slot, List<GameObject>>(slot, rows));
                Adders[slot] = () =>
                {
                    Weapon next = Enum.GetValues(typeof(Weapon)).Cast<Weapon>().FirstOrDefault(w => w != Weapon.None && ViewmodelTweaksPlugin.Slots.All(o => o.Weapon.Value != w));
                    if (next == Weapon.None) return;
                    foreach (ConfigEntryBase e in new ConfigEntryBase[] { slot.Fov, slot.X, slot.Y, slot.Z, slot.NoArms }) e.BoxedValue = e.DefaultValue;
                    slot.Weapon.Value = next;
                    ((TextDummy)head).Text = Weapons.Label(next);
                    foreach (Action r in refresh) r();
                };
            }
            ButtonDummy add = c.AppendButton("Add a weapon", "+", () =>
            {
                Slot free = ViewmodelTweaksPlugin.Slots.FirstOrDefault(s => !s.Used);
                if (free == null) return;
                Adders[free]();
                ViewmodelTweaksPlugin.Changed();
                Menu.Refresh();
            });
            Menu.AddRow = add.gameObject;
            Menu.Refresh();
        }

        private static readonly Dictionary<Slot, Action> Adders = new Dictionary<Slot, Action>();

        private static BoxedValueController Slider(OptionListContext c, List<GameObject> rows, string name, ConfigEntry<float> entry, float min, float max)
        {
            NumericSliderValueController s = c.AppendNumericSlider<float>(name, () => entry.Value, v => { entry.Value = v; ViewmodelTweaksPlugin.Changed(); }, min, max);
            rows.Add(s.gameObject);
            return s;
        }
    }
}
