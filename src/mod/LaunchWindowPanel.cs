#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Data;
using Game.UI;
using Language;
using Manager;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SolarExpanseLaunchWindows
{
    internal class LaunchWindowPanel : MonoBehaviour
    {
        // Set by injector
        internal TextMeshProUGUI OptDepHdrTMP;
        internal TextMeshProUGUI FstDepHdrTMP;
        internal Button          OriginBtn;
        internal Button          CraftBtn;
        internal Transform       ContentParent;
        internal TMP_FontAsset   FontAsset;
        internal TMP_FontAsset   HeaderFontAsset; // Oxanium if found, else same as FontAsset
        internal RectTransform   PanelRT;
        internal GameObject      OriginDropGO;
        internal GameObject      CraftDropGO;
        internal GameObject      SearchDropGO;
        internal GameObject      PresetsDropGO;
        internal Button          PresetsBtn;
        internal TMP_InputField  SearchInput;
        internal GameObject      CalcOverlayGO;
        internal TMP_InputField  OriginFilterInput;

        // Data
        private GameBodyEphemeris ephem;
        private WindowFinder      finder;
        private double            dvToKmS;
        private List<string>      originIds = new List<string>();
        private int               originIndex;
        private readonly Dictionary<string, List<string>> _destsByOrigin = new Dictionary<string, List<string>>();
        private List<string> DestIds
        {
            get
            {
                var o = OriginId ?? "";
                if (!_destsByOrigin.TryGetValue(o, out var d))
                    _destsByOrigin[o] = d = new List<string>();
                return d;
            }
        }

        // Craft budget
        private double _craftDvCapGameUnits  = double.MaxValue;
        private bool   _craftDropOpen;
        private bool   _craftManuallySelected;
        private bool   _craftLogged;
        private string _selectedCraftName;
        private double _craftMaxDvKmS    = double.MaxValue;
        private double _craftSolarRangeAU = 0.0; // >0 means solar sail; 0 means no range limit
        private double _craftMaxCargo  = 0.0;
        private double _craftExhaustV  = 0.0;
        private double _craftDryMass   = 0.0;
        private double _craftFuel      = 0.0;

        // Sort state
        private enum SortCol { None, OptDep, FstDep }
        private enum SortDir { Asc, Desc }
        private SortCol _sortCol = SortCol.OptDep;
        private SortDir _sortDir = SortDir.Asc;

        private readonly Dictionary<string, (LaunchWindow? opt1, LaunchWindow? fst1, LaunchWindow? opt2, LaunchWindow? fst2)> cache
            = new Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)>();
        // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
        // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
        private readonly Dictionary<string, TextMeshProUGUI>   rowNameTMPs = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, Image>             rowIconImgs = new Dictionary<string, Image>();
        private readonly Dictionary<string, TextMeshProUGUI[]> rowTMPs
            = new Dictionary<string, TextMeshProUGUI[]>();

        private float lastEphemBuildTime = -1000f;
        private float lastRefreshTime    = -1000f;
        private bool  needsRefresh;
        private bool  refreshing;
        private bool  originDropOpen;
        private bool  _presetsDropOpen;
        private HashSet<string> _originShipBodies;

        private volatile bool _calcDone;
        private Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)> _pendingCache;

        private string _pendingSearch;
        private string _lastSearch;

        // Alarm state
        private readonly HashSet<AlarmKey> _alarms     = new HashSet<AlarmKey>();
        private readonly HashSet<AlarmKey> _firedAlarms = new HashSet<AlarmKey>();
        private readonly HashSet<string>  _needsOpt2Recalc = new HashSet<string>();
        private readonly HashSet<string>  _needsFstRecalc  = new HashSet<string>();
        private readonly Dictionary<string, HashSet<string>> _needsOpt2ByOrigin = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, HashSet<string>> _needsFstByOrigin  = new Dictionary<string, HashSet<string>>();
        internal IGameClock _clock = new GameClock();

        // Per-row checkbox buttons: [0]=opt1, [1]=opt2, [2]=fst1, [3]=fst2
        private readonly Dictionary<string, Button[]> rowCheckboxBtns = new Dictionary<string, Button[]>();

        // Per-origin window cache — preserved across origin switches so no recalc on switch-back.
        private readonly Dictionary<string, Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)>> _cacheByOrigin
            = new Dictionary<string, Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)>>();

        // Sidecar load/apply state
        private bool       _sidecarLoaded;
        private bool       _sidecarApplied;
        private LWSaveData _sidecarData;
        private float      _ephemReadyTime = -1f;
        private bool       _sidecarDirty;
        private float      _lastAutoSaveTime;

        private string OriginId => originIds.Count > 0 ? originIds[originIndex % originIds.Count] : null;

        void Start()
        {
            if (OriginFilterInput != null)
                OriginFilterInput.onValueChanged.AddListener(filter => PopulateOriginDropdown(filter?.Trim() ?? ""));
            if (SearchInput != null)
                SearchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        // ── Public API called by injector ─────────────────────────────────────────

        internal void UpdateTick()
        {
            TryBuildEphem();
            TryApplySidecarData();
            MaybeAutoSaveSidecar();
            CheckAlarms();
            if (!gameObject.activeSelf) return;
            if (_calcDone)
            {
                _calcDone = false;
                ApplyPendingResults();
            }
            if (!refreshing && needsRefresh)
                DoRefresh();
            if (_pendingSearch != null && _pendingSearch != _lastSearch)
            {
                _lastSearch = _pendingSearch;
                ApplySearch(_pendingSearch);
            }
        }

        internal void ForceRefresh()
        {
            needsRefresh = true;
        }

        internal void ClosePanel()
        {
            HideOriginDropdown();
            HideCraftDropdown();
            HideSearchDropdown();
            HidePresetsDropdown();
            gameObject.SetActive(false);
        }

        internal void ToggleOriginDropdown()
        {
            if (originDropOpen) HideOriginDropdown();
            else                ShowOriginDropdown();
        }

        // ── Origin dropdown ───────────────────────────────────────────────────────

        private void ShowOriginDropdown()
        {
            if (OriginDropGO == null) return;
            if (OriginFilterInput != null) OriginFilterInput.SetTextWithoutNotify("");
            _originShipBodies = GetBodiesWithPlayerShips();
            PopulateOriginDropdown("");
            PositionDropdownBelow(OriginDropGO, OriginBtn?.GetComponent<RectTransform>(), below: true);
            OriginDropGO.SetActive(true);
            originDropOpen = true;
            if (OriginFilterInput != null) OriginFilterInput.ActivateInputField();
        }

        internal void HideOriginDropdown()
        {
            if (OriginDropGO != null) OriginDropGO.SetActive(false);
            if (OriginFilterInput != null) OriginFilterInput.SetTextWithoutNotify("");
            originDropOpen = false;
        }

        internal void ToggleCraftDropdown()
        {
            if (_craftDropOpen) HideCraftDropdown();
            else                ShowCraftDropdown();
        }

        private void ShowCraftDropdown()
        {
            if (CraftDropGO == null) return;
            _craftLogged = false;
            PopulateCraftDropdown();
            PositionDropdownBelow(CraftDropGO, CraftBtn?.GetComponent<RectTransform>(), below: true);
            CraftDropGO.SetActive(true);
            _craftDropOpen = true;
        }

        internal void HideCraftDropdown()
        {
            if (CraftDropGO != null) CraftDropGO.SetActive(false);
            _craftDropOpen = false;
        }

        private void PopulateCraftDropdown()
        {
            var content = GetDropContent(CraftDropGO);
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);

            var crafts = GetAllCraftDv();
            foreach (var (name, maxDvKmS, maxCargo, exhaustV, dryMass, fuel, solarRangeAU) in crafts.OrderByDescending(c => c.maxDvKmS == double.MaxValue ? double.MaxValue : c.maxDvKmS))
            {
                var capName    = name;
                var capMaxDv   = maxDvKmS;
                var capCargo   = maxCargo;
                var capExhV    = exhaustV;
                var capDry     = dryMass;
                var capFuel    = fuel;
                var capSolar   = solarRangeAU;
                bool isSel     = capName == _selectedCraftName;
                string label   = capSolar > 0
                    ? $"{capName}  (solar, {capSolar:F1}AU)"
                    : $"{capName}  ({capMaxDv:F0} km/s)";
                AddDropdownItem(content, label, isSel, () => {
                    _craftManuallySelected = true;
                    _sidecarDirty = true;
                    SetCraft(capName, capMaxDv, capCargo, capExhV, capDry, capFuel, capSolar);
                    HideCraftDropdown();
                    ClearAllRowData();
                    _cacheByOrigin.Clear();
                    _needsOpt2ByOrigin.Clear();
                    _needsFstByOrigin.Clear();
                    needsRefresh = true;
                });
            }

            if (crafts.Length == 0)
                AddDropdownItem(content, "No spacecraft found", dimmed: true, onClick: HideCraftDropdown);
        }

        private void HideSearchDropdown()
        {
            if (SearchDropGO != null) SearchDropGO.SetActive(false);
        }

        // ── Presets dropdown ──────────────────────────────────────────────────────

        internal void TogglePresetsDropdown()
        {
            if (_presetsDropOpen) HidePresetsDropdown();
            else                  ShowPresetsDropdown();
        }

        private void ShowPresetsDropdown()
        {
            if (PresetsDropGO == null) return;
            PopulatePresetsDropdown();
            PositionDropdownBelow(PresetsDropGO, PresetsBtn?.GetComponent<RectTransform>(), below: true);
            PresetsDropGO.SetActive(true);
            _presetsDropOpen = true;
        }

        internal void HidePresetsDropdown()
        {
            if (PresetsDropGO != null) PresetsDropGO.SetActive(false);
            _presetsDropOpen = false;
        }

        private void PopulatePresetsDropdown()
        {
            var content = GetDropContent(PresetsDropGO);
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);

            AddDropdownItem(content, "My Bases", false, () => {
                HidePresetsDropdown();
                AddPresenceBodies();
            });

            // One item per game group (NEOs, Inner/Middle/Outer Belt, Trojans, Kuiper Belt, …),
            // sorted sunward-out by the group's average orbital distance.
            foreach (var g in GetGameGroups())
            {
                var captured = g;
                AddDropdownItem(content, GroupLabel(captured), false, () => {
                    HidePresetsDropdown();
                    AddGroupBodies(captured);
                });
            }
        }

        // A body the game has "virtually destroyed" (impacted, nuked, mined out) keeps
        // its NBody in the scene, so the ephemeris still lists it. Hide such bodies
        // from presets, search, origins, and existing rows.
        private static bool IsBodyDestroyed(NBody nb)
        {
            try
            {
                var oi = nb != null ? nb.GetObjectInfo() : null;
                return oi != null && oi.IsInGameDestroy;
            }
            catch { return false; }
        }

        private bool IsBodyDestroyedId(string bodyId)
            => ephem != null && IsBodyDestroyed(ephem.GetNBodyForId(bodyId));

        // The game classifies minor bodies with ObjectInfoGroups scene components
        // (translateID → CelestialBodiesNames.NEOs / InnerBelt / MiddleBelt / OuterBelt /
        // Trojans / KuiperBelt / OthersAsteroid). Presets mirror those groups exactly.
        private List<ObjectInfoGroups> GetGameGroups()
        {
            try
            {
                return UnityEngine.Object.FindObjectsOfType<ObjectInfoGroups>()
                    .Where(g => g != null && g.gameObject.activeSelf && g.objectInGroup.Count > 0)
                    .OrderBy(g => g.auValue)
                    .ToList();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LW] GetGameGroups: {ex.Message}");
                return new List<ObjectInfoGroups>();
            }
        }

        private static string GroupLabel(ObjectInfoGroups group)
        {
            try
            {
                var name = LEManager.Get(group.translateID);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { /* fall through to translateID */ }
            var id = group.translateID ?? "";
            int dot = id.LastIndexOf('.');
            return dot >= 0 ? id.Substring(dot + 1) : id;
        }

        internal void AddGroupBodies(ObjectInfoGroups group)
        {
            TryBuildEphem();
            if (ephem == null || group == null) return;

            int added = 0;
            foreach (var oi in group.objectInGroup)
            {
                if (oi == null || oi.IsInGameDestroy) continue;
                NBody nb = null;
                try { nb = oi.NBody; } catch { }
                if (nb == null) continue;
                string id = nb.GetInstanceID().ToString();
                if (id == OriginId || DestIds.Contains(id)) continue;
                if (!ephem.AllBodyIds.Contains(id)) continue;
                DestIds.Add(id);
                _sidecarDirty = true;
                added++;
            }

            string label = GroupLabel(group);
            Plugin.Log.LogInfo($"[LW] AddGroupBodies: '{label}' added {added} of {group.objectInGroup.Count}");
            if (added > 0) needsRefresh = true;
        }

        // Removes every destination row (Clear button in the header).
        internal void ClearAllDests()
        {
            int n = DestIds.Count;
            foreach (var dId in DestIds.ToList())
                RemoveDest(dId);
            Plugin.Log.LogInfo($"[LW] ClearAllDests: removed {n}");
        }

        private void PopulateOriginDropdown(string filter = "")
        {
            var content = GetDropContent(OriginDropGO);
            if (content == null || ephem == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);

            var ships = _originShipBodies ?? new HashSet<string>();
            var filtered = originIds
                .Select(id => (id, label: ephem.GetDisplayName(id)))
                .Where(x => !IsBodyDestroyedId(x.id))
                .Where(x => string.IsNullOrEmpty(filter) ||
                            x.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            // Tier 1: presence; Tier 2: planet (0) vs non-planet (1); Tier 3: alphabetical.
            var items = filtered.OrderBy(x => ships.Contains(x.id) ? 0 : 1)
                                .ThenBy(x => ephem.IsPlanet(x.id) ? 0 : 1)
                                .ThenBy(x => x.label, StringComparer.OrdinalIgnoreCase);

            foreach (var (id, label) in items)
            {
                var captured = id;
                bool isCurrent = id == OriginId;
                AddDropdownItem(content, label, isCurrent, () => {
                    var prevOriginId = OriginId;
                    int idx = originIds.IndexOf(captured);
                    if (idx >= 0) originIndex = idx;
                    UpdateOriginLabel();
                    HideOriginDropdown();
                    // Save old origin's cache, then restore the new origin's cache (avoids recalc on switch-back).
                    if (prevOriginId != null)
                    {
                        _cacheByOrigin[prevOriginId] = new Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)>(cache);
                        _needsOpt2ByOrigin[prevOriginId] = new HashSet<string>(_needsOpt2Recalc);
                        _needsFstByOrigin[prevOriginId]  = new HashSet<string>(_needsFstRecalc);
                    }
                    ClearAllRowData();
                    _needsOpt2Recalc.Clear();
                    _needsFstRecalc.Clear();
                    if (_cacheByOrigin.TryGetValue(OriginId ?? "", out var saved))
                    {
                        foreach (var kv in saved) cache[kv.Key] = kv.Value;
                        if (_needsOpt2ByOrigin.TryGetValue(OriginId ?? "", out var o2saved)) foreach (var id in o2saved) _needsOpt2Recalc.Add(id);
                        if (_needsFstByOrigin.TryGetValue(OriginId ?? "", out var fssaved))  foreach (var id in fssaved) _needsFstRecalc.Add(id);
                    }
                    if (DestIds.Count == 0 && ephem != null)
                    {
                        // Pick the first non-origin planet from a sensible fallback list.
                        foreach (var fallback in new[] { "Earth", "Mars", "Venus", "Jupiter" })
                        {
                            var fid = ephem.AllBodyIds.FirstOrDefault(bid =>
                                string.Equals(ephem.GetDisplayName(bid), fallback, StringComparison.OrdinalIgnoreCase));
                            if (fid != null && fid != OriginId && !DestIds.Contains(fid))
                            { DestIds.Add(fid); _sidecarDirty = true; break; }
                        }
                    }
                    needsRefresh = true;
                });
            }
        }

        // ── Search / add destination ──────────────────────────────────────────────

        // Thin setter — actual rebuild happens in UpdateTick once per frame to avoid per-keystroke lag.
        private void OnSearchChanged(string query) => _pendingSearch = query?.Trim() ?? "";

        private void ApplySearch(string query)
        {
            if (query.Length == 0) { HideSearchDropdown(); return; }
            if (ephem == null || SearchDropGO == null) { HideSearchDropdown(); return; }

            var content = GetDropContent(SearchDropGO);
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);

            var matches = ephem.AllBodyIds
                .Where(id => ephem.GetDisplayName(id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(id => !IsBodyDestroyedId(id))
                .OrderBy(id => ephem.GetDisplayName(id))
                .Take(10)
                .ToList();

            int added = 0;
            foreach (var id in matches)
            {
                var captured = id;
                if (DestIds.Contains(captured) || captured == OriginId) continue;
                added++;
                string label = ephem.GetDisplayName(id);
                AddDropdownItem(content, label, false, () => {
                    if (!DestIds.Contains(captured))
                    {
                        Plugin.Log.LogInfo($"[LW] AddDest: {ephem?.GetDisplayName(captured) ?? captured}");
                        DestIds.Add(captured);
                        _sidecarDirty = true;
                        needsRefresh = true;
                    }
                    // SetTextWithoutNotify avoids firing onValueChanged (which would lose focus).
                    if (SearchInput != null) { SearchInput.SetTextWithoutNotify(""); SearchInput.ActivateInputField(); }
                    _pendingSearch = "";
                    _lastSearch    = "";
                    HideSearchDropdown();
                });
            }

            if (added == 0) { HideSearchDropdown(); return; }
            // Only reposition and re-show when first appearing; avoids forced layout rebuild each keystroke.
            if (!SearchDropGO.activeSelf)
            {
                if (SearchInput != null)
                    PositionDropdownBelow(SearchDropGO, SearchInput.GetComponent<RectTransform>(), below: true);
                SearchDropGO.SetActive(true);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Transform GetDropContent(GameObject dropGO)
            => dropGO?.transform.Find("Viewport/DropContent");

        private void PositionDropdownBelow(GameObject dropGO, RectTransform btnRT, bool below)
        {
            if (dropGO == null || btnRT == null) return;
            var dropRT = dropGO.GetComponent<RectTransform>();
            if (dropRT == null) return;

            var canvas = dropGO.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRT = canvas.GetComponent<RectTransform>();
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

            var corners = new Vector3[4];
            btnRT.GetWorldCorners(corners);
            // corners[0]=bottom-left, [1]=top-left, [2]=top-right, [3]=bottom-right
            // pivot is (0,1) on the dropdown = top-left; place top-left at button bottom-left
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, new Vector2(corners[0].x, corners[0].y), cam, out local))
            {
                dropRT.anchoredPosition = local;
            }
        }

        private void AddDropdownItem(Transform content, string label, bool dimmed, UnityAction onClick)
        {
            var go  = new GameObject("Item", typeof(RectTransform));
            go.transform.SetParent(content, false);
            var le  = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30f;
            var bg  = go.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.14f, 0.17f, 0.9f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.20f, 0.24f, 0.30f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var lbl   = new GameObject("Lbl", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.sizeDelta = new Vector2(-9f, 0f);
            var tmp = lbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = label;
            tmp.fontSize           = 16f;
            tmp.alignment          = TextAlignmentOptions.Left;
            tmp.color              = dimmed ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.raycastTarget      = false;
        }

        // ── Ephem + finder ────────────────────────────────────────────────────────

        private void TryBuildEphem(bool force = false)
        {
            if (!force && ephem != null && ephem.AllBodyIds.Count() >= 5) return;
            var ge = GravityEngine.Instance();
            if (ge == null) return;
            try
            {
                var built = GameBodyEphemeris.BuildFromScene();
                if (built.SunMu <= 0) return;
                ephem   = built;
                dvToKmS = ge.timeScale / (0.21094953 * ge.lengthScale);
                finder  = new WindowFinder(new GameLambertSolver(), ephem, dvToKmS);
                TrySelectBestCraft();
                lastEphemBuildTime = Time.realtimeSinceStartup;
                needsRefresh = true;

                if (originIds.Count == 0)
                {
                    originIds   = ephem.GetSortedOriginIds();
                    originIndex = originIds.FindIndex(id =>
                        string.Equals(ephem.GetDisplayName(id), "Earth",
                            System.StringComparison.OrdinalIgnoreCase));
                    if (originIndex < 0) originIndex = 0;
                }

                UpdateOriginLabel();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LW] BuildFromScene: {ex.GetType().Name}: {ex.Message}");
                ephem  = null;
                finder = null;
            }
        }

        private void UpdateOriginLabel()
        {
            if (OriginBtn == null || ephem == null || OriginId == null) return;
            string name = ephem.GetDisplayName(OriginId);
            var lbl = OriginBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = $"From: {name} ▼";
        }

        internal void ToggleSortOptDep() => ToggleSort(SortCol.OptDep);
        internal void ToggleSortFstDep() => ToggleSort(SortCol.FstDep);

        private void ToggleSort(SortCol col)
        {
            if (_sortCol == col)
                _sortDir = _sortDir == SortDir.Asc ? SortDir.Desc : SortDir.Asc;
            else { _sortCol = col; _sortDir = SortDir.Asc; }
            ApplySort();
        }

        private void ApplySort()
        {
            if (_sortCol == SortCol.None || cache.Count == 0) return;

            DestIds.Sort((a, b) => {
                double ka = SortKey(a), kb = SortKey(b);
                int c = ka.CompareTo(kb);
                return _sortDir == SortDir.Asc ? c : -c;
            });

            if (ContentParent != null)
                for (int i = 0; i < DestIds.Count; i++)
                {
                    var t = ContentParent.Find("Row_" + DestIds[i]);
                    if (t != null) t.SetSiblingIndex(i);
                }

            UpdateSortHeaders();
        }

        private double SortKey(string id)
        {
            if (!cache.TryGetValue(id, out var e)) return double.MaxValue;
            return _sortCol == SortCol.OptDep
                ? e.opt1?.DepartureEpoch ?? double.MaxValue
                : e.fst1?.DepartureEpoch ?? double.MaxValue;
        }

        private void UpdateSortHeaders()
        {
            string suf = _sortDir == SortDir.Asc ? " ▲" : " ▼";
            if (OptDepHdrTMP != null)
                OptDepHdrTMP.text = _sortCol == SortCol.OptDep ? "Departs" + suf : "Departs";
            if (FstDepHdrTMP != null)
                FstDepHdrTMP.text = _sortCol == SortCol.FstDep ? "Departs" + suf : "Departs";
        }

        private void TrySelectBestCraft()
        {
            if (_craftManuallySelected) return;
            var crafts = GetAllCraftDv();
            if (crafts.Length == 0) return;
            var best = crafts[0];
            for (int i = 1; i < crafts.Length; i++)
                if (crafts[i].maxDvKmS > best.maxDvKmS) best = crafts[i];
            SetCraft(best.name, best.maxDvKmS, best.maxCargo, best.exhaustV, best.dryMass, best.fuel, best.solarRangeAU);
        }

        private void SetCraft(string name, double maxDvKmS, double maxCargo, double exhaustV, double dryMass, double fuel, double solarRangeAU = 0.0)
        {
            Plugin.Log.LogInfo($"[LW] SetCraft '{name}': exhaustV={exhaustV:F3} mass={dryMass:F1} fuel={fuel:F1} maxDv={maxDvKmS:F3}km/s solarRange={solarRangeAU:F2}AU");
            _selectedCraftName   = name;
            _craftMaxDvKmS       = maxDvKmS;
            _craftSolarRangeAU   = solarRangeAU;
            _craftMaxCargo       = maxCargo;
            _craftExhaustV       = exhaustV;
            _craftDryMass        = dryMass;
            _craftFuel           = fuel;
            // Solar sails: Lambert-based Fastest is meaningless (continuous thrust, not impulsive).
            // dvCap=0 ensures FindWindows never returns a Fastest window for solar sails.
            _craftDvCapGameUnits = (solarRangeAU > 0) ? 0.0
                : (dvToKmS > 0 ? maxDvKmS / dvToKmS : double.MaxValue);
            if (CraftBtn == null) return;
            var lbl = CraftBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (lbl != null) lbl.text = $"Craft: {name} ▼";
        }

        // Returns (allObjectInfos enumerable, player Company object), or (null,null) on failure.
        private static (IEnumerable allInfos, object player) GetOmAndPlayer()
        {
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var gm = UnityEngine.Object.FindObjectOfType(typeof(GameManager));
                if (gm == null) { Plugin.Log.LogWarning("[LW] GetOmAndPlayer: GameManager not found"); return (null, null); }
                var player = gm.GetType().GetProperty("Player", bf)?.GetValue(gm)
                          ?? gm.GetType().GetField("player",    bf)?.GetValue(gm);
                if (player == null) { Plugin.Log.LogWarning("[LW] GetOmAndPlayer: Player is null"); return (null, null); }

                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return (null, null);
                var omType = asm.GetType("Manager.ObjectInfoManager");
                if (omType == null) { Plugin.Log.LogWarning("[LW] GetOmAndPlayer: ObjectInfoManager type not found"); return (null, null); }
                var om = UnityEngine.Object.FindObjectOfType(omType);
                if (om == null) { Plugin.Log.LogWarning("[LW] GetOmAndPlayer: ObjectInfoManager instance not found"); return (null, null); }

                var allInfos = omType.GetField("allObjectInfos", bf)?.GetValue(om) as IEnumerable;
                if (allInfos == null) { Plugin.Log.LogWarning("[LW] GetOmAndPlayer: allObjectInfos not found"); return (null, null); }
                return (allInfos, player);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] GetOmAndPlayer: {ex.Message}"); return (null, null); }
        }

        private (string name, double maxDvKmS, double maxCargo, double exhaustV, double dryMass, double fuel, double solarRangeAU)[] GetAllCraftDv()
        {
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var omResult = GetOmAndPlayer();
                var player = omResult.player;
                if (player == null) return Array.Empty<(string, double, double, double, double, double, double)>();

                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return Array.Empty<(string, double, double, double, double, double, double)>();

                // ShipManager.ListAllSpaceShip covers all owned spacecraft regardless of location.
                var smType = asm.GetType("ShipManager");
                if (smType == null) { Plugin.Log.LogWarning("[LW] GetAllCraftDv: ShipManager not found"); return Array.Empty<(string, double, double, double, double, double, double)>(); }
                var sm = UnityEngine.Object.FindObjectOfType(smType);
                if (sm == null) { Plugin.Log.LogWarning("[LW] GetAllCraftDv: ShipManager instance not found"); return Array.Empty<(string, double, double, double, double, double, double)>(); }

                var listAll = smType.GetProperty("ListAllSpaceShip", bf)?.GetValue(sm) as IEnumerable;
                if (listAll == null) { Plugin.Log.LogWarning("[LW] GetAllCraftDv: ListAllSpaceShip not found"); return Array.Empty<(string, double, double, double, double, double, double)>(); }

                var seen     = new HashSet<int>();
                var result   = new List<(string, double, double, double, double, double, double)>();
                System.Reflection.FieldInfo fieldSCT = null;

                foreach (var sc in listAll)
                {
                    if (fieldSCT == null)
                        fieldSCT = sc.GetType().GetField("spacecraftType", bf);
                    var scType = fieldSCT?.GetValue(sc);
                    if (scType == null) continue;
                    int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(scType);
                    if (!seen.Add(hash)) continue;

                    var scTypeType = scType.GetType();
                    bool isSolar = Convert.ToBoolean(scTypeType.GetProperty("SolarSC", bf)?.GetValue(scType));

                    double exhaustV, emptyMass, fuel, maxCargo;
                    if (isSolar)
                    {
                        exhaustV  = 0; emptyMass = 0; fuel = 0;
                        maxCargo  = 0;
                        try { maxCargo = Convert.ToDouble(scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetCargoCapacity" && m.GetParameters().Length == 1)?.Invoke(scType, new[] { player })); } catch { }
                    }
                    else
                    {
                        var mExhaustV = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetExhaustV"      && m.GetParameters().Length == 1);
                        var mMass     = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetMass"          && m.GetParameters().Length == 1);
                        var mFuel     = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetFuelCapacity"  && m.GetParameters().Length == 1);
                        var mCargo    = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetCargoCapacity" && m.GetParameters().Length == 1);
                        exhaustV  = mExhaustV != null ? Convert.ToDouble(mExhaustV.Invoke(scType, new[] { player })) : Convert.ToDouble(scTypeType.GetProperty("ExhaustV",     bf)?.GetValue(scType));
                        emptyMass = mMass     != null ? Convert.ToDouble(mMass    .Invoke(scType, new[] { player })) : Convert.ToDouble(scTypeType.GetProperty("Mass",         bf)?.GetValue(scType));
                        fuel      = mFuel     != null ? Convert.ToDouble(mFuel    .Invoke(scType, new[] { player })) : Convert.ToDouble(scTypeType.GetProperty("FuelCapacity", bf)?.GetValue(scType));
                        maxCargo  = mCargo    != null ? Convert.ToDouble(mCargo   .Invoke(scType, new[] { player })) : 0.0;
                    }

                    double maxDvKmS;
                    if (isSolar)
                    {
                        // Solar sails: use AvailableDeltaV (game-defined effective limit)
                        double availDv = 0;
                        try { availDv = Convert.ToDouble(scTypeType.GetProperty("AvailableDeltaV", bf)?.GetValue(scType)); } catch { }
                        maxDvKmS = availDv > 0 ? availDv : 100.0;
                    }
                    else
                    {
                        maxDvKmS = (emptyMass > 0 && fuel > 0)
                            ? exhaustV * Math.Log((emptyMass + fuel) / emptyMass)
                            : exhaustV;
                    }

                    double solarRangeAU = 0.0;
                    if (isSolar)
                    {
                        try
                        {
                            var mRange = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetSolarRange" && m.GetParameters().Length == 1);
                            if (mRange != null) solarRangeAU = Convert.ToDouble(mRange.Invoke(scType, new[] { player }));
                        }
                        catch { }
                    }

                    string scName;
                    try   { scName = scTypeType.GetProperty("Name", bf)?.GetValue(scType) as string ?? "?"; }
                    catch { scName = scTypeType.GetProperty("ID",   bf)?.GetValue(scType) as string ?? "?"; }

                    if (!_craftLogged)
                    {
                        if (isSolar) Plugin.Log.LogInfo($"[LW] craft '{scName}': solar sail, range={solarRangeAU:F2}AU maxCargo={maxCargo:F1}");
                        else         Plugin.Log.LogInfo($"[LW] craft '{scName}': exhaustV={exhaustV:F3} mass={emptyMass:F1} fuel={fuel:F1} maxCargo={maxCargo:F1} maxDv={maxDvKmS:F1}km/s");
                    }
                    result.Add((scName, maxDvKmS, maxCargo, exhaustV, emptyMass, fuel, solarRangeAU));
                }

                if (result.Count == 0)
                    Plugin.Log.LogWarning("[LW] GetAllCraftDv: ShipManager.ListAllSpaceShip found 0 craft");
                else
                    _craftLogged = true;
                return result.ToArray();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LW] GetAllCraftDv: {ex.Message}");
                return Array.Empty<(string, double, double, double, double, double, double)>();
            }
        }

        // Returns ephem body IDs that have at least one player spacecraft in their vicinity.
        // Uses Spacecraft.CurrentlyOnThisObject → walks up ParentObjectInfo until hitting a body in ephem.
        private HashSet<string> GetBodiesWithPlayerShips()
        {
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null || ephem == null) return new HashSet<string>();
                var smType = asm.GetType("ShipManager");
                if (smType == null) return new HashSet<string>();
                var sm = UnityEngine.Object.FindObjectOfType(smType);
                if (sm == null) return new HashSet<string>();
                var listAll = smType.GetProperty("ListAllSpaceShip", bf)?.GetValue(sm) as IEnumerable;
                if (listAll == null) return new HashSet<string>();

                var result = new HashSet<string>();
                foreach (var sc in listAll)
                {
                    // Walk: CurrentlyOnThisObject → parent → grandparent, stopping at first ephem hit.
                    var locOI = sc.GetType().GetProperty("CurrentlyOnThisObject", bf)?.GetValue(sc);
                    for (var oi = locOI; oi != null; )
                    {
                        var oiType = oi.GetType();
                        var nb = oiType.GetField("nBody", bf)?.GetValue(oi) as NBody;
                        if (nb != null)
                        {
                            string id = nb.GetInstanceID().ToString();
                            if (ephem.AllBodyIds.Contains(id)) { result.Add(id); break; }
                        }
                        oi = oiType.GetProperty("ParentObjectInfo", bf)?.GetValue(oi)
                          ?? oiType.GetField("parentObjectInfo",    bf)?.GetValue(oi);
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LW] GetBodiesWithPlayerShips: {ex.Message}");
                return new HashSet<string>();
            }
        }

        internal void AddPresenceBodies()
        {
            TryBuildEphem();
            if (ephem == null) return;

            var presenceIds = GetPresenceBodyEphemIds();
            if (presenceIds.Count == 0)
            {
                Plugin.Log.LogWarning("[LW] AddPresenceBodies: GetPresenceBodyEphemIds returned 0 — reflection target may have changed in this build");
                return;
            }

            int added = 0;
            foreach (var bodyId in ephem.AllBodyIds)
            {
                if (bodyId == OriginId || DestIds.Contains(bodyId)) continue;
                if (IsBodyDestroyedId(bodyId)) continue;
                if (presenceIds.Contains(bodyId))
                {
                    DestIds.Add(bodyId);
                    _sidecarDirty = true;
                    added++;
                }
            }

            if (added > 0) needsRefresh = true;
        }

        private HashSet<string> GetPresenceBodyEphemIds()
        {
            try
            {
                var (allInfos, player) = GetOmAndPlayer();
                if (allInfos == null) return new HashSet<string>();

                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var result = new HashSet<string>();

                System.Reflection.MethodInfo getOidM = null;
                foreach (var objectInfo in allInfos)
                {
                    // Get NBody early so facility loop can use body name for [ORBIT] check.
                    var nb = objectInfo.GetType().GetField("nBody", bf)?.GetValue(objectInfo) as NBody;
                    if (nb == null) continue;
                    var nbInfo = nb.GetObjectInfo();
                    if (nbInfo != null && nbInfo.objectTypes == EObjectTypes.Spacecraft) continue;
                    bool isOrbitBody = (nb.name ?? "").IndexOf("[ORBIT]", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (getOidM == null)
                        getOidM = objectInfo.GetType().GetMethods(bf)
                            .FirstOrDefault(m => m.Name == "GetObjectInfoData" && m.GetParameters().Length == 1);
                    var oid = getOidM?.Invoke(objectInfo, new[] { player });
                    if (oid == null) continue;
                    var facList = oid.GetType().GetProperty("ListFacility", bf)?.GetValue(oid) as ICollection;
                    if (facList == null || facList.Count == 0) continue;
                    // Only count as "base" if at least one non-probe facility has Quantity > 0.
                    bool hasBuilt = false;
                    foreach (var fac in (IEnumerable)facList)
                    {
                        var qty = fac.GetType().GetProperty("Quantity", bf)?.GetValue(fac)
                               ?? (object)fac.GetType().GetField("quantity", bf)?.GetValue(fac);
                        if (qty == null || Convert.ToInt64(qty) <= 0) continue;
                        // ProbeSpaceModule is the exact runtime type for exploration probes/rovers.
                        if (fac.GetType().Name == "ProbeSpaceModule") continue;
                        hasBuilt = true;
                        break;
                    }
                    if (!hasBuilt) continue;
                    string id = nb.GetInstanceID().ToString();
                    if (ephem != null && ephem.AllBodyIds.Contains(id))
                    {
                        result.Add(id);
                    }
                    else if (nbInfo != null && ephem != null)
                    {
                        // Moon or Orbit body not in ephem — walk to parent planet.
                        // (e.g. Callisto → Jupiter; an orbital station → its parent planet)
                        var parentInfo = nbInfo.GetType().GetProperty("ParentObjectInfo", bf)?.GetValue(nbInfo);
                        var parentNb = parentInfo?.GetType().GetField("nBody", bf)?.GetValue(parentInfo) as NBody;
                        if (parentNb != null)
                        {
                            string parentId = parentNb.GetInstanceID().ToString();
                            if (ephem.AllBodyIds.Contains(parentId)) result.Add(parentId);
                        }
                    }
                    else
                    {
                        result.Add(id); // fallback: add as-is, AddPresenceBodies will filter
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LW] GetPresenceBodyEphemIds: {ex.Message}");
                return new HashSet<string>();
            }
        }

        // ── Refresh + row building ────────────────────────────────────────────────

        private void ClearAllRowData()
        {
            cache.Clear();
            foreach (var tmps in rowTMPs.Values)
                foreach (var tmp in tmps)
                    if (tmp != null) { tmp.text = "—"; tmp.color = DashColor; }
        }

        private bool HasValidCache(string dId, double physNow)
        {
            if (!cache.TryGetValue(dId, out var entry)) return false;
            return entry.opt1.HasValue && entry.opt1.Value.DepartureEpoch > physNow;
        }

        private void DoRefresh()
        {
            refreshing   = true;
            needsRefresh = false;
            if (ephem == null || finder == null || OriginId == null) { refreshing = false; return; }

            // Drop destinations destroyed since they were added (e.g. mined-out asteroids).
            foreach (var deadId in DestIds.Where(IsBodyDestroyedId).ToList())
                RemoveDest(deadId);

            var ge = GravityEngine.Instance();
            if (ge == null) { refreshing = false; if (CalcOverlayGO != null) CalcOverlayGO.SetActive(false); return; }
            double physNow    = ge.GetPhysicalTimeDouble();
            double dvCap      = _craftDvCapGameUnits; // Fastest capped at selected craft's dv
            var destSnap      = new System.Collections.Generic.List<string>(DestIds);
            var originId      = OriginId;

            // Full recalc (cache absent or opt1 stale) vs. partial (promoted: opt1 valid, opt2 missing)
            // vs. fst-only partial (opt1 valid, fst1 stale).
            var needsOpt2Snap = new HashSet<string>(_needsOpt2Recalc);
            var needsFstSnap  = new HashSet<string>(_needsFstRecalc);
            var toCalcFull         = new List<string>();
            var toCalcPartial      = new List<(string dId, LaunchWindow opt1, LaunchWindow? fst1)>();
            var toCalcFstPartial   = new List<(string dId, LaunchWindow opt1, LaunchWindow? opt2)>();
            foreach (var dId in destSnap)
            {
                if (dId == originId) continue;
                if (!HasValidCache(dId, physNow))
                    toCalcFull.Add(dId);
                else if (needsOpt2Snap.Contains(dId) && cache.TryGetValue(dId, out var ce) && ce.opt1.HasValue)
                    toCalcPartial.Add((dId, ce.opt1.Value, ce.fst1));
                else if (needsFstSnap.Contains(dId) && cache.TryGetValue(dId, out var ce2) && ce2.opt1.HasValue)
                    toCalcFstPartial.Add((dId, ce2.opt1.Value, ce2.opt2));
            }

            if (toCalcFull.Count == 0 && toCalcPartial.Count == 0 && toCalcFstPartial.Count == 0)
            {
                // Everything is cached — rebuild UI immediately without a background thread.
                refreshing = false;
                RebuildRows();
                ApplySort();
                UpdateAllCheckboxVisuals();
                if (CalcOverlayGO != null) CalcOverlayGO.SetActive(false);
                return;
            }

            if (CalcOverlayGO != null) CalcOverlayGO.SetActive(true);

            var ephemSnap     = ephem;
            var dvToKmSSnap   = dvToKmS;

            // Snapshot propagators on main thread so background threads can call GetState safely.
            ephem.SnapshotPropagators();

            _calcDone     = false;
            _pendingCache = null;
            var t = new System.Threading.Thread(() =>
            {
                var results     = new Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)>();
                var resultsLock = new object();
                var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) };

                // Full recalcs: two FindWindows calls (opt1 + opt2).
                Parallel.ForEach<string, WindowFinder>(
                    toCalcFull,
                    parallelOpts,
                    () => new WindowFinder(new GameLambertSolver(), ephemSnap, dvToKmSSnap),
                    (dId, _, localFinder) =>
                    {
                        (LaunchWindow? opt1, LaunchWindow? fst1, LaunchWindow? opt2, LaunchWindow? fst2) entry;
                        try
                        {
                            var (o1, f1, syn) = localFinder.FindWindows(originId, dId, physNow, dvCap);
                            LaunchWindow? o2 = null, f2 = null;
                            if (syn > 0)
                            {
                                var (oo2, ff2, _) = localFinder.FindWindows(originId, dId, physNow + syn, dvCap);
                                o2 = oo2; f2 = ff2;
                            }
                            entry = (o1, f1, o2, f2);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[LW] FindWindows {dId}: {ex.Message}");
                            entry = (null, null, null, null);
                        }
                        lock (resultsLock) { results[dId] = entry; }
                        return localFinder;
                    },
                    _ => { }
                );

                // Partial recalcs: opt1 already known (promoted from opt2); one scan for new opt2.
                Parallel.ForEach<(string dId, LaunchWindow opt1, LaunchWindow? fst1), WindowFinder>(
                    toCalcPartial,
                    parallelOpts,
                    () => new WindowFinder(new GameLambertSolver(), ephemSnap, dvToKmSSnap),
                    (item, _, localFinder) =>
                    {
                        try
                        {
                            double syn = localFinder.GetSynodic(originId, item.dId);
                            // Back off by 1/12 of the origin's period so we don't clip the leading
                            // edge of the window if opt1 landed near the tail of the previous one.
                            double bufferPhys = ephemSnap.GetPeriod(originId) / 12.0;
                            double startTime  = syn > 0
                                ? item.opt1.DepartureEpoch + syn - bufferPhys
                                : item.opt1.DepartureEpoch;
                            var (o2, f2, _) = localFinder.FindWindows(originId, item.dId, startTime, dvCap);
                            lock (resultsLock) { results[item.dId] = (item.opt1, item.fst1, o2, f2); }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[LW] FindWindows opt2 {item.dId}: {ex.Message}");
                            lock (resultsLock) { results[item.dId] = (item.opt1, item.fst1, null, null); }
                        }
                        return localFinder;
                    },
                    _ => { }
                );

                // Fst-only partial recalcs: opt1/opt2 valid but fst1 stale — find fresh fst windows.
                Parallel.ForEach<(string dId, LaunchWindow opt1, LaunchWindow? opt2), WindowFinder>(
                    toCalcFstPartial,
                    parallelOpts,
                    () => new WindowFinder(new GameLambertSolver(), ephemSnap, dvToKmSSnap),
                    (item, loopState, localFinder) =>
                    {
                        try
                        {
                            double syn = localFinder.GetSynodic(originId, item.dId);
                            var r1 = localFinder.FindWindows(originId, item.dId, physNow, dvCap);
                            LaunchWindow? f1 = r1.fastest;
                            LaunchWindow? f2 = null;
                            if (syn > 0)
                            {
                                var r2 = localFinder.FindWindows(originId, item.dId, physNow + syn, dvCap);
                                f2 = r2.fastest;
                            }
                            lock (resultsLock) { results[item.dId] = (item.opt1, f1, item.opt2, f2); }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[LW] FindWindows fst {item.dId}: {ex.Message}");
                            lock (resultsLock) { results[item.dId] = (item.opt1, null, item.opt2, null); }
                        }
                        return localFinder;
                    },
                    localFinder => { }
                );

                _pendingCache = results;
                _calcDone = true;   // volatile write: flush _pendingCache before signalling
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ApplyPendingResults()
        {
            if (_pendingCache == null) { refreshing = false; return; }
            try
            {
                // Merge new results; existing valid cache entries for un-recalculated dests survive.
                foreach (var kv in _pendingCache) cache[kv.Key] = kv.Value;
                _pendingCache = null;
                _needsOpt2Recalc.Clear();
                _needsFstRecalc.Clear();
                RebuildRows();
                ApplySort();
                UpdateAllCheckboxVisuals();
                lastRefreshTime = Time.realtimeSinceStartup;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LW] ApplyPendingResults: {ex.Message}");
            }
            finally
            {
                refreshing = false;
                if (CalcOverlayGO != null) CalcOverlayGO.SetActive(false);
            }
        }

        private void RebuildRows()
        {
            if (ContentParent == null) return;
            var ge = GravityEngine.Instance();

            foreach (var dId in DestIds)
            {
                if (dId == OriginId) continue;
                if (!rowTMPs.ContainsKey(dId))
                    CreateRow(dId);
            }

            foreach (var dId in rowTMPs.Keys.Where(k => !DestIds.Contains(k) || k == OriginId).ToList())
            {
                rowTMPs.Remove(dId);
                rowCheckboxBtns.Remove(dId);
                var t = ContentParent.Find("Row_" + dId);
                if (t != null) Destroy(t.gameObject);
            }

            double physNow = ge != null ? ge.GetPhysicalTimeDouble() : 0;

            foreach (var dId in DestIds)
            {
                if (dId == OriginId || !rowTMPs.ContainsKey(dId)) continue;
                var tmps = rowTMPs[dId];

                // Out-of-range indicator for solar sails.
                bool outOfRange = false;
                if (_craftSolarRangeAU > 0 && ephem != null && ge != null)
                {
                    double dist = ephem.GetState(dId, physNow).Position.Magnitude;
                    outOfRange = dist > _craftSolarRangeAU;
                }
                if (rowNameTMPs.TryGetValue(dId, out var nameTMP))
                    nameTMP.color = outOfRange ? new Color(0.45f, 0.45f, 0.45f) : Color.white;

                if (cache.TryGetValue(dId, out var entry))
                {
                    // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
                    // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
                    // [12]=opt1Fuel [13]=fst1Fuel [14]=opt2Fuel [15]=fst2Fuel
                    SetWindowCells(entry.opt1, tmps[0], tmps[1], tmps[2], tmps[12], ge);
                    SetWindowCells(entry.fst1, tmps[3], tmps[4], tmps[5], tmps[13], ge);
                    SetNextCells(entry.opt2, tmps[6], tmps[7], tmps[8], tmps[14], ge);
                    SetNextCells(entry.fst2, tmps[9], tmps[10], tmps[11], tmps[15], ge);
                }
            }
        }

        // Sub-column widths — must match injector sub-header widths exactly.
        // Optimal: dep=108 (cb 18 + text 90), dv=95, tvl=flex, fuel=75 (within 458px group)
        // Fastest: dep=120, dv=110, tvl=flex, fuel=75 (within 458px group; row1 group is 437 + 21px trailing ×)
        private const float CB_W       = 18f;
        private const float OPT_DEP_W  = 108f;
        private const float OPT_DV_W   = 95f;
        private const float FST_DEP_W  = 120f;
        private const float FST_DV_W   = 110f;
        private const float FUEL_W     = 75f;

        private void CreateRow(string dId)
        {
            // Container is a VLG holding primary row (30px) + next-window row (23px).
            var container = new GameObject("Row_" + dId, typeof(RectTransform));
            container.transform.SetParent(ContentParent, false);
            container.AddComponent<LayoutElement>().preferredHeight = 53f;
            var containerVLG = container.AddComponent<VerticalLayoutGroup>();
            containerVLG.childControlHeight = true; containerVLG.childControlWidth = true;
            containerVLG.childForceExpandHeight = false; containerVLG.childForceExpandWidth = true;
            containerVLG.spacing = 0f;

            // ── Primary row ──────────────────────────────────────────────────────────
            var row1 = new GameObject("R1", typeof(RectTransform));
            row1.transform.SetParent(container.transform, false);
            row1.AddComponent<LayoutElement>().preferredHeight = 30f;

            var inner = new GameObject("HLG", typeof(RectTransform));
            inner.transform.SetParent(row1.transform, false);
            var rt = inner.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var hlg = inner.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.spacing = 0f;

            string displayName = ephem?.GetDisplayName(dId) ?? dId;

            // Try to get the body's icon and ObjectInfo for click-to-navigate.
            Sprite bodyIcon = null;
            object bodyOI   = null;
            var gameEphem = ephem as GameBodyEphemeris;
            if (gameEphem != null)
            {
                var nb = gameEphem.GetNBodyForId(dId);
                if (nb != null)
                {
                    bodyOI = nb.GetObjectInfo();
                    if (bodyOI != null)
                    {
                        const BindingFlags bfi = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        bodyIcon = bodyOI.GetType().GetProperty("ImagePlanetUI", bfi)?.GetValue(bodyOI) as Sprite;
                    }
                }
            }

            // Name cell (158px): icon (18px) + name label/btn (flex)
            var nameCell = new GameObject("NameCell", typeof(RectTransform));
            nameCell.transform.SetParent(inner.transform, false);
            nameCell.AddComponent<LayoutElement>().preferredWidth = 158f;
            var nHlg = nameCell.AddComponent<HorizontalLayoutGroup>();
            nHlg.childControlHeight = true; nHlg.childControlWidth = true;
            nHlg.childForceExpandHeight = true; nHlg.childForceExpandWidth = false;
            nHlg.spacing = 1f;

            // Icon slot (12px)
            var iconGO  = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(nameCell.transform, false);
            iconGO.AddComponent<LayoutElement>().preferredWidth = 18f;
            var iconImg = iconGO.AddComponent<Image>();
            if (bodyIcon != null) { iconImg.sprite = bodyIcon; iconImg.preserveAspect = true; }
            else                  { iconImg.color = Color.clear; }
            iconImg.raycastTarget = false;
            rowIconImgs[dId] = iconImg;

            // Name button (flex) — click focuses the body in the game view
            var nameGO  = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(nameCell.transform, false);
            nameGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameImg = nameGO.AddComponent<Image>(); nameImg.color = Color.clear; nameImg.raycastTarget = true;
            var nameBtn = nameGO.AddComponent<Button>(); nameBtn.targetGraphic = nameImg;
            var nameC   = nameBtn.colors; nameC.highlightedColor = new Color(1f, 1f, 1f, 0.08f); nameBtn.colors = nameC;
            nameBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            var capOI   = bodyOI;
            if (capOI != null)
            {
                nameBtn.onClick.AddListener(() =>
                {
                    try { UIManager.Instance.Open(EWindowType.ObjectInfo, capOI); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[LW] body click: {ex.Message}"); }
                });
            }
            var nameLblGO = new GameObject("L", typeof(RectTransform));
            nameLblGO.transform.SetParent(nameGO.transform, false);
            var nameLblRT = nameLblGO.GetComponent<RectTransform>();
            nameLblRT.anchorMin = Vector2.zero; nameLblRT.anchorMax = Vector2.one; nameLblRT.sizeDelta = Vector2.zero;
            var nameTMP = nameLblGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) nameTMP.font = FontAsset;
            nameTMP.text = displayName; nameTMP.fontSize = 16f;
            nameTMP.alignment = TextAlignmentOptions.Left; nameTMP.color = Color.white;
            nameTMP.enableWordWrapping = false; nameTMP.overflowMode = TextOverflowModes.Ellipsis;
            nameTMP.raycastTarget = false;
            rowNameTMPs[dId] = nameTMP;

            // Optimal group: [cb+dep | dv | tvl]  |gap|  Fastest: [dep | dv | tvl]
            var oGroup = new GameObject("OptCol", typeof(RectTransform));
            oGroup.transform.SetParent(inner.transform, false);
            oGroup.AddComponent<LayoutElement>().preferredWidth = 458f;
            var oHlg = oGroup.AddComponent<HorizontalLayoutGroup>();
            oHlg.childControlHeight = true; oHlg.childControlWidth = true;
            oHlg.childForceExpandHeight = true; oHlg.childForceExpandWidth = false;
            oHlg.spacing = 0f;
            // Dep cell wraps checkbox + text within OPT_DEP_W total
            var oDCell = new GameObject("DepC", typeof(RectTransform));
            oDCell.transform.SetParent(oGroup.transform, false);
            oDCell.AddComponent<LayoutElement>().preferredWidth = OPT_DEP_W;
            var oDHlg = oDCell.AddComponent<HorizontalLayoutGroup>();
            oDHlg.childControlHeight = true; oDHlg.childControlWidth = true;
            oDHlg.childForceExpandHeight = true; oDHlg.childForceExpandWidth = false;
            oDHlg.spacing = 0f;
            var cb1 = MakeCheckboxButton(oDCell.transform);
            var oD  = MakeColLabel(oDCell.transform, "—", 15f, TextAlignmentOptions.Left, OPT_DEP_W - CB_W);
            var oDv  = MakeColLabel(oGroup.transform, "—", 15f, TextAlignmentOptions.Left, OPT_DV_W);
            var oTvl = MakeColLabel(oGroup.transform, "—", 15f, TextAlignmentOptions.Left, 0f, flex: true);
            var oFu  = MakeColLabel(oGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W);
            var sep1 = new GameObject("Sep", typeof(RectTransform));
            sep1.transform.SetParent(inner.transform, false);
            sep1.AddComponent<LayoutElement>().preferredWidth = 12f;
            // Fastest group — inline with checkbox, matching optimal group structure
            var fGroup = new GameObject("FstCol", typeof(RectTransform));
            fGroup.transform.SetParent(inner.transform, false);
            // 21px narrower than the sub-header's 458 to make room for the trailing ×;
            // only the flex travel column shrinks, so dep/dv/fuel stay aligned.
            fGroup.AddComponent<LayoutElement>().preferredWidth = 437f;
            var fHlg = fGroup.AddComponent<HorizontalLayoutGroup>();
            fHlg.childControlHeight = true; fHlg.childControlWidth = true;
            fHlg.childForceExpandHeight = true; fHlg.childForceExpandWidth = false;
            fHlg.spacing = 0f;
            var fDCell = new GameObject("FDepC", typeof(RectTransform));
            fDCell.transform.SetParent(fGroup.transform, false);
            fDCell.AddComponent<LayoutElement>().preferredWidth = FST_DEP_W;
            var fDHlg = fDCell.AddComponent<HorizontalLayoutGroup>();
            fDHlg.childControlHeight = true; fDHlg.childControlWidth = true;
            fDHlg.childForceExpandHeight = true; fDHlg.childForceExpandWidth = false;
            fDHlg.spacing = 0f;
            var fstCb1 = MakeCheckboxButton(fDCell.transform);
            var fD     = MakeColLabel(fDCell.transform, "—", 15f, TextAlignmentOptions.Left, FST_DEP_W - CB_W);
            var fDv    = MakeColLabel(fGroup.transform, "—", 15f, TextAlignmentOptions.Left, FST_DV_W);
            var fTvl   = MakeColLabel(fGroup.transform, "—", 15f, TextAlignmentOptions.Left, 0f, flex: true);
            var fFu    = MakeColLabel(fGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W);

            // Trailing × delete button (21px, far right of the primary row)
            var xGO  = new GameObject("X", typeof(RectTransform));
            xGO.transform.SetParent(inner.transform, false);
            xGO.AddComponent<LayoutElement>().preferredWidth = 21f;
            var xImg = xGO.AddComponent<Image>(); xImg.color = new Color(0.35f, 0.06f, 0.06f, 0.55f);
            var xBtn = xGO.AddComponent<Button>(); xBtn.targetGraphic = xImg;
            var xC   = xBtn.colors;
            xC.normalColor      = new Color(0.35f, 0.06f, 0.06f, 0.55f);
            xC.highlightedColor = new Color(0.70f, 0.12f, 0.12f, 0.90f);
            xC.pressedColor     = new Color(0.90f, 0.15f, 0.15f, 1.00f);
            xBtn.colors = xC;
            var captured = dId;
            xBtn.onClick.AddListener(() => RemoveDest(captured));
            var xLbl = new GameObject("L", typeof(RectTransform));
            xLbl.transform.SetParent(xGO.transform, false);
            var xLblRT = xLbl.GetComponent<RectTransform>();
            xLblRT.anchorMin = Vector2.zero; xLblRT.anchorMax = Vector2.one; xLblRT.sizeDelta = Vector2.zero;
            var xTMP = xLbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) xTMP.font = FontAsset;
            xTMP.text = "×"; xTMP.fontSize = 15f; xTMP.alignment = TextAlignmentOptions.Center;
            xTMP.color = new Color(1f, 0.55f, 0.55f); xTMP.enableWordWrapping = false;
            xTMP.raycastTarget = false;

            // ── Next-window row (dimmed, 23px) ───────────────────────────────────────
            var row2 = new GameObject("R2", typeof(RectTransform));
            row2.transform.SetParent(container.transform, false);
            row2.AddComponent<LayoutElement>().preferredHeight = 23f;

            var inner2 = new GameObject("HLG2", typeof(RectTransform));
            inner2.transform.SetParent(row2.transform, false);
            var rt2 = inner2.GetComponent<RectTransform>();
            rt2.anchorMin = Vector2.zero; rt2.anchorMax = Vector2.one;
            rt2.offsetMin = Vector2.zero; rt2.offsetMax = Vector2.zero;
            var hlg2 = inner2.AddComponent<HorizontalLayoutGroup>();
            hlg2.childControlHeight = true; hlg2.childControlWidth = true;
            hlg2.childForceExpandHeight = true; hlg2.childForceExpandWidth = false;
            hlg2.spacing = 0f;

            // Blank name placeholder (158px)
            var ns = new GameObject("NS", typeof(RectTransform));
            ns.transform.SetParent(inner2.transform, false);
            ns.AddComponent<LayoutElement>().preferredWidth = 158f;

            Color dimC = new Color(0.50f, 0.50f, 0.50f);
            // Row-2 opt group: 458px container mirrors row1's oGroup so flex tvl consumes same width.
            var noOGroup = new GameObject("OptCol2", typeof(RectTransform));
            noOGroup.transform.SetParent(inner2.transform, false);
            noOGroup.AddComponent<LayoutElement>().preferredWidth = 458f;
            var noOHlg = noOGroup.AddComponent<HorizontalLayoutGroup>();
            noOHlg.childControlHeight = true; noOHlg.childControlWidth = true;
            noOHlg.childForceExpandHeight = true; noOHlg.childForceExpandWidth = false;
            noOHlg.spacing = 0f;
            // Row-2 opt2 dep cell: checkbox + text within OPT_DEP_W
            var noD2Cell = new GameObject("DepC2", typeof(RectTransform));
            noD2Cell.transform.SetParent(noOGroup.transform, false);
            noD2Cell.AddComponent<LayoutElement>().preferredWidth = OPT_DEP_W;
            var noD2Hlg = noD2Cell.AddComponent<HorizontalLayoutGroup>();
            noD2Hlg.childControlHeight = true; noD2Hlg.childControlWidth = true;
            noD2Hlg.childForceExpandHeight = true; noD2Hlg.childForceExpandWidth = false;
            noD2Hlg.spacing = 0f;
            var cb2 = MakeCheckboxButton(noD2Cell.transform, forRow2: true);
            var noD  = MakeColLabel(noD2Cell.transform, "—", 15f, TextAlignmentOptions.Left, OPT_DEP_W - CB_W, dimC);
            var noDv = MakeColLabel(noOGroup.transform, "—", 15f, TextAlignmentOptions.Left, OPT_DV_W,  dimC);
            var noTvl = MakeColLabel(noOGroup.transform, "—", 15f, TextAlignmentOptions.Left, 0f, dimC, flex: true);
            var noFu = MakeColLabel(noOGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W, dimC);
            var sep2 = new GameObject("Sep2", typeof(RectTransform));
            sep2.transform.SetParent(inner2.transform, false);
            sep2.AddComponent<LayoutElement>().preferredWidth = 12f;
            // Row-2 fst group: 458px container mirrors row1's fGroup so Fastest checkbox lands at same x.
            var noFGroup = new GameObject("FstCol2", typeof(RectTransform));
            noFGroup.transform.SetParent(inner2.transform, false);
            noFGroup.AddComponent<LayoutElement>().preferredWidth = 458f;
            var noFHlg = noFGroup.AddComponent<HorizontalLayoutGroup>();
            noFHlg.childControlHeight = true; noFHlg.childControlWidth = true;
            noFHlg.childForceExpandHeight = true; noFHlg.childForceExpandWidth = false;
            noFHlg.spacing = 0f;
            var nfDCell = new GameObject("FDepC2", typeof(RectTransform));
            nfDCell.transform.SetParent(noFGroup.transform, false);
            nfDCell.AddComponent<LayoutElement>().preferredWidth = FST_DEP_W;
            var nfDHlg = nfDCell.AddComponent<HorizontalLayoutGroup>();
            nfDHlg.childControlHeight = true; nfDHlg.childControlWidth = true;
            nfDHlg.childForceExpandHeight = true; nfDHlg.childForceExpandWidth = false;
            nfDHlg.spacing = 0f;
            var fstCb2 = MakeCheckboxButton(nfDCell.transform, forRow2: true);
            var nfD   = MakeColLabel(nfDCell.transform, "—", 15f, TextAlignmentOptions.Left, FST_DEP_W - CB_W, dimC);
            var nfDv  = MakeColLabel(noFGroup.transform, "—", 15f, TextAlignmentOptions.Left, FST_DV_W,  dimC);
            var nfTvl = MakeColLabel(noFGroup.transform, "—", 15f, TextAlignmentOptions.Left, 0f, dimC, flex: true);
            var nfFu  = MakeColLabel(noFGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W, dimC);

            // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
            // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
            // [12]=opt1Fuel [13]=fst1Fuel [14]=opt2Fuel [15]=fst2Fuel
            rowTMPs[dId] = new[] { oD, oDv, oTvl, fD, fDv, fTvl, noD, noDv, noTvl, nfD, nfDv, nfTvl, oFu, fFu, noFu, nfFu };

            var capDest = dId;
            cb1.onClick.AddListener(()    => ToggleAlarmForRow(capDest, false, false));
            cb2.onClick.AddListener(()    => ToggleAlarmForRow(capDest, true,  false));
            fstCb1.onClick.AddListener(() => ToggleAlarmForRow(capDest, false, true));
            fstCb2.onClick.AddListener(() => ToggleAlarmForRow(capDest, true,  true));
            rowCheckboxBtns[dId] = new[] { cb1, cb2, fstCb1, fstCb2 };
        }

        private TextMeshProUGUI MakeDataGroup(Transform parent,
            out TextMeshProUGUI dvTMP, out TextMeshProUGUI tvlTMP,
            bool isOptimal = false)
        {
            var go = new GameObject("Col", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 383f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.spacing = 0f;
            float depW = isOptimal ? OPT_DEP_W : FST_DEP_W;
            float dvW  = isOptimal ? OPT_DV_W  : FST_DV_W;
            var dep = MakeColLabel(go.transform, "—", 15f, TextAlignmentOptions.Left, depW);
            dvTMP   = MakeColLabel(go.transform, "—", 15f, TextAlignmentOptions.Left, dvW);
            tvlTMP  = MakeColLabel(go.transform, "—", 15f, TextAlignmentOptions.Left, 0f, flex: true);
            return dep;
        }

        private void RemoveDest(string dId)
        {
            string name = ephem?.GetDisplayName(dId) ?? dId;
            DestIds.Remove(dId);
            _sidecarDirty = true;
            cache.Remove(dId);
            rowTMPs.Remove(dId);
            rowNameTMPs.Remove(dId);
            rowIconImgs.Remove(dId);
            rowCheckboxBtns.Remove(dId);
            _alarms.RemoveWhere(k => k.DestId == dId);
            var t = ContentParent?.Find("Row_" + dId);
            Plugin.Log.LogInfo($"[LW] RemoveDest: {name} rowFound={t != null}");
            if (t != null) Destroy(t.gameObject);
        }

        private TextMeshProUGUI MakeColLabel(Transform parent, string text, float size,
                                              TextAlignmentOptions align, float width,
                                              Color? color = null, bool flex = false)
        {
            // Pure container: only LayoutElement on the GO so nothing competes with preferredWidth.
            var go  = new GameObject("C", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var lbl = new GameObject("L", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.sizeDelta = Vector2.zero;
            var tmp = lbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = text;
            tmp.fontSize           = size;
            tmp.alignment          = align;
            tmp.color              = color ?? Color.white;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Ellipsis;
            tmp.raycastTarget      = false;
            var le = go.AddComponent<LayoutElement>();
            if (flex) le.flexibleWidth = 1f;
            else      le.preferredWidth = width;
            return tmp;
        }

        // ── Formatting ────────────────────────────────────────────────────────────

        private static readonly Color RedMuted   = new Color(1f, 0.32f, 0.32f);
        private static readonly Color WhiteColor = Color.white;
        private static readonly Color DashColor  = new Color(0.55f, 0.55f, 0.55f);

        private void SetWindowCells(LaunchWindow? w,
            TextMeshProUGUI dep, TextMeshProUGUI dv, TextMeshProUGUI tvl,
            TextMeshProUGUI fuel, GravityEngine ge)
        {
            if (w == null || ge == null)
            {
                dep.text = dv.text = tvl.text = fuel.text = "—";
                dep.color = dv.color = tvl.color = fuel.color = DashColor;
                return;
            }
            dep.text  = FormatEpoch(w.Value.DepartureEpoch);
            dv.text   = $"{w.Value.DeltaVKmS:F1}km/s";
            tvl.text  = FormatTravel(w.Value.TravelTimeSeconds, ge);
            fuel.text = FormatFuel(w.Value.DeltaVKmS);
            bool unreachable = w.Value.DeltaVKmS > _craftMaxDvKmS;
            Color c = unreachable ? RedMuted : WhiteColor;
            dep.color = dv.color = tvl.color = fuel.color = c;
        }

        private static readonly Color DimColor = new Color(0.50f, 0.50f, 0.50f);

        private void SetNextCells(LaunchWindow? w,
            TextMeshProUGUI dep, TextMeshProUGUI dv, TextMeshProUGUI tvl,
            TextMeshProUGUI fuel, GravityEngine ge)
        {
            dep.color = dv.color = tvl.color = fuel.color = DimColor;
            if (w == null || ge == null)
            {
                dep.text = dv.text = tvl.text = fuel.text = "—";
                return;
            }
            dep.text  = FormatEpoch(w.Value.DepartureEpoch);
            dv.text   = $"{w.Value.DeltaVKmS:F1}km/s";
            tvl.text  = FormatTravel(w.Value.TravelTimeSeconds, ge);
            fuel.text = FormatFuel(w.Value.DeltaVKmS);
        }

        // Propellant for a transfer via the rocket equation: fuel = dry × (e^(Δv/ve) − 1).
        // _craftExhaustV comes from SpacecraftType.GetExhaustV(player), which multiplies the
        // base (or completed hull design) exhaust velocity by the company's researched
        // EBonus.ComponentExhaustV bonuses — so this always reflects the currently-researched
        // engine variant. Solar sails burn no fuel; no craft data shows "—".
        private string FormatFuel(double dvKmS)
        {
            if (_craftExhaustV <= 0 || _craftDryMass <= 0 || _craftSolarRangeAU > 0) return "—";
            double fuel = _craftDryMass * (Math.Exp(dvKmS / _craftExhaustV) - 1.0);
            if (double.IsNaN(fuel) || double.IsInfinity(fuel)) return "—";
            return fuel >= 100 ? $"{fuel:F0}t" : $"{fuel:F1}t";
        }

        private string FormatEpoch(double epoch)
        {
            try
            {
                var tc = MonoBehaviourSingleton<TimeController>.Instance;
                var ge = GravityEngine.Instance();
                if (tc == null || ge == null) return "—";
                double secPerPhys = GravityScaler.GetGameSecondPerPhysicsSecond();
                if (secPerPhys <= 0) secPerPhys = 1;
                DateTime d = tc.CurrentTime + TimeSpan.FromSeconds((epoch - ge.GetPhysicalTimeDouble()) * secPerPhys);
                return $"{d.ToString("MMM")} '{d.Year % 100:D2}";
            }
            catch { return "—"; }
        }

        private string FormatTravel(double travelPhys, GravityEngine ge)
        {
            double oneYear = ge.timeScale;
            if (oneYear <= 0) return "—";
            if (travelPhys < 1.5 * oneYear)
                return $"{travelPhys / (oneYear / 12.0):F1}mo";
            return $"{travelPhys / oneYear:F1}yr";
        }

        // ── Alarm checking ────────────────────────────────────────────────────────

        private void CheckAlarms()
        {
            if (_alarms.Count == 0 || _clock == null) return;
            var toFire = LWCacheHelper.GetAlarmsToFire(_alarms, OriginId, _clock.CurrentTime);
            foreach (var key in toFire)
            {
                _alarms.Remove(key);
                _sidecarDirty = true;
                _firedAlarms.Add(key);
                FireAlarm(key);
            }
        }

        private void FireAlarm(AlarmKey key)
        {
            string destName   = ephem?.GetDisplayName(key.DestId)   ?? key.DestId;
            string originName = ephem?.GetDisplayName(key.OriginId) ?? key.OriginId;
            string kind = key.IsFastest ? "fastest" : "optimal";

            if (!TryFireGameNotification(key.DestId, originName, destName, kind))
                SpawnToast($"Launch window ({kind}): {originName} → {destName}");

            UpdateAllCheckboxVisuals();
        }

        // Fire a real game notification so it appears in "New Notifications" and is saved.
        // Uses Schedule (13) which has locale text "Mission from {0} to {1} scheduled for {2}".
        // Game pauses if the player's pause-on-notification toggle is enabled.
        private bool TryFireGameNotification(string destId, string originName, string destName, string kind)
        {
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var nm = UnityEngine.Object.FindObjectOfType(typeof(NotificationManager)) as NotificationManager;
                if (nm == null) return false;

                // GetNotification(ENotificationActionAfterClick.Schedule = 13)
                var mGetNotif = nm.GetType().GetMethod("GetNotification", bf);
                if (mGetNotif == null) return false;
                var enumType  = mGetNotif.GetParameters()[0].ParameterType;
                var notifData = mGetNotif.Invoke(nm, new object[] { Enum.ToObject(enumType, 13) });
                if (notifData == null) return false;

                // Player company
                var omResult = GetOmAndPlayer();
                var player   = omResult.player;
                if (player == null) return false;

                // Origin and destination ObjectInfos
                var gameEphem = ephem as GameBodyEphemeris;
                var destNb    = gameEphem?.GetNBodyForId(destId);
                var destOI    = destNb?.GetObjectInfo();
                var originNb  = gameEphem?.GetNBodyForId(OriginId ?? "");
                var originOI  = originNb?.GetObjectInfo();

                // Click handler: open destination planet panel
                System.Action onClick = null;
                if (destOI != null)
                {
                    var capOI = (object)destOI;
                    onClick = () =>
                    {
                        try { UIManager.Instance.Open(EWindowType.ObjectInfo, (Game.Info.InfoBase)capOI); }
                        catch (Exception ex) { Plugin.Log.LogWarning($"[LW] body click: {ex.Message}"); }
                    };
                }

                // Date string from alarm key — find the current alarm we're firing
                string dateStr = "";
                foreach (var k in _firedAlarms)
                {
                    if (k.DestId == destId)
                    {
                        dateStr = new System.DateTime(k.Year, k.Month, 1).ToString("MMM") +
                                  " '" + (k.Year % 100).ToString("D2");
                        break;
                    }
                }

                // ShowNotification(NotificationData, Company, ObjectInfo, Action, params object[])
                // Schedule locale: "Mission from {0} to {1} scheduled for {2}"
                var mShow = nm.GetType().GetMethods(bf)
                    .FirstOrDefault(m => m.Name == "ShowNotification" && m.GetParameters().Length >= 4);
                if (mShow == null) return false;
                mShow.Invoke(nm, new object[] { notifData, player, destOI, onClick,
                    new object[] { originName, destName, dateStr } });

                // After creation: swap notification icon to destination planet icon,
                // and override text with colored origin/destination names.
                var notifUILast = nm.GetType().GetField("notificationUILast", bf)?.GetValue(nm);
                if (notifUILast != null)
                {
                    var notifType = notifUILast.GetType();
                    // Swap image to destination planet icon
                    if (destOI != null)
                    {
                        var destSprite = destOI.GetType().GetProperty("ImagePlanetUI", bf)?.GetValue(destOI) as Sprite;
                        if (destSprite != null)
                        {
                            var imgField = notifType.GetField("image", bf);
                            var img = imgField?.GetValue(notifUILast) as Image;
                            if (img != null) img.sprite = destSprite;
                        }
                    }
                    // Override text with highlighted names: "Earth → Mars\nlaunch window (optimal) Jul '37"
                    var textField = notifType.GetField("text", bf);
                    var tmp = textField?.GetValue(notifUILast) as TextMeshProUGUI;
                    if (tmp != null)
                    {
                        string originHL = originOI?.GetType().GetProperty("ObjectNameHighLight", bf)?.GetValue(originOI) as string ?? originName;
                        string destHL   = destOI?.GetType().GetProperty("ObjectNameHighLight", bf)?.GetValue(destOI) as string ?? destName;
                        tmp.text = $"{originHL} → {destHL}\nlaunch window ({kind}){(string.IsNullOrEmpty(dateStr) ? "" : " " + dateStr)}";
                    }
                }

                _clock.PauseGame();
                Plugin.Log.LogInfo($"[LW] Notification fired: {originName} → {destName} ({kind})");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LW] TryFireGameNotification: {ex.Message}");
                return false;
            }
        }

        private void SpawnToast(Sprite iconA, Sprite iconB, string richText)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var toastGO = new GameObject("LWToast", typeof(RectTransform));
            toastGO.transform.SetParent(canvas.transform, false);
            toastGO.AddComponent<LayoutElement>().ignoreLayout = true;
            var toastRT = toastGO.GetComponent<RectTransform>();
            toastRT.anchorMin = new Vector2(0.5f, 0f); toastRT.anchorMax = new Vector2(0.5f, 0f);
            toastRT.pivot = new Vector2(0.5f, 0f); toastRT.sizeDelta = new Vector2(480f, 72f);
            toastRT.anchoredPosition = new Vector2(0f, 90f);
            var bg = toastGO.AddComponent<Image>(); bg.color = new Color(0.05f, 0.50f, 0.58f, 0.95f); bg.raycastTarget = true;
            var hlg = toastGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.padding = new RectOffset(12, 3, 6, 6); hlg.spacing = 9f;

            void AddIcon(Sprite spr) {
                var iGO = new GameObject("Ic", typeof(RectTransform));
                iGO.transform.SetParent(toastGO.transform, false);
                iGO.AddComponent<LayoutElement>().preferredWidth = 36f;
                var img = iGO.AddComponent<Image>();
                img.raycastTarget = false;
                if (spr != null) { img.sprite = spr; img.preserveAspect = true; }
                else              img.color = Color.clear;
            }
            AddIcon(iconA);
            AddIcon(iconB);

            var msgGO = new GameObject("Msg", typeof(RectTransform));
            msgGO.transform.SetParent(toastGO.transform, false);
            msgGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var msgTMP = msgGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) msgTMP.font = FontAsset;
            msgTMP.text = richText; msgTMP.fontSize = 16f;
            msgTMP.color = Color.white; msgTMP.alignment = TextAlignmentOptions.Left;
            msgTMP.enableWordWrapping = false; msgTMP.overflowMode = TextOverflowModes.Ellipsis;
            msgTMP.richText = true; msgTMP.raycastTarget = false;

            var closeGO = new GameObject("X", typeof(RectTransform));
            closeGO.transform.SetParent(toastGO.transform, false);
            closeGO.AddComponent<LayoutElement>().preferredWidth = 36f;
            var cImg = closeGO.AddComponent<Image>(); cImg.color = new Color(1f, 1f, 1f, 0.08f);
            var cBtn = closeGO.AddComponent<Button>(); cBtn.targetGraphic = cImg;
            var capT = toastGO; cBtn.onClick.AddListener(() => Destroy(capT));
            var cLbl = new GameObject("L", typeof(RectTransform)); cLbl.transform.SetParent(closeGO.transform, false);
            var cLblRT = cLbl.GetComponent<RectTransform>(); cLblRT.anchorMin = Vector2.zero; cLblRT.anchorMax = Vector2.one; cLblRT.sizeDelta = Vector2.zero;
            var cTMP = cLbl.AddComponent<TextMeshProUGUI>(); if (FontAsset != null) cTMP.font = FontAsset;
            cTMP.text = "×"; cTMP.fontSize = 22f; cTMP.alignment = TextAlignmentOptions.Center;
            cTMP.color = Color.white; cTMP.raycastTarget = false; cTMP.enableWordWrapping = false;
        }

        private void SpawnToast(string message)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            var toastGO = new GameObject("LWToast", typeof(RectTransform));
            toastGO.transform.SetParent(canvas.transform, false);
            toastGO.AddComponent<LayoutElement>().ignoreLayout = true;

            var toastRT = toastGO.GetComponent<RectTransform>();
            toastRT.anchorMin        = new Vector2(0.5f, 0f);
            toastRT.anchorMax        = new Vector2(0.5f, 0f);
            toastRT.pivot            = new Vector2(0.5f, 0f);
            toastRT.sizeDelta        = new Vector2(450f, 60f);
            toastRT.anchoredPosition = new Vector2(0f, 90f);

            var bg = toastGO.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.50f, 0.58f, 0.95f);
            bg.raycastTarget = true;

            var hlg = toastGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.padding = new RectOffset(12, 3, 6, 6); hlg.spacing = 6f;

            var msgGO = new GameObject("Msg", typeof(RectTransform));
            msgGO.transform.SetParent(toastGO.transform, false);
            msgGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var msgTMP = msgGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) msgTMP.font = FontAsset;
            msgTMP.text = message; msgTMP.fontSize = 18f;
            msgTMP.color = Color.white; msgTMP.alignment = TextAlignmentOptions.Left;
            msgTMP.enableWordWrapping = false; msgTMP.overflowMode = TextOverflowModes.Ellipsis;
            msgTMP.raycastTarget = false;

            var closeGO = new GameObject("X", typeof(RectTransform));
            closeGO.transform.SetParent(toastGO.transform, false);
            var closeLE = closeGO.AddComponent<LayoutElement>(); closeLE.preferredWidth = 36f;
            var closeImg = closeGO.AddComponent<Image>(); closeImg.color = new Color(1f, 1f, 1f, 0.08f);
            var closeBtn = closeGO.AddComponent<Button>(); closeBtn.targetGraphic = closeImg;
            var cc = closeBtn.colors; cc.highlightedColor = new Color(1f, 1f, 1f, 0.25f); closeBtn.colors = cc;
            var capToast = toastGO;
            closeBtn.onClick.AddListener(() => Destroy(capToast));
            var closeLbl = new GameObject("L", typeof(RectTransform));
            closeLbl.transform.SetParent(closeGO.transform, false);
            var clRT = closeLbl.GetComponent<RectTransform>();
            clRT.anchorMin = Vector2.zero; clRT.anchorMax = Vector2.one; clRT.sizeDelta = Vector2.zero;
            var closeTMP = closeLbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) closeTMP.font = FontAsset;
            closeTMP.text = "×"; closeTMP.fontSize = 22f;
            closeTMP.alignment = TextAlignmentOptions.Center;
            closeTMP.color = Color.white; closeTMP.raycastTarget = false; closeTMP.enableWordWrapping = false;
        }

        // ── Checkbox helpers ──────────────────────────────────────────────────────

        private static readonly Color CbUncheckedBg = Color.clear;
        private static readonly Color CbCheckedBg   = new Color(0.05f, 0.55f, 0.62f, 0.85f);
        private static readonly Color CbUncheckedFg = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color CbCheckedFg   = Color.white;

        private Button MakeCheckboxButton(Transform parent, bool forRow2 = false)
        {
            var go  = new GameObject("CB", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 18f;
            var img = go.AddComponent<Image>(); img.color = CbUncheckedBg;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cols = btn.colors; cols.highlightedColor = new Color(0.15f, 0.28f, 0.32f, 0.9f); btn.colors = cols;
            var lbl = new GameObject("L", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.sizeDelta = Vector2.zero;
            var tmp = lbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text = "□"; tmp.fontSize = forRow2 ? 12f : 13f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.clear; tmp.enableWordWrapping = false; tmp.raycastTarget = false;
            img.color = Color.clear;
            btn.interactable = false; // transparent + non-interactable until window data available
            return btn;
        }

        private void ToggleAlarmForRow(string destId, bool isRow2, bool isFastest)
        {
            string dest = ephem?.GetDisplayName(destId) ?? destId;
            if (!cache.TryGetValue(destId, out var entry))
            { Plugin.Log.LogInfo($"[LW] ToggleAlarm '{dest}': no cache entry"); return; }
            LaunchWindow? window;
            if (!isFastest) window = isRow2 ? entry.opt2 : entry.opt1;
            else            window = isRow2 ? entry.fst2  : entry.fst1;
            if (window == null)
            { Plugin.Log.LogInfo($"[LW] ToggleAlarm '{dest}': window slot is null (row2={isRow2} fast={isFastest})"); return; }
            if (ephem == null)
            { Plugin.Log.LogInfo($"[LW] ToggleAlarm '{dest}': ephem null"); return; }
            if (!TryEpochToDate(window.Value.DepartureEpoch, out var depDate))
            { Plugin.Log.LogInfo($"[LW] ToggleAlarm '{dest}': TryEpochToDate failed"); return; }

            var key = new AlarmKey { OriginId = OriginId, DestId = destId, Year = depDate.Year, Month = depDate.Month, IsFastest = isFastest };
            if (!_alarms.Remove(key)) _alarms.Add(key);
            _sidecarDirty = true;
            bool armed = _alarms.Contains(key);
            Plugin.Log.LogInfo($"[LW] ToggleAlarm '{dest}': armed={armed} row2={isRow2} fast={isFastest}");
            int idx = (!isFastest ? 0 : 2) + (isRow2 ? 1 : 0);
            UpdateCheckboxVisual(destId, idx, armed);
        }

        private bool TryEpochToDate(double epoch, out DateTime date)
        {
            date = default;
            var tc = MonoBehaviourSingleton<TimeController>.Instance;
            var ge = GravityEngine.Instance();
            if (tc == null || ge == null) return false;
            double spp = GravityScaler.GetGameSecondPerPhysicsSecond();
            if (spp <= 0) spp = 1;
            date = tc.CurrentTime + TimeSpan.FromSeconds((epoch - ge.GetPhysicalTimeDouble()) * spp);
            return true;
        }

        private void UpdateCheckboxVisual(string destId, int idx, bool armed)
        {
            if (!rowCheckboxBtns.TryGetValue(destId, out var btns)) return;
            if (idx < 0 || idx >= btns.Length) return;
            var btn = btns[idx];
            if (btn == null) return;
            btn.interactable = true;
            var img = btn.GetComponent<Image>();
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (img != null) img.color = armed ? CbCheckedBg : CbUncheckedBg;
            if (tmp != null) { tmp.text = armed ? "✓" : "□"; tmp.color = armed ? CbCheckedFg : CbUncheckedFg; }
        }

        private void UpdateAllCheckboxVisuals()
        {
            var ge = GravityEngine.Instance();
            if (ge == null) return;
            foreach (var destId in DestIds)
            {
                if (!rowCheckboxBtns.TryGetValue(destId, out var btns)) continue;
                cache.TryGetValue(destId, out var entry);
                // idx: 0=opt1, 1=opt2, 2=fst1, 3=fst2
                var windows    = new LaunchWindow?[] { entry.opt1, entry.opt2, entry.fst1, entry.fst2 };
                var isFastests = new bool[]          { false,      false,      true,       true       };
                for (int i = 0; i < 4; i++)
                {
                    if (i >= btns.Length) break;
                    var btn = btns[i];
                    if (btn == null) continue;
                    var window = windows[i];
                    var img = btn.GetComponent<Image>();
                    var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                    if (window == null || !TryEpochToDate(window.Value.DepartureEpoch, out var depDate))
                    {
                        btn.interactable = false;
                        if (img != null) img.color = Color.clear;
                        if (tmp != null) tmp.color = Color.clear;
                        continue;
                    }
                    btn.interactable = true;
                    var key = new AlarmKey { OriginId = OriginId, DestId = destId, Year = depDate.Year, Month = depDate.Month, IsFastest = isFastests[i] };
                    bool armed = _alarms.Contains(key);
                    if (img != null) img.color = armed ? CbCheckedBg : CbUncheckedBg;
                    if (tmp != null) { tmp.text = armed ? "✓" : "□"; tmp.color = armed ? CbCheckedFg : CbUncheckedFg; }
                }
            }
        }

        // ── Sidecar persistence ───────────────────────────────────────────────────

        private void TryApplySidecarData()
        {
            if (_sidecarApplied || ephem == null) return;
            if (!_sidecarLoaded)
            {
                // ExtractFromSaveGameData may have been called before Panel was set up.
                // Probe LoadSaveManager directly now that ephem is ready.
                try
                {
                    const BindingFlags bf2 = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var lsm = UnityEngine.Object.FindObjectOfType(typeof(Manager.LoadSaveManager));
                    if (lsm != null)
                    {
                        var saveName = lsm.GetType().GetProperty("LastSaveName", bf2)?.GetValue(lsm) as string;
                        if (!string.IsNullOrEmpty(saveName))
                            LoadFromSidecar(saveName);
                        else
                            return; // save name not set yet — wait for next frame
                    }
                    else return;
                }
                catch { return; }
                if (!_sidecarLoaded) _sidecarLoaded = true; // prevent infinite loop on error
            }
            _sidecarApplied = true;
            ApplySidecarData();
        }

        private void ApplySidecarData()
        {
            if (_sidecarData == null)
            {
                // First load for this save — default to Earth → Mars
                int earthIdx = originIds.FindIndex(id =>
                    string.Equals(ephem.GetDisplayName(id), "Earth", StringComparison.OrdinalIgnoreCase));
                if (earthIdx >= 0) { originIndex = earthIdx; UpdateOriginLabel(); }
                var marsId = ephem.AllBodyIds.FirstOrDefault(id =>
                    string.Equals(ephem.GetDisplayName(id), "Mars", StringComparison.OrdinalIgnoreCase));
                if (marsId != null && !DestIds.Contains(marsId)) DestIds.Add(marsId);
                needsRefresh = true;
                return;
            }
            if (!string.IsNullOrEmpty(_sidecarData.originId))
            {
                int idx = originIds.IndexOf(_sidecarData.originId);
                if (idx >= 0) { originIndex = idx; UpdateOriginLabel(); }
            }
            if (!string.IsNullOrEmpty(_sidecarData.selectedCraftName) && !_craftManuallySelected)
            {
                var crafts = GetAllCraftDv();
                foreach (var c in crafts)
                {
                    if (c.name == _sidecarData.selectedCraftName)
                    {
                        _craftManuallySelected = true;
                        SetCraft(c.name, c.maxDvKmS, c.maxCargo, c.exhaustV, c.dryMass, c.fuel, c.solarRangeAU);
                        break;
                    }
                }
            }
            var allIds = new HashSet<string>(ephem.AllBodyIds);
            _destsByOrigin.Clear();
            if (_sidecarData.originDests?.Count > 0)
            {
                foreach (var od in _sidecarData.originDests)
                    if (!string.IsNullOrEmpty(od.originId))
                        _destsByOrigin[od.originId] = (od.destIds ?? new List<string>())
                            .Where(id => allIds.Contains(id)).ToList();
            }
            else if (_sidecarData.destIds?.Count > 0)
            {
                // v1 compat: treat saved DestIds as belonging to the saved origin
                var v1Origin = _sidecarData.originId;
                if (!string.IsNullOrEmpty(v1Origin))
                    _destsByOrigin[v1Origin] = _sidecarData.destIds
                        .Where(id => allIds.Contains(id)).ToList();
            }
            _firedAlarms.Clear();
            _alarms.Clear();
            foreach (var a in _sidecarData.alarms ?? new List<LWAlarmSave>())
                _alarms.Add(new AlarmKey { OriginId = a.originId, DestId = a.destId, Year = a.year, Month = a.month, IsFastest = a.isFastest });

            var ge2 = GravityEngine.Instance();
            double physNow2 = ge2 != null ? ge2.GetPhysicalTimeDouble() : 0;
            var (promoted, needsOpt2, needsFst) = LWCacheHelper.PromoteWindowCache(
                _sidecarData.windowCache, allIds, physNow2);
            cache.Clear();
            _needsOpt2Recalc.Clear();
            _needsFstRecalc.Clear();
            foreach (var kv in promoted) cache[kv.Key] = kv.Value;
            foreach (var id in needsOpt2) _needsOpt2Recalc.Add(id);
            foreach (var id in needsFst)  _needsFstRecalc.Add(id);

            _cacheByOrigin.Clear();
            _needsOpt2ByOrigin.Clear();
            _needsFstByOrigin.Clear();
            foreach (var oc in _sidecarData.originCaches ?? new List<LWOriginCacheSave>())
            {
                if (string.IsNullOrEmpty(oc.originId)) continue;
                var (prom, o2set, fsset) = LWCacheHelper.PromoteWindowCache(oc.cache, allIds, physNow2);
                if (prom.Count > 0)
                {
                    _cacheByOrigin[oc.originId] = prom;
                    if (o2set.Count > 0) _needsOpt2ByOrigin[oc.originId] = o2set;
                    if (fsset.Count > 0) _needsFstByOrigin[oc.originId]  = fsset;
                }
            }

            needsRefresh = true;
        }

        internal void LoadFromSidecar(string saveName)
        {
            _sidecarLoaded  = true;
            _sidecarApplied = false;
            _sidecarData    = null;
            try
            {
                string path = SidecarPath(saveName);
                if (File.Exists(path))
                {
                    _sidecarData = JsonUtility.FromJson<LWSaveData>(File.ReadAllText(path));
                    Plugin.Log.LogInfo($"[LW] Loaded sidecar: {path}");
                }
                else Plugin.Log.LogInfo($"[LW] No sidecar for '{saveName}', using defaults");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] LoadFromSidecar: {ex.Message}"); }
        }

        private void MaybeAutoSaveSidecar()
        {
            if (!_sidecarDirty || !_sidecarApplied) return;
            if (Time.realtimeSinceStartup - _lastAutoSaveTime < 2f) return;
            _sidecarDirty = false;
            _lastAutoSaveTime = Time.realtimeSinceStartup;
            try
            {
                var lsm = UnityEngine.Object.FindObjectOfType<Manager.LoadSaveManager>();
                if (lsm != null && !string.IsNullOrEmpty(lsm.LastSaveName))
                    SaveToSidecar(lsm.LastSaveName);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] auto-save sidecar: {ex.Message}"); }
        }

        internal void SaveToSidecar(string saveName)
        {
            try
            {
                var originDestsList = new List<LWOriginDestsSave>();
                foreach (var kv in _destsByOrigin)
                    originDestsList.Add(new LWOriginDestsSave { originId = kv.Key, destIds = new List<string>(kv.Value) });

                var alarmsList = new List<LWAlarmSave>();
                foreach (var a in _alarms)
                    alarmsList.Add(new LWAlarmSave { originId = a.OriginId, destId = a.DestId, year = a.Year, month = a.Month, isFastest = a.IsFastest });

                var cacheList = new List<LWDestCacheSave>();
                foreach (var kv in cache)
                    cacheList.Add(new LWDestCacheSave
                    {
                        destId = kv.Key,
                        opt1   = kv.Value.opt1.HasValue ? LWSaveConvert.ToSave(kv.Value.opt1.Value) : null,
                        fst1   = kv.Value.fst1.HasValue ? LWSaveConvert.ToSave(kv.Value.fst1.Value) : null,
                        opt2   = kv.Value.opt2.HasValue ? LWSaveConvert.ToSave(kv.Value.opt2.Value) : null,
                        fst2   = kv.Value.fst2.HasValue ? LWSaveConvert.ToSave(kv.Value.fst2.Value) : null,
                    });

                // Persist all other origins' caches so switching back doesn't force a full recalc.
                var originCachesList = new List<LWOriginCacheSave>();
                foreach (var oc in _cacheByOrigin)
                {
                    var ocList = new List<LWDestCacheSave>();
                    foreach (var dc in oc.Value)
                    {
                        // _cacheByOrigin uses unnamed tuple elements — access positionally.
                        var (o1, f1, o2, f2) = dc.Value;
                        ocList.Add(new LWDestCacheSave
                        {
                            destId = dc.Key,
                            opt1   = o1.HasValue ? LWSaveConvert.ToSave(o1.Value) : null,
                            fst1   = f1.HasValue ? LWSaveConvert.ToSave(f1.Value) : null,
                            opt2   = o2.HasValue ? LWSaveConvert.ToSave(o2.Value) : null,
                            fst2   = f2.HasValue ? LWSaveConvert.ToSave(f2.Value) : null,
                        });
                    }
                    originCachesList.Add(new LWOriginCacheSave { originId = oc.Key, cache = ocList });
                }

                var data = new LWSaveData
                {
                    originId          = OriginId ?? "",
                    selectedCraftName = _selectedCraftName ?? "",
                    destIds           = new List<string>(DestIds),
                    originDests       = originDestsList,
                    alarms            = alarmsList,
                    windowCache       = cacheList,
                    originCaches      = originCachesList,
                };
                string path = SidecarPath(saveName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data));
                Plugin.Log.LogInfo($"[LW] Saved sidecar: {path}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] SaveToSidecar: {ex.Message}"); }
        }

        private static string SidecarPath(string saveName)
        {
            string name = Path.GetFileName(saveName ?? "");
            foreach (var ext in new[] { ".json.gz", ".info.gz", ".json", ".gz" })
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - ext.Length);
            // Strip in-game date and slot suffix so the sidecar is stable across saves:
            // "NASA REALISTIC SOLAR SYSTEM 2035-12-11_3" → "NASA REALISTIC SOLAR SYSTEM"
            name = System.Text.RegularExpressions.Regex.Replace(
                name, @"\s+\d{4}-\d{2}-\d{2}(_\d+)?$", "");
            if (string.IsNullOrWhiteSpace(name)) name = "default";
            return Path.Combine(
                Path.GetDirectoryName(Plugin.Location ?? "") ?? "",
                "saves", name + ".lw.json");
        }

    }
}
