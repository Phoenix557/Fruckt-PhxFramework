using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInfrastructure.Components.ManagedBehaviours;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPresenters.Pause;
using Il2CppTMPro;
using Il2CppViews.Generic;
using Il2CppViews.Pause;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Phx
{
    static class Menu
    {
        const string Prefix = "Phx_";

        enum Page
        {
            None,
            List,
            Mod
        }

        internal static bool BlocksHotkeys => _page != Page.None || _listening != null;

        static Page _page;
        static bool _failed;
        static PauseScreen _root;
        static PauseScreen _list;
        static PauseScreen _mod;
        static PathHeaderView _listHeader;
        static PathHeaderView _modHeader;
        static SettingTable _table;
        static MenuLineButton _button;
        static UnityAction _buttonClick;
        static PausePresenter _pause;
        static ModEntry _open;
        static Hotkey _listening;
        static SettingTableLine _listeningLine;
        static readonly List<UnityAction> _clicks = new List<UnityAction>();
        static readonly List<SettingTableLine> _optionLines = new List<SettingTableLine>();

        internal static void Reset()
        {
            _page = Page.None;
            _failed = false;
            _root = null;
            _list = null;
            _mod = null;
            _listHeader = null;
            _modHeader = null;
            _table = null;
            _button = null;
            _buttonClick = null;
            _pause = null;
            _open = null;
            _listening = null;
            _listeningLine = null;
            _clicks.Clear();
            _optionLines.Clear();
        }

        internal static void Tick()
        {
            bool paused = IsPaused();
            if (!paused && _page != Page.None)
                HideImmediate();
            if (paused)
                EnsureButton();
            if (_listening != null)
                CaptureKey();
        }

        static void ShowList()
        {
            if (!EnsureScreens())
                return;
            if (_page != Page.None)
                return;

            FillMods();
            WriteHeader(_listHeader, "PHX");
            if (Ride(_root, _list, false))
                _page = Page.List;
        }

        static void OpenMod(ModEntry mod)
        {
            if (_list == null || _mod == null || mod == null || _page != Page.List)
                return;

            _open = mod;
            _listening = null;
            _listeningLine = null;
            FillOptions(mod);
            WriteHeader(_modHeader, mod.Name.ToUpperInvariant());
            if (Ride(_list, _mod, false))
                _page = Page.Mod;
        }

        internal static void Back()
        {
            if (_listening != null)
            {
                CancelListen();
                return;
            }

            if (_page == Page.Mod)
            {
                if (Ride(_mod, _list, true))
                    _page = Page.List;
                _open = null;
                return;
            }

            if (_page == Page.List && Ride(_list, _root, true))
                _page = Page.None;
        }

        static void CancelListen()
        {
            _listening = null;
            Paint(_listeningLine, false);
            _listeningLine = null;
        }

        static void HideImmediate()
        {
            _listening = null;
            _listeningLine = null;
            _open = null;
            _page = Page.None;
            try { if (_mod != null) _mod.Erase(); } catch { }
            try { if (_list != null) _list.Erase(); } catch { }
        }

        static void EnsureButton()
        {
            if (_button != null || _failed)
                return;

            PauseView view = UnityEngine.Object.FindFirstObjectByType<PauseView>();
            if (view == null || view.m_settingsButton == null || view.m_settingsButton.transform.parent == null)
                return;

            MenuLineButton proto = view.m_settingsButton;
            try
            {
                MenuLineButton clone = Clone(proto, proto.transform.parent, Prefix + "Button");
                if (clone == null)
                    return;

                int index = proto.transform.GetSiblingIndex() + 1;
                Transform mods = proto.transform.parent.Find("FruitLib_ModsButton");
                if (mods != null)
                    index = mods.GetSiblingIndex() + 1;
                clone.transform.SetSiblingIndex(index);
                clone.gameObject.SetActive(true);
                clone.SetWord("PHX");
                if (clone.m_button == null)
                {
                    UnityEngine.Object.Destroy(clone.gameObject);
                    _failed = true;
                    return;
                }

                _buttonClick = (UnityAction)(Action)ShowList;
                clone.m_button.onClick.AddListener(_buttonClick);
                _button = clone;
                MelonLogger.Msg("[Phx] Pause menu PHX button added.");
            }
            catch (Exception e)
            {
                _failed = true;
                MelonLogger.Warning("[Phx] Could not add the pause button: " + e.Message);
            }
        }

        static bool EnsureScreens()
        {
            if (_list != null && _mod != null)
                return true;
            if (_failed)
                return false;

            PauseView view = UnityEngine.Object.FindFirstObjectByType<PauseView>();
            if (view == null)
                return false;

            try
            {
                _root = FindScreen(view, "RootScreen");
                PauseScreen source = FindScreen(view, "SettingsScreen");
                if (_root == null || source == null || source.Timings == null)
                {
                    Fail("the pause screens were not ready");
                    return false;
                }

                _list = CopyScreen(source, Prefix + "List");
                _mod = CopyScreen(source, Prefix + "Mod");
                if (_list == null || _mod == null)
                {
                    Fail("the settings screen could not be copied");
                    return false;
                }

                _listHeader = _list.GetComponentInChildren<PathHeaderView>(true);
                _modHeader = _mod.GetComponentInChildren<PathHeaderView>(true);
                Transform column = Column(_mod);
                _table = column == null ? null : BorrowTable(view, column);
                if (_table == null)
                {
                    Fail("no settings table to copy");
                    return false;
                }

                WriteHeader(_listHeader, "PHX");
                MelonLogger.Msg("[Phx] Settings pages built.");
                return true;
            }
            catch (Exception e)
            {
                Fail(e.Message);
                return false;
            }
        }

        static PauseScreen CopyScreen(PauseScreen source, string name)
        {
            PauseScreen clone = Clone(source, source.transform.parent, name);
            if (clone == null)
                return null;
            clone.gameObject.SetActive(true);
            clone.SetRideTimings(source.Timings);
            try { clone.Erase(); } catch { }
            MenuLineButton[] lines = clone.GetComponentsInChildren<MenuLineButton>(true);
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] != null)
                        lines[i].gameObject.SetActive(false);
                }
            }
            return clone;
        }

        static void FillMods()
        {
            Transform column = Column(_list);
            if (column == null)
                return;

            MenuLineButton[] lines = _list.GetComponentsInChildren<MenuLineButton>(true);
            MenuLineButton proto = null;
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    MenuLineButton line = lines[i];
                    if (line == null)
                        continue;
                    if (line.gameObject.name.StartsWith(Prefix + "Mod_"))
                    {
                        UnityEngine.Object.Destroy(line.gameObject);
                        continue;
                    }
                    line.gameObject.SetActive(false);
                    if (proto == null)
                        proto = line;
                }
            }
            if (proto == null)
                return;

            if (Registry.All.Count == 0)
            {
                MenuLineButton empty = Clone(proto, column, Prefix + "Mod_Empty");
                if (empty == null)
                    return;
                empty.gameObject.SetActive(true);
                empty.SetWord("NO MODS");
                return;
            }

            for (int i = 0; i < Registry.All.Count; i++)
            {
                ModEntry mod = Registry.All[i];
                MenuLineButton row = Clone(proto, column, Prefix + "Mod_" + i);
                if (row == null || row.m_button == null)
                    continue;
                row.gameObject.SetActive(true);
                row.SetWord(mod.Name.ToUpperInvariant());
                ModEntry captured = mod;
                UnityAction click = (UnityAction)(Action)(() => OpenMod(captured));
                _clicks.Add(click);
                row.m_button.onClick.AddListener(click);
            }
        }

        static void FillOptions(ModEntry mod)
        {
            if (_table == null || _table.m_linePrefab == null || _table.m_linesPivot == null)
                return;

            for (int i = 0; i < _optionLines.Count; i++)
            {
                if (_optionLines[i] != null)
                    UnityEngine.Object.Destroy(_optionLines[i].gameObject);
            }
            _optionLines.Clear();
            _listening = null;
            _listeningLine = null;

            SettingTableLine[] stale = _table.GetComponentsInChildren<SettingTableLine>(true);
            if (stale != null)
            {
                for (int i = 0; i < stale.Length; i++)
                {
                    if (stale[i] != null && stale[i] != _table.m_linePrefab)
                        stale[i].gameObject.SetActive(false);
                }
            }

            for (int i = 0; i < mod.Keys.Count; i++)
                AddKeyRow(mod.Keys[i]);
            for (int i = 0; i < mod.Flags.Count; i++)
                AddFlagRow(mod.Flags[i]);

            WriteCells(_table.m_headerCells, "OPTION", "VALUE");
            try { _table.ShareTheWidestLastCell(); } catch { }
        }

        static void AddKeyRow(Hotkey option)
        {
            SettingTableLine line = AddRow(option.Label, option.Current.ToString());
            if (line == null || line.m_button == null)
                return;
            Hotkey captured = option;
            SettingTableLine row = line;
            UnityAction click = (UnityAction)(Action)(() =>
            {
                _listening = captured;
                _listeningLine = row;
                Paint(row, true);
            });
            _clicks.Add(click);
            line.m_button.onClick.RemoveAllListeners();
            line.m_button.onClick.AddListener(click);
        }

        static void AddFlagRow(Switch option)
        {
            SettingTableLine line = AddRow(option.Label, option.On ? "ON" : "OFF");
            if (line == null || line.m_button == null)
                return;
            Switch captured = option;
            SettingTableLine row = line;
            UnityAction click = (UnityAction)(Action)(() =>
            {
                if (_listening != null)
                    return;
                captured.Set(!captured.On);
                WriteCells(row.m_cells, captured.Label.ToUpperInvariant(), captured.On ? "ON" : "OFF");
            });
            _clicks.Add(click);
            line.m_button.onClick.RemoveAllListeners();
            line.m_button.onClick.AddListener(click);
        }

        static SettingTableLine AddRow(string label, string value)
        {
            SettingTableLine line = Clone(_table.m_linePrefab, _table.m_linesPivot, Prefix + "Option_" + _optionLines.Count);
            if (line == null)
                return null;
            line.gameObject.SetActive(true);
            WriteCells(line.m_cells, label.ToUpperInvariant(), value);
            _optionLines.Add(line);
            return line;
        }

        static void CaptureKey()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || keyboard.allKeys == null || _listening == null)
                return;

            foreach (KeyControl control in keyboard.allKeys)
            {
                if (control == null || !control.wasPressedThisFrame)
                    continue;
                if (control.keyCode == Key.Escape || control.keyCode == Key.None)
                {
                    CancelListen();
                    return;
                }

                _listening.Set(control.keyCode);
                MelonLogger.Msg("[Phx] " + _listening.Mod + " " + _listening.Label + " set to " + control.keyCode + ".");
                if (_listeningLine != null)
                    WriteCells(_listeningLine.m_cells, _listening.Label.ToUpperInvariant(), control.keyCode.ToString());
                CancelListen();
                return;
            }
        }

        static void WriteHeader(PathHeaderView header, string current)
        {
            if (header == null)
                return;
            try
            {
                if (header.m_trail != null)
                    header.m_trail.text = "pause / phx";
                if (header.m_current != null)
                    header.m_current.text = current;
            }
            catch { }
        }

        static void WriteCells(Il2CppReferenceArray<TextMeshProUGUI> cells, string action, string value)
        {
            if (cells == null || cells.Length == 0)
                return;
            for (int i = 0; i < cells.Length; i++)
            {
                TextMeshProUGUI cell = cells[i];
                if (cell == null)
                    continue;
                string text = null;
                if (i == 0)
                    text = action;
                else if (i == cells.Length - 1)
                    text = value;
                bool used = text != null;
                if (cell.gameObject.activeSelf != used)
                    cell.gameObject.SetActive(used);
                if (used)
                    cell.text = text;
            }
        }

        static void Paint(SettingTableLine line, bool chosen)
        {
            if (line == null)
                return;
            try { line.SetChosen(chosen); } catch { }
        }

        static Transform Column(PauseScreen screen)
        {
            if (screen == null)
                return null;
            MenuLineButton[] lines = screen.GetComponentsInChildren<MenuLineButton>(true);
            if (lines == null || lines.Length == 0 || lines[0] == null)
                return null;
            return lines[0].transform.parent;
        }

        static SettingTable BorrowTable(PauseView view, Transform column)
        {
            SettingTable[] found = view.GetComponentsInChildren<SettingTable>(true);
            if (found == null)
                return null;
            for (int i = 0; i < found.Length; i++)
            {
                SettingTable candidate = found[i];
                if (candidate == null || candidate.gameObject.name.StartsWith(Prefix))
                    continue;
                if (candidate.gameObject.name.StartsWith("FruitLib_") || candidate.gameObject.name.StartsWith("FirstPerson_"))
                    continue;
                SettingTable copy = Clone(candidate, column, Prefix + "Table");
                if (copy == null)
                    continue;
                try { copy.m_monoFactory = candidate.m_monoFactory; } catch { }
                copy.gameObject.SetActive(true);
                return copy;
            }
            return null;
        }

        static bool Ride(PauseScreen outgoing, PauseScreen incoming, bool rewind)
        {
            if (outgoing == null || incoming == null)
                return false;
            try
            {
                MenuLineButton[] buttons = outgoing.GetComponentsInChildren<MenuLineButton>(true);
                if (buttons != null)
                {
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        if (buttons[i] != null)
                            buttons[i].Flash.Snap(false);
                    }
                }
            }
            catch { }

            try { incoming.Open(rewind); }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phx] Could not open the page: " + e.Message);
                return false;
            }

            try { outgoing.Close(rewind); }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phx] Could not close the page: " + e.Message);
            }
            return true;
        }

        static PauseScreen FindScreen(PauseView view, string name)
        {
            var screens = view.m_screens;
            if (screens == null)
                return null;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                if (screen != null && screen.name == name)
                    return screen.TryCast<PauseScreen>();
            }
            return null;
        }

        static T Clone<T>(T prototype, Transform parent, string name) where T : Component
        {
            GameObject crib = new GameObject(Prefix + "Crib");
            crib.SetActive(false);
            try
            {
                T clone = UnityEngine.Object.Instantiate(prototype, crib.transform, false);
                clone.gameObject.name = name;
                Inject(prototype.gameObject, clone.gameObject);
                clone.transform.SetParent(parent, false);
                return clone;
            }
            finally
            {
                UnityEngine.Object.Destroy(crib);
            }
        }

        static void Inject(GameObject source, GameObject clone)
        {
            ManagedBehaviour[] sources = source.GetComponentsInChildren<ManagedBehaviour>(true);
            Il2CppServices.Infrastructure.IManagedBehaviourCoreServicesProvider provider = null;
            if (sources != null)
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] != null && sources[i].m_coreServicesProvider != null)
                    {
                        provider = sources[i].m_coreServicesProvider;
                        break;
                    }
                }
            }

            if (provider != null)
            {
                ManagedBehaviour[] behaviours = clone.GetComponentsInChildren<ManagedBehaviour>(true);
                if (behaviours != null)
                {
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        if (behaviours[i] != null)
                            behaviours[i].m_coreServicesProvider = provider;
                    }
                }
            }

            SceneRevealEdgeScreenTransition[] rides = source.GetComponentsInChildren<SceneRevealEdgeScreenTransition>(true);
            SceneRevealEdgeScreenTransition template = null;
            if (rides != null)
            {
                for (int i = 0; i < rides.Length; i++)
                {
                    if (rides[i] != null && rides[i].m_revealEdges != null)
                    {
                        template = rides[i];
                        break;
                    }
                }
            }
            if (template == null)
                return;

            SceneRevealEdgeScreenTransition[] copies = clone.GetComponentsInChildren<SceneRevealEdgeScreenTransition>(true);
            if (copies == null)
                return;
            for (int i = 0; i < copies.Length; i++)
            {
                if (copies[i] == null)
                    continue;
                copies[i].m_updateLoop = template.m_updateLoop;
                copies[i].m_revealEdges = template.m_revealEdges;
            }
        }

        static bool IsPaused()
        {
            if (_pause == null)
                _pause = UnityEngine.Object.FindFirstObjectByType<PausePresenter>();
            if (_pause == null || _pause.m_pauseService == null)
                return false;
            var pause = _pause.m_pauseService.TryCast<Il2CppGame.PauseService>();
            return pause != null && pause.Paused;
        }

        static void Fail(string reason)
        {
            _failed = true;
            MelonLogger.Warning("[Phx] Settings pages were not built (" + reason + ").");
        }
    }

    [HarmonyPatch(typeof(PausePresenter), nameof(PausePresenter.Services_UI_IPauseBackNavigation_TryStepBack))]
    static class PhxBackPatch
    {
        static bool Prefix(ref bool __result)
        {
            if (!Menu.BlocksHotkeys)
                return true;
            Menu.Back();
            __result = true;
            return false;
        }
    }
}
