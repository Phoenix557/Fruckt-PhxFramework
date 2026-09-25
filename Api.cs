using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine.InputSystem;

namespace Phx
{
    /// <summary>
    /// One mod installed on this framework. Register it from the mod's startup,
    /// then add the options its page should show.
    /// </summary>
    public sealed class ModEntry
    {
        public string Name { get; }

        internal readonly List<Hotkey> Keys = new List<Hotkey>();
        internal readonly List<Switch> Flags = new List<Switch>();

        internal ModEntry(string name)
        {
            Name = name;
        }

        public Hotkey Key(string label, Key fallback)
        {
            for (int i = 0; i < Keys.Count; i++)
            {
                if (Keys[i].Label == label)
                    return Keys[i];
            }

            var option = new Hotkey(Name, label, Config.ReadKey(Name, label, fallback));
            Keys.Add(option);
            return option;
        }

        public Switch Toggle(string label, bool fallback)
        {
            for (int i = 0; i < Flags.Count; i++)
            {
                if (Flags[i].Label == label)
                    return Flags[i];
            }

            var option = new Switch(Name, label, Config.Flag(Name, label, fallback));
            Flags.Add(option);
            return option;
        }
    }

    public sealed class Hotkey
    {
        public string Label { get; }
        public Key Current { get; internal set; }

        internal Hotkey(string mod, string label, Key current)
        {
            Label = label;
            Current = current;
            Mod = mod;
        }

        internal string Mod { get; }

        public bool Pressed()
        {
            if (Menu.BlocksHotkeys || Current == Key.None)
                return false;
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[Current].wasPressedThisFrame;
        }

        internal void Set(Key key)
        {
            Current = key;
            Config.Save();
        }
    }

    public sealed class Switch
    {
        public string Label { get; }
        public bool On { get; internal set; }

        internal Switch(string mod, string label, bool on)
        {
            Label = label;
            On = on;
            Mod = mod;
        }

        internal string Mod { get; }

        internal void Set(bool on)
        {
            On = on;
            Config.Save();
        }
    }

    public static class Mods
    {
        public static ModEntry Register(string name)
        {
            if (string.IsNullOrEmpty(name))
                name = "Mod";
            return Registry.GetOrAdd(name);
        }
    }

    internal static class Registry
    {
        internal static readonly List<ModEntry> All = new List<ModEntry>();

        internal static ModEntry GetOrAdd(string name)
        {
            Config.EnsureLoaded();
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Name == name)
                    return All[i];
            }

            var entry = new ModEntry(name);
            All.Add(entry);
            All.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entry;
        }
    }

    internal static class Config
    {
        static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool _loaded;

        internal static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            try
            {
                string path = FilePath();
                if (!File.Exists(path))
                    return;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int split = line.IndexOf('=');
                    if (split <= 0)
                        continue;
                    Values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phx] Could not read settings: " + e.Message);
            }
        }

        internal static Key ReadKey(string mod, string label, Key fallback)
        {
            EnsureLoaded();
            if (Values.TryGetValue(Slot(mod, label), out string text) && Enum.TryParse(text, true, out Key key) && key != Key.None)
                return key;
            return fallback;
        }

        internal static bool Flag(string mod, string label, bool fallback)
        {
            EnsureLoaded();
            if (!Values.TryGetValue(Slot(mod, label), out string text))
                return fallback;
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1")
                return true;
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0")
                return false;
            return fallback;
        }

        internal static void Save()
        {
            try
            {
                var lines = new List<string>();
                for (int i = 0; i < Registry.All.Count; i++)
                {
                    ModEntry mod = Registry.All[i];
                    for (int k = 0; k < mod.Keys.Count; k++)
                        lines.Add(Slot(mod.Name, mod.Keys[k].Label) + "=" + mod.Keys[k].Current);
                    for (int f = 0; f < mod.Flags.Count; f++)
                        lines.Add(Slot(mod.Name, mod.Flags[f].Label) + "=" + (mod.Flags[f].On ? "true" : "false"));
                }
                File.WriteAllText(FilePath(), string.Join("\n", lines.ToArray()));
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phx] Could not save settings: " + e.Message);
            }
        }

        static string Slot(string mod, string label)
        {
            return mod + "." + label;
        }

        static string FilePath()
        {
            return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "Phx.cfg");
        }
    }
}
