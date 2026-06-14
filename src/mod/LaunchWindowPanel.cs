#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Data;
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
        internal TextMeshProUGUI StatusTMP;
        internal TextMeshProUGUI OptDepHdrTMP;
        internal TextMeshProUGUI FstDepHdrTMP;
        internal Button          OriginBtn;
        internal Button          CraftBtn;
        internal Transform       ContentParent;
        internal TMP_FontAsset   FontAsset;
        internal RectTransform   PanelRT;
        internal GameObject      OriginDropGO;
        internal GameObject      CraftDropGO;
        internal GameObject      SearchDropGO;
        internal TMP_InputField  SearchInput;
        internal GameObject      CalcOverlayGO;
        internal TMP_InputField  OriginFilterInput;

        // Data
        private GameBodyEphemeris ephem;
        private WindowFinder      finder;
        private double            dvToKmS;
        private List<string>      originIds = new List<string>();
        private int               originIndex;
        private List<string>      destIds   = new List<string>();

        // Craft budget
        private double _craftDvCapGameUnits  = double.MaxValue;
        private bool   _craftDropOpen;
        private bool   _craftManuallySelected;
        private bool   _craftLogged;
        private string _selectedCraftName;
        private double _craftMaxDvKmS  = double.MaxValue; // zero-cargo theoretical max dv
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
        private readonly Dictionary<string, TextMeshProUGUI[]> rowTMPs
            = new Dictionary<string, TextMeshProUGUI[]>();

        private float lastEphemBuildTime = -1000f;
        private float lastRefreshTime    = -1000f;
        private bool  needsRefresh;
        private bool  refreshing;
        private bool  originDropOpen;

        private volatile bool _calcDone;
        private Dictionary<string, (LaunchWindow?, LaunchWindow?, LaunchWindow?, LaunchWindow?)> _pendingCache;

        private string _pendingSearch;
        private string _lastSearch;

        // Alarm state
        private readonly HashSet<AlarmKey> _alarms     = new HashSet<AlarmKey>();
        private readonly HashSet<AlarmKey> _firedAlarms = new HashSet<AlarmKey>();
        private readonly HashSet<string>  _needsOpt2Recalc = new HashSet<string>();
        internal IGameClock _clock = new GameClock();

        // Per-row checkbox buttons: [0]=row1 (opt1), [1]=row2 (opt2)
        private readonly Dictionary<string, Button[]> rowCheckboxBtns = new Dictionary<string, Button[]>();

        // Sidecar load/apply state
        private bool      _sidecarLoaded;
        private bool      _sidecarApplied;
        private LWSaveData _sidecarData;

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
                Destroy(content.GetChild(i).gameObject);

            var crafts = GetAllCraftDv();
            foreach (var (name, maxDvKmS, maxCargo, exhaustV, dryMass, fuel) in crafts.OrderByDescending(c => c.maxDvKmS))
            {
                var capName    = name;
                var capMaxDv   = maxDvKmS;
                var capCargo   = maxCargo;
                var capExhV    = exhaustV;
                var capDry     = dryMass;
                var capFuel    = fuel;
                bool isSel     = capName == _selectedCraftName;
                AddDropdownItem(content, $"{capName}  ({capMaxDv:F0} km/s)", isSel, () => {
                    _craftManuallySelected = true;
                    SetCraft(capName, capMaxDv, capCargo, capExhV, capDry, capFuel);
                    HideCraftDropdown();
                    ClearAllRowData();
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

        private void PopulateOriginDropdown(string filter = "")
        {
            var content = GetDropContent(OriginDropGO);
            if (content == null || ephem == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            var items = originIds
                .Select(id => (id, label: ephem.GetDisplayName(id)))
                .Where(x => string.IsNullOrEmpty(filter) ||
                            x.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => x.label, StringComparer.OrdinalIgnoreCase);

            foreach (var (id, label) in items)
            {
                var captured = id;
                bool isCurrent = id == OriginId;
                AddDropdownItem(content, label, isCurrent, () => {
                    int idx = originIds.IndexOf(captured);
                    if (idx >= 0) originIndex = idx;
                    UpdateOriginLabel();
                    HideOriginDropdown();
                    ClearAllRowData();
                    if (destIds.Count == 0 && ephem != null)
                    {
                        var earthId = ephem.AllBodyIds.FirstOrDefault(id =>
                            string.Equals(ephem.GetDisplayName(id), "Earth", StringComparison.OrdinalIgnoreCase));
                        if (earthId != null && earthId != OriginId) destIds.Add(earthId);
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
                Destroy(content.GetChild(i).gameObject);

            var matches = ephem.AllBodyIds
                .Where(id => ephem.GetDisplayName(id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(id => ephem.GetDisplayName(id))
                .Take(10)
                .ToList();

            if (matches.Count == 0) { HideSearchDropdown(); return; }

            if (SearchInput != null)
                PositionDropdownBelow(SearchDropGO, SearchInput.GetComponent<RectTransform>(), below: true);

            foreach (var id in matches)
            {
                var captured = id;
                string label = ephem.GetDisplayName(id);
                bool already = destIds.Contains(id);
                AddDropdownItem(content, already ? $"{label} ✓" : label, already, () => {
                    if (!destIds.Contains(captured))
                    {
                        destIds.Add(captured);
                        needsRefresh = true;
                    }
                    // SetTextWithoutNotify avoids firing onValueChanged (which would lose focus).
                    if (SearchInput != null) { SearchInput.SetTextWithoutNotify(""); SearchInput.ActivateInputField(); }
                    _pendingSearch = "";
                    _lastSearch    = "";
                    HideSearchDropdown();
                });
            }

            SearchDropGO.SetActive(true);
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
            le.preferredHeight = 20f;
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
            lblRT.sizeDelta = new Vector2(-6f, 0f);
            var tmp = lbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text               = label;
            tmp.fontSize           = 10f;
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

            destIds.Sort((a, b) => {
                double ka = SortKey(a), kb = SortKey(b);
                int c = ka.CompareTo(kb);
                return _sortDir == SortDir.Asc ? c : -c;
            });

            if (ContentParent != null)
                for (int i = 0; i < destIds.Count; i++)
                {
                    var t = ContentParent.Find("Row_" + destIds[i]);
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
            var best = crafts.OrderByDescending(c => c.maxDvKmS).First();
            SetCraft(best.name, best.maxDvKmS, best.maxCargo, best.exhaustV, best.dryMass, best.fuel);
        }

        private void SetCraft(string name, double maxDvKmS, double maxCargo, double exhaustV, double dryMass, double fuel)
        {
            _selectedCraftName   = name;
            _craftMaxDvKmS       = maxDvKmS;
            _craftMaxCargo       = maxCargo;
            _craftExhaustV       = exhaustV;
            _craftDryMass        = dryMass;
            _craftFuel           = fuel;
            _craftDvCapGameUnits = dvToKmS > 0 ? maxDvKmS / dvToKmS : double.MaxValue;
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

        private (string name, double maxDvKmS, double maxCargo, double exhaustV, double dryMass, double fuel)[] GetAllCraftDv()
        {
            try
            {
                var (allInfos, player) = GetOmAndPlayer();
                if (allInfos == null) return Array.Empty<(string, double, double, double, double, double)>();

                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var seen   = new HashSet<int>();
                var result = new List<(string, double, double, double, double, double)>();

                // Match by name+param-count to avoid exact-type mismatch with Company subclasses.
                System.Reflection.MethodInfo getOidM = null;
                int infoCount = 0;
                foreach (var objectInfo in allInfos)
                {
                    if (getOidM == null)
                        getOidM = objectInfo.GetType().GetMethods(bf)
                            .FirstOrDefault(m => m.Name == "GetObjectInfoData" && m.GetParameters().Length == 1);

                    var oid = getOidM?.Invoke(objectInfo, new[] { player });
                    if (oid == null) { infoCount++; continue; }
                    infoCount++;
                    var listSC = oid.GetType().GetProperty("ListSpaceCrafts", bf)?.GetValue(oid) as IEnumerable;
                    if (listSC == null) continue;

                    foreach (var sc in listSC)
                    {
                        var scType = sc.GetType().GetField("spacecraftType", bf)?.GetValue(sc);
                        if (scType == null) continue;
                        int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(scType);
                        if (!seen.Add(hash)) continue;

                        var scTypeType = scType.GetType();
                        // Use company-bonus-adjusted values — same path as PlanMissionWindow.Update.
                        var mExhaustV = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetExhaustV"      && m.GetParameters().Length == 1);
                        var mMass     = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetMass"          && m.GetParameters().Length == 1);
                        var mFuel     = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetFuelCapacity"  && m.GetParameters().Length == 1);
                        var mCargo    = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetCargoCapacity" && m.GetParameters().Length == 1);
                        double exhaustV  = mExhaustV != null ? Convert.ToDouble(mExhaustV.Invoke(scType, new[] { player })) : Convert.ToDouble(scTypeType.GetProperty("ExhaustV",     bf)?.GetValue(scType));
                        double emptyMass = mMass     != null ? Convert.ToDouble(mMass    .Invoke(scType, new[] { player })) : Convert.ToDouble(scTypeType.GetProperty("Mass",         bf)?.GetValue(scType));
                        double fuel      = mFuel     != null ? Convert.ToDouble(mFuel    .Invoke(scType, new[] { player })) : Convert.ToDouble(scTypeType.GetProperty("FuelCapacity", bf)?.GetValue(scType));
                        double maxCargo  = mCargo    != null ? Convert.ToDouble(mCargo   .Invoke(scType, new[] { player })) : 0.0;
                        // exhaustV is km/s; zero-cargo max dv is the physical ceiling.
                        double maxDvKmS = (emptyMass > 0 && fuel > 0)
                            ? exhaustV * Math.Log((emptyMass + fuel) / emptyMass)
                            : exhaustV;
                        string scName;
                        try   { scName = scTypeType.GetProperty("Name", bf)?.GetValue(scType) as string ?? "?"; }
                        catch { scName = scTypeType.GetProperty("ID",   bf)?.GetValue(scType) as string ?? "?"; }
                        if (!_craftLogged) Plugin.Log.LogInfo($"[LW] craft '{scName}': exhaustV={exhaustV:F3} mass={emptyMass:F1} fuel={fuel:F1} maxCargo={maxCargo:F1} maxDv={maxDvKmS:F1}km/s");
                        result.Add((scName, maxDvKmS, maxCargo, exhaustV, emptyMass, fuel));
                    }
                }

                if (result.Count == 0)
                    Plugin.Log.LogWarning($"[LW] GetAllCraftDv: scanned {infoCount} objectInfos, found 0 craft types");
                else
                    _craftLogged = true;
                return result.ToArray();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[LW] GetAllCraftDv: {ex.Message}");
                return Array.Empty<(string, double, double, double, double, double)>();
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
                if (StatusTMP != null) StatusTMP.text = "No bases found";
                return;
            }

            int added = 0;
            foreach (var bodyId in ephem.AllBodyIds)
            {
                if (bodyId == OriginId || destIds.Contains(bodyId)) continue;
                if (presenceIds.Contains(bodyId))
                {
                    destIds.Add(bodyId);
                    added++;
                }
            }

            if (added > 0) needsRefresh = true;
            if (StatusTMP != null)
                StatusTMP.text = added > 0 ? $"Added {added} base(s)" : "No new bases to add";
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
                    if (getOidM == null)
                        getOidM = objectInfo.GetType().GetMethods(bf)
                            .FirstOrDefault(m => m.Name == "GetObjectInfoData" && m.GetParameters().Length == 1);
                    var oid = getOidM?.Invoke(objectInfo, new[] { player });
                    if (oid == null) continue;
                    var facList = oid.GetType().GetProperty("ListFacility", bf)?.GetValue(oid) as ICollection;
                    if (facList == null || facList.Count == 0) continue;
                    // ListFacility is non-empty for any tracked body; only count as "base" if
                    // at least one facility has actually been built (Quantity > 0).
                    bool hasBuilt = false;
                    foreach (var fac in (IEnumerable)facList)
                    {
                        var qty = fac.GetType().GetProperty("Quantity", bf)?.GetValue(fac)
                               ?? (object)fac.GetType().GetField("quantity", bf)?.GetValue(fac);
                        if (qty != null && Convert.ToInt64(qty) > 0) { hasBuilt = true; break; }
                    }
                    if (!hasBuilt) continue;
                    // ObjectInfo.nBody (line 97 of ObjectInfo.cs) is the authoritative link to the NBody
                    // whose GetInstanceID().ToString() is the ephemeris key.
                    var nb = objectInfo.GetType().GetField("nBody", bf)?.GetValue(objectInfo) as NBody;
                    if (nb == null) continue;
                    // Skip spacecraft (probes) — only celestial bodies count as bases.
                    var nbInfo = nb.GetObjectInfo();
                    if (nbInfo != null && nbInfo.objectTypes == EObjectTypes.Spacecraft) continue;
                    string id = nb.GetInstanceID().ToString();
                    result.Add(id);
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

            var ge = GravityEngine.Instance();
            if (ge == null) { refreshing = false; if (CalcOverlayGO != null) CalcOverlayGO.SetActive(false); return; }
            double physNow    = ge.GetPhysicalTimeDouble();
            double dvCap      = _craftDvCapGameUnits;
            var destSnap      = new System.Collections.Generic.List<string>(destIds);
            var originId      = OriginId;

            // Full recalc (cache absent or opt1 stale) vs. partial (promoted: opt1 valid, opt2 missing).
            var needsOpt2Snap = new HashSet<string>(_needsOpt2Recalc);
            var toCalcFull    = new List<string>();
            var toCalcPartial = new List<(string dId, LaunchWindow opt1, LaunchWindow? fst1)>();
            foreach (var dId in destSnap)
            {
                if (dId == originId) continue;
                if (!HasValidCache(dId, physNow))
                    toCalcFull.Add(dId);
                else if (needsOpt2Snap.Contains(dId) && cache.TryGetValue(dId, out var ce) && ce.opt1.HasValue)
                    toCalcPartial.Add((dId, ce.opt1.Value, ce.fst1));
            }

            if (toCalcFull.Count == 0 && toCalcPartial.Count == 0)
            {
                // Everything is cached — rebuild UI immediately without a background thread.
                refreshing = false;
                RebuildRows();
                ApplySort();
                UpdateAllCheckboxVisuals();
                if (StatusTMP != null) StatusTMP.text = $"Updated: {FormatNow()} (cached)";
                if (CalcOverlayGO != null) CalcOverlayGO.SetActive(false);
                return;
            }

            if (StatusTMP != null) StatusTMP.text = "Calculating…";
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

                // Full recalcs: two FindWindows calls (opt1 + opt2).
                Parallel.ForEach<string, WindowFinder>(
                    toCalcFull,
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
                    () => new WindowFinder(new GameLambertSolver(), ephemSnap, dvToKmSSnap),
                    (item, _, localFinder) =>
                    {
                        try
                        {
                            double syn = localFinder.GetSynodic(originId, item.dId);
                            // Back off 30 days so we don't clip the leading edge of the window if
                            // opt1 landed near the tail of the previous window.
                            double thirtyDays = ephemSnap.GetPeriod(originId) / 12.0;
                            double startTime  = syn > 0
                                ? item.opt1.DepartureEpoch + syn - thirtyDays
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
                RebuildRows();
                ApplySort();
                UpdateAllCheckboxVisuals();
                lastRefreshTime = Time.realtimeSinceStartup;
                if (StatusTMP != null) StatusTMP.text = $"Updated: {FormatNow()}";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LW] ApplyPendingResults: {ex.Message}");
                if (StatusTMP != null) StatusTMP.text = "Error — see log";
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

            foreach (var dId in destIds)
            {
                if (dId == OriginId) continue;
                if (!rowTMPs.ContainsKey(dId))
                    CreateRow(dId);
            }

            foreach (var dId in rowTMPs.Keys.Where(k => !destIds.Contains(k) || k == OriginId).ToList())
            {
                rowTMPs.Remove(dId);
                rowCheckboxBtns.Remove(dId);
                var t = ContentParent.Find("Row_" + dId);
                if (t != null) Destroy(t.gameObject);
            }

            foreach (var dId in destIds)
            {
                if (dId == OriginId || !rowTMPs.ContainsKey(dId)) continue;
                var tmps = rowTMPs[dId];
                if (cache.TryGetValue(dId, out var entry))
                {
                    // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
                    // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
                    SetWindowCells(entry.opt1, tmps[0], tmps[1], tmps[2], ge);
                    SetWindowCells(entry.fst1, tmps[3], tmps[4], tmps[5], ge);
                    SetNextCells(entry.opt2, tmps[6], tmps[7], tmps[8], ge);
                    SetNextCells(entry.fst2, tmps[9], tmps[10], tmps[11], ge);
                }
            }
        }

        // Sub-column widths — must match injector sub-header widths exactly.
        // Optimal: dep=62 (cb 12 + text 50), dv=78, tvl=flex (within 255px group)
        // Fastest: dep=70, dv=88, tvl=flex (within 255px group)
        private const float CB_W       = 12f;
        private const float OPT_DEP_W  = 62f;
        private const float OPT_DV_W   = 78f;
        private const float FST_DEP_W  = 70f;
        private const float FST_DV_W   = 88f;

        private void CreateRow(string dId)
        {
            // Container is a VLG holding primary row (20px) + next-window row (15px).
            var container = new GameObject("Row_" + dId, typeof(RectTransform));
            container.transform.SetParent(ContentParent, false);
            container.AddComponent<LayoutElement>().preferredHeight = 35f;
            var containerVLG = container.AddComponent<VerticalLayoutGroup>();
            containerVLG.childControlHeight = true; containerVLG.childControlWidth = true;
            containerVLG.childForceExpandHeight = false; containerVLG.childForceExpandWidth = true;
            containerVLG.spacing = 0f;

            // ── Primary row ──────────────────────────────────────────────────────────
            var row1 = new GameObject("R1", typeof(RectTransform));
            row1.transform.SetParent(container.transform, false);
            row1.AddComponent<LayoutElement>().preferredHeight = 20f;

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

            // Name cell (105px): name label (flex) + × button (14px)
            var nameCell = new GameObject("NameCell", typeof(RectTransform));
            nameCell.transform.SetParent(inner.transform, false);
            nameCell.AddComponent<LayoutElement>().preferredWidth = 105f;
            var nHlg = nameCell.AddComponent<HorizontalLayoutGroup>();
            nHlg.childControlHeight = true; nHlg.childControlWidth = true;
            nHlg.childForceExpandHeight = true; nHlg.childForceExpandWidth = false;
            nHlg.spacing = 0f;

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(nameCell.transform, false);
            nameGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var nameTMP = nameGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) nameTMP.font = FontAsset;
            nameTMP.text = displayName; nameTMP.fontSize = 10f;
            nameTMP.alignment = TextAlignmentOptions.Left; nameTMP.color = Color.white;
            nameTMP.enableWordWrapping = false; nameTMP.overflowMode = TextOverflowModes.Ellipsis;
            nameTMP.raycastTarget = false;

            var xGO  = new GameObject("X", typeof(RectTransform));
            xGO.transform.SetParent(nameCell.transform, false);
            xGO.AddComponent<LayoutElement>().preferredWidth = 14f;
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
            xTMP.text = "×"; xTMP.fontSize = 9f; xTMP.alignment = TextAlignmentOptions.Center;
            xTMP.color = new Color(1f, 0.55f, 0.55f); xTMP.enableWordWrapping = false;
            xTMP.raycastTarget = false;

            // Optimal group: [cb+dep | dv | tvl]  |gap|  Fastest: [dep | dv | tvl]
            var oGroup = new GameObject("OptCol", typeof(RectTransform));
            oGroup.transform.SetParent(inner.transform, false);
            oGroup.AddComponent<LayoutElement>().preferredWidth = 255f;
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
            var oD  = MakeColLabel(oDCell.transform, "—", 10f, TextAlignmentOptions.Left, OPT_DEP_W - CB_W);
            var oDv  = MakeColLabel(oGroup.transform, "—", 10f, TextAlignmentOptions.Left, OPT_DV_W);
            var oTvl = MakeColLabel(oGroup.transform, "—", 10f, TextAlignmentOptions.Left, 0f, flex: true);
            var sep1 = new GameObject("Sep", typeof(RectTransform));
            sep1.transform.SetParent(inner.transform, false);
            sep1.AddComponent<LayoutElement>().preferredWidth = 8f;
            var fD  = MakeDataGroup(inner.transform, out var fDv, out var fTvl, isOptimal: false);

            // ── Next-window row (dimmed, 15px) ───────────────────────────────────────
            var row2 = new GameObject("R2", typeof(RectTransform));
            row2.transform.SetParent(container.transform, false);
            row2.AddComponent<LayoutElement>().preferredHeight = 15f;

            var inner2 = new GameObject("HLG2", typeof(RectTransform));
            inner2.transform.SetParent(row2.transform, false);
            var rt2 = inner2.GetComponent<RectTransform>();
            rt2.anchorMin = Vector2.zero; rt2.anchorMax = Vector2.one;
            rt2.offsetMin = Vector2.zero; rt2.offsetMax = Vector2.zero;
            var hlg2 = inner2.AddComponent<HorizontalLayoutGroup>();
            hlg2.childControlHeight = true; hlg2.childControlWidth = true;
            hlg2.childForceExpandHeight = true; hlg2.childForceExpandWidth = false;
            hlg2.spacing = 0f;

            // Blank name placeholder (105px)
            var ns = new GameObject("NS", typeof(RectTransform));
            ns.transform.SetParent(inner2.transform, false);
            ns.AddComponent<LayoutElement>().preferredWidth = 105f;

            Color dimC = new Color(0.50f, 0.50f, 0.50f);
            // Row-2 opt2 dep cell: checkbox + text within OPT_DEP_W
            var noD2Cell = new GameObject("DepC2", typeof(RectTransform));
            noD2Cell.transform.SetParent(inner2.transform, false);
            noD2Cell.AddComponent<LayoutElement>().preferredWidth = OPT_DEP_W;
            var noD2Hlg = noD2Cell.AddComponent<HorizontalLayoutGroup>();
            noD2Hlg.childControlHeight = true; noD2Hlg.childControlWidth = true;
            noD2Hlg.childForceExpandHeight = true; noD2Hlg.childForceExpandWidth = false;
            noD2Hlg.spacing = 0f;
            var cb2 = MakeCheckboxButton(noD2Cell.transform, forRow2: true);
            var noD  = MakeColLabel(noD2Cell.transform, "—", 9f, TextAlignmentOptions.Left, OPT_DEP_W - CB_W, dimC);
            var noDv = MakeColLabel(inner2.transform, "—", 9f, TextAlignmentOptions.Left, OPT_DV_W,  dimC);
            var noTvl = MakeColLabel(inner2.transform, "—", 9f, TextAlignmentOptions.Left, 0f, dimC, flex: true);
            var sep2 = new GameObject("Sep2", typeof(RectTransform));
            sep2.transform.SetParent(inner2.transform, false);
            sep2.AddComponent<LayoutElement>().preferredWidth = 8f;
            var nfD  = MakeColLabel(inner2.transform, "—", 9f, TextAlignmentOptions.Left, FST_DEP_W, dimC);
            var nfDv = MakeColLabel(inner2.transform, "—", 9f, TextAlignmentOptions.Left, FST_DV_W,  dimC);
            var nfTvl = MakeColLabel(inner2.transform, "—", 9f, TextAlignmentOptions.Left, 0f, dimC, flex: true);

            // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
            // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
            rowTMPs[dId] = new[] { oD, oDv, oTvl, fD, fDv, fTvl, noD, noDv, noTvl, nfD, nfDv, nfTvl };

            var capDest = dId;
            cb1.onClick.AddListener(() => ToggleAlarmForRow(capDest, isRow2: false));
            cb2.onClick.AddListener(() => ToggleAlarmForRow(capDest, isRow2: true));
            rowCheckboxBtns[dId] = new[] { cb1, cb2 };
        }

        private TextMeshProUGUI MakeDataGroup(Transform parent,
            out TextMeshProUGUI dvTMP, out TextMeshProUGUI tvlTMP,
            bool isOptimal = false)
        {
            var go = new GameObject("Col", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 255f;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.spacing = 0f;
            float depW = isOptimal ? OPT_DEP_W : FST_DEP_W;
            float dvW  = isOptimal ? OPT_DV_W  : FST_DV_W;
            var dep = MakeColLabel(go.transform, "—", 10f, TextAlignmentOptions.Left, depW);
            dvTMP   = MakeColLabel(go.transform, "—", 10f, TextAlignmentOptions.Left, dvW);
            tvlTMP  = MakeColLabel(go.transform, "—", 10f, TextAlignmentOptions.Left, 0f, flex: true);
            return dep;
        }

        private void RemoveDest(string dId)
        {
            destIds.Remove(dId);
            cache.Remove(dId);
            rowTMPs.Remove(dId);
            rowCheckboxBtns.Remove(dId);
            _alarms.RemoveWhere(k => k.DestId == dId);
            var t = ContentParent?.Find("Row_" + dId);
            if (t != null) Destroy(t.gameObject);
        }

        private TextMeshProUGUI MakeColLabel(Transform parent, string text, float size,
                                              TextAlignmentOptions align, float width,
                                              Color? color = null, bool flex = false)
        {
            var go  = new GameObject("C", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
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
            GravityEngine ge)
        {
            if (w == null || ge == null)
            {
                dep.text = dv.text = tvl.text = "—";
                dep.color = dv.color = tvl.color = DashColor;
                return;
            }
            dep.text = FormatEpoch(w.Value.DepartureEpoch);
            dv.text  = $"{w.Value.DeltaVKmS:F1}km/s";
            tvl.text = FormatTravel(w.Value.TravelTimeSeconds, ge);
            bool unreachable = w.Value.DeltaVKmS > _craftMaxDvKmS;
            Color c = unreachable ? RedMuted : WhiteColor;
            dep.color = dv.color = tvl.color = c;
        }

        private static readonly Color DimColor = new Color(0.50f, 0.50f, 0.50f);

        private void SetNextCells(LaunchWindow? w,
            TextMeshProUGUI dep, TextMeshProUGUI dv, TextMeshProUGUI tvl,
            GravityEngine ge)
        {
            dep.color = dv.color = tvl.color = DimColor;
            if (w == null || ge == null)
            {
                dep.text = dv.text = tvl.text = "—";
                return;
            }
            dep.text = FormatEpoch(w.Value.DepartureEpoch);
            dv.text  = $"{w.Value.DeltaVKmS:F1}km/s";
            tvl.text = FormatTravel(w.Value.TravelTimeSeconds, ge);
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

        private string FormatNow()
        {
            try
            {
                var tc = MonoBehaviourSingleton<TimeController>.Instance;
                if (tc == null) return "";
                var d = tc.CurrentTime;
                return $"{d.ToString("MMM")} '{d.Year % 100:D2}";
            }
            catch { return ""; }
        }

        // ── Alarm checking ────────────────────────────────────────────────────────

        private void CheckAlarms()
        {
            if (_alarms.Count == 0 || _clock == null) return;
            var toFire = LWCacheHelper.GetAlarmsToFire(_alarms, OriginId, _clock.CurrentTime);
            foreach (var key in toFire)
            {
                _alarms.Remove(key);
                _firedAlarms.Add(key);
                FireAlarm(key);
            }
        }

        private void FireAlarm(AlarmKey key)
        {
            try { _clock?.PauseGame(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[LW] PauseGame: {ex.Message}"); }

            string destName   = ephem?.GetDisplayName(key.DestId)   ?? key.DestId;
            string originName = ephem?.GetDisplayName(key.OriginId) ?? key.OriginId;
            SpawnToast($"Launch window: {originName} → {destName}");
            UpdateAllCheckboxVisuals();
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
            toastRT.sizeDelta        = new Vector2(300f, 40f);
            toastRT.anchoredPosition = new Vector2(0f, 60f);

            var bg = toastGO.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.50f, 0.58f, 0.95f);
            bg.raycastTarget = true;

            var hlg = toastGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = true;
            hlg.childForceExpandHeight = true; hlg.childForceExpandWidth = false;
            hlg.padding = new RectOffset(8, 2, 4, 4); hlg.spacing = 4f;

            var msgGO = new GameObject("Msg", typeof(RectTransform));
            msgGO.transform.SetParent(toastGO.transform, false);
            msgGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var msgTMP = msgGO.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) msgTMP.font = FontAsset;
            msgTMP.text = message; msgTMP.fontSize = 11f;
            msgTMP.color = Color.white; msgTMP.alignment = TextAlignmentOptions.Left;
            msgTMP.enableWordWrapping = false; msgTMP.overflowMode = TextOverflowModes.Ellipsis;
            msgTMP.raycastTarget = false;

            var closeGO = new GameObject("X", typeof(RectTransform));
            closeGO.transform.SetParent(toastGO.transform, false);
            var closeLE = closeGO.AddComponent<LayoutElement>(); closeLE.preferredWidth = 24f;
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
            closeTMP.text = "×"; closeTMP.fontSize = 14f;
            closeTMP.alignment = TextAlignmentOptions.Center;
            closeTMP.color = Color.white; closeTMP.raycastTarget = false; closeTMP.enableWordWrapping = false;
        }

        // ── Checkbox helpers ──────────────────────────────────────────────────────

        private static readonly Color CbUncheckedBg = new Color(0.15f, 0.17f, 0.20f, 0.6f);
        private static readonly Color CbCheckedBg   = new Color(0.05f, 0.55f, 0.62f, 0.85f);
        private static readonly Color CbUncheckedFg = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color CbCheckedFg   = Color.white;

        private Button MakeCheckboxButton(Transform parent, bool forRow2 = false)
        {
            var go  = new GameObject("CB", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 12f;
            var img = go.AddComponent<Image>(); img.color = CbUncheckedBg;
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            var cols = btn.colors; cols.highlightedColor = new Color(0.15f, 0.28f, 0.32f, 0.9f); btn.colors = cols;
            var lbl = new GameObject("L", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one; lblRT.sizeDelta = Vector2.zero;
            var tmp = lbl.AddComponent<TextMeshProUGUI>();
            if (FontAsset != null) tmp.font = FontAsset;
            tmp.text = "□"; tmp.fontSize = forRow2 ? 7f : 8f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = CbUncheckedFg; tmp.enableWordWrapping = false; tmp.raycastTarget = false;
            btn.gameObject.SetActive(false); // hidden until window data available
            return btn;
        }

        private void ToggleAlarmForRow(string destId, bool isRow2)
        {
            if (!cache.TryGetValue(destId, out var entry)) return;
            var window = isRow2 ? entry.opt2 : entry.opt1;
            if (window == null || ephem == null) return;
            if (!TryEpochToDate(window.Value.DepartureEpoch, out var depDate)) return;

            var key = new AlarmKey { OriginId = OriginId, DestId = destId, Year = depDate.Year, Month = depDate.Month };
            if (!_alarms.Remove(key)) _alarms.Add(key);
            UpdateCheckboxVisual(destId, isRow2, _alarms.Contains(key));
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

        private void UpdateCheckboxVisual(string destId, bool isRow2, bool armed)
        {
            if (!rowCheckboxBtns.TryGetValue(destId, out var btns)) return;
            var btn = btns[isRow2 ? 1 : 0];
            if (btn == null) return;
            btn.gameObject.SetActive(true);
            var img = btn.GetComponent<Image>();
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (img != null) img.color = armed ? CbCheckedBg : CbUncheckedBg;
            if (tmp != null) { tmp.text = armed ? "✓" : "□"; tmp.color = armed ? CbCheckedFg : CbUncheckedFg; }
        }

        private void UpdateAllCheckboxVisuals()
        {
            var ge = GravityEngine.Instance();
            if (ge == null) return;
            foreach (var destId in destIds)
            {
                if (!rowCheckboxBtns.TryGetValue(destId, out var btns)) continue;
                cache.TryGetValue(destId, out var entry);
                for (int i = 0; i < 2; i++)
                {
                    var btn = btns[i];
                    if (btn == null) continue;
                    var window = i == 0 ? entry.opt1 : entry.opt2;
                    if (window == null) { btn.gameObject.SetActive(false); continue; }
                    if (!TryEpochToDate(window.Value.DepartureEpoch, out var depDate)) { btn.gameObject.SetActive(false); continue; }
                    btn.gameObject.SetActive(true);
                    var key = new AlarmKey { OriginId = OriginId, DestId = destId, Year = depDate.Year, Month = depDate.Month };
                    bool armed = _alarms.Contains(key);
                    var img = btn.GetComponent<Image>();
                    var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
                    if (img != null) img.color = armed ? CbCheckedBg : CbUncheckedBg;
                    if (tmp != null) { tmp.text = armed ? "✓" : "□"; tmp.color = armed ? CbCheckedFg : CbUncheckedFg; }
                }
            }
        }

        // ── Sidecar persistence ───────────────────────────────────────────────────

        private void TryApplySidecarData()
        {
            if (_sidecarApplied || ephem == null) return;
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
                if (marsId != null && !destIds.Contains(marsId)) destIds.Add(marsId);
                needsRefresh = true;
                return;
            }
            if (!string.IsNullOrEmpty(_sidecarData.originId))
            {
                int idx = originIds.IndexOf(_sidecarData.originId);
                if (idx >= 0) { originIndex = idx; UpdateOriginLabel(); }
            }
            var allIds = new HashSet<string>(ephem.AllBodyIds);
            destIds = (_sidecarData.destIds ?? new List<string>()).Where(id => allIds.Contains(id)).ToList();
            _alarms.Clear();
            foreach (var a in _sidecarData.alarms ?? new List<LWAlarmSave>())
                _alarms.Add(new AlarmKey { OriginId = a.originId, DestId = a.destId, Year = a.year, Month = a.month });

            var ge2 = GravityEngine.Instance();
            double physNow2 = ge2 != null ? ge2.GetPhysicalTimeDouble() : 0;
            var (promoted, needsOpt2) = LWCacheHelper.PromoteWindowCache(
                _sidecarData.windowCache, allIds, physNow2);
            cache.Clear();
            _needsOpt2Recalc.Clear();
            foreach (var kv in promoted) cache[kv.Key] = kv.Value;
            foreach (var id in needsOpt2) _needsOpt2Recalc.Add(id);

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

        internal void SaveToSidecar(string saveName)
        {
            try
            {
                var data = new LWSaveData
                {
                    originId = OriginId ?? "",
                    destIds  = new List<string>(destIds),
                    alarms   = _alarms.Select(a => new LWAlarmSave
                        { originId = a.OriginId, destId = a.DestId, year = a.Year, month = a.Month }).ToList(),
                    windowCache = cache.Select(kv => new LWDestCacheSave
                    {
                        destId = kv.Key,
                        opt1 = kv.Value.opt1.HasValue ? LWSaveConvert.ToSave(kv.Value.opt1.Value) : null,
                        fst1 = kv.Value.fst1.HasValue ? LWSaveConvert.ToSave(kv.Value.fst1.Value) : null,
                        opt2 = kv.Value.opt2.HasValue ? LWSaveConvert.ToSave(kv.Value.opt2.Value) : null,
                        fst2 = kv.Value.fst2.HasValue ? LWSaveConvert.ToSave(kv.Value.fst2.Value) : null,
                    }).ToList()
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
            if (string.IsNullOrWhiteSpace(name)) name = "default";
            return Path.Combine(
                Path.GetDirectoryName(Plugin.Location ?? "") ?? "",
                "saves", name + ".lw.json");
        }

    }
}
