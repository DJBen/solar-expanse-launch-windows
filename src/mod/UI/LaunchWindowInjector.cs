#nullable disable
using System;
using System.Collections;
using System.Reflection;
using Language;
using Manager;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SolarExpanseLaunchWindows.UI
{
    internal static class LaunchWindowInjector
    {
        internal static readonly FieldInfo FieldShowBtn =
            typeof(NotificationManager).GetField("showNotificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);
        internal static readonly FieldInfo FieldHistoryGO =
            typeof(NotificationManager).GetField("notificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Inject(NotificationManager nm)
        {
            try
            {
                Button showBtn = FieldShowBtn?.GetValue(nm) as Button;
                if (showBtn == null) { Plugin.Log.LogError("[LW] showNotificationHistory not found"); return; }

                GameObject historyGO = FieldHistoryGO?.GetValue(nm) as GameObject;
                if (historyGO == null) { Plugin.Log.LogError("[LW] notificationHistory not found"); return; }

                Canvas canvas = showBtn.GetComponentInParent<Canvas>();
                if (canvas == null) { Plugin.Log.LogError("[LW] Canvas not found"); return; }

                TMP_FontAsset font = historyGO.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
                // Prefer Oxanium (the game's heading font) for a cleaner look; fall back to Inter.
                // For table value cells, prefer a true monospace font if the game ever ships one;
                // otherwise Orbitron (near-uniform glyph widths, matches the game's HUD numerals).
                TMP_FontAsset oxanium = null, mono = null, orbitron = null;
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    var n = f.name ?? "";
                    if (oxanium  == null && n.IndexOf("Oxanium",  StringComparison.OrdinalIgnoreCase) >= 0) oxanium  = f;
                    if (mono     == null && n.IndexOf("Mono",     StringComparison.OrdinalIgnoreCase) >= 0) mono     = f;
                    if (orbitron == null && n.IndexOf("Orbitron", StringComparison.OrdinalIgnoreCase) >= 0) orbitron = f;
                }
                TMP_FontAsset headerFont = oxanium ?? font;
                TMP_FontAsset tableFont  = mono ?? orbitron;
                LWTooltip.Font = font;

                // ── Panel: clone notificationHistory for background style ──────────────────────
                GameObject panelGO = UnityEngine.Object.Instantiate(historyGO, canvas.transform);
                panelGO.name = "modLaunchWindowsPanel";
                // Insert just below the notification history so notifications always render on top.
                panelGO.transform.SetSiblingIndex(historyGO.transform.GetSiblingIndex());

                for (int i = panelGO.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(panelGO.transform.GetChild(i).gameObject);
                foreach (var sr in panelGO.GetComponents<ScrollRect>())   UnityEngine.Object.DestroyImmediate(sr);
                foreach (var lg in panelGO.GetComponents<LayoutGroup>()) UnityEngine.Object.DestroyImmediate(lg);
                var existCSF = panelGO.GetComponent<ContentSizeFitter>();
                if (existCSF != null) UnityEngine.Object.DestroyImmediate(existCSF);

                Image panelBg = panelGO.GetComponent<Image>() ?? panelGO.AddComponent<Image>();
                Image bgSrc = historyGO.GetComponent<Image>();
                if (bgSrc?.sprite != null)
                { panelBg.sprite = bgSrc.sprite; panelBg.color = bgSrc.color; panelBg.type = bgSrc.type; panelBg.material = bgSrc.material; }
                else panelBg.color = new Color(0.07f, 0.08f, 0.10f, 0.96f);
                panelBg.raycastTarget = true;

                foreach (var cg in panelGO.GetComponents<CanvasGroup>())
                { cg.interactable = true; cg.blocksRaycasts = true; }

                panelGO.AddComponent<LayoutElement>().ignoreLayout = true;

                RectTransform panelRT = panelGO.GetComponent<RectTransform>();
                panelRT.anchorMin = new Vector2(0.5f, 0.5f);
                panelRT.anchorMax = new Vector2(0.5f, 0.5f);
                panelRT.pivot     = new Vector2(0f, 1f);
                panelRT.sizeDelta = new Vector2(1135f, 570f);
                panelRT.anchoredPosition = new Vector2(-9999f, -9999f);

                // ── VLG drives all rows ───────────────────────────────────────────────────────
                var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
                vlg.childControlHeight     = true;
                vlg.childControlWidth      = true;
                vlg.childForceExpandHeight = false;
                vlg.childForceExpandWidth  = true;
                vlg.spacing = 2f;
                vlg.padding = new RectOffset(9, 9, 5, 5);

                // Row 1: Header (From / Craft / Refresh / ×)
                var headerGO  = MakeHRow("Header", panelGO.transform, 33f, 5f);
                var originBtn = MakeButton("OriginBtn", headerGO.transform, font, "From: — ▼",
                    expandWidth: true, height: 27f,
                    bgColor: new Color(0.06f, 0.16f, 0.22f, 0.55f));

                var craftBtn = MakeButton("CraftBtn", headerGO.transform, font, "Craft: — ▼",
                    expandWidth: true, height: 27f,
                    bgColor: new Color(0.06f, 0.16f, 0.22f, 0.55f));

                var clearBtn = MakeButton("ClearBtn", headerGO.transform, font, "Clear",
                    fixedWidth: 60f, height: 27f,
                    bgColor: new Color(0.10f, 0.12f, 0.15f, 0.0f),
                    hoverColor: new Color(0.55f, 0.10f, 0.10f, 0.8f));
                AddTooltip(clearBtn.gameObject, "Remove all destinations from the list.");
                MakeButton("RefreshBtn", headerGO.transform, font, "Refresh",
                    fixedWidth: 78f, height: 27f,
                    bgColor: new Color(0.10f, 0.12f, 0.15f, 0.0f));
                var closeBtn = MakeButton("CloseBtn", headerGO.transform, font, "×",
                    fixedWidth: 30f, height: 27f,
                    bgColor: new Color(0.20f, 0.05f, 0.05f, 0.0f),
                    hoverColor: new Color(0.55f, 0.10f, 0.10f, 0.8f));

                // Row 2: Column headers — use game locale keys so they match the player's language.
                var colHdrGO = MakeHRow("ColHdr", panelGO.transform, 22f, 0f);
                MakeColLabel("CH0",   colHdrGO.transform, headerFont ?? font, Loc("Game.UI.Windows.Windows.PlanMissionWindow.Destination",   "DESTINATION"), 15f, 158f, TextAlignmentOptions.Left, bold: true);
                // 18px spacer + (groupW−18) labels keep OPTIMAL/FASTEST left-aligned under the NT-offset "Departs" sub-header.
                MakeColLabel("CHNT1", colHdrGO.transform, font, "", 15f, 18f, TextAlignmentOptions.Left);
                MakeColLabel("CH1",   colHdrGO.transform, headerFont ?? font, Loc("Game.UI.Windows.Windows.PlanMissionWindow.ButtonOptimal", "OPTIMAL"), 15f, 425f, TextAlignmentOptions.Left, bold: true);
                MakeColLabel("CHSep", colHdrGO.transform, font, "", 15f, 12f, TextAlignmentOptions.Left);
                MakeColLabel("CHNT2", colHdrGO.transform, font, "", 15f, 18f, TextAlignmentOptions.Left);
                MakeColLabel("CH2",   colHdrGO.transform, headerFont ?? font, Loc("Game.UI.Windows.Windows.PlanMissionWindow.ButtonFastest", "FASTEST"), 15f, 442f, TextAlignmentOptions.Left, bold: true);

                // Row 4: Sub-header — cells must match LaunchWindowPanel OPT_*/FST_* constants.
                // Optimal: dep=118 dv=95 arr=100 fuel=130 (443); Fastest: dep=120 dv=110 arr=100 fuel=130 (460)
                var subHdrGO = MakeHRow("SubHdr", panelGO.transform, 21f, 0f);
                MakeColLabel("SH0", subHdrGO.transform, font, "", 15f, 158f, TextAlignmentOptions.Left, muted: true);
                var (optDepBtn, optDepTMP, optDvBtn, optDvTMP) = MakeSubHdrGroup(subHdrGO.transform, font, headerFont, isOptimal: true);
                MakeColLabel("SHSep", subHdrGO.transform, font, "", 15f, 12f, TextAlignmentOptions.Left);
                var (fstDepBtn, fstDepTMP, fstDvBtn, fstDvTMP) = MakeSubHdrGroup(subHdrGO.transform, font, headerFont, isOptimal: false);

                // Divider
                Divider("Div", panelGO.transform);

                // Scroll area (takes all remaining height via flexibleHeight)
                var scrollGO = new GameObject("Scroll", typeof(RectTransform));
                scrollGO.transform.SetParent(panelGO.transform, false);
                var scrollLE = scrollGO.AddComponent<LayoutElement>();
                scrollLE.minHeight     = 45f;
                scrollLE.flexibleHeight = 1f;

                // Scrollbar (5px, right edge)
                var sbGO = new GameObject("Scrollbar", typeof(RectTransform));
                sbGO.transform.SetParent(scrollGO.transform, false);
                var sbRT = sbGO.GetComponent<RectTransform>();
                sbRT.anchorMin = new Vector2(1f, 0f); sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 0.5f);
                sbRT.sizeDelta = new Vector2(8f, 0f); sbRT.anchoredPosition = Vector2.zero;
                sbGO.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.10f, 0.9f);
                var sbComp = sbGO.AddComponent<Scrollbar>();
                sbComp.direction = Scrollbar.Direction.BottomToTop;
                var sbSlide = new GameObject("Slide", typeof(RectTransform));
                sbSlide.transform.SetParent(sbGO.transform, false);
                var sbSlideRT = sbSlide.GetComponent<RectTransform>();
                sbSlideRT.anchorMin = Vector2.zero; sbSlideRT.anchorMax = Vector2.one; sbSlideRT.sizeDelta = Vector2.zero;
                var sbHandle = new GameObject("Handle", typeof(RectTransform));
                sbHandle.transform.SetParent(sbSlide.transform, false);
                var sbHandleRT = sbHandle.GetComponent<RectTransform>();
                sbHandleRT.anchorMin = Vector2.zero; sbHandleRT.anchorMax = Vector2.one; sbHandleRT.sizeDelta = Vector2.zero;
                var sbHandleImg = sbHandle.AddComponent<Image>(); sbHandleImg.color = new Color(0.05f, 0.62f, 0.68f, 0.9f);
                sbComp.handleRect = sbHandleRT; sbComp.targetGraphic = sbHandleImg;

                // Viewport
                var vpGO = new GameObject("Viewport", typeof(RectTransform));
                vpGO.transform.SetParent(scrollGO.transform, false);
                var vpRT = vpGO.GetComponent<RectTransform>();
                vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
                vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = new Vector2(-10f, 0f);
                vpGO.AddComponent<RectMask2D>();
                // Invisible raycast surface so the mouse wheel scrolls from anywhere in the
                // list, not just over raycastable row elements (name buttons, checkboxes).
                var vpImg = vpGO.AddComponent<Image>();
                vpImg.color = Color.clear;
                vpImg.raycastTarget = true;

                // Content
                var contentGO = new GameObject("Content", typeof(RectTransform));
                contentGO.transform.SetParent(vpGO.transform, false);
                var contentRT = contentGO.GetComponent<RectTransform>();
                contentRT.anchorMin = new Vector2(0f, 1f); contentRT.anchorMax = new Vector2(1f, 1f);
                contentRT.pivot = new Vector2(0.5f, 1f); contentRT.sizeDelta = Vector2.zero;
                var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
                contentVLG.childControlHeight = true; contentVLG.childControlWidth = true;
                contentVLG.childForceExpandHeight = false; contentVLG.childForceExpandWidth = true;
                contentVLG.spacing = 2f; contentVLG.padding = new RectOffset(3, 3, 3, 3);
                contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var scrollRect = scrollGO.AddComponent<ScrollRect>();
                scrollRect.viewport = vpRT; scrollRect.content = contentRT;
                scrollRect.verticalScrollbar = sbComp;
                scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                scrollRect.horizontal = false; scrollRect.vertical = true;
                scrollRect.scrollSensitivity = 30f; scrollRect.movementType = ScrollRect.MovementType.Clamped;

                // Divider
                Divider("Div2", panelGO.transform);

                // Search row
                var searchRowGO = MakeHRow("SearchRow", panelGO.transform, 27f, 6f);
                MakeColLabel("SrchLbl", searchRowGO.transform, font, "+ Add:", 15f, 57f, TextAlignmentOptions.Right, muted: true);
                var presetsBtn = MakeButton("PresetsBtn", searchRowGO.transform, font, "Presets ▼",
                    fixedWidth: 93f, height: 27f,
                    bgColor: new Color(0.06f, 0.18f, 0.10f, 0.55f),
                    hoverColor: new Color(0.10f, 0.32f, 0.16f, 0.80f));
                AddTooltip(presetsBtn.gameObject, "Add a preset group of destinations: My Bases (bodies with a built facility), or any of the game's celestial body groups (Near-Earth Objects, Inner/Middle/Outer Belt, Jupiter Trojans, Kuiper Belt…).");
                var searchInput = MakeInputField("SearchField", searchRowGO.transform, font, "Search bodies…", 27f);

                // Calculating overlay — full-panel, shown during refresh
                var calcOverlayGO = new GameObject("CalcOverlay", typeof(RectTransform));
                calcOverlayGO.transform.SetParent(panelGO.transform, false);
                calcOverlayGO.transform.SetAsLastSibling();
                calcOverlayGO.AddComponent<LayoutElement>().ignoreLayout = true;
                var overlayRT = calcOverlayGO.GetComponent<RectTransform>();
                overlayRT.anchorMin = Vector2.zero; overlayRT.anchorMax = Vector2.one;
                overlayRT.offsetMin = Vector2.zero; overlayRT.offsetMax = Vector2.zero;
                var overlayBg = calcOverlayGO.AddComponent<Image>();
                overlayBg.color = new Color(0.05f, 0.07f, 0.10f, 0.88f);
                overlayBg.raycastTarget = true;
                var calcLbl = new GameObject("Lbl", typeof(RectTransform));
                calcLbl.transform.SetParent(calcOverlayGO.transform, false);
                var calcLblRT = calcLbl.GetComponent<RectTransform>();
                calcLblRT.anchorMin = Vector2.zero; calcLblRT.anchorMax = Vector2.one; calcLblRT.sizeDelta = Vector2.zero;
                var calcTMP = calcLbl.AddComponent<TextMeshProUGUI>();
                if (font != null) calcTMP.font = font;
                calcTMP.text = "Calculating…";
                calcTMP.fontSize = 28f;
                calcTMP.alignment = TextAlignmentOptions.Center;
                calcTMP.color = new Color(0.60f, 0.85f, 0.90f);
                calcTMP.enableWordWrapping = false;
                calcTMP.raycastTarget = false;
                calcOverlayGO.SetActive(false);

                panelGO.SetActive(false);

                // ── Origin dropdown overlay ───────────────────────────────────────────────────
                var originDropGO = MakeDropdownPanel("LWOriginDropdown", canvas.transform, font, 330f, 345f);

                // Shift viewport down 39px to leave room for the typeahead filter input.
                var originVpRT = originDropGO.transform.Find("Viewport")?.GetComponent<RectTransform>();
                if (originVpRT != null)
                {
                    originVpRT.offsetMin = new Vector2(4f, 3f);
                    originVpRT.offsetMax = new Vector2(-4f, -39f);
                }

                var originFilterField = MakeInputField("OriginFilter", originDropGO.transform, font, "Filter…", 33f);
                var originFilterRT    = originFilterField.GetComponent<RectTransform>();
                originFilterRT.anchorMin         = new Vector2(0f, 1f);
                originFilterRT.anchorMax         = new Vector2(1f, 1f);
                originFilterRT.pivot             = new Vector2(0.5f, 1f);
                originFilterRT.sizeDelta         = new Vector2(-9f, 33f);
                originFilterRT.anchoredPosition  = new Vector2(0f, -3f);

                originDropGO.SetActive(false);

                // ── Craft dropdown overlay ────────────────────────────────────────────────────
                var craftDropGO = MakeDropdownPanel("LWCraftDropdown", canvas.transform, font, 420f, 300f);
                craftDropGO.SetActive(false);

                // ── Search results overlay ────────────────────────────────────────────────────
                var searchDropGO = MakeDropdownPanel("LWSearchDropdown", canvas.transform, font, 420f, 240f);
                searchDropGO.SetActive(false);

                // ── Presets dropdown overlay ──────────────────────────────────────────────────
                // My Bases + one item per game ObjectInfoGroups (~8 total); scrolls if more.
                var presetsDropGO = MakeDropdownPanel("LWPresetsDropdown", canvas.transform, font, 255f, 264f);
                presetsDropGO.SetActive(false);

                // ── Attach panel MonoBehaviour ────────────────────────────────────────────────
                var panel = panelGO.AddComponent<LaunchWindowPanel>();
                panel.OriginBtn     = originBtn;
                panel.CraftBtn      = craftBtn;
                panel.ContentParent = contentGO.transform;
                panel.FontAsset       = font;
                panel.HeaderFontAsset = headerFont;
                panel.PanelRT       = panelRT;
                panel.OriginDropGO     = originDropGO;
                panel.OriginFilterInput = originFilterField;
                panel.CraftDropGO   = craftDropGO;
                panel.SearchDropGO  = searchDropGO;
                panel.PresetsDropGO = presetsDropGO;
                panel.PresetsBtn    = presetsBtn;
                panel.SearchInput   = searchInput;

                panel.OptDepHdrTMP  = optDepTMP;
                panel.FstDepHdrTMP  = fstDepTMP;
                panel.OptDvHdrTMP   = optDvTMP;
                panel.FstDvHdrTMP   = fstDvTMP;
                panel.TableFontAsset = tableFont;
                panel.CalcOverlayGO = calcOverlayGO;

                closeBtn.onClick.AddListener(panel.ClosePanel);
                originBtn.onClick.AddListener(panel.ToggleOriginDropdown);
                craftBtn.onClick.AddListener(panel.ToggleCraftDropdown);
                presetsBtn.onClick.AddListener(panel.TogglePresetsDropdown);
                clearBtn.onClick.AddListener(panel.ClearAllDests);
                optDepBtn.onClick.AddListener(panel.ToggleSortOptDep);
                fstDepBtn.onClick.AddListener(panel.ToggleSortFstDep);
                optDvBtn.onClick.AddListener(panel.ToggleSortOptDv);
                fstDvBtn.onClick.AddListener(panel.ToggleSortFstDv);

                var refreshBtnComp = headerGO.transform.Find("RefreshBtn")?.GetComponent<Button>();
                if (refreshBtnComp != null) refreshBtnComp.onClick.AddListener(panel.ForceRefresh);

                // ── Indicator toggle button ───────────────────────────────────────────────────
                var indicatorGO = new GameObject("modLaunchWindowsButton", typeof(RectTransform));
                indicatorGO.transform.SetParent(canvas.transform, false);
                indicatorGO.transform.SetAsLastSibling();
                indicatorGO.AddComponent<LayoutElement>().ignoreLayout = true;

                var indicatorRT = indicatorGO.GetComponent<RectTransform>();
                indicatorRT.anchorMin = new Vector2(0.5f, 0.5f);
                indicatorRT.anchorMax = new Vector2(0.5f, 0.5f);
                indicatorRT.pivot     = new Vector2(0f, 1f);
                indicatorRT.sizeDelta = new Vector2(180f, 33f);
                indicatorRT.anchoredPosition = new Vector2(-9999f, -9999f);

                var indicatorBg = indicatorGO.AddComponent<Image>();
                var origBtnImg  = showBtn.GetComponent<Image>();
                if (origBtnImg != null)
                { indicatorBg.sprite = origBtnImg.sprite; indicatorBg.type = origBtnImg.type; indicatorBg.color = origBtnImg.color; indicatorBg.material = origBtnImg.material; }
                else indicatorBg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
                indicatorBg.raycastTarget = true;

                MakeFillLabel(indicatorGO, font, "LAUNCH WINDOWS", 15f);

                var mover = indicatorGO.AddComponent<LWMover>();
                mover.Bg          = indicatorBg;
                mover.NormalColor = indicatorBg.color;
                mover.PanelRT     = panelRT;
                mover.PanelGO     = panelGO;
                mover.Panel       = panel;
                mover.ShowBtnRT   = showBtn.GetComponent<RectTransform>();

                indicatorGO.AddComponent<LWUpdater>().Panel = panel;

                Plugin.Log.LogInfo("[LW] Injection complete");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[LW] Inject exception: {e}");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────────

        // Horizontal row that is a VLG child.
        // Container has only LayoutElement (no HLG) so VLG reads only our desired height.
        // HLG lives on a full-stretch child; VLG never measures it.
        // Returns the inner HLG GO — callers add their children to it directly.
        static GameObject MakeHRow(string name, Transform parent, float height, float spacing)
        {
            var container = new GameObject(name, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            var le = container.AddComponent<LayoutElement>();
            le.minHeight       = height;
            le.preferredHeight = height;
            le.flexibleHeight  = 0f;

            var inner = new GameObject("HLG", typeof(RectTransform));
            inner.transform.SetParent(container.transform, false);
            var rt = inner.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var hlg = inner.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth  = false;
            hlg.spacing = spacing;
            return inner;
        }

        static Button MakeButton(string name, Transform parent, TMP_FontAsset font, string text,
                                  bool expandWidth = false, float fixedWidth = 0f, float height = 27f,
                                  Color? bgColor = null, Color? hoverColor = null)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.10f, 0.12f, 0.15f, 0.0f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = hoverColor ?? new Color(0.15f, 0.30f, 0.40f, 0.7f);
            btn.colors = colors;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = height;
            le.preferredHeight = height;
            le.flexibleHeight  = 0f;
            if (expandWidth) le.flexibleWidth = 1f;
            else             le.preferredWidth = fixedWidth;
            MakeFillLabel(go, font, text, 15f);
            return btn;
        }

        // Row-spanning label (direct VLG child).
        static TextMeshProUGUI MakeRowLabel(string name, Transform parent, TMP_FontAsset font,
                                             string text, float fontSize, float height,
                                             TextAlignmentOptions align, bool muted = false)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = AddTMP(go, font, text, fontSize, align, muted);
            var le  = go.AddComponent<LayoutElement>();
            le.minHeight       = height;
            le.preferredHeight = height;
            le.flexibleHeight  = 0f;
            return tmp;
        }

        // Sub-header group — layout EXACTLY mirrors CreateRow's DepCell structure so they align.
        // Both columns: [DepCell(NT 12px + Departs depTextW px)][dvW Δv][flex Travel]
        // NT uses Image+LayoutElement (no TMP on GO, label on child) — same as row checkbox —
        // so TMP's ILayoutElement never competes with LayoutElement.preferredWidth.
        static (Button depBtn, TextMeshProUGUI depTMP, Button dvBtn, TextMeshProUGUI dvTMP) MakeSubHdrGroup(Transform parent, TMP_FontAsset font, TMP_FontAsset headerFont, bool isOptimal = false)
        {
            float ntW      = 18f;
            float depTextW = isOptimal ? 100f : 102f; // "Departs ▲" label / "26/07/18" cells at 15pt
            float depCellW = ntW + depTextW; // 118 or 120 — matches OPT_DEP_W / FST_DEP_W
            float dvW      = isOptimal ? 95f : 110f;

            var go = new GameObject("SubGrp", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            // dep + dv + arr(100) + fuel(130) — matches OPT_GRP_W / FST_GRP_W
            go.AddComponent<LayoutElement>().preferredWidth = isOptimal ? 443f : 460f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.spacing = 0f;

            // DepCell container — mirrors row's DepCell (62/70px HLG wrapper).
            var dcGO = new GameObject("DC", typeof(RectTransform));
            dcGO.transform.SetParent(go.transform, false);
            dcGO.AddComponent<LayoutElement>().preferredWidth = depCellW;
            var dcHlg = dcGO.AddComponent<HorizontalLayoutGroup>();
            dcHlg.childControlHeight = true; dcHlg.childControlWidth = true;
            dcHlg.childForceExpandHeight = true; dcHlg.childForceExpandWidth = false;
            dcHlg.spacing = 0f;

            // NT cell (12px) — mirrors row checkbox: Image+LayoutElement on GO, TMP label on child.
            var ntGO  = new GameObject("NT", typeof(RectTransform));
            ntGO.transform.SetParent(dcGO.transform, false);
            ntGO.AddComponent<LayoutElement>().preferredWidth = ntW;
            var ntImg = ntGO.AddComponent<Image>(); ntImg.color = Color.clear; ntImg.raycastTarget = true;
            ntGO.AddComponent<LWTooltipTrigger>().Text = "Notify: click ☐ to get an in-game notification when this departure window arrives.";
            var ntLblGO = new GameObject("L", typeof(RectTransform));
            ntLblGO.transform.SetParent(ntGO.transform, false);
            var ntLblRT = ntLblGO.GetComponent<RectTransform>();
            ntLblRT.anchorMin = Vector2.zero; ntLblRT.anchorMax = Vector2.one; ntLblRT.sizeDelta = Vector2.zero;
            var ntTMP = AddTMP(ntLblGO, font, "!", 15f, TextAlignmentOptions.Center, muted: false, bold: true);
            ntTMP.color = new Color(0.65f, 0.82f, 0.95f, 0.9f);

            // Departs sort button (50/58px) — mirrors row DepText.
            var depGO  = new GameObject("D", typeof(RectTransform));
            depGO.transform.SetParent(dcGO.transform, false);
            depGO.AddComponent<LayoutElement>().preferredWidth = depTextW;
            var depImg = depGO.AddComponent<Image>(); depImg.color = Color.clear; depImg.raycastTarget = true;
            var depBtn = depGO.AddComponent<Button>(); depBtn.targetGraphic = depImg;
            var depC   = depBtn.colors;
            depC.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            depBtn.colors = depC;
            var depLbl = new GameObject("L", typeof(RectTransform));
            depLbl.transform.SetParent(depGO.transform, false);
            var depLblRT = depLbl.GetComponent<RectTransform>();
            depLblRT.anchorMin = Vector2.zero; depLblRT.anchorMax = Vector2.one; depLblRT.sizeDelta = Vector2.zero;
            var depTMP = AddTMP(depLbl, font, "Departs", 15f, TextAlignmentOptions.Left, muted: true);
            AddTooltip(depGO, "Departure date. Click column header to sort.");

            // Δv sort button — mirrors the Departs button pattern.
            var dvGO  = new GameObject("V", typeof(RectTransform));
            dvGO.transform.SetParent(go.transform, false);
            dvGO.AddComponent<LayoutElement>().preferredWidth = dvW;
            var dvImg = dvGO.AddComponent<Image>(); dvImg.color = Color.clear; dvImg.raycastTarget = true;
            var dvBtn = dvGO.AddComponent<Button>(); dvBtn.targetGraphic = dvImg;
            var dvBC  = dvBtn.colors;
            dvBC.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            dvBtn.colors = dvBC;
            var dvLblGO = new GameObject("L", typeof(RectTransform));
            dvLblGO.transform.SetParent(dvGO.transform, false);
            var dvLblRT = dvLblGO.GetComponent<RectTransform>();
            dvLblRT.anchorMin = Vector2.zero; dvLblRT.anchorMax = Vector2.one; dvLblRT.sizeDelta = Vector2.zero;
            var dvTMP = AddTMP(dvLblGO, font, "Δv", 15f, TextAlignmentOptions.Left, muted: true);
            AddTooltip(dvGO, "Estimated fuel cost (km/s). Shown in red when it exceeds your craft's Δv budget. Click to sort.");

            var tvlTMP = MakeColLabel("T", go.transform, font, "Arrives", 15f, 100f, TextAlignmentOptions.Left, muted: true);
            AddTooltip(tvlTMP.gameObject, "Estimated arrival date at the destination.");
            var fuTMP  = MakeColLabel("F", go.transform, font, "Fuel (E/F)", 15f, 130f, TextAlignmentOptions.Left, muted: true);
            AddTooltip(fuTMP.gameObject, "Estimated propellant for this transfer with the selected craft: Empty / Full cargo load (rocket equation, using the currently researched exhaust velocity). Red: exceeds the craft's fuel tank capacity — it cannot carry enough propellant for this transfer at that load.");

            return (depBtn, depTMP, dvBtn, dvTMP);
        }

        static void AddTooltip(GameObject go, string text)
        {
            if (go == null) return;
            // Unity blocks adding Image to a GO that already has TextMeshProUGUI (both are Graphic).
            // Use a full-stretch child overlay instead so raycasts still work.
            GameObject target = go;
            if (go.GetComponent<TextMeshProUGUI>() != null)
            {
                target = new GameObject("TTOverlay", typeof(RectTransform));
                target.transform.SetParent(go.transform, false);
                var rt = target.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
            }
            Image img = target.GetComponent<Image>();
            if (img == null) img = target.AddComponent<Image>();
            if (img == null) return;
            img.color = Color.clear;
            img.raycastTarget = true;
            target.AddComponent<LWTooltipTrigger>().Text = text;
        }

        // Fixed-width (or flex) column label (HLG child).
        // Pure container: only LayoutElement on the GO so TMP's ILayoutElement never competes.
        static TextMeshProUGUI MakeColLabel(string name, Transform parent, TMP_FontAsset font,
                                             string text, float fontSize, float width,
                                             TextAlignmentOptions align,
                                             bool muted = false, bool bold = false, bool flex = false)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le  = go.AddComponent<LayoutElement>();
            if (flex) le.flexibleWidth = 1f;
            else      le.preferredWidth = width;
            var lbl = new GameObject("L", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            return AddTMP(lbl, font, text, fontSize, align, muted, bold);
        }

        // Label that fills its parent GO via full-stretch RT (used inside buttons/indicator).
        internal static TextMeshProUGUI MakeFillLabel(GameObject parent, TMP_FontAsset font,
                                                       string text, float fontSize)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
            return AddTMP(go, font, text, fontSize, TextAlignmentOptions.Center);
        }

        static TextMeshProUGUI AddTMP(GameObject go, TMP_FontAsset font, string text,
                                       float fontSize, TextAlignmentOptions align,
                                       bool muted = false, bool bold = false)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text               = text;
            tmp.fontSize           = fontSize;
            tmp.alignment          = align;
            tmp.color              = muted ? new Color(0.55f, 0.55f, 0.55f) : Color.white;
            tmp.fontStyle          = bold ? FontStyles.Bold : FontStyles.Normal;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.raycastTarget      = false;
            return tmp;
        }

        static void Divider(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = 2f;
            le.preferredHeight = 2f;
            le.flexibleHeight  = 0f;
        }

        static TMP_InputField MakeInputField(string name, Transform parent, TMP_FontAsset font,
                                              string placeholder, float height)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg  = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.14f, 0.85f);
            var le  = go.AddComponent<LayoutElement>();
            le.flexibleWidth   = 1f;
            le.minHeight       = height;
            le.preferredHeight = height;
            le.flexibleHeight  = 0f;

            var vpGO = new GameObject("Viewport", typeof(RectTransform));
            vpGO.transform.SetParent(go.transform, false);
            var vpRT = vpGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.sizeDelta = new Vector2(-9f, -6f);
            vpGO.AddComponent<RectMask2D>();

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(vpGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one; textRT.sizeDelta = Vector2.zero;
            var textTMP = textGO.AddComponent<TextMeshProUGUI>();
            if (font != null) textTMP.font = font;
            textTMP.fontSize = 15f; textTMP.color = Color.white;
            textTMP.enableWordWrapping = false;

            var phGO  = new GameObject("Placeholder", typeof(RectTransform));
            phGO.transform.SetParent(vpGO.transform, false);
            var phRT  = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one; phRT.sizeDelta = Vector2.zero;
            var phTMP = phGO.AddComponent<TextMeshProUGUI>();
            if (font != null) phTMP.font = font;
            phTMP.fontSize = 15f; phTMP.color = new Color(0.45f, 0.45f, 0.45f);
            phTMP.fontStyle = FontStyles.Italic; phTMP.text = placeholder;
            phTMP.enableWordWrapping = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport    = vpRT;
            field.textComponent   = textTMP;
            field.placeholder     = phTMP;
            field.targetGraphic   = bg;
            if (font != null) field.fontAsset = font;
            field.pointSize       = 15f;
            field.caretColor      = Color.white;
            field.selectionColor  = new Color(0.27f, 0.55f, 0.75f, 0.75f);
            return field;
        }

        static GameObject MakeDropdownPanel(string name, Transform parent, TMP_FontAsset font,
                                             float width, float maxHeight)
        {
            var go   = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt   = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, maxHeight);
            rt.anchoredPosition = new Vector2(-9999f, -9999f);

            go.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

            var vpGO  = new GameObject("Viewport", typeof(RectTransform));
            vpGO.transform.SetParent(go.transform, false);
            var vpRT  = vpGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.sizeDelta = new Vector2(-9f, -6f);
            vpGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("DropContent", typeof(RectTransform));
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f); contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f); contentRT.sizeDelta = Vector2.zero;
            var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.childControlHeight = true; contentVLG.childControlWidth = true;
            contentVLG.childForceExpandHeight = false; contentVLG.childForceExpandWidth = true;
            contentVLG.spacing = 2f; contentVLG.padding = new RectOffset(3, 3, 3, 3);
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr   = go.AddComponent<ScrollRect>();
            sr.viewport = vpRT; sr.content = contentRT;
            sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = 20f; sr.movementType = ScrollRect.MovementType.Clamped;

            return go;
        }

        // Wrap LEManager.Get with an English fallback so missing keys never break injection.
        static string Loc(string key, string fallback)
        {
            try   { return LEManager.Get(key, fallback) ?? fallback; }
            catch { return fallback; }
        }
    }

    // ── Always-active ticker + save/load provider ─────────────────────────────────────────────
    internal class LWUpdater : MonoBehaviour, Manager.ISaveStateDataProvider
    {
        internal LaunchWindowPanel Panel;
        void Update() { Panel?.UpdateTick(); }

        public void BeforeSaveState()  {}
        public void AfterSaveState(bool success) {}
        public void BeforeLoadState() {}
        public void AfterLoadState(bool success) {}
        public int  SaveStateDataPriority() => 0;

        public bool InjectIntoSaveGameData(Manager.SaveGameData saveGameData)
        {
            try
            {
                var lsm = UnityEngine.Object.FindObjectOfType<Manager.LoadSaveManager>();
                Plugin.Log.LogInfo($"[LW] InjectIntoSaveGameData: LastSaveName='{lsm?.LastSaveName}'");
                if (lsm != null && !string.IsNullOrEmpty(lsm.LastSaveName))
                    Panel?.SaveToSidecar(lsm.LastSaveName);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] InjectIntoSaveGameData: {ex.Message}"); }
            return true;
        }

        public bool ExtractFromSaveGameData(Manager.SaveGameData saveGameData)
        {
            try
            {
                var lsm = UnityEngine.Object.FindObjectOfType<Manager.LoadSaveManager>();
                Plugin.Log.LogInfo($"[LW] ExtractFromSaveGameData: LastSaveName='{lsm?.LastSaveName}'");
                if (lsm != null && !string.IsNullOrEmpty(lsm.LastSaveName))
                    Panel?.LoadFromSidecar(lsm.LastSaveName);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] ExtractFromSaveGameData: {ex.Message}"); }
            return true;
        }
    }

    // ── Toggle button + drag ──────────────────────────────────────────────────────────────────
    internal class LWMover : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Image              Bg;
        internal Color              NormalColor;
        internal RectTransform      ShowBtnRT;
        internal RectTransform      PanelRT;
        internal GameObject         PanelGO;
        internal LaunchWindowPanel  Panel;

        private RectTransform _rt;
        private Canvas        _canvas;
        private RectTransform _canvasRT;
        private Vector2       _pressScreenPos;
        private Vector2       _dragStartPos;
        private Vector2       _lastCanvasSize;
        private Vector2       _normalizedPos;
        private bool          _normalizedPosSet;

        void Awake()
        {
            _rt       = GetComponent<RectTransform>();
            _canvas   = GetComponentInParent<Canvas>();
            _canvasRT = _canvas?.GetComponent<RectTransform>();
        }

        IEnumerator Start()
        {
            yield return null;
            yield return null;
            PositionButton();
        }

        void Update()
        {
            if (_canvasRT == null) return;
            Vector2 sz = _canvasRT.rect.size;
            if (sz != _lastCanvasSize)
            {
                _lastCanvasSize = sz;
                RestoreFromNormalizedPos();
                RepositionPanel();
            }
        }

        void PositionButton()
        {
            var refRT = FindReferenceButton() ?? ShowBtnRT;
            if (refRT == null || _rt == null || _canvasRT == null || _canvas == null) return;
            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            var corners = new Vector3[4];
            refRT.GetWorldCorners(corners);
            Vector2 topLeft;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRT, new Vector2(corners[1].x, corners[1].y), cam, out topLeft)) return;
            _rt.anchoredPosition = new Vector2(topLeft.x - 6f - _rt.sizeDelta.x, topLeft.y);
            Clamp();
            StoreNormalizedPos();
            RepositionPanel();
        }

        RectTransform FindReferenceButton()
        {
            if (_canvas == null) return null;
            foreach (RectTransform rt in _canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == _rt) continue;
                string n = rt.gameObject.name ?? "";
                if ((n.Equals("modPowerTrackerButton",  StringComparison.OrdinalIgnoreCase) ||
                     n.Equals("modLifeSupportButton",   StringComparison.OrdinalIgnoreCase) ||
                     n.Equals("modFleetTrackerButton",  StringComparison.OrdinalIgnoreCase)) &&
                    rt.GetComponent<Image>() != null)
                    return rt;
            }
            return null;
        }

        internal void PlacePanelUnderButton()
        {
            if (PanelRT == null || _rt == null) return;
            RepositionPanel();
        }

        void StoreNormalizedPos()
        {
            if (_canvasRT == null) return;
            Rect cr = _canvasRT.rect;
            if (cr.xMax <= 0f || cr.yMax <= 0f) return;
            _normalizedPos = new Vector2(_rt.anchoredPosition.x / cr.xMax, _rt.anchoredPosition.y / cr.yMax);
            _normalizedPosSet = true;
        }

        void RestoreFromNormalizedPos()
        {
            if (_canvasRT == null) return;
            if (_normalizedPosSet)
            {
                Rect cr = _canvasRT.rect;
                _rt.anchoredPosition = new Vector2(_normalizedPos.x * cr.xMax, _normalizedPos.y * cr.yMax);
            }
            Clamp();
        }

        void Clamp()
        {
            if (_canvasRT == null || _rt == null) return;
            Rect cr = _canvasRT.rect; Vector2 s = _rt.sizeDelta, p = _rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, cr.xMin, cr.xMax - s.x);
            p.y = Mathf.Clamp(p.y, cr.yMin + s.y, cr.yMax);
            _rt.anchoredPosition = p;
        }

        void RepositionPanel()
        {
            if (PanelRT == null || PanelGO == null || !PanelGO.activeSelf) return;
            Vector2 p = new Vector2(_rt.anchoredPosition.x, _rt.anchoredPosition.y - _rt.sizeDelta.y - 6f);
            if (_canvasRT != null)
            {
                Rect cr = _canvasRT.rect; Vector2 s = PanelRT.sizeDelta;
                p.x = Mathf.Clamp(p.x, cr.xMin, cr.xMax - s.x);
                p.y = Mathf.Clamp(p.y, cr.yMin + s.y, cr.yMax);
            }
            PanelRT.anchoredPosition = p;
        }

        public void OnPointerEnter(PointerEventData e) { if (Bg) Bg.color = NormalColor * 1.3f; }
        public void OnPointerExit(PointerEventData e)  { if (Bg) Bg.color = NormalColor; }

        public void OnPointerDown(PointerEventData e)
        {
            _pressScreenPos = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (Bg) Bg.color = NormalColor;
            if (Vector2.Distance(e.position, _pressScreenPos) >= EventSystem.current.pixelDragThreshold) return;
            bool wasOpen = PanelGO != null && PanelGO.activeSelf;
            if (!wasOpen) { Plugin.Log.LogInfo("[LW] Panel open"); PanelGO?.SetActive(true); RepositionPanel(); Panel?.ForceRefresh(); }
            else          { Plugin.Log.LogInfo("[LW] Panel close"); Panel?.ClosePanel(); }
        }

        public void OnBeginDrag(PointerEventData e)
        {
            _dragStartPos = _rt.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            float scale = _canvas != null ? _canvas.scaleFactor : 1f;
            _rt.anchoredPosition = _dragStartPos + (e.position - _pressScreenPos) / scale;
            Clamp();
            RepositionPanel();
        }

        public void OnEndDrag(PointerEventData e)
        {
            Clamp();
            StoreNormalizedPos();
            RepositionPanel();
        }
    }

    // ── Tooltip trigger ───────────────────────────────────────────────────────────────────────
    internal class LWTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Text;
        public void OnPointerEnter(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) LWTooltip.Show(Text, e.position, canvas);
        }
        public void OnPointerExit(PointerEventData e) => LWTooltip.Hide();
        void OnDisable() => LWTooltip.Hide();
    }

    // ── Shared tooltip panel (created lazily, one instance per canvas) ────────────────────────
    internal static class LWTooltip
    {
        static GameObject        _go;
        static TextMeshProUGUI   _tmp;
        static TMP_FontAsset     _font;

        internal static TMP_FontAsset Font { set => _font = value; }

        internal static void Show(string text, Vector2 screenPos, Canvas canvas)
        {
            if (canvas == null) return;
            if (_go == null || _go.transform.parent != canvas.transform) Build(canvas);
            if (_go == null) return;

            _tmp.text = text;
            _go.SetActive(true);
            _go.transform.SetAsLastSibling();

            var canvasRT = canvas.GetComponent<RectTransform>();
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, screenPos, cam, out local))
            {
                _go.GetComponent<RectTransform>().anchoredPosition = local + new Vector2(18f, -18f);
            }
        }

        internal static void Hide() { if (_go != null) _go.SetActive(false); }

        static void Build(Canvas canvas)
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = new GameObject("LWTooltip", typeof(RectTransform));
            _go.transform.SetParent(canvas.transform, false);
            _go.AddComponent<LayoutElement>().ignoreLayout = true;

            var rt = _go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0f, 1f);

            var bg = _go.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.10f, 0.13f, 0.97f);
            bg.raycastTarget = false;

            var vlg = _go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(8, 8, 5, 5);
            vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;

            var csf = _go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var textGO = new GameObject("T", typeof(RectTransform));
            textGO.transform.SetParent(_go.transform, false);
            _tmp = textGO.AddComponent<TextMeshProUGUI>();
            if (_font != null) _tmp.font = _font;
            _tmp.fontSize           = 13f;
            _tmp.color              = new Color(0.85f, 0.85f, 0.85f);
            _tmp.enableWordWrapping = true;
            _tmp.raycastTarget      = false;
            textGO.AddComponent<LayoutElement>().preferredWidth = 270f;

            _go.SetActive(false);
        }
    }
}
