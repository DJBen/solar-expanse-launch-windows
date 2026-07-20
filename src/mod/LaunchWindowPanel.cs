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
        internal TextMeshProUGUI OptDvHdrTMP;
        internal TextMeshProUGUI FstDvHdrTMP;
        internal TextMeshProUGUI OptArrHdrTMP;
        internal TextMeshProUGUI FstArrHdrTMP;
        internal TextMeshProUGUI OptFuelHdrTMP;
        internal TextMeshProUGUI FstFuelHdrTMP;
        internal TextMeshProUGUI RetDepHdrTMP;
        internal TextMeshProUGUI RetDvHdrTMP;
        internal TextMeshProUGUI RetArrHdrTMP;
        internal TextMeshProUGUI RetFuelHdrTMP;
        internal TMP_FontAsset   TableFontAsset; // monospace-ish font for value cells; null → FontAsset
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
        internal GameObject      OptionsDropGO;
        internal Button          OptionsBtn;
        internal GameObject      OptDvHdrGO;      // sub-header Δv sort cell (Optimal side)
        internal GameObject      FstDvHdrGO;      // sub-header Δv sort cell (Fastest side)
        internal GameObject      RetDvHdrGO;      // sub-header Δv sort cell (Return side)
        internal LayoutElement   OptColHdrLE;     // OPTIMAL column-header label width
        internal LayoutElement   FstColHdrLE;     // FASTEST column-header label width
        internal LayoutElement   RetColHdrLE;     // RETURN column-header label width
        internal GameObject[]    FstHdrGOs;       // header/sub-header pieces of the Fastest section
        internal GameObject[]    RetHdrGOs;       // header/sub-header pieces of the Return section
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
        private double _craftThrustN   = 0.0;
        private bool   _craftConstAccel;
        private double _thrustMultiplier = 1.0; // Economic.DeltaVMultiplayerCheckingThrust

        // Sort state
        private enum SortCol { None, OptDep, FstDep, OptDv, FstDv, OptArr, FstArr, OptFuel, FstFuel,
                               RetDep, RetDv, RetArr, RetFuel }
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
        private bool  _optionsDropOpen;

        // Display options — persisted via BepInEx config (Options dropdown in the header).
        private bool ShowDv      => Plugin.CfgShowDv == null || Plugin.CfgShowDv.Value;
        private bool ShowNext    => Plugin.CfgShowNextWindow == null || Plugin.CfgShowNextWindow.Value;
        private bool ShowFastest => Plugin.CfgShowFastest != null && Plugin.CfgShowFastest.Value;
        private bool ShowReturn  => Plugin.CfgShowReturn == null || Plugin.CfgShowReturn.Value;
        private bool ShowUndiscovered => Plugin.CfgShowUndiscovered != null && Plugin.CfgShowUndiscovered.Value;
        private int  AlertDaysBefore => Plugin.CfgAlertDaysBefore != null
            ? Mathf.Clamp(Plugin.CfgAlertDaysBefore.Value, 0, 365) : 0;
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

        // Return-trip windows (dest → origin, departing after arrival), computed on demand
        // when the Return section is visible. Kept separate from `cache` so the sidecar
        // format is untouched; missing entries are backfilled by DoRefresh.
        private readonly Dictionary<string, (LaunchWindow? ret1, LaunchWindow? ret2)> retCache
            = new Dictionary<string, (LaunchWindow?, LaunchWindow?)>();
        private readonly Dictionary<string, Dictionary<string, (LaunchWindow?, LaunchWindow?)>> _retCacheByOrigin
            = new Dictionary<string, Dictionary<string, (LaunchWindow?, LaunchWindow?)>>();
        private volatile Dictionary<string, (LaunchWindow?, LaunchWindow?)> _pendingRetCache;

        // Negative-result caching — prevents redoing known-empty calcs on every refresh:
        // game-time of the last full calc that found no outbound window per dest, and
        // dests whose ret2 backfill already ran (a null ret2 is an answer, not a gap).
        private readonly Dictionary<string, double> _nullCalcAt = new Dictionary<string, double>();
        private readonly HashSet<string> _ret2Tried = new HashSet<string>();

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
            // Rebuild the ephemeris so bodies spawned since the last build (the game
            // creates asteroids at runtime, e.g. randomly generated NEOs) become
            // searchable and preset-addable. Fires on panel open and the Refresh button —
            // throttled to a full scene rescan at most every 30 real seconds (preset adds
            // still force a rebuild whenever they meet an unknown body).
            if (Time.realtimeSinceStartup - lastEphemBuildTime > 30f)
            {
                TryBuildEphem(force: true);
                RefreshOriginIdsPreservingSelection();
            }
            else
                TryBuildEphem();
            needsRefresh = true;
        }

        private void RefreshOriginIdsPreservingSelection()
        {
            if (ephem == null) return;
            var fresh = ephem.GetSortedOriginIds();
            if (fresh.Count == 0) return;
            var cur = OriginId;
            originIds = fresh;
            int idx = cur != null ? originIds.IndexOf(cur) : -1;
            originIndex = idx >= 0 ? idx : 0;
            UpdateOriginLabel();
        }

        internal void ClosePanel()
        {
            HideOriginDropdown();
            HideCraftDropdown();
            HideSearchDropdown();
            HidePresetsDropdown();
            HideOptionsDropdown();
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
            // The synthetic Solar Orbit origin may initialize after originIds was first
            // built (needs GravityEngine + Earth); pick it up here without losing the
            // current origin selection.
            if (ephem != null)
            {
                ephem.TryInitSolarOrbit();
                if (ephem.SolarOrbitReady && !originIds.Contains(GameBodyEphemeris.SolarOrbitId))
                {
                    var cur = OriginId;
                    originIds = ephem.GetSortedOriginIds();
                    int idx = cur != null ? originIds.IndexOf(cur) : -1;
                    originIndex = idx >= 0 ? idx : 0;
                }
            }
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
            foreach (var (name, maxDvKmS, maxCargo, exhaustV, dryMass, fuel, solarRangeAU, thrust, constAccel, icon) in crafts.OrderByDescending(c => c.maxDvKmS == double.MaxValue ? double.MaxValue : c.maxDvKmS))
            {
                var capName    = name;
                var capMaxDv   = maxDvKmS;
                var capCargo   = maxCargo;
                var capExhV    = exhaustV;
                var capDry     = dryMass;
                var capFuel    = fuel;
                var capSolar   = solarRangeAU;
                var capThrust  = thrust;
                var capCA      = constAccel;
                bool isSel     = capName == _selectedCraftName;
                string label   = capSolar > 0
                    ? $"{PrettyCraftName(capName)}  (solar, {capSolar:F1}AU)"
                    : $"{PrettyCraftName(capName)}  ({capMaxDv:F0} km/s)";
                AddDropdownItem(content, label, isSel, () => {
                    _craftManuallySelected = true;
                    _sidecarDirty = true;
                    SetCraft(capName, capMaxDv, capCargo, capExhV, capDry, capFuel, capSolar, capThrust, capCA);
                    HideCraftDropdown();
                    ClearAllRowData();
                    _cacheByOrigin.Clear();
                    _retCacheByOrigin.Clear();
                    _needsOpt2ByOrigin.Clear();
                    _needsFstByOrigin.Clear();
                    needsRefresh = true;
                }, icon);
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

            // Planets (Mercury … Neptune, incl. Mars) — right after My Bases.
            var marsId = ephem?.AllBodyIds.FirstOrDefault(id =>
                string.Equals(ephem.GetDisplayName(id), "Mars", StringComparison.OrdinalIgnoreCase));
            AddDropdownItem(content, "Planets", false, () => {
                HidePresetsDropdown();
                AddPlanetBodies();
            }, marsId != null ? GetBodyIcon(marsId) : null);

            // One item per game group (NEOs, Inner/Middle/Outer Belt, Trojans, Kuiper Belt, …),
            // sorted sunward-out by the group's average orbital distance.
            foreach (var g in GetGameGroups())
            {
                var captured = g;
                AddDropdownItem(content, GroupLabel(captured), false, () => {
                    HidePresetsDropdown();
                    AddGroupBodies(captured);
                }, captured.imagePlanetUI);
            }
        }

        internal void AddPlanetBodies()
        {
            TryBuildEphem();
            if (ephem == null) return;
            int added = 0;
            foreach (var id in ephem.AllBodyIds)
            {
                if (!ephem.IsPlanet(id)) continue;
                if (id == OriginId || DestIds.Contains(id) || IsBodyDestroyedId(id)) continue;
                DestIds.Add(id);
                _sidecarDirty = true;
                added++;
            }
            Plugin.Log.LogInfo($"[LW] AddPlanetBodies: added {added}");
            if (added > 0) needsRefresh = true;
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

        // ── Options dropdown ──────────────────────────────────────────────────────

        internal void ToggleOptionsDropdown()
        {
            if (_optionsDropOpen) HideOptionsDropdown();
            else                  ShowOptionsDropdown();
        }

        private void ShowOptionsDropdown()
        {
            if (OptionsDropGO == null) return;
            PopulateOptionsDropdown();
            PositionDropdownBelow(OptionsDropGO, OptionsBtn?.GetComponent<RectTransform>(), below: true);
            OptionsDropGO.SetActive(true);
            _optionsDropOpen = true;
        }

        internal void HideOptionsDropdown()
        {
            if (OptionsDropGO != null) OptionsDropGO.SetActive(false);
            _optionsDropOpen = false;
        }

        // Stays open after a click so both options can be toggled in one visit.
        private void PopulateOptionsDropdown()
        {
            var content = GetDropContent(OptionsDropGO);
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);

            AddDropdownItem(content, (ShowDv ? "■ " : "□ ") + "Show Δv column", false, () => {
                if (Plugin.CfgShowDv != null) Plugin.CfgShowDv.Value = !ShowDv;
                ApplySubHdrLayout();
                RebuildAllRowsForLayout();
                PopulateOptionsDropdown();
            });
            AddDropdownItem(content, (ShowNext ? "■ " : "□ ") + "Show next transfer window", false, () => {
                bool newVal = !ShowNext;
                if (Plugin.CfgShowNextWindow != null) Plugin.CfgShowNextWindow.Value = newVal;
                if (newVal)
                {
                    // Second windows were skipped while disabled — schedule the cheap
                    // partial recalc (opt1 stays cached) for every destination missing one.
                    foreach (var kv in cache)
                        if (kv.Value.opt1.HasValue && !kv.Value.opt2.HasValue)
                            _needsOpt2Recalc.Add(kv.Key);
                }
                RebuildAllRowsForLayout();
                PopulateOptionsDropdown();
            });
            AddDropdownItem(content, (ShowFastest ? "■ " : "□ ") + "Show Fastest", false, () => {
                if (Plugin.CfgShowFastest != null) Plugin.CfgShowFastest.Value = !ShowFastest;
                // Fastest windows ride along with the Optimal grid scan, so they are
                // always cached — this is purely a visibility toggle.
                ApplySubHdrLayout();
                RebuildAllRowsForLayout();
                PopulateOptionsDropdown();
            });
            AddDropdownItem(content, (ShowUndiscovered ? "■ " : "□ ") + "Show undiscovered", false, () => {
                if (Plugin.CfgShowUndiscovered != null) Plugin.CfgShowUndiscovered.Value = !ShowUndiscovered;
                needsRefresh = true; // row visibility is applied in RebuildRows
                PopulateOptionsDropdown();
            });
            AddDropdownItem(content, (ShowReturn ? "■ " : "□ ") + "Show Return trip", false, () => {
                if (Plugin.CfgShowReturn != null) Plugin.CfgShowReturn.Value = !ShowReturn;
                // Missing return windows are backfilled on demand by DoRefresh
                // (Calculating overlay shows while they compute).
                ApplySubHdrLayout();
                RebuildAllRowsForLayout();
                PopulateOptionsDropdown();
            });

            // "Alert [N] day(s) before" — editable numeric field.
            var alertRow = new GameObject("AlertRow", typeof(RectTransform));
            alertRow.transform.SetParent(content, false);
            alertRow.AddComponent<LayoutElement>().preferredHeight = 34f;
            var alertHlg = alertRow.AddComponent<HorizontalLayoutGroup>();
            alertHlg.childControlHeight = true; alertHlg.childControlWidth = true;
            alertHlg.childForceExpandHeight = true; alertHlg.childForceExpandWidth = false;
            alertHlg.spacing = 8f; alertHlg.padding = new RectOffset(9, 6, 3, 3);

            TextMeshProUGUI AlertLbl(string text, float width, bool flex = false)
            {
                var go = new GameObject("L", typeof(RectTransform));
                go.transform.SetParent(alertRow.transform, false);
                var le = go.AddComponent<LayoutElement>();
                if (flex) le.flexibleWidth = 1f; else le.preferredWidth = width;
                var tmp = go.AddComponent<TextMeshProUGUI>();
                if (FontAsset != null) tmp.font = FontAsset;
                tmp.text = text; tmp.fontSize = 16f;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.color = Color.white; tmp.enableWordWrapping = false;
                tmp.raycastTarget = false;
                return tmp;
            }

            AlertLbl("Alert", 46f);
            var daysInput = UI.LaunchWindowInjector.MakeInputField("AlertDays", alertRow.transform, FontAsset, "0", 28f);
            var daysLE = daysInput.GetComponent<LayoutElement>();
            if (daysLE != null) { daysLE.flexibleWidth = 0f; daysLE.preferredWidth = 56f; }
            daysInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            daysInput.SetTextWithoutNotify(AlertDaysBefore.ToString());
            daysInput.onEndEdit.AddListener(v => {
                int.TryParse(v, out var days);
                days = Mathf.Clamp(days, 0, 365);
                if (Plugin.CfgAlertDaysBefore != null) Plugin.CfgAlertDaysBefore.Value = days;
                daysInput.SetTextWithoutNotify(days.ToString());
            });
            AlertLbl("day(s) before", 0f, flex: true);
        }

        // Sub-header/column-header widths + section visibility + panel width for the
        // current ShowDv/ShowFastest/ShowReturn state. The Return section reuses the
        // Fastest column widths.
        internal void ApplySubHdrLayout()
        {
            bool dv = ShowDv;
            float optW = OPT_DEP_W + ARR_W + FUEL_W + (dv ? OPT_DV_W : 0f);
            float fstW = FST_DEP_W + ARR_W + FUEL_W + (dv ? FST_DV_W : 0f);
            if (OptDvHdrGO != null)
            {
                OptDvHdrGO.SetActive(dv);
                var le = OptDvHdrGO.transform.parent?.GetComponent<LayoutElement>();
                if (le != null) le.preferredWidth = optW;
            }
            if (FstDvHdrGO != null)
            {
                FstDvHdrGO.SetActive(dv);
                var le = FstDvHdrGO.transform.parent?.GetComponent<LayoutElement>();
                if (le != null) le.preferredWidth = fstW;
            }
            if (RetDvHdrGO != null)
            {
                RetDvHdrGO.SetActive(dv);
                var le = RetDvHdrGO.transform.parent?.GetComponent<LayoutElement>();
                if (le != null) le.preferredWidth = fstW;
            }
            if (OptColHdrLE != null) OptColHdrLE.preferredWidth = optW - 18f;
            if (FstColHdrLE != null) FstColHdrLE.preferredWidth = fstW - 18f;
            if (RetColHdrLE != null) RetColHdrLE.preferredWidth = fstW - 18f;

            if (FstHdrGOs != null) foreach (var go in FstHdrGOs) if (go != null) go.SetActive(ShowFastest);
            if (RetHdrGOs != null) foreach (var go in RetHdrGOs) if (go != null) go.SetActive(ShowReturn);

            // Panel width tracks the visible sections (name 158 + × 21 + chrome 34).
            if (PanelRT != null)
            {
                float total = 158f + optW
                    + (ShowFastest ? 12f + fstW : 0f)
                    + (ShowReturn  ? 12f + fstW : 0f)
                    + 21f + 34f;
                PanelRT.sizeDelta = new Vector2(total, PanelRT.sizeDelta.y);
            }
        }

        // Destroy every row so CreateRow rebuilds them under the current options.
        private void RebuildAllRowsForLayout()
        {
            foreach (var key in rowTMPs.Keys.ToList())
            {
                var t = ContentParent?.Find("Row_" + key);
                if (t != null) Destroy(t.gameObject);
            }
            rowTMPs.Clear();
            rowNameTMPs.Clear();
            rowIconImgs.Clear();
            rowCheckboxBtns.Clear();
            needsRefresh = true;
        }

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

            // Runtime-created asteroids may postdate the ephemeris build; if any group
            // member is unknown, rebuild once so it becomes addable.
            var known = new HashSet<string>(ephem.AllBodyIds);
            foreach (var oi in group.objectInGroup)
            {
                NBody sNb = null;
                try { sNb = oi != null && !oi.IsInGameDestroy ? oi.NBody : null; } catch { }
                if (sNb != null && !known.Contains(sNb.GetInstanceID().ToString()))
                {
                    Plugin.Log.LogInfo($"[LW] AddGroupBodies: unknown member '{sNb.name}' — rebuilding ephemeris");
                    TryBuildEphem(force: true);
                    if (ephem == null) return;
                    known = new HashSet<string>(ephem.AllBodyIds);
                    break;
                }
            }

            int added = 0;
            // Undiscovered members are added too (rows stay hidden while the
            // Show undiscovered option is off, and appear when it's toggled on).
            foreach (var oi in group.objectInGroup)
            {
                if (oi == null || oi.IsInGameDestroy) continue;
                NBody nb = null;
                try { nb = oi.NBody; } catch { }
                if (nb == null) continue;
                string id = nb.GetInstanceID().ToString();
                if (id == OriginId || DestIds.Contains(id)) continue;
                if (!known.Contains(id)) continue;
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
                        _retCacheByOrigin[prevOriginId] = new Dictionary<string, (LaunchWindow?, LaunchWindow?)>(retCache);
                        _needsOpt2ByOrigin[prevOriginId] = new HashSet<string>(_needsOpt2Recalc);
                        _needsFstByOrigin[prevOriginId]  = new HashSet<string>(_needsFstRecalc);
                    }
                    ClearAllRowData();
                    _needsOpt2Recalc.Clear();
                    _needsFstRecalc.Clear();
                    if (_retCacheByOrigin.TryGetValue(OriginId ?? "", out var retSaved))
                        foreach (var kv in retSaved) retCache[kv.Key] = kv.Value;
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

            // Filter out already-added/origin/destroyed BEFORE capping at 10 — otherwise a
            // query whose first alphabetical matches are all in the list shows nothing.
            // Prefix matches rank first so "7" surfaces "7 Iris" ahead of "17 Thetis".
            var matches = ephem.AllBodyIds
                .Where(id => ephem.GetDisplayName(id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(id => !IsBodyDestroyedId(id) && !DestIds.Contains(id) && id != OriginId)
                .Where(id => ShowUndiscovered || !IsUndiscoveredId(id))
                .OrderBy(id => ephem.GetDisplayName(id).StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(id => ephem.GetDisplayName(id))
                .Take(10)
                .ToList();

            void AddSearchItem(string bodyId, string label)
            {
                var captured = bodyId;
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
                }, GetBodyIcon(captured));
            }

            int added = 0;
            foreach (var id in matches)
            {
                added++;
                AddSearchItem(id, ephem.GetDisplayName(id));
            }

            // Moons aren't heliocentric bodies; match them by name and resolve to their
            // parent planet ("Ganymede → Jupiter").
            if (added < 10)
            {
                foreach (var kv in ephem.MoonAliases
                             .Where(kv => kv.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                             .Where(kv => !DestIds.Contains(kv.Value) && kv.Value != OriginId && !IsBodyDestroyedId(kv.Value))
                             .OrderBy(kv => kv.Key)
                             .Take(10 - added))
                {
                    added++;
                    AddSearchItem(kv.Value, $"{kv.Key} → {ephem.GetDisplayName(kv.Value)}");
                }
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

        private void AddDropdownItem(Transform content, string label, bool dimmed, UnityAction onClick,
                                     Sprite icon = null)
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

            if (icon != null)
            {
                var icoGO = new GameObject("Ico", typeof(RectTransform));
                icoGO.transform.SetParent(go.transform, false);
                var icoRT = icoGO.GetComponent<RectTransform>();
                icoRT.anchorMin = new Vector2(0f, 0f); icoRT.anchorMax = new Vector2(0f, 1f);
                icoRT.pivot = new Vector2(0f, 0.5f);
                icoRT.sizeDelta = new Vector2(22f, -8f);
                icoRT.anchoredPosition = new Vector2(6f, 0f);
                var icoImg = icoGO.AddComponent<Image>();
                icoImg.sprite = icon; icoImg.preserveAspect = true; icoImg.raycastTarget = false;
            }

            var lbl   = new GameObject("Lbl", typeof(RectTransform));
            lbl.transform.SetParent(go.transform, false);
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(icon != null ? 34f : 5f, 0f);
            lblRT.offsetMax = new Vector2(-4f, 0f);
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

                // Economic tuning constant used by the game's thrust feasibility check.
                try
                {
                    const BindingFlags bfE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var gm   = UnityEngine.Object.FindObjectOfType(typeof(GameManager));
                    var econ = gm?.GetType().GetProperty("Economic", bfE)?.GetValue(gm)
                            ?? gm?.GetType().GetField("economic", bfE)?.GetValue(gm);
                    var mult = econ?.GetType().GetProperty("DeltaVMultiplayerCheckingThrust", bfE)?.GetValue(econ)
                            ?? econ?.GetType().GetField("deltaVMultiplayerCheckingThrust", bfE)?.GetValue(econ);
                    if (mult != null) _thrustMultiplier = Convert.ToDouble(mult);
                }
                catch { }

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
        internal void ToggleSortOptDv()  => ToggleSort(SortCol.OptDv);
        internal void ToggleSortFstDv()  => ToggleSort(SortCol.FstDv);
        internal void ToggleSortOptArr() => ToggleSort(SortCol.OptArr);
        internal void ToggleSortFstArr() => ToggleSort(SortCol.FstArr);
        internal void ToggleSortOptFuel() => ToggleSort(SortCol.OptFuel);
        internal void ToggleSortFstFuel() => ToggleSort(SortCol.FstFuel);
        internal void ToggleSortRetDep()  => ToggleSort(SortCol.RetDep);
        internal void ToggleSortRetDv()   => ToggleSort(SortCol.RetDv);
        internal void ToggleSortRetArr()  => ToggleSort(SortCol.RetArr);
        internal void ToggleSortRetFuel() => ToggleSort(SortCol.RetFuel);

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
            switch (_sortCol)
            {
                case SortCol.OptDep: return e.opt1?.DepartureEpoch ?? double.MaxValue;
                case SortCol.FstDep: return e.fst1?.DepartureEpoch ?? double.MaxValue;
                case SortCol.OptDv:  return e.opt1?.DeltaVKmS ?? double.MaxValue;
                case SortCol.FstDv:  return e.fst1?.DeltaVKmS ?? double.MaxValue;
                case SortCol.OptArr: return e.opt1?.ArrivalEpoch ?? double.MaxValue;
                case SortCol.FstArr: return e.fst1?.ArrivalEpoch ?? double.MaxValue;
                // Fuel is monotonic in Δv for the selected craft, so Δv is the sort key.
                case SortCol.OptFuel: return e.opt1?.DeltaVKmS ?? double.MaxValue;
                case SortCol.FstFuel: return e.fst1?.DeltaVKmS ?? double.MaxValue;
                case SortCol.RetDep:  return RetKey(id)?.DepartureEpoch ?? double.MaxValue;
                case SortCol.RetArr:  return RetKey(id)?.ArrivalEpoch ?? double.MaxValue;
                case SortCol.RetDv:
                case SortCol.RetFuel: return RetKey(id)?.DeltaVKmS ?? double.MaxValue;
                default:             return double.MaxValue;
            }
        }

        private LaunchWindow? RetKey(string id)
            => retCache.TryGetValue(id, out var r) ? r.ret1 : null;

        // Craft names ship ALL CAPS ("PROMETHEUS") — display them Title Cased.
        // Mixed-case names are left untouched.
        private static string PrettyCraftName(string name)
        {
            if (string.IsNullOrEmpty(name) || name != name.ToUpperInvariant()) return name;
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        }

        private bool IsUndiscoveredId(string bodyId)
        {
            try
            {
                var oi = ephem?.GetNBodyForId(bodyId)?.GetObjectInfo();
                return oi != null && !oi.IsDiscoveredForPlayerCache;
            }
            catch { return false; }
        }

        private Sprite GetBodyIcon(string bodyId)
        {
            try
            {
                var oi = ephem?.GetNBodyForId(bodyId)?.GetObjectInfo();
                if (oi == null) return null;
                const BindingFlags bfi = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                return oi.GetType().GetProperty("ImagePlanetUI", bfi)?.GetValue(oi) as Sprite;
            }
            catch { return null; }
        }

        private void UpdateSortHeaders()
        {
            string suf = _sortDir == SortDir.Asc ? " ▲" : " ▼";
            if (OptDepHdrTMP != null)
                OptDepHdrTMP.text = _sortCol == SortCol.OptDep ? "Departs" + suf : "Departs";
            if (FstDepHdrTMP != null)
                FstDepHdrTMP.text = _sortCol == SortCol.FstDep ? "Departs" + suf : "Departs";
            if (OptDvHdrTMP != null)
                OptDvHdrTMP.text = _sortCol == SortCol.OptDv ? "Δv" + suf : "Δv";
            if (FstDvHdrTMP != null)
                FstDvHdrTMP.text = _sortCol == SortCol.FstDv ? "Δv" + suf : "Δv";
            if (OptArrHdrTMP != null)
                OptArrHdrTMP.text = _sortCol == SortCol.OptArr ? "Arrives" + suf : "Arrives";
            if (FstArrHdrTMP != null)
                FstArrHdrTMP.text = _sortCol == SortCol.FstArr ? "Arrives" + suf : "Arrives";
            if (OptFuelHdrTMP != null)
                OptFuelHdrTMP.text = _sortCol == SortCol.OptFuel ? "Fuel (E/F)" + suf : "Fuel (E/F)";
            if (FstFuelHdrTMP != null)
                FstFuelHdrTMP.text = _sortCol == SortCol.FstFuel ? "Fuel (E/F)" + suf : "Fuel (E/F)";
            if (RetDepHdrTMP != null)
                RetDepHdrTMP.text = _sortCol == SortCol.RetDep ? "Departs" + suf : "Departs";
            if (RetDvHdrTMP != null)
                RetDvHdrTMP.text = _sortCol == SortCol.RetDv ? "Δv" + suf : "Δv";
            if (RetArrHdrTMP != null)
                RetArrHdrTMP.text = _sortCol == SortCol.RetArr ? "Arrives" + suf : "Arrives";
            if (RetFuelHdrTMP != null)
                RetFuelHdrTMP.text = _sortCol == SortCol.RetFuel ? "Fuel (E/F)" + suf : "Fuel (E/F)";
        }

        private void TrySelectBestCraft()
        {
            if (_craftManuallySelected) return;
            var crafts = GetAllCraftDv();
            if (crafts.Length == 0) return;
            var best = crafts[0];
            for (int i = 1; i < crafts.Length; i++)
                if (crafts[i].maxDvKmS > best.maxDvKmS) best = crafts[i];
            SetCraft(best.name, best.maxDvKmS, best.maxCargo, best.exhaustV, best.dryMass, best.fuel, best.solarRangeAU, best.thrust, best.constAccel);
        }

        private void SetCraft(string name, double maxDvKmS, double maxCargo, double exhaustV, double dryMass, double fuel, double solarRangeAU = 0.0, double thrustN = 0.0, bool constAccel = false)
        {
            _craftThrustN    = thrustN;
            _craftConstAccel = constAccel;
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
            if (lbl != null) lbl.text = $"Craft: {PrettyCraftName(name)} ▼";
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

        private (string name, double maxDvKmS, double maxCargo, double exhaustV, double dryMass, double fuel, double solarRangeAU, double thrust, bool constAccel, Sprite icon)[] GetAllCraftDv()
        {
            try
            {
                const BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var omResult = GetOmAndPlayer();
                var player = omResult.player;
                if (player == null) return Array.Empty<(string, double, double, double, double, double, double, double, bool, Sprite)>();

                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asm == null) return Array.Empty<(string, double, double, double, double, double, double, double, bool, Sprite)>();

                // ShipManager.ListAllSpaceShip covers all owned spacecraft regardless of location.
                var smType = asm.GetType("ShipManager");
                if (smType == null) { Plugin.Log.LogWarning("[LW] GetAllCraftDv: ShipManager not found"); return Array.Empty<(string, double, double, double, double, double, double, double, bool, Sprite)>(); }
                var sm = UnityEngine.Object.FindObjectOfType(smType);
                if (sm == null) { Plugin.Log.LogWarning("[LW] GetAllCraftDv: ShipManager instance not found"); return Array.Empty<(string, double, double, double, double, double, double, double, bool, Sprite)>(); }

                var listAll = smType.GetProperty("ListAllSpaceShip", bf)?.GetValue(sm) as IEnumerable;
                if (listAll == null) { Plugin.Log.LogWarning("[LW] GetAllCraftDv: ListAllSpaceShip not found"); return Array.Empty<(string, double, double, double, double, double, double, double, bool, Sprite)>(); }

                var seen     = new HashSet<int>();
                var typeObjs = new List<object>();
                var result   = new List<(string, double, double, double, double, double, double, double, bool, Sprite)>();
                System.Reflection.FieldInfo fieldSCT = null;

                foreach (var sc in listAll)
                {
                    if (fieldSCT == null)
                        fieldSCT = sc.GetType().GetField("spacecraftType", bf);
                    var scType0 = fieldSCT?.GetValue(sc);
                    if (scType0 == null) continue;
                    if (seen.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(scType0)))
                        typeObjs.Add(scType0);
                }

                // Also include unlocked-but-not-yet-built types — matches the game's
                // mission planner (AllSpacecraftType.GetUnlockRocketType(player)), so a
                // freshly researched craft (e.g. Stratos still under construction) is
                // available for planning.
                try
                {
                    var asomType = asm.GetType("Manager.AllScriptableObjectManager")
                                ?? asm.GetType("AllScriptableObjectManager");
                    var asom = asomType != null ? UnityEngine.Object.FindObjectOfType(asomType) : null;
                    var allSct = asom?.GetType().GetProperty("AllSpacecraftType", bf)?.GetValue(asom)
                              ?? asom?.GetType().GetField("allSpacecraftType", bf)?.GetValue(asom);
                    var mUnlock = allSct?.GetType().GetMethods(bf)
                        .FirstOrDefault(m => m.Name == "GetUnlockRocketType" && m.GetParameters().Length == 1);
                    if (mUnlock != null && mUnlock.Invoke(allSct, new[] { player }) is IEnumerable unlocked)
                    {
                        int before = typeObjs.Count;
                        foreach (var t in unlocked)
                            if (t != null && seen.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t)))
                                typeObjs.Add(t);
                        if (!_craftLogged)
                            Plugin.Log.LogInfo($"[LW] GetAllCraftDv: +{typeObjs.Count - before} unlocked type(s) beyond built ships");
                    }
                    else if (!_craftLogged)
                        Plugin.Log.LogWarning($"[LW] GetAllCraftDv: unlocked-type lookup failed (type={(asomType != null)} inst={(asom != null)} list={(allSct != null)})");
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[LW] GetAllCraftDv unlocked types: {ex.Message}"); }

                foreach (var scType in typeObjs)
                {
                    try
                    {
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

                    Sprite scIcon = null;
                    try { scIcon = scTypeType.GetProperty("RocketBackGround", bf)?.GetValue(scType) as Sprite; }
                    catch { }

                    // Thrust (research-adjusted) + constant-acceleration flag for the
                    // finite-burn feasibility check (game: CheckCanLaunchThrust).
                    double thrustN = 0; bool constAccel = false;
                    try
                    {
                        var mThrust = scTypeType.GetMethods(bf).FirstOrDefault(m => m.Name == "GetThrust" && m.GetParameters().Length == 1);
                        if (mThrust != null) thrustN = Convert.ToDouble(mThrust.Invoke(scType, new[] { player }));
                    }
                    catch { }
                    try
                    {
                        var vCA = scTypeType.GetProperty("ConstanceAcceleration", bf)?.GetValue(scType)
                               ?? scTypeType.GetField("constanceAcceleration", bf)?.GetValue(scType);
                        if (vCA != null) constAccel = Convert.ToBoolean(vCA);
                    }
                    catch { }

                    if (!_craftLogged)
                    {
                        if (isSolar) Plugin.Log.LogInfo($"[LW] craft '{scName}': solar sail, range={solarRangeAU:F2}AU maxCargo={maxCargo:F1}");
                        else         Plugin.Log.LogInfo($"[LW] craft '{scName}': exhaustV={exhaustV:F3} mass={emptyMass:F1} fuel={fuel:F1} maxCargo={maxCargo:F1} maxDv={maxDvKmS:F1}km/s");
                    }
                    result.Add((scName, maxDvKmS, maxCargo, exhaustV, emptyMass, fuel, solarRangeAU, thrustN, constAccel, scIcon));
                    }
                    catch (Exception ex)
                    {
                        // One broken type must not empty the whole craft list.
                        Plugin.Log.LogWarning($"[LW] GetAllCraftDv: craft type extraction failed: {ex.Message}");
                    }
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
                return Array.Empty<(string, double, double, double, double, double, double, double, bool, Sprite)>();
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
            retCache.Clear();
            _nullCalcAt.Clear();
            _ret2Tried.Clear();
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
            bool showNextSnap = ShowNext; // skip second-window (next synodic) calcs when hidden
            bool showRetSnap  = ShowReturn;
            var retSnapDict   = new Dictionary<string, (LaunchWindow? ret1, LaunchWindow? ret2)>(retCache);
            var toCalcRet     = new List<(string dId, LaunchWindow opt1, LaunchWindow? opt2)>();
            bool showUndiscSnap = ShowUndiscovered;
            foreach (var dId in destSnap)
            {
                if (dId == originId) continue;
                // Hidden undiscovered rows don't compute; toggling Show undiscovered on
                // sets needsRefresh, which backfills them here.
                if (!showUndiscSnap && IsUndiscoveredId(dId)) continue;
                if (!HasValidCache(dId, physNow))
                {
                    // Known no-window result: the transfer geometry barely changes faster
                    // than ~1/24 of the origin's orbit, so don't retry a failed search
                    // until that much game time has passed.
                    if (cache.TryGetValue(dId, out var ceN) && !ceN.opt1.HasValue &&
                        _nullCalcAt.TryGetValue(dId, out var tN) &&
                        physNow - tN < ephem.GetPeriod(originId) / 24.0)
                        continue;
                    toCalcFull.Add(dId);
                }
                else if (showNextSnap && needsOpt2Snap.Contains(dId) && cache.TryGetValue(dId, out var ce) && ce.opt1.HasValue)
                    toCalcPartial.Add((dId, ce.opt1.Value, ce.fst1));
                else if (needsFstSnap.Contains(dId) && cache.TryGetValue(dId, out var ce2) && ce2.opt1.HasValue)
                    toCalcFstPartial.Add((dId, ce2.opt1.Value, ce2.opt2));
                else if (showRetSnap && cache.TryGetValue(dId, out var ce3) && ce3.opt1.HasValue &&
                         (!retSnapDict.TryGetValue(dId, out var rr) ||
                          (showNextSnap && ce3.opt2.HasValue && rr.ret2 == null && !_ret2Tried.Contains(dId))))
                    // On-demand return backfill: Return section just enabled, sidecar
                    // load, or next-window row newly available.
                    toCalcRet.Add((dId, ce3.opt1.Value, ce3.opt2));
            }

            if (toCalcFull.Count == 0 && toCalcPartial.Count == 0 && toCalcFstPartial.Count == 0 && toCalcRet.Count == 0)
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
                var retResults  = new Dictionary<string, (LaunchWindow?, LaunchWindow?)>();
                var resultsLock = new object();

                // Return trip: first optimal window dest → origin departing after arrival.
                (LaunchWindow?, LaunchWindow?) CalcReturn(WindowFinder f, string dId,
                    LaunchWindow? outb1, LaunchWindow? outb2)
                {
                    LaunchWindow? r1 = null, r2 = null;
                    if (outb1.HasValue)
                        r1 = f.FindWindows(dId, originId, outb1.Value.ArrivalEpoch, dvCap).optimal;
                    if (showNextSnap && outb2.HasValue)
                        r2 = f.FindWindows(dId, originId, outb2.Value.ArrivalEpoch, dvCap).optimal;
                    return (r1, r2);
                }
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
                            if (syn > 0 && showNextSnap)
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
                        if (showRetSnap)
                        {
                            try
                            {
                                var ret = CalcReturn(localFinder, dId, entry.opt1, entry.opt2);
                                lock (resultsLock) { retResults[dId] = ret; }
                            }
                            catch (Exception ex)
                            {
                                Plugin.Log.LogError($"[LW] FindWindows return {dId}: {ex.Message}");
                                lock (resultsLock) { retResults[dId] = (null, null); }
                            }
                        }
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
                            if (showRetSnap && o2.HasValue)
                            {
                                // Keep the cached ret1; only the new second window needs a return.
                                LaunchWindow? keep1 = retSnapDict.TryGetValue(item.dId, out var prev) ? prev.ret1 : null;
                                var r2 = localFinder.FindWindows(item.dId, originId, o2.Value.ArrivalEpoch, dvCap).optimal;
                                lock (resultsLock) { retResults[item.dId] = (keep1, r2); }
                            }
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
                            if (syn > 0 && showNextSnap)
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

                // Return-only backfills (Return section enabled with outbound windows cached).
                Parallel.ForEach<(string dId, LaunchWindow opt1, LaunchWindow? opt2), WindowFinder>(
                    toCalcRet,
                    parallelOpts,
                    () => new WindowFinder(new GameLambertSolver(), ephemSnap, dvToKmSSnap),
                    (item, _, localFinder) =>
                    {
                        try
                        {
                            var ret = CalcReturn(localFinder, item.dId, item.opt1, item.opt2);
                            lock (resultsLock) { retResults[item.dId] = ret; }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[LW] FindWindows return {item.dId}: {ex.Message}");
                            lock (resultsLock) { retResults[item.dId] = (null, null); }
                        }
                        return localFinder;
                    },
                    _ => { }
                );

                _pendingRetCache = retResults;
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
                var geA = GravityEngine.Instance();
                double nowA = geA != null ? geA.GetPhysicalTimeDouble() : 0;
                foreach (var kv in _pendingCache)
                {
                    cache[kv.Key] = kv.Value;
                    // Remember empty outcomes so they aren't re-searched every refresh.
                    if (!kv.Value.Item1.HasValue) _nullCalcAt[kv.Key] = nowA;
                    else                          _nullCalcAt.Remove(kv.Key);
                }
                _pendingCache = null;
                if (_pendingRetCache != null)
                {
                    foreach (var kv in _pendingRetCache)
                    {
                        retCache[kv.Key] = kv.Value;
                        if (kv.Value.Item2 == null && cache.TryGetValue(kv.Key, out var ceR) && ceR.opt2.HasValue)
                            _ret2Tried.Add(kv.Key);
                        else
                            _ret2Tried.Remove(kv.Key);
                    }
                    _pendingRetCache = null;
                }
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
                // Undiscovered bodies: hidden entirely unless the Show undiscovered
                // option is on, in which case they render with a greyed-out name.
                bool undiscovered = IsUndiscoveredId(dId);
                var rowGO = ContentParent.Find("Row_" + dId)?.gameObject;
                if (rowGO != null)
                {
                    bool visible = ShowUndiscovered || !undiscovered;
                    if (rowGO.activeSelf != visible) rowGO.SetActive(visible);
                }
                if (rowNameTMPs.TryGetValue(dId, out var nameTMP))
                    nameTMP.color = (outOfRange || undiscovered) ? new Color(0.45f, 0.45f, 0.45f) : Color.white;

                if (cache.TryGetValue(dId, out var entry))
                {
                    // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
                    // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
                    // [12]=opt1Fuel [13]=fst1Fuel [14]=opt2Fuel [15]=fst2Fuel
                    // [16..19]=ret1 dep/dv/arr/fuel [20..23]=ret2 dep/dv/arr/fuel
                    SetWindowCells(entry.opt1, tmps[0], tmps[1], tmps[2], tmps[12], ge);
                    SetWindowCells(entry.fst1, tmps[3], tmps[4], tmps[5], tmps[13], ge);
                    SetNextCells(entry.opt2, tmps[6], tmps[7], tmps[8], tmps[14], ge);
                    SetNextCells(entry.fst2, tmps[9], tmps[10], tmps[11], tmps[15], ge);
                    if (tmps.Length >= 24)
                    {
                        retCache.TryGetValue(dId, out var re);
                        SetWindowCells(re.ret1, tmps[16], tmps[17], tmps[18], tmps[19], ge);
                        SetNextCells(re.ret2, tmps[20], tmps[21], tmps[22], tmps[23], ge);
                    }
                }
            }
        }

        // Sub-column widths — must match injector sub-header widths exactly.
        // Column order: Departs | Arrives | Δv | Fuel. All fixed so both rows align;
        // group widths depend on ShowDv (Δv column hidden by default) — see
        // ApplySubHdrLayout and the locals in CreateRow.
        private const float CB_W       = 18f;
        private const float OPT_DEP_W  = 118f; // cb 18 + "26/07/18" at 15pt table font
        private const float OPT_DV_W   = 95f;
        private const float FST_DEP_W  = 120f;
        private const float FST_DV_W   = 110f;
        private const float ARR_W      = 100f; // "26/07/18" arrival date
        private const float FUEL_W     = 130f; // "23.8/31.2t" (E/F) at 15pt table font

        private void CreateRow(string dId)
        {
            // Container is a VLG holding primary row (30px) + next-window row (23px, optional).
            var container = new GameObject("Row_" + dId, typeof(RectTransform));
            container.transform.SetParent(ContentParent, false);
            container.AddComponent<LayoutElement>().preferredHeight = ShowNext ? 53f : 30f;

            float optGrpW = OPT_DEP_W + ARR_W + FUEL_W + (ShowDv ? OPT_DV_W : 0f);
            float fstGrpW = FST_DEP_W + ARR_W + FUEL_W + (ShowDv ? FST_DV_W : 0f);
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
            var nameCellLE = nameCell.AddComponent<LayoutElement>();
            nameCellLE.preferredWidth = 158f;
            // Must pin flexibleWidth: LayoutElement leaves it unset (-1), which falls through
            // to the nested HLG's flexible=1 (from the flex name label). That made the name
            // cell absorb the row's slack and shift every column right vs the second row.
            nameCellLE.flexibleWidth = 0f;
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
            oGroup.AddComponent<LayoutElement>().preferredWidth = optGrpW;
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
            var oTvl = MakeColLabel(oGroup.transform, "—", 15f, TextAlignmentOptions.Left, ARR_W);
            var oDv  = MakeColLabel(oGroup.transform, "—", 15f, TextAlignmentOptions.Left, OPT_DV_W);
            if (!ShowDv) oDv.transform.parent.gameObject.SetActive(false);
            var oFu  = MakeColLabel(oGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W);
            var sep1 = new GameObject("Sep", typeof(RectTransform));
            sep1.transform.SetParent(inner.transform, false);
            sep1.AddComponent<LayoutElement>().preferredWidth = 12f;
            sep1.SetActive(ShowFastest);
            // Fastest group — inline with checkbox, matching optimal group structure
            var fGroup = new GameObject("FstCol", typeof(RectTransform));
            fGroup.transform.SetParent(inner.transform, false);
            fGroup.AddComponent<LayoutElement>().preferredWidth = fstGrpW;
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
            var fTvl   = MakeColLabel(fGroup.transform, "—", 15f, TextAlignmentOptions.Left, ARR_W);
            var fDv    = MakeColLabel(fGroup.transform, "—", 15f, TextAlignmentOptions.Left, FST_DV_W);
            if (!ShowDv) fDv.transform.parent.gameObject.SetActive(false);
            var fFu    = MakeColLabel(fGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W);
            fGroup.SetActive(ShowFastest);

            // Return group — first optimal window back to the origin after arrival.
            // Same column layout as Fastest; an 18px spacer stands in for the checkbox.
            var rsep = new GameObject("RSep", typeof(RectTransform));
            rsep.transform.SetParent(inner.transform, false);
            rsep.AddComponent<LayoutElement>().preferredWidth = 12f;
            rsep.SetActive(ShowReturn);
            var rGroup = new GameObject("RetCol", typeof(RectTransform));
            rGroup.transform.SetParent(inner.transform, false);
            rGroup.AddComponent<LayoutElement>().preferredWidth = fstGrpW;
            var rHlg = rGroup.AddComponent<HorizontalLayoutGroup>();
            rHlg.childControlHeight = true; rHlg.childControlWidth = true;
            rHlg.childForceExpandHeight = true; rHlg.childForceExpandWidth = false;
            rHlg.spacing = 0f;
            var rDCell = new GameObject("RDepC", typeof(RectTransform));
            rDCell.transform.SetParent(rGroup.transform, false);
            rDCell.AddComponent<LayoutElement>().preferredWidth = FST_DEP_W;
            var rDHlg = rDCell.AddComponent<HorizontalLayoutGroup>();
            rDHlg.childControlHeight = true; rDHlg.childControlWidth = true;
            rDHlg.childForceExpandHeight = true; rDHlg.childForceExpandWidth = false;
            rDHlg.spacing = 0f;
            var retCb1 = MakeCheckboxButton(rDCell.transform);
            var rD    = MakeColLabel(rDCell.transform, "—", 15f, TextAlignmentOptions.Left, FST_DEP_W - CB_W);
            var rArr  = MakeColLabel(rGroup.transform, "—", 15f, TextAlignmentOptions.Left, ARR_W);
            var rDv   = MakeColLabel(rGroup.transform, "—", 15f, TextAlignmentOptions.Left, FST_DV_W);
            if (!ShowDv) rDv.transform.parent.gameObject.SetActive(false);
            var rFu   = MakeColLabel(rGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W);
            rGroup.SetActive(ShowReturn);

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

            // ── Next-window row (dimmed, 23px; hidden unless ShowNext) ───────────────
            var row2 = new GameObject("R2", typeof(RectTransform));
            row2.transform.SetParent(container.transform, false);
            row2.AddComponent<LayoutElement>().preferredHeight = 23f;
            if (!ShowNext) row2.SetActive(false);

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
            // Row-2 opt group mirrors row1's oGroup exactly.
            var noOGroup = new GameObject("OptCol2", typeof(RectTransform));
            noOGroup.transform.SetParent(inner2.transform, false);
            noOGroup.AddComponent<LayoutElement>().preferredWidth = optGrpW;
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
            var noTvl = MakeColLabel(noOGroup.transform, "—", 15f, TextAlignmentOptions.Left, ARR_W, dimC);
            var noDv = MakeColLabel(noOGroup.transform, "—", 15f, TextAlignmentOptions.Left, OPT_DV_W,  dimC);
            if (!ShowDv) noDv.transform.parent.gameObject.SetActive(false);
            var noFu = MakeColLabel(noOGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W, dimC);
            var sep2 = new GameObject("Sep2", typeof(RectTransform));
            sep2.transform.SetParent(inner2.transform, false);
            sep2.AddComponent<LayoutElement>().preferredWidth = 12f;
            sep2.SetActive(ShowFastest);
            // Row-2 fst group: same width as row1's fGroup so every column lands at the same x.
            var noFGroup = new GameObject("FstCol2", typeof(RectTransform));
            noFGroup.transform.SetParent(inner2.transform, false);
            noFGroup.AddComponent<LayoutElement>().preferredWidth = fstGrpW;
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
            var nfTvl = MakeColLabel(noFGroup.transform, "—", 15f, TextAlignmentOptions.Left, ARR_W, dimC);
            var nfDv  = MakeColLabel(noFGroup.transform, "—", 15f, TextAlignmentOptions.Left, FST_DV_W,  dimC);
            if (!ShowDv) nfDv.transform.parent.gameObject.SetActive(false);
            var nfFu  = MakeColLabel(noFGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W, dimC);
            noFGroup.SetActive(ShowFastest);

            // Row-2 return group — mirrors row1's rGroup.
            var rsep2 = new GameObject("RSep2", typeof(RectTransform));
            rsep2.transform.SetParent(inner2.transform, false);
            rsep2.AddComponent<LayoutElement>().preferredWidth = 12f;
            rsep2.SetActive(ShowReturn);
            var nrGroup = new GameObject("RetCol2", typeof(RectTransform));
            nrGroup.transform.SetParent(inner2.transform, false);
            nrGroup.AddComponent<LayoutElement>().preferredWidth = fstGrpW;
            var nrHlg = nrGroup.AddComponent<HorizontalLayoutGroup>();
            nrHlg.childControlHeight = true; nrHlg.childControlWidth = true;
            nrHlg.childForceExpandHeight = true; nrHlg.childForceExpandWidth = false;
            nrHlg.spacing = 0f;
            var nrDCell = new GameObject("RDepC2", typeof(RectTransform));
            nrDCell.transform.SetParent(nrGroup.transform, false);
            nrDCell.AddComponent<LayoutElement>().preferredWidth = FST_DEP_W;
            var nrDHlg = nrDCell.AddComponent<HorizontalLayoutGroup>();
            nrDHlg.childControlHeight = true; nrDHlg.childControlWidth = true;
            nrDHlg.childForceExpandHeight = true; nrDHlg.childForceExpandWidth = false;
            nrDHlg.spacing = 0f;
            var retCb2 = MakeCheckboxButton(nrDCell.transform, forRow2: true);
            var nrD   = MakeColLabel(nrDCell.transform, "—", 15f, TextAlignmentOptions.Left, FST_DEP_W - CB_W, dimC);
            var nrArr = MakeColLabel(nrGroup.transform, "—", 15f, TextAlignmentOptions.Left, ARR_W, dimC);
            var nrDv  = MakeColLabel(nrGroup.transform, "—", 15f, TextAlignmentOptions.Left, FST_DV_W, dimC);
            if (!ShowDv) nrDv.transform.parent.gameObject.SetActive(false);
            var nrFu  = MakeColLabel(nrGroup.transform, "—", 15f, TextAlignmentOptions.Left, FUEL_W, dimC);
            nrGroup.SetActive(ShowReturn);

            // [0]=opt1Dep [1]=opt1Dv [2]=opt1Tvl [3]=fst1Dep [4]=fst1Dv [5]=fst1Tvl
            // [6]=opt2Dep [7]=opt2Dv [8]=opt2Tvl [9]=fst2Dep [10]=fst2Dv [11]=fst2Tvl
            // [12]=opt1Fuel [13]=fst1Fuel [14]=opt2Fuel [15]=fst2Fuel
            // [16..19]=ret1 dep/dv/arr/fuel [20..23]=ret2 dep/dv/arr/fuel
            rowTMPs[dId] = new[] { oD, oDv, oTvl, fD, fDv, fTvl, noD, noDv, noTvl, nfD, nfDv, nfTvl, oFu, fFu, noFu, nfFu,
                                   rD, rDv, rArr, rFu, nrD, nrDv, nrArr, nrFu };

            var capDest = dId;
            cb1.onClick.AddListener(()    => ToggleAlarmForRow(capDest, false, false));
            cb2.onClick.AddListener(()    => ToggleAlarmForRow(capDest, true,  false));
            fstCb1.onClick.AddListener(() => ToggleAlarmForRow(capDest, false, true));
            fstCb2.onClick.AddListener(() => ToggleAlarmForRow(capDest, true,  true));
            retCb1.onClick.AddListener(() => ToggleAlarmForReturnRow(capDest, false));
            retCb2.onClick.AddListener(() => ToggleAlarmForReturnRow(capDest, true));
            // idx: 0=opt1, 1=opt2, 2=fst1, 3=fst2, 4=ret1, 5=ret2
            rowCheckboxBtns[dId] = new[] { cb1, cb2, fstCb1, fstCb2, retCb1, retCb2 };
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
            retCache.Remove(dId);
            _nullCalcAt.Remove(dId);
            _ret2Tried.Remove(dId);
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
            var cellFont = TableFontAsset ?? FontAsset;
            if (cellFont != null) tmp.font = cellFont;
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
            TextMeshProUGUI dep, TextMeshProUGUI dv, TextMeshProUGUI arr,
            TextMeshProUGUI fuel, GravityEngine ge)
        {
            if (w == null || ge == null)
            {
                dep.text = dv.text = arr.text = fuel.text = "—";
                dep.color = dv.color = arr.color = fuel.color = DashColor;
                return;
            }
            dep.text  = FormatEpoch(w.Value.DepartureEpoch);
            dv.text   = $"{w.Value.DeltaVKmS:F1}km/s";
            arr.text  = FormatEpoch(w.Value.ArrivalEpoch);
            fuel.text = FormatFuel(w.Value.DeltaVKmS);
            bool unreachable = w.Value.DeltaVKmS > _craftMaxDvKmS;
            Color c = unreachable ? RedMuted : (ThrustShort(w.Value) ? AmberColor : WhiteColor);
            dep.color = dv.color = arr.color = fuel.color = c;
        }

        private static readonly Color DimColor   = new Color(0.50f, 0.50f, 0.50f);
        private static readonly Color AmberColor = new Color(0.87f, 0.62f, 0.24f);
        private static readonly Color AmberDim   = new Color(0.58f, 0.44f, 0.20f);

        // Finite-burn feasibility (game: "Not enough thrust for this maneuver").
        // Empty-cargo mass with full tanks; solar sails and constant-acceleration
        // drives use different flight models and are exempt, matching the game.
        private bool ThrustShort(LaunchWindow w)
        {
            if (_craftThrustN <= 0 || _craftConstAccel || _craftSolarRangeAU > 0) return false;
            double massTons = _craftDryMass + _craftFuel;
            double spp = 1.0;
            try { spp = GravityScaler.GetGameSecondPerPhysicsSecond(); } catch { }
            if (spp <= 0) spp = 1.0;
            double travelGameSec = w.TravelTimeSeconds * spp;
            return !ThrustCheck.HasEnoughThrust(w.DeltaVKmS, _craftThrustN, massTons, travelGameSec, _thrustMultiplier);
        }

        private void SetNextCells(LaunchWindow? w,
            TextMeshProUGUI dep, TextMeshProUGUI dv, TextMeshProUGUI arr,
            TextMeshProUGUI fuel, GravityEngine ge)
        {
            if (w == null || ge == null)
            {
                dep.text = dv.text = arr.text = fuel.text = "—";
                dep.color = dv.color = arr.color = fuel.color = DimColor;
                return;
            }
            dep.text  = FormatEpoch(w.Value.DepartureEpoch);
            dv.text   = $"{w.Value.DeltaVKmS:F1}km/s";
            arr.text  = FormatEpoch(w.Value.ArrivalEpoch);
            fuel.text = FormatFuel(w.Value.DeltaVKmS);
            Color c = ThrustShort(w.Value) ? AmberDim : DimColor;
            dep.color = dv.color = arr.color = fuel.color = c;
        }

        // Propellant for a transfer via the rocket equation: fuel = mass × (e^(Δv/ve) − 1),
        // shown as Empty/Full-cargo load. _craftExhaustV comes from
        // SpacecraftType.GetExhaustV(player), which multiplies the base (or completed hull
        // design) exhaust velocity by the company's researched EBonus.ComponentExhaustV
        // bonuses — so this always reflects the currently-researched engine variant.
        // Solar sails burn no fuel; no craft data shows "—".
        private string FormatFuel(double dvKmS)
        {
            if (_craftExhaustV <= 0 || _craftDryMass <= 0 || _craftSolarRangeAU > 0) return "—";
            double factor = Math.Exp(dvKmS / _craftExhaustV) - 1.0;
            if (double.IsNaN(factor) || double.IsInfinity(factor)) return "—";
            double empty = _craftDryMass * factor;
            if (_craftMaxCargo <= 0) return FuelFig(empty);
            double full = (_craftDryMass + _craftMaxCargo) * factor;
            // Compact shared-unit form when both fit the tank; otherwise per-figure
            // units so an over-capacity figure can be flagged red individually.
            if (!OverCap(empty) && !OverCap(full) && MassUnit(empty) == MassUnit(full))
                return $"{MassNum(empty)}/{MassNum(full)}{MassUnit(full)}";
            return $"{FuelFig(empty)}/{FuelFig(full)}";
        }

        // The tank is finite: a figure above GetFuelCapacity means the craft cannot
        // actually carry enough propellant for that transfer at that load — flag it red.
        // (0.05% tolerance absorbs FP rounding at dv == max-dv, where empty == capacity.)
        private bool OverCap(double tons) => _craftFuel > 0 && tons > _craftFuel * 1.0005;

        private string FuelFig(double tons)
        {
            string s = MassNum(tons) + MassUnit(tons);
            return OverCap(tons) ? $"<color=#D05050>{s}</color>" : s;
        }

        private static string MassNum(double t)
            => t >= 1000 ? $"{t / 1000:F1}" : (t >= 100 ? $"{t:F0}" : $"{t:F1}");

        private static string MassUnit(double t) => t >= 1000 ? "kt" : "t";

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
                return d.ToString("yy/MM/dd");
            }
            catch { return "—"; }
        }

        // ── Alarm checking ────────────────────────────────────────────────────────

        private void CheckAlarms()
        {
            if (_alarms.Count == 0 || _clock == null) return;
            var toFire = LWCacheHelper.GetAlarmsToFire(_alarms, OriginId, _clock.CurrentTime, AlertDaysBefore);
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
            string kind = key.IsReturn ? "Return" : (key.IsFastest ? "Fastest" : "Optimal");
            // Return windows fly dest → origin.
            string fromName = key.IsReturn ? destName : originName;
            string toName   = key.IsReturn ? originName : destName;

            if (!TryFireGameNotification(key.DestId, originName, destName, kind, reversed: key.IsReturn))
                SpawnToast($"Launch Window ({kind}): {fromName} → {toName}");

            UpdateAllCheckboxVisuals();
        }

        // Fire a real game notification so it appears in "New Notifications" and is saved.
        // Uses Schedule (13) which has locale text "Mission from {0} to {1} scheduled for {2}".
        // Game pauses if the player's pause-on-notification toggle is enabled.
        private bool TryFireGameNotification(string destId, string originName, string destName, string kind, bool reversed = false)
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
                        int day = Math.Max(1, Math.Min(k.Day, DateTime.DaysInMonth(k.Year, k.Month)));
                        dateStr = new DateTime(k.Year, k.Month, day).ToString("yy/MM/dd");
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
                        if (reversed) { var t2 = originHL; originHL = destHL; destHL = t2; } // return: dest → origin
                        tmp.text = $"{originHL} → {destHL}\nLaunch Window ({kind}){(string.IsNullOrEmpty(dateStr) ? "" : " " + dateStr)}";
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

        // An alarm belongs to a window slot if it matches origin/dest/kind and its date
        // is within ±16 days of the window's departure. Exact-date keys proved brittle:
        // epoch→date conversion can drift a day between sessions or recalcs, which used
        // to orphan the alarm and render the checkbox unarmed. Adjacent windows of the
        // same slot are months apart (synodic), so the tolerance cannot mis-match.
        private AlarmKey? FindAlarmNear(string destId, bool isFastest, bool isReturn, DateTime depDate)
        {
            foreach (var a in _alarms)
            {
                if (a.DestId != destId || a.OriginId != OriginId ||
                    a.IsFastest != isFastest || a.IsReturn != isReturn) continue;
                int day = Math.Max(1, Math.Min(a.Day <= 0 ? 15 : a.Day, DateTime.DaysInMonth(a.Year, a.Month)));
                var d = new DateTime(a.Year, a.Month, day);
                if (Math.Abs((d - depDate).TotalDays) <= 16) return a;
            }
            return null;
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

            var existing = FindAlarmNear(destId, isFastest, isReturn: false, depDate);
            bool armed;
            if (existing.HasValue) { _alarms.Remove(existing.Value); armed = false; }
            else
            {
                _alarms.Add(new AlarmKey { OriginId = OriginId, DestId = destId, Year = depDate.Year, Month = depDate.Month, Day = depDate.Day, IsFastest = isFastest });
                armed = true;
            }
            _sidecarDirty = true;
            Plugin.Log.LogInfo($"[LW] ToggleAlarm '{dest}': armed={armed} row2={isRow2} fast={isFastest}");
            int idx = (!isFastest ? 0 : 2) + (isRow2 ? 1 : 0);
            UpdateCheckboxVisual(destId, idx, armed);
        }

        private void ToggleAlarmForReturnRow(string destId, bool isRow2)
        {
            string dest = ephem?.GetDisplayName(destId) ?? destId;
            if (!retCache.TryGetValue(destId, out var entry))
            { Plugin.Log.LogInfo($"[LW] ToggleReturnAlarm '{dest}': no return cache entry"); return; }
            LaunchWindow? window = isRow2 ? entry.ret2 : entry.ret1;
            if (window == null)
            { Plugin.Log.LogInfo($"[LW] ToggleReturnAlarm '{dest}': window slot is null (row2={isRow2})"); return; }
            if (ephem == null || !TryEpochToDate(window.Value.DepartureEpoch, out var depDate))
            { Plugin.Log.LogInfo($"[LW] ToggleReturnAlarm '{dest}': date unavailable"); return; }

            var existing = FindAlarmNear(destId, isFastest: false, isReturn: true, depDate);
            bool armed;
            if (existing.HasValue) { _alarms.Remove(existing.Value); armed = false; }
            else
            {
                _alarms.Add(new AlarmKey { OriginId = OriginId, DestId = destId, Year = depDate.Year, Month = depDate.Month, Day = depDate.Day, IsFastest = false, IsReturn = true });
                armed = true;
            }
            _sidecarDirty = true;
            Plugin.Log.LogInfo($"[LW] ToggleReturnAlarm '{dest}': armed={armed} row2={isRow2}");
            UpdateCheckboxVisual(destId, 4 + (isRow2 ? 1 : 0), armed);
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
                retCache.TryGetValue(destId, out var retEntry);
                // idx: 0=opt1, 1=opt2, 2=fst1, 3=fst2, 4=ret1, 5=ret2
                var windows    = new LaunchWindow?[] { entry.opt1, entry.opt2, entry.fst1, entry.fst2, retEntry.ret1, retEntry.ret2 };
                var isFastests = new bool[]          { false,      false,      true,       true,       false,         false };
                var isReturns  = new bool[]          { false,      false,      false,      false,      true,          true  };
                for (int i = 0; i < windows.Length; i++)
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
                    bool armed = FindAlarmNear(destId, isFastests[i], isReturns[i], depDate) != null;
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
            // Sidecars persist display names (v4+); older files hold raw instance ids,
            // which only resolve within the session that wrote them. Accept both.
            var allIdsR = new HashSet<string>(ephem.AllBodyIds);
            var nameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bid in ephem.AllBodyIds)
            {
                var bn = ephem.GetDisplayName(bid);
                if (!string.IsNullOrEmpty(bn) && !nameToId.ContainsKey(bn)) nameToId[bn] = bid;
            }
            string R(string s)
            {
                if (string.IsNullOrEmpty(s)) return null;
                if (nameToId.TryGetValue(s, out var rid)) return rid;
                return allIdsR.Contains(s) ? s : null;
            }

            var originResolved = R(_sidecarData.originId);
            if (originResolved != null)
            {
                int idx = originIds.IndexOf(originResolved);
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
                        SetCraft(c.name, c.maxDvKmS, c.maxCargo, c.exhaustV, c.dryMass, c.fuel, c.solarRangeAU, c.thrust, c.constAccel);
                        break;
                    }
                }
            }
            var allIds = allIdsR;
            _destsByOrigin.Clear();
            if (_sidecarData.originDests?.Count > 0)
            {
                foreach (var od in _sidecarData.originDests)
                {
                    var ro = R(od.originId);
                    if (ro != null)
                        _destsByOrigin[ro] = (od.destIds ?? new List<string>())
                            .Select(R).Where(id => id != null).ToList();
                }
            }
            else if (_sidecarData.destIds?.Count > 0)
            {
                // v1 compat: treat saved DestIds as belonging to the saved origin
                if (originResolved != null)
                    _destsByOrigin[originResolved] = _sidecarData.destIds
                        .Select(R).Where(id => id != null).ToList();
            }
            _firedAlarms.Clear();
            _alarms.Clear();
            foreach (var a in _sidecarData.alarms ?? new List<LWAlarmSave>())
            {
                var ao = R(a.originId); var ad = R(a.destId);
                if (ao == null || ad == null) continue;
                _alarms.Add(new AlarmKey { OriginId = ao, DestId = ad, Year = a.year, Month = a.month, Day = a.day, IsFastest = a.isFastest, IsReturn = a.isReturn });
            }

            // Resolve cache destIds (names → live ids) before promotion.
            List<LWDestCacheSave> ResolveCacheList(List<LWDestCacheSave> src)
            {
                var outList = new List<LWDestCacheSave>();
                foreach (var cs in src ?? new List<LWDestCacheSave>())
                {
                    var rid = R(cs.destId);
                    if (rid == null) continue;
                    cs.destId = rid;
                    outList.Add(cs);
                }
                return outList;
            }

            var ge2 = GravityEngine.Instance();
            double physNow2 = ge2 != null ? ge2.GetPhysicalTimeDouble() : 0;
            var (promoted, needsOpt2, needsFst) = LWCacheHelper.PromoteWindowCache(
                ResolveCacheList(_sidecarData.windowCache), allIds, physNow2);
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
                var ro = R(oc.originId);
                if (ro == null) continue;
                var (prom, o2set, fsset) = LWCacheHelper.PromoteWindowCache(ResolveCacheList(oc.cache), allIds, physNow2);
                if (prom.Count > 0)
                {
                    _cacheByOrigin[ro] = prom;
                    if (o2set.Count > 0) _needsOpt2ByOrigin[ro] = o2set;
                    if (fsset.Count > 0) _needsFstByOrigin[ro]  = fsset;
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
                // Persist DISPLAY NAMES, not NBody instance ids: Unity reassigns instance
                // ids every session, so id-based sidecars only survived same-session
                // reloads (observed: the same Earth saved as 224236 / 49598 / 233066 in
                // three sessions). Names are scene-stable and mapped back on load.
                string N(string id) => ephem != null ? ephem.GetDisplayName(id) : id;

                var originDestsList = new List<LWOriginDestsSave>();
                foreach (var kv in _destsByOrigin)
                    originDestsList.Add(new LWOriginDestsSave { originId = N(kv.Key), destIds = kv.Value.Select(N).ToList() });

                var alarmsList = new List<LWAlarmSave>();
                foreach (var a in _alarms)
                    alarmsList.Add(new LWAlarmSave { originId = N(a.OriginId), destId = N(a.DestId), year = a.Year, month = a.Month, day = a.Day, isFastest = a.IsFastest, isReturn = a.IsReturn });

                var cacheList = new List<LWDestCacheSave>();
                foreach (var kv in cache)
                    cacheList.Add(new LWDestCacheSave
                    {
                        destId = N(kv.Key),
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
                            destId = N(dc.Key),
                            opt1   = o1.HasValue ? LWSaveConvert.ToSave(o1.Value) : null,
                            fst1   = f1.HasValue ? LWSaveConvert.ToSave(f1.Value) : null,
                            opt2   = o2.HasValue ? LWSaveConvert.ToSave(o2.Value) : null,
                            fst2   = f2.HasValue ? LWSaveConvert.ToSave(f2.Value) : null,
                        });
                    }
                    originCachesList.Add(new LWOriginCacheSave { originId = N(oc.Key), cache = ocList });
                }

                var data = new LWSaveData
                {
                    originId          = OriginId != null ? N(OriginId) : "",
                    selectedCraftName = _selectedCraftName ?? "",
                    destIds           = DestIds.Select(N).ToList(),
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
