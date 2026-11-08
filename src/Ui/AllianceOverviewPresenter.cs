using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrusaderDE;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private int _allianceSelectedPlayer;
        private long _allianceUiGeneration = -1;
        // These are the same name sources used by the native Allies panel.
        // computer_register identifies the displayed computer profile, not a slot.
        private string PlayerName(int player)
        {
            if (player < 1 || player > 8)
                return Text("Unbekannter Herrscher", "Unknown ruler");
            GameData game = GameData.Instance;
            EngineInterface.PlayState live = game == null ? null : game.lastGameState;
            string name = PlayerBaseName(live, player);
            // Use the session roster, including defeated rulers: the surviving
            // names must not lose their disambiguator when a namesake dies.
            bool duplicate = _roster.Any(other => other != player && other >= 1 && other <= 8 && string.Equals(name, PlayerBaseName(live, other), StringComparison.OrdinalIgnoreCase));
            return duplicate ? name + " · " + string.Format(CultureInfo.InvariantCulture, Text("Spieler {0}", "Player {0}"), player) : name;
        }

        private string PlayerBaseName(EngineInterface.PlayState live, int player)
        {
            string name = live == null ? null : live.is_skirmish_player(player) ? OnScreenText.getComputerName(live.computer_register[player], live.computer_names[player]) : Platform_Multiplayer.Instance.getPlayerName(player);
            return string.IsNullOrWhiteSpace(name) ? Text("Unbekannter Herrscher", "Unknown ruler") : name.Trim();
        }

        private bool TryAllianceMember(int player, out CoalitionState coalition, out RelationState relation)
        {
            coalition = null;
            relation = null;
            string id;
            return player != _localPlayer && player >= 1 && player <= 8 && _mapActive && !_mapDisabled && IsAlivePlayer(player) && _playerCoalition.TryGetValue(player, out id) && _coalitions.TryGetValue(id, out coalition) && _relations.TryGetValue(id, out relation) && relation.Allied && relation.ExtendedState != null && coalition.MemberPlayerIds.Contains(player);
        }

        private bool SelectAllianceMember(object panel, int player)
        {
            CoalitionState coalition;
            RelationState relation;
            if (!TryAllianceMember(player, out coalition, out relation))
                return false;
            int slot = _visiblePlayers.IndexOf(player);
            if (slot < 0)
                return false;
            _allianceSelectedPlayer = player;
            _allianceUiGeneration = _sessionGeneration;
            _selectedVisibleIndex = slot;
            _currentAllyField.SetValue(panel, slot);
            _updateAlliesTarget.Invoke(panel, null);
            return true;
        }

        private void OpenAllianceOverview(object panel, int selectedPlayer)
        {
            _overviewVisible = true;
            if (SelectAllianceMember(panel, selectedPlayer) || SelectAllianceMember(panel, _allianceSelectedPlayer))
                return;
            foreach (int player in _visiblePlayers)
                if (SelectAllianceMember(panel, player))
                    return;
        }

        private void RefreshAllianceManagement(object panel)
        {
            try
            {
                RefreshAllianceManagementCore(panel);
            }
            catch (Exception exception)
            {
                DegradeUiRefresh("alliance_refresh", panel, exception);
            }
        }

        private object AllianceUiElement(object panel, string name)
        {
            object element = FindElement(panel, name);
            if (element == null)
                throw new MissingMemberException("Alliance view control is missing: " + name);
            return element;
        }

        private void SetAllianceText(object panel, string name, string text)
        {
            object element = AllianceUiElement(panel, name);
            SetProperty(element, "Text", text);
            SetProperty(element, "ToolTip", text);
        }

        private void RefreshAllianceManagementCore(object panel)
        {
            int[] members = _coalitions.Values.SelectMany(c => c.MemberPlayerIds).Distinct().OrderBy(p => p).Where(p =>
            {
                CoalitionState c;
                RelationState r;
                return TryAllianceMember(p, out c, out r);
            }).ToArray();
            if (_allianceUiGeneration != _sessionGeneration || !members.Contains(_allianceSelectedPlayer))
            {
                _allianceSelectedPlayer = members.FirstOrDefault();
                _allianceUiGeneration = _sessionGeneration;
            }

            int day = Math.Max(0, _lastProcessedDay);
            SetAllianceText(panel, "AllianceSelectedRuler", Text("Euer Bündnis", "Your alliance"));
            SetAllianceText(panel, "AllianceOverviewTitle", string.Format(CultureInfo.InvariantCulture, Text("Verbündete: {0} · Vasallen: {1}", "Allies: {0} · Vassals: {1}"), members.Length, members.Count(p =>
            {
                CoalitionState c;
                RelationState r;
                return TryAllianceMember(p, out c, out r) && IsFealtyCounterpart(r.ExtendedState, p) && DiplomacyVassalage.OverlordOf(r.ExtendedState) == _localPlayer;
            })));
            SetAllianceText(panel, "AllianceOverviewHeader", Text("Herrscher", "Ruler"));
            SetAllianceText(panel, "AllianceRoleHeader", Text("Rolle", "Role"));
            SetAllianceText(panel, "AllianceDutyHeader", Text("Pflichten und Befehle", "Obligations and orders"));
            for (int row = 0; row < OverviewRowCount; row++)
            {
                string suffix = (row + 1).ToString(CultureInfo.InvariantCulture);
                object button = AllianceUiElement(panel, "AllianceMember" + suffix);
                bool present = row < members.Length;
                SetVisibilityIfPresent(AllianceUiElement(panel, "AllianceRow" + suffix), present);
                if (!present)
                    continue;
                int player = members[row];
                CoalitionState coalition;
                RelationState relation;
                if (!TryAllianceMember(player, out coalition, out relation))
                    continue;
                string role = IsFealtyCounterpart(relation.ExtendedState, player) ? VassalRoleText(relation.ExtendedState) : Text("Verbündeter", "Ally");
                string duty = AllianceObligation(relation, day, player);
                string orderStatus = AllyOrderStatusText(player);
                if (!string.IsNullOrEmpty(orderStatus))
                    duty = orderStatus + " · " + duty;
                if (button != null)
                {
                    SetProperty(button, "IsEnabled", true);
                    SetProperty(button, "IsChecked", player == _allianceSelectedPlayer);
                    SetProperty(button, "CommandParameter", "Diplomacy:AllianceSelect:" + player.ToString(CultureInfo.InvariantCulture) + ":" + _sessionGeneration.ToString(CultureInfo.InvariantCulture));
                    SetProperty(button, "ToolTip", PlayerName(player) + " · " + role + " · " + duty);
                }

                SetAllianceText(panel, "AllianceOverviewRow" + suffix, PlayerName(player));
                SetAllianceText(panel, "AllianceRole" + suffix, role);
                SetAllianceText(panel, "AllianceDuty" + suffix, duty);
                int order = GameData.Instance.lastGameState.skirmish_order[player];
                SetProperty(AllianceUiElement(panel, "AllianceOrder" + suffix), "Source", order == 1 ? GetAlliesGoodsIcon(-3) : order == 2 ? GetAlliesGoodsIcon(-4) : null);
            }

            CoalitionState selectedCoalition;
            RelationState selectedRelation;
            bool selected = TryAllianceMember(_allianceSelectedPlayer, out selectedCoalition, out selectedRelation);
            Dictionary<int, int> selectedStock = selected ? DemandStockSnapshot(selectedRelation.Demand) : null;
            bool demandReady = selected && CanFulfilDemand(selectedRelation.Demand, selectedStock);
            string summary = HasPendingGoodsCommand ? PendingGoodsCommandStatusText() : selected ? PlayerName(_allianceSelectedPlayer) + " · " + AllianceObligation(selectedRelation, day, _allianceSelectedPlayer) : Text("Noch keine Verbündeten. Waffenruhe und Handel begründen kein Bündnis.", "No allies yet. A truce or trade pact does not establish an alliance.");
            if (!HasPendingGoodsCommand)
            {
                MainViewModel visibleView = MainViewModel.Instance;
                bool overviewVisible = _overviewVisible && visibleView != null && visibleView.AlliesPanelVisible && visibleView.Allies_MainViewVis;
                string goodsOutcome = ShownGoodsOutcome(selected ? _allianceSelectedPlayer : 0, overviewVisible);
                if (!string.IsNullOrEmpty(goodsOutcome))
                    summary = goodsOutcome + " · " + summary;
                else if (selected)
                {
                    if (overviewVisible)
                        NoteAllyOrderStatusShown(_allianceSelectedPlayer);
                    string orderStatus = AllyOrderStatusText(_allianceSelectedPlayer);
                    if (!string.IsNullOrEmpty(orderStatus))
                        summary = orderStatus + " · " + summary;
                }
            }

            if (selected && IsFealtyCounterpart(selectedRelation.ExtendedState, _allianceSelectedPlayer))
                summary += " · " + TributeLedgerText(selectedRelation.ExtendedState);
            SetAllianceText(panel, "AllianceOverviewFooter", summary);
            object footer = AllianceUiElement(panel, "AllianceOverviewFooter");
            if (footer != null)
                SetProperty(footer, "ToolTip", summary);
            // "Decline" kam dazu - eine Forderung muss ablehnbar sein.
            // Der Knopf erscheint nur, solange eine offene Forderung da ist.
            string[] actions =
            {
                "Send",
                "RequestGoods",
                "Fulfill",
                "Orders",
                "Fealty",
                "Break",
                "Decline"
            };
            string[] labels =
            {
                Text("Güter senden", "Send goods"),
                Text("Güter erbitten", "Request goods"),
                Text("Forderung erfüllen", "Fulfil demand"),
                Text("Befehle", "Orders"),
                Text("Lehnsverhältnis", "Fealty"),
                Text("Bündnis lösen", "End alliance"),
                Text("Forderung ablehnen", "Decline demand")
            };
            if (selected && IsFealtyCounterpart(selectedRelation.ExtendedState, _allianceSelectedPlayer))
            {
                bool localOverlord = DiplomacyVassalage.OverlordOf(selectedRelation.ExtendedState) == _localPlayer;
                labels[1] = localOverlord ? Text("Güter anfordern", "Request supplies") : labels[1];
                labels[4] = localOverlord ? Text("Vasallen freilassen", "Release vassal") : Text("Gleichstellung fordern", "Request equal standing");
            }

            for (int index = 0; index < actions.Length; index++)
            {
                object action = AllianceUiElement(panel, "Alliance" + actions[index]);
                object label = AllianceUiElement(panel, "Alliance" + actions[index] + "Label");
                bool enabled = selected && !HasPendingGoodsCommand && (actions[index] != "Fulfill" || demandReady) && (actions[index] != "RequestGoods" || CanRequestGoodsFrom(_allianceSelectedPlayer)) && (actions[index] != "Fealty" || !DiplomacyVassalActions.HasPending(selectedRelation.ExtendedState) && (DiplomacyVassalage.VassalOf(selectedRelation.ExtendedState) != _localPlayer || DiplomacyVassalage.CanRequestEquality(selectedRelation.ExtendedState, day)));
                if (actions[index] == "Decline")
                    enabled = selected && !HasPendingGoodsCommand && selectedRelation.Demand != null;
                // Ablehnen zeigt sich nur bei offener Forderung: ein Knopf ohne
                // Gegenstand verwirrt mehr, als er nuetzt.
                bool show = (actions[index] != "Fealty" || selected && IsFealtyCounterpart(selectedRelation.ExtendedState, _allianceSelectedPlayer)) && (actions[index] != "Decline" || selected && selectedRelation.Demand != null);
                SetVisibilityIfPresent(action, show);
                SetVisibilityIfPresent(label, show);
                if (action != null)
                {
                    SetProperty(action, "IsEnabled", enabled);
                    SetProperty(action, "ToolTip", selected && actions[index] == "Fulfill" && selectedRelation.Demand != null ? DiplomacyDemandText(selectedRelation.Demand) : selected && actions[index] == "RequestGoods" && !CanRequestGoodsFrom(_allianceSelectedPlayer) ? Text("Als Vasall könnt Ihr keine Güter fordern.", "As a vassal, you cannot request goods.") : selected && actions[index] == "Fealty" ? VassalPrimaryHint(selectedRelation.ExtendedState) : labels[index]);
                    SetProperty(action, "CommandParameter", "Diplomacy:AllianceAction:" + actions[index] + ":" + _allianceSelectedPlayer.ToString(CultureInfo.InvariantCulture) + ":" + _sessionGeneration.ToString(CultureInfo.InvariantCulture));
                }

                if (label != null)
                {
                    SetProperty(label, "Text", labels[index]);
                    SetNumericProperty(label, "Opacity", enabled ? 1.0 : 0.48);
                }
            }
        }

        private string ShownGoodsOutcome(int player, bool visible)
        {
            string outcome = GoodsOutcomeText(player);
            if (string.IsNullOrEmpty(outcome) && player != 0)
            {
                player = 0;
                outcome = GoodsOutcomeText(0);
            }

            if (visible && !string.IsNullOrEmpty(outcome))
            {
                NoteGoodsOutcomeShown(player);
                return GoodsOutcomeText(player);
            }

            return outcome;
        }

        private string AllianceObligation(RelationState relation, int day, int player)
        {
            DiplomacyRelationState state = relation.ExtendedState;
            var obligations = new List<string>();
            if (relation.Demand != null)
                obligations.Add(DiplomacyDemandText(relation.Demand));
            if (IsFealtyCounterpart(state, player) && DiplomacyTribute.IsActive(state))
            {
                string direction = DiplomacyVassalage.VassalOf(state) == _localPlayer ? Text("Ihr zahlt", "You pay") : Text("Ihr erhaltet", "You receive");
                string due = state.TributeMissedCycles > 0 ? Text("Rückstand; täglicher Zahlungsversuch", "Arrears; daily payment retry") : DiplomacyTribute.IsDue(state, day) ? GraceText(DiplomacyTribute.DaysUntilGraceDeadline(state, day)) : DurationText(DiplomacyTribute.DaysUntilDue(state, day));
                obligations.Add(direction + " " + state.TributeAmount.ToString(CultureInfo.InvariantCulture) + " " + GoodName(state.TributeGoodId, state.TributeAmount) + (state.TributeGoodId == DiplomacyTribute.GoldGoodId ? " · " + Text("Waren statt Gold", "Goods accepted") : "") + " · " + due);
            }

            if (state.RestitutionOutstandingValue > 0m)
                obligations.Add(Text("Wiedergutmachung offen", "restitution due"));
            return obligations.Count == 0 ? Text("Keine offene Pflicht", "No obligation due") : string.Join(" · ", obligations);
        }

        private string TributeLedgerText(DiplomacyRelationState state)
        {
            if (state == null || !DiplomacyTribute.IsActive(state))
                return string.Empty;
            return string.Format(CultureInfo.InvariantCulture, Text("Alle {0} Tage · Zahlungen: {1} · Rückstände: {2} · Offen: {3}", "Every {0} days · Payments: {1} · Arrears: {2} · Open: {3}"), state.TributeIntervalDays, state.TributePaidCycles, state.TributeDefaults, state.TributeMissedCycles);
        }

        /// <summary>
        /// Weist die offene Forderung des ausgewaehlten Mitglieds zurueck.
        /// </summary>
        /// <remarks>
        /// Benutzervorgabe dieselbe Wirkung wie eine verstrichene
        /// Frist. Ein Ultimatum beendet damit das Buendnis, deshalb fragt es
        /// vorher nach - ein Fehlklick soll keine lange gepflegte Allianz
        /// zerstoeren. Die gewoehnliche Forderung geht ohne Rueckfrage.
        /// </remarks>
        private void HandleDeclineDemand(object panel, int player)
        {
            // KEIN RequireNativeDiplomacySection: das ist der Klickweg, und der
            // UI-Faden haelt den Motor-Monitor nie (PlayerName wirft sogar,
            // wenn er gehalten wird). Der Aufruf stand hier bis und
            // haette jeden Druck auf den Knopf mit einer Ausnahme beendet.
            // Abgelehnt wird ausschliesslich im Modell, unter _gate aus
            // HandleButton - nativer Zustand wird dabei nicht angefasst.
            CoalitionState coalition;
            RelationState relation;
            if (!TryAllianceMember(player, out coalition, out relation) || relation.Demand == null)
                return;
            // Benutzervorgabe eine abgelehnte Bitte kostet Ansehen,
            // aber niemals das Buendnis - auch dann nicht, wenn es die zweite
            // Mahnung ist. Deshalb gibt es hier keinen Sonderweg und keine
            // Rueckfrage mehr; wer ablehnt, weiss was er tut.
            DeclineDemandNow(coalition, relation, player);
        }

        /// <summary>
        /// Eine Forderung ablehnen, ohne dass ein Buendnis bestehen muss.
        /// </summary>
        /// <remarks>
        /// `HandleDeclineDemand` geht ueber `TryAllianceMember`
        /// und findet damit nur Verbuendete. Bedingungen aus einer Verhandlung
        /// (Courtship/TradeProof/AllianceTerms) stellt aber ein Fuerst, mit dem
        /// noch gar kein Buendnis besteht - genau die waren unablehnbar. Dieser
        /// Weg kommt aus der Detailansicht und nimmt jeden Fuersten, zu dem eine
        /// Beziehung gefuehrt wird.
        /// </remarks>
        private void HandleDeclineDemandForPlayer(int player)
        {
            string id;
            RelationState relation;
            CoalitionState coalition;
            if (!_playerCoalition.TryGetValue(player, out id) || !_relations.TryGetValue(id, out relation) || !_coalitions.TryGetValue(id, out coalition) || relation.ExtendedState == null || relation.Demand == null)
                return;
            DeclineDemandNow(coalition, relation, player);
        }

        private void DeclineDemandNow(CoalitionState coalition, RelationState relation, int player)
        {
            int day = Math.Max(0, _lastProcessedDay);
            bool ultimatum = DiplomacyAgreementRules.DemandIsUltimatum(relation);
            // Der Zielwert entsteht erst in der Koalitionsbewertung des Taktes.
            // Direkt nach dem Laden eines Spielstands steht die gespeicherte
            // Forderung schon da, der Zielwert aber noch auf 0 - und
            // SyncGoodwill wirft darauf. Ein Klick in genau diesem Augenblick
            // ist kein Spielerfehler, also wird hier aufgerundet statt geworfen:
            // Goodwill setzt der naechste Takt ohnehin neu.
            int target = Math.Max(1, coalition.Target);
            if (!DiplomacyAgreementRules.DeclineDemand(relation, day, target))
                return;
            Log("demand", "coalition=" + coalition.CoalitionId + "|status=declined|player=" + player.ToString(CultureInfo.InvariantCulture) + "|ultimatum=" + BoolText(ultimatum));
            CancelPendingGoodsCommand("demand_declined");
            ShowMessage(PlayerName(player) + ": " + Text("Die Forderung wurde zurückgewiesen.", "The demand has been rejected."));
        }

        private bool TryHandleAllianceUiCommand(object panel, string command)
        {
            if (!command.StartsWith("Diplomacy:Alliance", StringComparison.Ordinal))
                return false;
            // The target and map generation travel with the displayed control.
            // A dead/reordered member or a stale control cannot retarget an action.
            if (!_overviewVisible || !ReferenceEquals(panel, CurrentAlliesPanel()))
                return true;
            string[] parts = command.Split(':');
            bool select = parts.Length == 4 && parts[1] == "AllianceSelect";
            bool action = parts.Length == 5 && parts[1] == "AllianceAction";
            if (!select && !action)
                return true;
            int player;
            long generation;
            if (!int.TryParse(parts[parts.Length - 2], NumberStyles.None, CultureInfo.InvariantCulture, out player) || !long.TryParse(parts[parts.Length - 1], NumberStyles.None, CultureInfo.InvariantCulture, out generation) || generation != _sessionGeneration)
                return true;
            CoalitionState coalition;
            RelationState relation;
            if (!TryAllianceMember(player, out coalition, out relation))
                return true;
            if (select)
            {
                CancelPendingGoodsCommand("alliance_selection");
                if (!SelectAllianceMember(panel, player))
                    return true;
                RefreshAllianceManagement(panel);
                return true;
            }

            if (player != _allianceSelectedPlayer)
                return true;
            string actionName = parts[2];
            if (actionName == "Fulfill")
            {
                if (relation.Demand != null)
                    QueueGoodsCommand(panel, player, 0, 0, relation.Demand);
            }
            else if (actionName == "Decline")
                HandleDeclineDemand(panel, player);
            else if (actionName == "Fealty")
                HandleFealtyAction(player);
            else if (actionName == "Break")
                ConfirmPlayerBreakOne(player);
            else if (actionName == "RequestGoods")
            {
                if (!CanRequestGoodsFrom(player))
                    return true;
                BeginGoodsRequest(panel, player, true);
                return true;
            }
            else if (actionName == "Send" || actionName == "Orders")
            {
                int slot = _visiblePlayers.IndexOf(player);
                if (slot < 0)
                    return true;
                CancelPendingGoodsCommand("alliance_action");
                _pendingGoodsTarget = 0;
                _selectedVisibleIndex = slot;
                _currentAllyField.SetValue(panel, slot);
                _overviewVisible = false;
                _updateAlliesTarget.Invoke(panel, null);
                RefreshVanillaDiplomacyUi(panel);
                _buttonTarget.Invoke(panel, new object[] { actionName });
                return true;
            }

            RefreshAllianceManagement(panel);
            return true;
        }

        private void HandleFealtyAction(int player)
        {
            string id;
            RelationState relation;
            if (!_playerCoalition.TryGetValue(player, out id) || !_relations.TryGetValue(id, out relation))
                return;
            string stamp = FealtyActionStamp(relation);
            QueueFealtyCommand(CurrentAlliesPanel(), player, delegate
            {
                HandleFealtyActionNow(player);
            }, null, delegate
            {
                string currentId;
                RelationState current;
                return _playerCoalition.TryGetValue(player, out currentId) && currentId == id && _relations.TryGetValue(id, out current) && FealtyActionStamp(current) == stamp;
            });
        }

        private static string FealtyActionStamp(RelationState relation)
        {
            DiplomacyRelationState s = relation.ExtendedState;
            if (s == null)
                return string.Empty;
            // Stable action identity, not reference identity: a journal rollback
            // may clone the same contract without changing the user's request.
            return string.Join(":", new[] { relation.Allied.ToString(), s.Agreement.ToString(), s.VassalRole.ToString(), DiplomacyVassalage.VassalOf(s).ToString(CultureInfo.InvariantCulture), DiplomacyVassalage.OverlordOf(s).ToString(CultureInfo.InvariantCulture), s.SubmissionOfferPlayerId.ToString(CultureInfo.InvariantCulture), s.SubmissionOfferCreatedDay.ToString(CultureInfo.InvariantCulture), s.SubmissionOfferOrigin.ToString(), s.SubmissionOfferAccepted.ToString(), s.SubmissionOfferTributeAmount.ToString(CultureInfo.InvariantCulture) });
        }

        private void HandleFealtyActionNow(int player)
        {
            RequireNativeDiplomacySection();
            if (!IsAlivePlayer(_localPlayer) || !IsAlivePlayer(player))
                return;
            if (TryHandleVassalPrimary(player))
                return;
            string id;
            RelationState relation;
            CoalitionState coalition;
            if (!_playerCoalition.TryGetValue(player, out id) || !_relations.TryGetValue(id, out relation) || !_coalitions.TryGetValue(id, out coalition) || relation.ExtendedState == null)
                return;
            if (DiplomacyVassalage.IsActive(relation.ExtendedState) && !IsFealtyCounterpart(relation.ExtendedState, player))
            {
                ShowMessage(PlayerName(player) + " · " + string.Format(CultureInfo.InvariantCulture, Text("Dieser Bündnispartner ist an diesem Lehnsverhältnis nicht beteiligt. Vasall: {0}; Lehnsherr: {1}.", "This ally is not a party to this fealty. Vassal: {0}; overlord: {1}."), PlayerName(DiplomacyVassalage.VassalOf(relation.ExtendedState)), PlayerName(DiplomacyVassalage.OverlordOf(relation.ExtendedState))));
                return;
            }

            int day = Math.Max(0, _lastProcessedDay);
            if (DiplomacySubmission.HasOffer(relation.ExtendedState))
            {
                if (ReviewSubmissionOffer(coalition, relation, day))
                    AskSubmissionOffer(coalition, relation);
                return;
            }

            int lord;
            TryLordIdentity(player, out lord);
            int band = 0;
            if (lord != 0 && !TrySubmissionPressureBand(player, lord, day, out band))
            {
                ShowMessage(PlayerName(player) + ": " + Text("Die militärische Lage wird noch geprüft.", "The military situation is still being assessed."));
                return;
            }

            DiplomacySubmissionAssessment assessment = DiplomacySubmission.Assess(relation, day, lord, coalition.MemberPlayerIds.Length, CanJoinAlliance(id), band, DiplomacySubmissionOfferOrigin.PlayerDemand);
            if (assessment.Availability == DiplomacySubmissionAvailability.OfferPending)
            {
                AskSubmissionOffer(coalition, relation);
                return;
            }

            if (!assessment.CanAttempt)
            {
                ShowMessage(PlayerName(player) + ": " + FealtyAvailabilityText(assessment.Availability, relation, day));
                return;
            }

            DiplomacySubmissionAttemptResult result = DiplomacySubmission.TryAttempt(relation, player, lord, TributeQuoteForPlayer(player), day, _mapFingerprint, DiplomacySubmissionOfferOrigin.PlayerDemand, band);
            if (result == DiplomacySubmissionAttemptResult.Offered)
                PresentNewSubmissionOffer(coalition, relation);
            else if (result == DiplomacySubmissionAttemptResult.Refused)
            {
                Log("vassalage", "coalition=" + id + "|status=submission_demand_refused|lord=" + lord.ToString(CultureInfo.InvariantCulture));
                ShowMessage(PlayerName(player) + ": " + Text("Forderung abgelehnt.", "Demand refused.") + " " + Text("Eine erneute Forderung ist möglich — ", "You may demand submission again — ") + DurationText(relation.ExtendedState.SubmissionNextDemandDay - day));
            }
        }

        private string FealtyAvailabilityText(DiplomacySubmissionAvailability availability, RelationState relation, int day)
        {
            switch (availability)
            {
                case DiplomacySubmissionAvailability.ActiveFealty:
                    return VassalRoleText(relation.ExtendedState);
                case DiplomacySubmissionAvailability.OfferCommitting:
                    return Text("Die angenommene Unterwerfung wird noch vollzogen.", "The accepted submission is still being completed.");
                case DiplomacySubmissionAvailability.OtherDecisionPending:
                    return Text("Zuerst muss das bereits offene Angebot entschieden werden.", "The existing offer must be resolved first.");
                case DiplomacySubmissionAvailability.CoalitionHasMultipleMembers:
                    return Text("Dieser Fürst gehört einem gemeinsamen KI-Team an. Einzelne Teammitglieder können sich nicht unterwerfen.", "This ruler belongs to a shared AI team. Individual team members cannot submit.");
                case DiplomacySubmissionAvailability.EqualAlliance:
                    return Text("Ihr seid gleichberechtigte Verbündete. Ein Bündnis begründet keine Lehnspflicht.", "You are equal allies. An alliance does not create fealty.");
                case DiplomacySubmissionAvailability.ActiveTreaty:
                    return Text("Das laufende Abkommen verhindert ein Unterwerfungsangebot.", "The current agreement prevents a submission offer.");
                case DiplomacySubmissionAvailability.NegotiationPending:
                    return Text("Eine offene Forderung muss zunächst erfüllt oder beendet werden.", "An outstanding demand must first be fulfilled or ended.");
                case DiplomacySubmissionAvailability.RelationshipBlocked:
                    return Text("Gespräche ausgesetzt — ", "Talks suspended — ") + DurationText(relation.RelationshipBlockedUntilDay - day);
                case DiplomacySubmissionAvailability.LordUnknown:
                    return Text("Die Identität dieses Herrschers ist noch nicht verfügbar.", "This ruler's identity is not available yet.");
                case DiplomacySubmissionAvailability.LordNeverSubmits:
                    return Text("Unterwerfung ausgeschlossen.", "Will never submit.");
                case DiplomacySubmissionAvailability.LordNotEligible:
                    return Text("Für diesen Herrscher ist keine Unterwerfungsentscheidung verfügbar.", "A submission decision is not available for this ruler.");
                case DiplomacySubmissionAvailability.NoRemainingOpponent:
                    return Text("Ein weiterer lebender Gegner muss verbleiben.", "Another living enemy must remain.");
                case DiplomacySubmissionAvailability.OfferCooldown:
                    return Text("Eine erneute Forderung ist möglich — ", "You may demand submission again — ") + DurationText(relation.ExtendedState.SubmissionNextDemandDay - day);
                case DiplomacySubmissionAvailability.AwaitingDailyAssessment:
                    return Text("Die aktuelle Lage wird beim nächsten diplomatischen Tageswechsel bewertet.", "The current situation will be assessed at the next diplomatic day change.");
                case DiplomacySubmissionAvailability.PressureNotEstablished:
                    return Text("Militärisch nicht ausreichend unterlegen.", "Insufficient military disadvantage.");
                case DiplomacySubmissionAvailability.PressureDeveloping:
                    return Text("Hält dem Druck noch stand.", "Still resisting the pressure.");
                case DiplomacySubmissionAvailability.AwaitingOffer:
                    return Text("Die erforderlichen Drucktage sind beobachtet; die Angebotsprüfung steht noch aus.", "The required days of pressure have been observed; the offer assessment is pending.");
                default:
                    return Text("Derzeit liegt kein Unterwerfungsangebot vor.", "There is currently no submission offer.");
            }
        }
    }
}
