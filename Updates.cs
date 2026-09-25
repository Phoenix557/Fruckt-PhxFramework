using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Phx
{
    static class Updates
    {
        const string Owner = "Phoenix557";

        static readonly Dictionary<string, string> KnownRepos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Phx Pause", "Frukt-PhxPauseMenu" },
            { "Walking", "Frukt-Walking" },
            { "X-Ray", "Frukt-Xray-Organs" },
        };

        sealed class Check
        {
            public string Name;
            public string Version;
            public string Repo;
        }

        struct Outdated
        {
            public string Name;
            public string Installed;
            public string Latest;
        }

        static readonly object Gate = new object();
        static readonly List<Outdated> Found = new List<Outdated>();
        static bool _started;
        static bool _dirty;

        static Canvas _canvas;
        static TextMeshProUGUI _text;

        internal static void Start()
        {
            if (_started)
                return;
            _started = true;

            var checks = new List<Check>();
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                if (melon?.Info == null)
                    continue;
                string repo = RepoOf(melon.Info);
                if (repo != null)
                    checks.Add(new Check { Name = melon.Info.Name, Version = melon.Info.Version, Repo = repo });
            }
            if (checks.Count == 0)
                return;

            _ = CheckAll(checks);
        }

        static string RepoOf(MelonInfoAttribute info)
        {
            string link = info.DownloadLink;
            if (!string.IsNullOrEmpty(link))
            {
                const string marker = "github.com/" + Owner + "/";
                int at = link.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at >= 0)
                {
                    string repo = link.Substring(at + marker.Length).Trim('/');
                    int slash = repo.IndexOf('/');
                    if (slash >= 0)
                        repo = repo.Substring(0, slash);
                    if (repo.Length > 0)
                        return repo;
                }
            }
            return KnownRepos.TryGetValue(info.Name, out string known) ? known : null;
        }

        static async Task CheckAll(List<Check> checks)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Phx-UpdateCheck");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            foreach (Check check in checks)
            {
                try
                {
                    using HttpResponseMessage response = await http.GetAsync("https://api.github.com/repos/" + Owner + "/" + check.Repo + "/releases/latest");
                    if (response.StatusCode == HttpStatusCode.NotFound)
                        continue;
                    if (!response.IsSuccessStatusCode)
                    {
                        MelonLogger.Warning("[Phx] Could not check " + check.Name + " for updates (" + (int)response.StatusCode + ").");
                        continue;
                    }

                    using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (!json.RootElement.TryGetProperty("tag_name", out JsonElement tag))
                        continue;
                    string latest = tag.GetString();
                    if (!IsNewer(latest, check.Version))
                        continue;

                    lock (Gate)
                    {
                        Found.Add(new Outdated { Name = check.Name, Installed = Clean(check.Version), Latest = Clean(latest) });
                        _dirty = true;
                    }
                    MelonLogger.Warning("[Phx] " + check.Name + " v" + Clean(check.Version) + " is outdated, update to v" + Clean(latest) + ".");
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[Phx] Could not check " + check.Name + " for updates: " + e.Message);
                }
            }
        }

        static string Clean(string version)
        {
            version = (version ?? "").Trim();
            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version.Substring(1) : version;
        }

        static bool IsNewer(string latest, string installed)
        {
            if (!Version.TryParse(Pad(Clean(latest)), out Version remote) || !Version.TryParse(Pad(Clean(installed)), out Version local))
                return false;
            return remote > local;
        }

        static string Pad(string version)
        {
            int dash = version.IndexOfAny(new[] { '-', '+' });
            if (dash >= 0)
                version = version.Substring(0, dash);
            return version.IndexOf('.') < 0 ? version + ".0" : version;
        }

        internal static void Tick()
        {
            string message;
            lock (Gate)
            {
                if (Found.Count == 0)
                    return;
                if (!_dirty && _canvas != null && _text != null)
                    return;
                var lines = new List<string>();
                for (int i = 0; i < Found.Count; i++)
                    lines.Add(Found[i].Name + " v" + Found[i].Installed + " is outdated, update to v" + Found[i].Latest);
                message = string.Join("\n", lines);
            }

            try
            {
                if ((_canvas == null || _text == null) && !Build())
                    return;
                _text.text = message;
                lock (Gate)
                    _dirty = false;
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[Phx] Could not show the update warning: " + e.Message);
                lock (Gate)
                    Found.Clear();
            }
        }

        static bool Build()
        {
            TMP_FontAsset font = null;
            TextMeshProUGUI[] existing = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i].font != null)
                {
                    font = existing[i].font;
                    break;
                }
            }
            if (font == null)
                return false;

            var root = new GameObject("Phx_UpdateWarning");
            Object.DontDestroyOnLoad(root);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5001;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panelObject = new GameObject("Panel");
            RectTransform panel = panelObject.AddComponent<RectTransform>();
            panel.SetParent(root.transform, false);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(28f, -28f);
            Image back = panelObject.AddComponent<Image>();
            back.sprite = Solid();
            back.color = new Color(0.08f, 0.07f, 0.02f, 0.85f);
            back.raycastTarget = false;
            HorizontalLayoutGroup row = panelObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(12, 16, 10, 10);
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            ContentSizeFitter fit = panelObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var signObject = new GameObject("Sign");
            signObject.AddComponent<RectTransform>().SetParent(panel, false);
            Image sign = signObject.AddComponent<Image>();
            sign.sprite = Triangle();
            sign.color = new Color(1f, 0.78f, 0.1f);
            sign.raycastTarget = false;
            LayoutElement signSize = signObject.AddComponent<LayoutElement>();
            signSize.preferredWidth = 34f;
            signSize.preferredHeight = 30f;

            var markObject = new GameObject("Mark");
            RectTransform markRect = markObject.AddComponent<RectTransform>();
            markRect.SetParent(signObject.transform, false);
            markRect.anchorMin = Vector2.zero;
            markRect.anchorMax = Vector2.one;
            markRect.offsetMin = new Vector2(0f, -2f);
            markRect.offsetMax = Vector2.zero;
            TextMeshProUGUI mark = markObject.AddComponent<TextMeshProUGUI>();
            mark.font = font;
            mark.text = "!";
            mark.fontSize = 22f;
            mark.fontStyle = FontStyles.Bold;
            mark.color = new Color(0.1f, 0.08f, 0.02f);
            mark.alignment = TextAlignmentOptions.Bottom;
            mark.raycastTarget = false;

            var textObject = new GameObject("Text");
            textObject.AddComponent<RectTransform>().SetParent(panel, false);
            _text = textObject.AddComponent<TextMeshProUGUI>();
            _text.font = font;
            _text.fontSize = 22f;
            _text.color = new Color(1f, 0.85f, 0.35f);
            _text.alignment = TextAlignmentOptions.MidlineLeft;
            _text.enableWordWrapping = false;
            _text.raycastTarget = false;
            return true;
        }

        static Sprite Solid()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        static Sprite Triangle()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                float half = (1f - y / (float)(size - 1)) * size * 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float fromCenter = Mathf.Abs(x + 0.5f - size * 0.5f);
                    float alpha = Mathf.Clamp01(half - fromCenter + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
