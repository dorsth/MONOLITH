// MONOLITH - the menu in another language. Dorsth was here.
//
// The menu is written in English (Layout.cs, Profiles.cs); this turns a text into the chosen language at the moment
// it is drawn, by looking the English text up in a table (Russian.cs, made by lang/make.py from lang/ru.tsv, which
// also checks that every Russian text fits where its English one goes). A text that is not in the table stays
// English: nothing breaks when a row is added. Settings files and profiles are the same in every language: only
// what is drawn changes, never what is stored.
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Monolith
{
    internal static partial class Lang
    {
        internal enum Language { English, Russian }

        internal static ConfigEntry<Language> Pick;
        /// <summary>Russian is on. Kept as a plain field: the menu asks for it with every text it draws.</summary>
        internal static bool Ru;

        internal static void Bind(ConfigFile c)
        {
            // the first time: the language Windows is in
            Language first = Application.systemLanguage == SystemLanguage.Russian ? Language.Russian : Language.English;
            Pick = c.Bind("Menu", "Language", first, "The language of the MONOLITH menu. / Язык меню MONOLITH.");
            Ru = Pick.Value == Language.Russian;
            Pick.SettingChanged += (s, e) => { Ru = Pick.Value == Language.Russian; Profiles.Refill(); };
        }

        // texts that end in a number: the words before it are translated, the number stays
        private static readonly string[] Numbered = { "Taunt ", "Насмешка ", "Weapon ", "Оружие ", "key ", "клавиша " };

        /// <summary>The text in the menu's language. Leading spaces (a row set in under another) are kept.</summary>
        internal static string T(string en)
        {
            if (!Ru || string.IsNullOrEmpty(en)) return en;
            string ru;
            if (Table.TryGetValue(en, out ru)) return ru;
            int lead = 0; while (lead < en.Length && en[lead] == ' ') lead++;
            if (lead > 0 && lead < en.Length && Table.TryGetValue(en.Substring(lead), out ru)) return en.Substring(0, lead) + ru;
            for (int i = 0; i < Numbered.Length; i += 2)
                if (en.Length > Numbered[i].Length && en.StartsWith(Numbered[i], System.StringComparison.Ordinal) && char.IsDigit(en[en.Length - 1]))
                    return Numbered[i + 1] + en.Substring(Numbered[i].Length);
            return en;
        }

        /// <summary>A count with its word: "2 weapons" in English; in Russian the form that needs no case endings,
        /// "оружий: 2", looked up under the English text given as colon.</summary>
        internal static string Count(int n, string one, string many, string colon)
        {
            return Ru ? T(colon) + n : n + (n == 1 ? one : many);
        }
    }
}
