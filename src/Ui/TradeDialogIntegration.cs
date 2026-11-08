// Staged DIPLOMACY ExtendedState source integration, not an installed release.
// Compiles inside the original StrongholdEuropeDE.Runtime.DiplomacyFeature
// partial class, reads the existing AlliesPanel, never creates another
// Harmony/Noesis/MarketPrice hook, and never changes Native goods.
// SERPS APIShared + rawra ScriptExtender are hard dependencies for the stage.
using System;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;
using SHCDESE.API;
using StrongholdEuropeDE.TradeNegotiation;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private const string TradeCommandPrefix = "Diplomacy:Trade:";
        private static readonly int[] TradeAllowedGoods =
        {
            2,
            4,
            6,
            7,
            9,
            10,
            11,
            12,
            13,
            14,
            16,
            17,
            18,
            19,
            20,
            21,
            22,
            23,
            24
        };
        private bool _tradeTab;
        private int _tradeWantedIndex = 1; // stone
        private int _tradeOfferedIndex = 0; // wood
        private int _tradeRequestedAmount = 10;
        private TradeOfferGenerosity _tradeGenerosity = TradeOfferGenerosity.Fair;
        private string _tradeStatus = string.Empty;
        // Only display action receipts for the ruler who produced them.
        private int _statusPartner;
        // A draft belongs to one selected in-game lord; stale queued clicks
        // must never silently redirect it to another ruler.
        private int _draftPartner;
        // lookup all 38 vanilla Good sprites at most once per XAML
        // panel lifetime. Per-frame O(N^2) VisualTree rescans would slow game.
        private object _tradePanelOwner;
        private readonly Dictionary<string, object> _iconRefs = new Dictionary<string, object>(StringComparer.Ordinal);
        private int _lastIconSelection = -1;
        private int _lastWanted = -1, _lastOffered = -1, _lastAmount = -1;
        private bool _draftControlsReady;
        private object _draftControlsOwner;
        // The trade window is a sibling of the vanilla details panel. Hide
        // both its backdrop and content even when the native roster is gone.
        private void CloseTradeWindow(object panel)
        {
            // Other vanilla tabs and roster refreshes also close this modal;
            // do not discard the current partner's unfinished text in those
            // paths merely because the user did not click our own Close.
            if (_tradeTab && _draftPartner >= 1 && panel != null)
                RememberDraft(_draftPartner, panel);
            _tradeTab = false;
            _draftPartner = 0;
            _lastIconSelection = -1;
            _draftControlsReady = false;
            _draftControlsOwner = null;
            _tradePanelOwner = null;
            _iconRefs.Clear();
            if (panel != null)
            {
                SetVisibilityIfPresent(FindElement(panel, "TradeModal"), false);
                SetVisibilityIfPresent(FindElement(panel, "TradePanel"), false);
            }
        }

        private static bool TryCustomAmount(string value, out int amount)
        {
            amount = 0;
            if (string.IsNullOrEmpty(value) || value.Length > 6 || value[0] == '0')
                return false;
            for (int i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9')
                    return false;
            int parsed;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) || parsed < 1 || parsed > 100000)
                return false;
            amount = parsed;
            return true;
        }

        private static bool TrySliderTier(double value, out TradeOfferGenerosity tier)
        {
            tier = TradeOfferGenerosity.Fair;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 2)
                return false;
            int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            if (Math.Abs(value - rounded) > 0.001d)
                return false;
            tier = (TradeOfferGenerosity)rounded;
            return true;
        }

        private static object ReadControlProperty(object target, string name)
        {
            if (target == null)
                return null;
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return property != null && property.CanRead ? property.GetValue(target, null) : null;
        }

        private void InitializeDraftControls(object panel)
        {
            if (!Object.ReferenceEquals(_draftControlsOwner, panel))
            {
                _draftControlsReady = false;
                _draftControlsOwner = panel;
            }

            if (_draftControlsReady)
                return;
            object input = TradeElement(panel, "TradeAmountInput");
            object slider = TradeElement(panel, "TradeGenerositySlider");
            if (input == null || slider == null)
                return;
            SetProperty(input, "Text", CurrentTextAmount());
            SetNumericProperty(slider, "Value", (int)_tradeGenerosity);
            _draftControlsReady = true;
        }

        private bool ReadDraftControls(object panel, out string reason)
        {
            reason = string.Empty;
            // Never submit old model values if a TextBox/Slider is absent or
            // currently contains an invalid partial edit.
            if (!_draftControlsReady)
            {
                reason = Text("Handelsfenster wird vorbereitet.", "Preparing trade window.");
                return false;
            }

            object input = TradeElement(panel, "TradeAmountInput");
            object slider = TradeElement(panel, "TradeGenerositySlider");
            int amount;
            if (!TryCustomAmount(ReadControlProperty(input, "Text") as string, out amount))
            {
                reason = Text("Menge: ganze Zahl von 1 bis 100000 eingeben.", "Amount: enter a whole number from 1 to 100000.");
                return false;
            }

            object raw = ReadControlProperty(slider, "Value");
            TradeOfferGenerosity tier;
            if (raw == null || !(raw is float || raw is double || raw is int) || !TrySliderTier(Convert.ToDouble(raw, CultureInfo.InvariantCulture), out tier))
            {
                reason = Text("Verhandlungsstufe auswählen.", "Select negotiation terms.");
                return false;
            }

            _tradeRequestedAmount = amount;
            _tradeGenerosity = tier;
            return true;
        }

        private void SetDraftTier(object panel, TradeOfferGenerosity tier)
        {
            _tradeGenerosity = tier;
            SetNumericProperty(TradeElement(panel, "TradeGenerositySlider"), "Value", (int)tier);
        }

        private void ResetTradeUiForMap()
        {
            CloseTradeWindow(null);
            ClearPartnerDrafts();
            _tradeWantedIndex = 1;
            _tradeOfferedIndex = 0;
            _tradeRequestedAmount = 10;
            _tradeGenerosity = TradeOfferGenerosity.Fair;
            _tradeStatus = string.Empty;
            _statusPartner = 0;
            _draftPartner = 0;
            _tradePanelOwner = null;
            _iconRefs.Clear();
            _lastIconSelection = -1;
            ResetTradeUiState();
        }

        // A stale queued Noesis command from a hidden trade tab must not
        // change draft terms or execute any trading action.
        private static bool TradeActionPermitted(bool tabVisible, string action)
        {
            if (action == "Open" || action == "Close")
                return true;
            return tabVisible && !string.IsNullOrEmpty(action);
        }

        // This pure scope check also runs against the actual compiled DLL
        // in an offline regression; a draft must not jump to another lord.
        private static bool TradePartnerMatches(int draftPartner, int selectedPartner)
        {
            return draftPartner >= 1 && draftPartner <= 8 && selectedPartner == draftPartner;
        }

        private bool HandleTradeButton(object panel, string param)
        {
            if (string.IsNullOrEmpty(param) || !param.StartsWith(TradeCommandPrefix, StringComparison.Ordinal))
                return false;
            string act = param.Substring(TradeCommandPrefix.Length);
            // The close action must always work, even if the ally has died,
            // the selected game slot changed, or the map is shutting down.
            // It NEVER enters the native inventory/commit path.
            if (act == "Close")
            {
                if (_tradeTab && _draftPartner >= 1)
                    RememberDraft(_draftPartner, panel);
                CloseTradeWindow(panel);
                _tradeStatus = string.Empty;
                if (_mapActive && !_mapDisabled)
                    RefreshVanillaDiplomacyUi(panel);
                return true;
            }

            if (!_mapActive || _mapDisabled)
                return true;
            if (!TradeActionPermitted(_tradeTab, act))
                return true;
            // The modal partner is independent of the vanilla portrait.
            // Opening from a different portrait is an explicit retarget;
            // all subsequent actions belong only to the modal's own owner.
            int counterpart = act == "Open" ? SelectedPlayer(panel) : _draftPartner;
            if (counterpart <= 0 || counterpart == _localPlayer || !_roster.Contains(counterpart))
            {
                CloseTradeWindow(panel);
                _tradeStatus = Text("Ungültiger Handelspartner", "Invalid trading counterpart");
                RefreshVanillaDiplomacyUi(panel);
                return true;
            }

            string permission;
            if (!CanTradeWith(counterpart, out permission))
            {
                if (_tradeTab && _draftPartner >= 1)
                    RememberDraft(_draftPartner, panel);
                CloseTradeWindow(panel);
                _tradeStatus = permission;
                RefreshVanillaDiplomacyUi(panel);
                return true;
            }

            // Nav arrows can switch partners even while one offer is pending,
            // and never accept any counter silently on behalf of the next.
            if (act == "Partner:Previous" || act == "Partner:Next")
            {
                int nextPartner;
                if (FindNextTradingPartner(counterpart, act == "Partner:Next", out nextPartner))
                {
                    SwitchTradePartner(nextPartner, panel);
                }
                else
                {
                    _tradeStatus = Text("Kein anderer Handelspartner verfügbar.", "No other trading partner available.");
                }

                RefreshVanillaDiplomacyUi(panel);
                return true;
            }

            // A saved canonical human offer has its own immutable
            // partner-bound ConsentId and NativeAuthorized checks.
            // Always process a genuine Accept/Decline/Withdraw/Counter
            // response BEFORE checking for a legacy in-flight messenger.
            // Otherwise a leftover legacy mailbox command silently eats a
            // real original Noesis consent click, even when a valid new
            // canonical offer is currently pending.
            if (HandleConsentUi(act, counterpart, panel))
            {
                RememberDraft(_draftPartner, panel);
                RefreshVanillaDiplomacyUi(panel);
                return true;
            }

            // Only a NEW proposal/draft is blocked by a legacy pending
            // messenger. No Native goods, lease or saved quote is granted
            // by this priority change; HandleConsentUi owns all those gates.
            if (act != "Open" && HasPendingTrade(counterpart))
            {
                _tradeStatus = PendingTradeStatus(counterpart);
                RefreshVanillaDiplomacyUi(panel);
                return true;
            }

            TradeRecord pendingCounter = PendingCounterFor(counterpart);
            if (act != "Open" && act != "Accept" && act != "Decline" && pendingCounter != null)
                return true;
            int selectedGood, presetAmount;
            if (act == "Open")
            {
                // Each counterpart has independent goods, quantity, tier and
                // even a temporarily unfinished text input. Reopening or
                // explicitly selecting another portrait restores its draft.
                SwitchTradePartner(counterpart, panel);
            }
            else if (act == "Offered:Auto")
            {
                _tradeOfferedIndex = TradeAllowedGoods.Length;
            }
            else if (act == "Wanted:Auto")
            {
                _tradeWantedIndex = TradeAllowedGoods.Length;
            }
            else if (TryIconSelection(act, "Wanted:", out selectedGood))
            {
                _tradeWantedIndex = selectedGood;
                _tradeStatus = string.Empty;
            }
            else if (TryIconSelection(act, "Offered:", out selectedGood))
            {
                _tradeOfferedIndex = selectedGood;
                _tradeStatus = string.Empty;
            }
            else if (TryQuantityPreset(act, out presetAmount))
            {
                _tradeRequestedAmount = presetAmount;
                object input = TradeElement(panel, "TradeAmountInput");
                if (input != null)
                    SetProperty(input, "Text", presetAmount.ToString(CultureInfo.InvariantCulture));
                _tradeStatus = string.Empty;
            }
            else if (act == "Amount:Apply")
            {
                string inputReason;
                _tradeStatus = ReadDraftControls(panel, out inputReason) ? string.Empty : inputReason;
            }
            else if (act == "Tier:0")
            {
                SetDraftTier(panel, TradeOfferGenerosity.Grosszuegig);
            }
            else if (act == "Tier:1")
            {
                SetDraftTier(panel, TradeOfferGenerosity.Fair);
            }
            else if (act == "Tier:2")
            {
                SetDraftTier(panel, TradeOfferGenerosity.Knausrig);
            }
            else if (act == "Send" || act == "Accept" || act == "Decline")
            {
                string inputReason;
                if (act == "Send" && !ReadDraftControls(panel, out inputReason))
                    _tradeStatus = inputReason;
                else
                    QueueSelectedTrade(act, counterpart);
            }
            else
            {
                _tradeStatus = Text("Unbekannte Handelsaktion", "Unknown trade action");
            }

            RememberDraft(_draftPartner, panel);
            RefreshVanillaDiplomacyUi(panel);
            return true;
        }

        // Only original catalog IDs are allowed. Do not accept malformed
        // or stale Noesis commands as free-text inventory identifiers.
        private static bool TryIconSelection(string command, string prefix, out int index)
        {
            index = -1;
            if (command == null || !command.StartsWith(prefix, StringComparison.Ordinal) || command.Length != prefix.Length + 2)
                return false;
            int good;
            if (!int.TryParse(command.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out good))
                return false;
            index = Array.IndexOf(TradeAllowedGoods, good);
            return index >= 0;
        }

        private static bool TryQuantityPreset(string command, out int amount)
        {
            amount = 0;
            const string prefix = "Amount:Set:";
            if (command == null || !command.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            int parsed;
            if (!int.TryParse(command.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                return false;
            // Only accept the exact five commands emitted by the actual
            //  Noesis markup, not stale '05' or '010' aliases.
            if (!string.Equals(command, prefix + parsed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return false;
            switch (parsed)
            {
                case 5:
                case 10:
                case 25:
                case 100:
                case 500:
                    amount = parsed;
                    return true;
                default:
                    return false;
            }
        }

        // Noesis visual-tree lookup is relatively expensive. Refresh the
        // selection borders only when terms or the panel owner change.
        // Never cache game inventory or player authority in UI state.
        private object TradeElement(object panel, string name)
        {
            if (!Object.ReferenceEquals(panel, _tradePanelOwner))
            {
                _tradePanelOwner = panel;
                _iconRefs.Clear();
                _lastIconSelection = -1;
            }

            object control;
            if (_iconRefs.TryGetValue(name, out control))
                return control;
            control = FindElement(panel, name);
            if (control != null)
                _iconRefs[name] = control;
            return control;
        }

        private void RefreshVanillaIconSelection(object panel)
        {
            // Custom quantities can exceed the former 16/4096 bit stride.
            // Compare the complete tuple; arithmetic packing caused stale
            // selection markers after e.g. a 16-unit amount change.
            if (Object.ReferenceEquals(panel, _tradePanelOwner) && _lastIconSelection >= 0 && _lastWanted == _tradeWantedIndex && _lastOffered == _tradeOfferedIndex && _lastAmount == _tradeRequestedAmount)
                return;
            bool full = true;
            for (int i = 0; i < TradeAllowedGoods.Length; i++)
            {
                int good = TradeAllowedGoods[i];
                string id = good.ToString("00", CultureInfo.InvariantCulture);
                foreach (string side in new[]
                {
                    "Wanted",
                    "Offered"
                }

                )
                {
                    string prefix = "Trade" + side;
                    object control = TradeElement(panel, prefix + id);
                    object marker = TradeElement(panel, prefix + "Marker" + id);
                    if (control == null || marker == null)
                    {
                        full = false;
                        continue;
                    }

                    bool selected = side == "Wanted" ? _tradeWantedIndex == i : _tradeOfferedIndex == i;
                    SetProperty(control, "IsChecked", selected);
                    SetProperty(control, "ToolTip", GoodName(good, 1));
                    SetVisibilityIfPresent(marker, selected);
                }
            }

            object wantedAuto = TradeElement(panel, "TradeWantedAuto");
            object offeredAuto = TradeElement(panel, "TradeOfferedAuto");
            if (wantedAuto == null || offeredAuto == null)
                full = false;
            if (wantedAuto != null)
                SetProperty(wantedAuto, "IsChecked", _tradeWantedIndex == TradeAllowedGoods.Length);
            if (offeredAuto != null)
                SetProperty(offeredAuto, "IsChecked", _tradeOfferedIndex == TradeAllowedGoods.Length);
            foreach (int amount in new[]
            {
                5,
                10,
                25,
                100,
                500
            }

            )
            {
                object mark = TradeElement(panel, "TradeAmountMarker" + amount.ToString(CultureInfo.InvariantCulture));
                if (mark == null)
                    full = false;
                else
                    SetVisibilityIfPresent(mark, _tradeRequestedAmount == amount);
            }

            _lastWanted = _tradeWantedIndex;
            _lastOffered = _tradeOfferedIndex;
            _lastAmount = _tradeRequestedAmount;
            _lastIconSelection = full ? 1 : -1;
        }

        // Pending-counter actions must correspond to this exact visible
        // partner, live saved root, and current game day.  UI cannot confer
        // settlement authority; actual booking retains the separate gate.
        private static bool ValidPendingCounter(ulong id, int trackedPartner, int selectedPartner, int local, int day, TradeRecord counter, TradeRecord root)
        {
            return id >= TradeHistoryArchive.MinimumTradeId && selectedPartner >= 1 && selectedPartner <= 8 && trackedPartner == selectedPartner && local >= 1 && local <= 8 && counter != null && root != null && counter.Id == id && counter.RootId == root.Id && root.Id >= TradeHistoryArchive.MinimumTradeId && root.Id < counter.Id && root.State == TradeDisposition.Superseded && counter.State == TradeDisposition.PendingCounter && counter.Requester == local && counter.Recipient == selectedPartner && root.Requester == local && root.Recipient == selectedPartner && counter.IssuedDay >= 1 && counter.IssuedDay <= day && counter.ExpiryDay >= day;
        }

        private static TradeRecord SelectPendingCounter(TradeHistoryArchive archive, int selected, int local, int day)
        {
            if (archive == null || selected < 1 || selected > 8 || local < 1 || local > 8 || selected == local || day < 1)
                return null;
            TradeRecord chosen = null;
            foreach (TradeRecord candidate in archive.Records)
            {
                if (candidate == null || candidate.Requester != local || candidate.Recipient != selected || candidate.State != TradeDisposition.PendingCounter || candidate.AiToAi || candidate.ExpiryDay < day || candidate.Id >= archive.NextId || chosen != null && candidate.Id <= chosen.Id)
                    continue;
                TradeRecord root = archive.Record(candidate.RootId);
                if (!ValidPendingCounter(candidate.Id, selected, selected, local, day, candidate, root) || root.RootId != 0 || root.AiToAi || root.OfferedGood != candidate.OfferedGood || root.RequestedGood != candidate.RequestedGood || root.IssuedDay != candidate.IssuedDay)
                    continue;
                try
                {
                    candidate.Validate();
                    root.Validate();
                }
                catch (System.IO.InvalidDataException)
                {
                    continue;
                }

                chosen = candidate;
            }

            return chosen;
        }

        private TradeRecord PendingCounterFor(int selected)
        {
            // More than one lord may answer while a messenger is away. The
            // singleton tracker is only a projection of the visible partner;
            // it must never hide another partner's canonical archived counter.
            TradeRecord consent = ConsentDisplayCounter(selected);
            if (consent != null)
                return consent;
            TradeRecord candidate = SelectPendingCounter(_archive, selected, _localPlayer, Math.Max(1, _lastProcessedDay));
            _uiCounterId = candidate == null ? 0 : candidate.Id;
            _uiCounterpart = candidate == null ? 0 : selected;
            return candidate;
        }

        // A DRAFT, never an offered native receipt or committed quote.
        // Game-native goods are not mutated while this preview is drawn.
        private static string DraftPreview(string wanted, string offered, int amount, string wantText, string offerText)
        {
            return wantText + " " + amount.ToString(CultureInfo.InvariantCulture) + " x " + wanted + "\n" + offerText + " " + offered;
        }

        private string GoodLabel(int index)
        {
            if (index >= TradeAllowedGoods.Length)
                return "?  " + Text("Automatisch wählen", "Choose automatically");
            if (index < 0)
                return Text("Unbekannt", "Unknown");
            int id = TradeAllowedGoods[index];
            return GoodName(id, 1);
        }

        private void RefreshTradePanel(object panel, int selectedPlayer)
        {
            object tradeTab = FindElement(panel, "TradeTab");
            object tradeLabel = FindElement(panel, "TradeTabLabel");
            object tradePanel = FindElement(panel, "TradePanel");
            object tradeModal = FindElement(panel, "TradeModal");
            // The portrait only decides whom Open will address. Once shown,
            // the modal owns its partner until Close or explicit navigation.
            bool portraitValid = _mapActive && !_mapDisabled && selectedPlayer > 0 && selectedPlayer <= 8 && selectedPlayer != _localPlayer && _roster.Contains(selectedPlayer);
            string portraitPermission = string.Empty;
            bool portraitEligible = portraitValid && CanTradeWith(selectedPlayer, out portraitPermission);
            int visiblePartner = _tradeTab ? _draftPartner : selectedPlayer;
            bool modalValid = _mapActive && !_mapDisabled && visiblePartner > 0 && visiblePartner <= 8 && visiblePartner != _localPlayer && _roster.Contains(visiblePartner);
            string permission = string.Empty;
            bool eligible = modalValid && CanTradeWith(visiblePartner, out permission);
            if (_tradeTab && !eligible)
            {
                RememberDraft(_draftPartner, panel);
                CloseTradeWindow(panel);
            }

            SetVisibilityIfPresent(tradeTab, portraitValid);
            SetVisibilityIfPresent(tradeLabel, portraitValid);
            SetVisibilityIfPresent(tradePanel, eligible && _tradeTab);
            SetVisibilityIfPresent(tradeModal, eligible && _tradeTab);
            if (tradeTab != null)
            {
                SetProperty(tradeTab, "IsChecked", portraitEligible && _tradeTab && visiblePartner == selectedPlayer);
                SetProperty(tradeTab, "IsEnabled", portraitEligible);
                SetProperty(tradeTab, "ToolTip", portraitEligible ? Text("Eigenes Handelsfenster öffnen.", "Open trade window.") : portraitPermission);
            }

            if (!eligible || !_tradeTab)
                return;
            int previousPartner, nextPartner;
            bool canPrevious = FindNextTradingPartner(visiblePartner, false, out previousPartner);
            bool canNext = FindNextTradingPartner(visiblePartner, true, out nextPartner);
            object previous = TradeElement(panel, "TradePartnerPrevious");
            object next = TradeElement(panel, "TradePartnerNext");
            if (previous != null)
                SetProperty(previous, "IsEnabled", canPrevious);
            if (next != null)
                SetProperty(next, "IsEnabled", canNext);
            // This guard is a release fence, not a UI shortcut to Native goods.
            // Reuse one snapshot for button state and player-facing status.
            bool canTrade = eligible && NativeAuthorized();
            // original incoming offer: an ORIGINAL incoming canonical offer has a distinct
            // immutable ConsentId. A legacy mailbox messenger from the same
            // ally must not gray out its Accept/Decline/Counter buttons.
            // This is ONLY a visibility/enablement decision. New proposals
            // remain blocked by old pending, and HandleConsentUi still owns
            // the exact NativeAuthorized/Singleplayer/engine lease checks.
            bool waiting = HasPendingTrade(visiblePartner) &&
                PendingConsent(visiblePartner, true) == null;
            InitializeDraftControls(panel);
            string inputReason;
            bool inputValid = ReadDraftControls(panel, out inputReason);
            RefreshVanillaIconSelection(panel);
            bool sameGoods = _tradeWantedIndex < TradeAllowedGoods.Length && _tradeOfferedIndex < TradeAllowedGoods.Length && _tradeWantedIndex == _tradeOfferedIndex;
            SetVisibilityIfPresent(FindElement(panel, "AllianceDetailsPanel"), false);
            SetVisibilityIfPresent(FindElement(panel, "AllianceOverviewPanel"), false);
            SetTextIfPresent(panel, "TradeWantedValue", GoodLabel(_tradeWantedIndex));
            SetTextIfPresent(panel, "TradeOfferedValue", GoodLabel(_tradeOfferedIndex));
            SetTextIfPresent(panel, "TradeAmountValue", Text("Gewählt: ", "Selected: ") + _tradeRequestedAmount.ToString(CultureInfo.InvariantCulture));
            SetTextIfPresent(panel, "TradeToneValue", _tradeGenerosity == TradeOfferGenerosity.Grosszuegig ? Text("Großzügig", "Generous") : _tradeGenerosity == TradeOfferGenerosity.Fair ? Text("Fair", "Fair") : Text("Knausrig", "Stingy"));
            // The radio group survives panel re-use, whereas the real
            // selected tier resets on a new map. Always synchronize UI state.
            object generous = FindElement(panel, "TradeTierGenerous");
            object fair = FindElement(panel, "TradeTierFair");
            object stingy = FindElement(panel, "TradeTierStingy");
            if (generous != null)
                SetProperty(generous, "IsChecked", _tradeGenerosity == TradeOfferGenerosity.Grosszuegig);
            if (fair != null)
                SetProperty(fair, "IsChecked", _tradeGenerosity == TradeOfferGenerosity.Fair);
            if (stingy != null)
                SetProperty(stingy, "IsChecked", _tradeGenerosity == TradeOfferGenerosity.Knausrig);
            SetTextIfPresent(panel, "TradeTitle", Text("Tauschhandel mit ", "Barter with ") + PlayerName(visiblePartner));
            SetTextIfPresent(panel, "TradePreview", DraftPreview(GoodLabel(_tradeWantedIndex), GoodLabel(_tradeOfferedIndex), _tradeRequestedAmount, Text("Wunsch:", "Want:"), Text("Biete:", "Offer:")));
            SetTextIfPresent(panel, "TradeCloseLabel", Text("Schließen", "Close"));
            // Economic preview is a read-only observation of *current*
            // native inventories and mod-adjusted original market prices.
            // The actual quote still has separate last-second admission.
            SetTextIfPresent(panel, "TradeEconomyPreview", ReadTradePreview(visiblePartner, panel));
            // visible trade panel: when the ORIGINAL published-API release denies Native
            // goods, do NOT show "Send barter proposal" on a disabled
            // button. This read-only caption cannot authorize any goods.
            SetTextIfPresent(panel, "TradeSendCaption",
                canTrade ? Text("ANFRAGE SENDEN", "SEND PROPOSAL")
                         : Text("VORSCHAU – NICHT SENDEN", "PREVIEW ONLY – NO SEND"));
            TradeRecord currentCounter = PendingCounterFor(visiblePartner);
            bool hasCounter = currentCounter != null;
            object draft = TradeElement(panel, "TradeDraftControls");
            if (draft != null)
            {
                SetProperty(draft, "IsEnabled", !waiting && !hasCounter);
                SetNumericProperty(draft, "Opacity", waiting || hasCounter ? 0.55d : 1d);
            }

            string counterText = hasCounter ? Text("Gegenangebot: ", "Counter: ") + currentCounter.OfferedAmount + " " + GoodName(currentCounter.OfferedGood, currentCounter.OfferedAmount) + " / " + currentCounter.RequestedAmount + " " + GoodName(currentCounter.RequestedGood, currentCounter.RequestedAmount) + (canTrade ? "" : Text(" (Handel gesperrt)", " (trade locked)")) : string.Empty;
            SetTextIfPresent(panel, "TradeStatus", waiting ? PendingTradeStatus(visiblePartner) : hasCounter ? counterText : !inputValid ? inputReason : sameGoods ? Text("Wunschware und Gegenware müssen verschieden sein.", "Requested and offered goods must differ.") : string.IsNullOrEmpty(_tradeStatus) ? (canTrade ? Text("Wählt die Waren und die Menge für Euer Angebot.", "Choose the goods and quantity for your offer.") : Text("Handel ist für diese Partie noch nicht verfügbar.", "Trading is not yet available in this match.")) : _tradeStatus);
#if DIPLOMACY_PRIVATE_TESTS
            // interactive gui test interactive GUI test: offer an ACTIONABLE instruction
            // instead of a vague locked-trade caption when the genuine
            // original SaveSaveGame receipt has not been promoted yet.
            // This is UI text ONLY: the original save hook, active map and
            // NativeAuthorized() retain their existing fail-closed guards.
            // Never show this instruction in normal published builds.
            if (!canTrade && !waiting && !hasCounter &&
                _mapActive && !_mapDisabled &&
                _coherentSaveGeneration != _sessionGeneration)
            {
                SetTextIfPresent(panel, "TradeStatus",
                    Text("TESTMODUS: Erst die Partie im Spielmenü speichern, danach Handel erneut öffnen. Bis dahin keine Warenbuchung.",
                         "TEST MODE: Save this match in the original game menu, then reopen Trade. No goods move before that."));
            }
#endif
            // A lightweight, read-only projection of this game's EXISTING
            // checked schema14 trade archive. Never infer native settlement
            // merely from an AI decision, journal prepare or UI click.
            SetTextIfPresent(panel, "TradeChronicle", DiplomacyChronicle.Latest(_archive, visiblePartner, Math.Max(1, _lastProcessedDay), Text("Deutsch", "English") == "Deutsch", id => PlayerName(id)));
            object submit = FindElement(panel, "TradeSend");
            // Hide Send and its caption while the same lane displays a counteroffer.
            SetVisibilityIfPresent(submit, !hasCounter);
            SetVisibilityIfPresent(FindElement(panel, "TradeSendCaption"), !hasCounter);
            if (submit != null)
            {
                SetProperty(submit, "IsEnabled", canTrade && inputValid && !sameGoods && !hasCounter && !waiting);
                SetProperty(submit, "ToolTip", canTrade && inputValid && !sameGoods && !hasCounter && !waiting ? Text("Anfrage mit ausgewählten Warensymbolen senden.", "Send a barter proposal with the selected goods.") : waiting ? PendingTradeStatus(visiblePartner) : !inputValid ? inputReason : Text("Der Handel ist noch gesperrt oder die Waren sind gleich.", "Trading is not yet available, or both goods are the same."));
            }

            object counterAccept = FindElement(panel, "TradeCounterAccept");
            object counterDecline = FindElement(panel, "TradeCounterDecline");
            bool canAnswer = canTrade && hasCounter && !waiting;
            SetVisibilityIfPresent(counterAccept, hasCounter);
            SetVisibilityIfPresent(counterDecline, hasCounter);
            SetVisibilityIfPresent(FindElement(panel, "TradeCounterAcceptLabel"), hasCounter);
            SetVisibilityIfPresent(FindElement(panel, "TradeCounterDeclineLabel"), hasCounter);
            if (counterAccept != null)
                SetProperty(counterAccept, "IsEnabled", canAnswer);
            if (counterDecline != null)
                SetProperty(counterDecline, "IsEnabled", canAnswer);
            RefreshConsentControls(panel, visiblePartner);
            // Read only actual ORIGINAL Noesis panel state after the
            // disabled Native Send button and localized status were drawn.
            // A visible panel or synthesized UI action is not a human
            // authenticated consent or an original Native quote grant.
            ObserveActualVisibleTradePanel(panel, visiblePartner, canTrade);
        }
    }
}
