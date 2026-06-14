#nullable disable
using System;
using System.Collections;
using System.Reflection;
using Manager;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SolarExpanseLaunchWindows.UI
{
    internal static class LaunchWindowInjector
    {
        static readonly FieldInfo FieldShowBtn =
            typeof(NotificationManager).GetField("showNotificationHistory",
                BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo FieldHistoryGO =
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
                panelRT.sizeDelta = new Vector2(650f, 380f);
                panelRT.anchoredPosition = new Vector2(-9999f, -9999f);

                // ── VLG drives all rows ───────────────────────────────────────────────────────
                var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
                vlg.childControlHeight     = true;
                vlg.childControlWidth      = true;
                vlg.childForceExpandHeight = false;
                vlg.childForceExpandWidth  = true;
                vlg.spacing = 1f;
                vlg.padding = new RectOffset(6, 6, 3, 3);

                // Row 1: Header (From / Craft / Refresh / ×)
                var headerGO  = MakeHRow("Header", panelGO.transform, 22f, 3f);
                var originBtn = MakeButton("OriginBtn", headerGO.transform, font, "From: — ▼",
                    expandWidth: true, height: 18f,
                    bgColor: new Color(0.06f, 0.16f, 0.22f, 0.55f));

                var craftBtn = MakeButton("CraftBtn", headerGO.transform, font, "Craft: — ▼",
                    expandWidth: true, height: 18f,
                    bgColor: new Color(0.06f, 0.16f, 0.22f, 0.55f));

                MakeButton("RefreshBtn", headerGO.transform, font, "Refresh",
                    fixedWidth: 52f, height: 18f,
                    bgColor: new Color(0.10f, 0.12f, 0.15f, 0.0f));
                var closeBtn = MakeButton("CloseBtn", headerGO.transform, font, "×",
                    fixedWidth: 20f, height: 18f,
                    bgColor: new Color(0.20f, 0.05f, 0.05f, 0.0f),
                    hoverColor: new Color(0.55f, 0.10f, 0.10f, 0.8f));

                // Row 2: Status line
                var statusTMP = MakeRowLabel("Status", panelGO.transform, font,
                    "Not yet calculated", 9f, 12f, TextAlignmentOptions.Left, muted: true);

                // Row 3: Column headers
                var colHdrGO = MakeHRow("ColHdr", panelGO.transform, 13f, 0f);
                MakeColLabel("CH0", colHdrGO.transform, font, "Destination", 9f, 105f, TextAlignmentOptions.Left,  bold: true);
                MakeColLabel("CH1", colHdrGO.transform, font, "OPTIMAL",     9f, 255f, TextAlignmentOptions.Center, bold: true);
                MakeColLabel("CHSep", colHdrGO.transform, font, "",           9f,   8f, TextAlignmentOptions.Left);
                MakeColLabel("CH2", colHdrGO.transform, font, "FASTEST",     9f, 255f, TextAlignmentOptions.Center, bold: true);

                // Row 4: Sub-header — cells must match LaunchWindowPanel OPT_*/FST_* constants.
                // Optimal: dep=62 dv=78 tvl=flex; Fastest: dep=70 dv=88 tvl=flex
                var subHdrGO = MakeHRow("SubHdr", panelGO.transform, 12f, 0f);
                MakeColLabel("SH0", subHdrGO.transform, font, "", 9f, 105f, TextAlignmentOptions.Left, muted: true);
                var (optDepBtn, optDepTMP) = MakeSubHdrGroup(subHdrGO.transform, font, isOptimal: true);
                MakeColLabel("SHSep", subHdrGO.transform, font, "", 9f, 8f, TextAlignmentOptions.Left);
                var (fstDepBtn, fstDepTMP) = MakeSubHdrGroup(subHdrGO.transform, font, isOptimal: false);

                // Divider
                Divider("Div", panelGO.transform);

                // Scroll area (takes all remaining height via flexibleHeight)
                var scrollGO = new GameObject("Scroll", typeof(RectTransform));
                scrollGO.transform.SetParent(panelGO.transform, false);
                var scrollLE = scrollGO.AddComponent<LayoutElement>();
                scrollLE.minHeight     = 30f;
                scrollLE.flexibleHeight = 1f;

                // Scrollbar (5px, right edge)
                var sbGO = new GameObject("Scrollbar", typeof(RectTransform));
                sbGO.transform.SetParent(scrollGO.transform, false);
                var sbRT = sbGO.GetComponent<RectTransform>();
                sbRT.anchorMin = new Vector2(1f, 0f); sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 0.5f);
                sbRT.sizeDelta = new Vector2(5f, 0f); sbRT.anchoredPosition = Vector2.zero;
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
                vpRT.offsetMin = Vector2.zero; vpRT.offsetMax = new Vector2(-7f, 0f);
                vpGO.AddComponent<RectMask2D>();

                // Content
                var contentGO = new GameObject("Content", typeof(RectTransform));
                contentGO.transform.SetParent(vpGO.transform, false);
                var contentRT = contentGO.GetComponent<RectTransform>();
                contentRT.anchorMin = new Vector2(0f, 1f); contentRT.anchorMax = new Vector2(1f, 1f);
                contentRT.pivot = new Vector2(0.5f, 1f); contentRT.sizeDelta = Vector2.zero;
                var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
                contentVLG.childControlHeight = true; contentVLG.childControlWidth = true;
                contentVLG.childForceExpandHeight = false; contentVLG.childForceExpandWidth = true;
                contentVLG.spacing = 1f; contentVLG.padding = new RectOffset(2, 2, 2, 2);
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
                var searchRowGO = MakeHRow("SearchRow", panelGO.transform, 18f, 4f);
                MakeColLabel("SrchLbl", searchRowGO.transform, font, "+ Add:", 9f, 38f, TextAlignmentOptions.Right, muted: true);
                var basesBtn = MakeButton("BasesBtn", searchRowGO.transform, font, "My Bases",
                    fixedWidth: 62f, height: 18f,
                    bgColor: new Color(0.06f, 0.18f, 0.10f, 0.55f),
                    hoverColor: new Color(0.10f, 0.32f, 0.16f, 0.80f));
                var searchInput = MakeInputField("SearchField", searchRowGO.transform, font, "Search bodies…", 18f);

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
                calcTMP.fontSize = 18f;
                calcTMP.alignment = TextAlignmentOptions.Center;
                calcTMP.color = new Color(0.60f, 0.85f, 0.90f);
                calcTMP.enableWordWrapping = false;
                calcTMP.raycastTarget = false;
                calcOverlayGO.SetActive(false);

                panelGO.SetActive(false);

                // ── Origin dropdown overlay ───────────────────────────────────────────────────
                var originDropGO = MakeDropdownPanel("LWOriginDropdown", canvas.transform, font, 220f, 230f);

                // Shift viewport down 26px to leave room for the typeahead filter input.
                var originVpRT = originDropGO.transform.Find("Viewport")?.GetComponent<RectTransform>();
                if (originVpRT != null)
                {
                    originVpRT.offsetMin = new Vector2(3f, 2f);
                    originVpRT.offsetMax = new Vector2(-3f, -26f);
                }

                var originFilterField = MakeInputField("OriginFilter", originDropGO.transform, font, "Filter…", 22f);
                var originFilterRT    = originFilterField.GetComponent<RectTransform>();
                originFilterRT.anchorMin         = new Vector2(0f, 1f);
                originFilterRT.anchorMax         = new Vector2(1f, 1f);
                originFilterRT.pivot             = new Vector2(0.5f, 1f);
                originFilterRT.sizeDelta         = new Vector2(-6f, 22f);
                originFilterRT.anchoredPosition  = new Vector2(0f, -2f);

                originDropGO.SetActive(false);

                // ── Craft dropdown overlay ────────────────────────────────────────────────────
                var craftDropGO = MakeDropdownPanel("LWCraftDropdown", canvas.transform, font, 280f, 200f);
                craftDropGO.SetActive(false);

                // ── Search results overlay ────────────────────────────────────────────────────
                var searchDropGO = MakeDropdownPanel("LWSearchDropdown", canvas.transform, font, 280f, 160f);
                searchDropGO.SetActive(false);

                // ── Attach panel MonoBehaviour ────────────────────────────────────────────────
                var panel = panelGO.AddComponent<LaunchWindowPanel>();
                panel.StatusTMP     = statusTMP;
                panel.OriginBtn     = originBtn;
                panel.CraftBtn      = craftBtn;
                panel.ContentParent = contentGO.transform;
                panel.FontAsset     = font;
                panel.PanelRT       = panelRT;
                panel.OriginDropGO     = originDropGO;
                panel.OriginFilterInput = originFilterField;
                panel.CraftDropGO   = craftDropGO;
                panel.SearchDropGO  = searchDropGO;
                panel.SearchInput   = searchInput;

                panel.OptDepHdrTMP  = optDepTMP;
                panel.FstDepHdrTMP  = fstDepTMP;
                panel.CalcOverlayGO = calcOverlayGO;

                closeBtn.onClick.AddListener(panel.ClosePanel);
                originBtn.onClick.AddListener(panel.ToggleOriginDropdown);
                craftBtn.onClick.AddListener(panel.ToggleCraftDropdown);
                basesBtn.onClick.AddListener(panel.AddPresenceBodies);
                optDepBtn.onClick.AddListener(panel.ToggleSortOptDep);
                fstDepBtn.onClick.AddListener(panel.ToggleSortFstDep);

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
                indicatorRT.sizeDelta = new Vector2(120f, 22f);
                indicatorRT.anchoredPosition = new Vector2(-9999f, -9999f);

                var indicatorBg = indicatorGO.AddComponent<Image>();
                var origBtnImg  = showBtn.GetComponent<Image>();
                if (origBtnImg != null)
                { indicatorBg.sprite = origBtnImg.sprite; indicatorBg.type = origBtnImg.type; indicatorBg.color = origBtnImg.color; indicatorBg.material = origBtnImg.material; }
                else indicatorBg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
                indicatorBg.raycastTarget = true;

                MakeFillLabel(indicatorGO, font, "LAUNCH WINDOWS", 9f);

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
                                  bool expandWidth = false, float fixedWidth = 0f, float height = 18f,
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
            MakeFillLabel(go, font, text, 9f);
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

        // Sub-header group — "Departs" cell is a clickable button for sorting.
        // isOptimal=true uses Optimal column widths and adds a Cargo cell.
        static (Button depBtn, TextMeshProUGUI depTMP) MakeSubHdrGroup(Transform parent, TMP_FontAsset font, bool isOptimal = false)
        {
            float depW = isOptimal ? 62f : 70f;
            float dvW  = isOptimal ? 78f : 88f;

            var go = new GameObject("SubGrp", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 255f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.spacing = 0f;

            // Departs — clickable sort button
            var depGO  = new GameObject("D", typeof(RectTransform));
            depGO.transform.SetParent(go.transform, false);
            depGO.AddComponent<LayoutElement>().preferredWidth = depW;
            var depImg = depGO.AddComponent<Image>(); depImg.color = Color.clear;
            var depBtn = depGO.AddComponent<Button>(); depBtn.targetGraphic = depImg;
            var depC   = depBtn.colors;
            depC.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            depBtn.colors = depC;
            var depLbl = new GameObject("L", typeof(RectTransform));
            depLbl.transform.SetParent(depGO.transform, false);
            var depLblRT = depLbl.GetComponent<RectTransform>();
            depLblRT.anchorMin = Vector2.zero; depLblRT.anchorMax = Vector2.one; depLblRT.sizeDelta = Vector2.zero;
            var depTMP = AddTMP(depLbl, font, "Departs", 9f, TextAlignmentOptions.Left, muted: true);

            MakeColLabel("V", go.transform, font, "Δv",     9f, dvW, TextAlignmentOptions.Left, muted: true);
            MakeColLabel("T", go.transform, font, "Travel", 9f,  0f, TextAlignmentOptions.Left, muted: true, flex: true);

            return (depBtn, depTMP);
        }

        // Fixed-width (or flex) column label (HLG child).
        static TextMeshProUGUI MakeColLabel(string name, Transform parent, TMP_FontAsset font,
                                             string text, float fontSize, float width,
                                             TextAlignmentOptions align,
                                             bool muted = false, bool bold = false, bool flex = false)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = AddTMP(go, font, text, fontSize, align, muted, bold);
            var le  = go.AddComponent<LayoutElement>();
            if (flex) le.flexibleWidth = 1f;
            else      le.preferredWidth = width;
            return tmp;
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
            le.minHeight       = 1f;
            le.preferredHeight = 1f;
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
            vpRT.sizeDelta = new Vector2(-6f, -4f);
            vpGO.AddComponent<RectMask2D>();

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(vpGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one; textRT.sizeDelta = Vector2.zero;
            var textTMP = textGO.AddComponent<TextMeshProUGUI>();
            if (font != null) textTMP.font = font;
            textTMP.fontSize = 9f; textTMP.color = Color.white;
            textTMP.enableWordWrapping = false;

            var phGO  = new GameObject("Placeholder", typeof(RectTransform));
            phGO.transform.SetParent(vpGO.transform, false);
            var phRT  = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one; phRT.sizeDelta = Vector2.zero;
            var phTMP = phGO.AddComponent<TextMeshProUGUI>();
            if (font != null) phTMP.font = font;
            phTMP.fontSize = 9f; phTMP.color = new Color(0.45f, 0.45f, 0.45f);
            phTMP.fontStyle = FontStyles.Italic; phTMP.text = placeholder;
            phTMP.enableWordWrapping = false;

            var field = go.AddComponent<TMP_InputField>();
            field.textViewport    = vpRT;
            field.textComponent   = textTMP;
            field.placeholder     = phTMP;
            field.targetGraphic   = bg;
            if (font != null) field.fontAsset = font;
            field.pointSize       = 9f;
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
            vpRT.sizeDelta = new Vector2(-6f, -4f);
            vpGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("DropContent", typeof(RectTransform));
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f); contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f); contentRT.sizeDelta = Vector2.zero;
            var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.childControlHeight = true; contentVLG.childControlWidth = true;
            contentVLG.childForceExpandHeight = false; contentVLG.childForceExpandWidth = true;
            contentVLG.spacing = 1f; contentVLG.padding = new RectOffset(2, 2, 2, 2);
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr   = go.AddComponent<ScrollRect>();
            sr.viewport = vpRT; sr.content = contentRT;
            sr.horizontal = false; sr.vertical = true;
            sr.scrollSensitivity = 20f; sr.movementType = ScrollRect.MovementType.Clamped;

            return go;
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
            _rt.anchoredPosition = new Vector2(topLeft.x - 4f - _rt.sizeDelta.x, topLeft.y);
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
            Vector2 p = new Vector2(_rt.anchoredPosition.x, _rt.anchoredPosition.y - _rt.sizeDelta.y - 4f);
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
            if (!wasOpen) { PanelGO?.SetActive(true); RepositionPanel(); Panel?.ForceRefresh(); }
            else          { Panel?.ClosePanel(); }
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
}
