using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StrongholdEuropeDE.Runtime
{
    /// <summary>
    /// ExtendedState is a checksummed envelope around a legacy-compatible structural payload.
    /// Schema 7 owns the complete transfer journal separately because tribute
    /// can name a nonlocal sender or the local recipient. Legacy schema 2–6
    /// journals retain their original local-sender recovery semantics.
    /// </summary>
    internal static class DiplomacyStateCodec
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes(StrongholdEuropeDE.Runtime.CompatibilityContracts.SaveMagic);
        private static readonly byte[] ChecksumMagic = Encoding.ASCII.GetBytes("SHA2");
        // Stufe 4 (): Tributpflicht. Aeltere Staende laden
        // weiter; die neuen Felder bleiben dann auf ihren Vorgaben, was
        // "keine Tributpflicht" bedeutet.
        // Stufe 5 (): Lehnsverhaeltnis. Eigene Stufe statt
        // Anhaengen an 4, weil Stufe 4 bereits ausgeliefert war - ein damit
        // geschriebener Stand muss weiter laden.
        // Schema 7: equality refusal cooldown, native calendar anchor and
        // direction/purpose-aware transfer journals (audit ).
        // Schema 10: independent submission attempt clocks, fixed quote origin
        // and a resumable reassessment of the next tribute period. A confirmed
        // answer awaiting the native monitor is distinct from a team journal.
        // Schema 11 preserves contract-bound alliance/pact answers awaiting
        // their engine transaction. The released Schema 10 layout is unchanged.
        // Schema 13 keeps the wire layout and migrates legacy missed cycles
        // into one payable overdue invoice without erasing the obligation.
        //  checked schema15 appends a bounded aid/favor memory AFTER the
        // existing TradeHistoryArchive schema14 wire layout. Old schemas2..14
        // remain read-compatible; the installed release is not upgraded.
        // Schema16 appends simulation-tick requests and sustained rebellion observations.
        // Schema18 binds the historical map token to full original map content.
        // Schema20 appends bounded request evidence after the unchanged schema19 fields.
        // Schema21 appends the Mod-gift owner serial and the active journal's
        // explicit request/receipt binding AFTER all schema20 bytes.
        internal const int SchemaVersion = 24;
        private const int MinimumSupportedSchemaVersion = 2;
        private const int ChecksumLength = 32;
        private const int MaximumBasePayloadBytes = 64 * 1024;
        private const int MaximumPayloadBytes = 768 * 1024;
        private const int MaximumTextBytes = 256;
        private const int MaximumGiftMemories = 21;
        internal static byte[] SerializeInactive(string reason)
        {
            return DiplomacySaveCodec.SerializeInactive(reason);
        }

        internal static bool IsInactive(byte[] payload)
        {
            return DiplomacySaveCodec.IsInactive(payload);
        }

        private static LegacyDiplomacySave BaseEnvelopeSeed(LegacyDiplomacySave source)
        {
            // legacy's journal implicitly names the local sender and cannot express
            // tribute.  authenticates that journal separately and in full.
            return new LegacyDiplomacySave
            {
                SchemaVersion = source.SchemaVersion,
                MapFingerprint = source.MapFingerprint,
                RosterFingerprint = source.RosterFingerprint,
                LocalPlayerId = source.LocalPlayerId,
                LastProcessedDay = source.LastProcessedDay,
                FrozenPrices = source.FrozenPrices,
                Coalitions = source.Coalitions,
                Relations = source.Relations,
                TeamPartition = source.TeamPartition,
                OriginalTeamPartition = source.OriginalTeamPartition,
                PendingTeamTransition = source.PendingTeamTransition,
            };
        }

        private static void WriteModernTransfer(BinaryWriter writer, DiplomacyPendingTransfer transfer)
        {
            writer.Write(transfer != null);
            if (transfer == null)
                return;
            writer.Write(transfer.SenderPlayer);
            writer.Write(transfer.TargetPlayer);
            writer.Write(transfer.GoodId);
            writer.Write(transfer.Amount);
            writer.Write(transfer.SenderBefore);
            writer.Write(transfer.RecipientBefore);
            writer.Write(transfer.AlliedNativeTransfer);
            writer.Write((int)transfer.Purpose);
            writer.Write(transfer.DirectAtomicTransfer);
            writer.Write(transfer.GameDay);
            writer.Write(transfer.RollbackPending);
            WriteProgress(writer, transfer.Progress);
        }

        private static DiplomacyPendingTransfer ReadModernTransfer(BinaryReader reader, int schemaVersion)
        {
            if (!reader.ReadBoolean())
                return null;
            var transfer = new DiplomacyPendingTransfer
            {
                SenderPlayer = reader.ReadInt32(),
                TargetPlayer = reader.ReadInt32(),
                GoodId = reader.ReadInt32(),
                Amount = reader.ReadInt32(),
                SenderBefore = reader.ReadInt32(),
                RecipientBefore = reader.ReadInt32(),
                AlliedNativeTransfer = reader.ReadBoolean(),
                Purpose = (DiplomacyTransferPurpose)reader.ReadInt32(),
                DirectAtomicTransfer = reader.ReadBoolean(),
                GameDay = reader.ReadInt32(),
            };
            if (schemaVersion >= 8)
            {
                transfer.RollbackPending = reader.ReadBoolean();
                transfer.Progress = ReadProgress(reader);
            }

            return transfer;
        }

        private static void WriteProgress(BinaryWriter writer, DiplomacyTransferProgress p)
        {
            writer.Write(p != null);
            if (p == null)
                return;
            writer.Write(p.SenderTouched);
            writer.Write(p.RecipientTouched);
            writer.Write(p.SenderObserved);
            writer.Write(p.RecipientObserved);
            writer.Write(p.SenderAfter);
            writer.Write(p.RecipientAfter);
        }

        private static DiplomacyTransferProgress ReadProgress(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
                return null;
            return new DiplomacyTransferProgress
            {
                SenderTouched = reader.ReadBoolean(),
                RecipientTouched = reader.ReadBoolean(),
                SenderObserved = reader.ReadBoolean(),
                RecipientObserved = reader.ReadBoolean(),
                SenderAfter = reader.ReadInt32(),
                RecipientAfter = reader.ReadInt32(),
            };
        }

        private static void WritePackage(BinaryWriter writer, DiplomacyPendingPackage p)
        {
            writer.Write(p != null);
            if (p == null)
                return;
            writer.Write(p.SenderPlayer);
            writer.Write(p.TargetPlayer);
            writer.Write(p.GameDay);
            writer.Write(p.RollbackPending);
            writer.Write(p.Lines.Count);
            foreach (DiplomacyPendingTransfer line in p.Lines)
                WriteModernTransfer(writer, line);
            writer.Write(p.TradeRecordId);
        }

        private static DiplomacyPendingPackage ReadPackage(BinaryReader reader, int schemaVersion)
        {
            if (!reader.ReadBoolean())
                return null;
            var p = new DiplomacyPendingPackage
            {
                SenderPlayer = reader.ReadInt32(),
                TargetPlayer = reader.ReadInt32(),
                GameDay = reader.ReadInt32(),
                RollbackPending = reader.ReadBoolean(),
            };
            int count = reader.ReadInt32();
            if (count < 1 || count > MaximumGiftMemories)
                throw new InvalidDataException("ExtendedState package line count is invalid");
            for (int i = 0; i < count; i++)
                p.Lines.Add(ReadModernTransfer(reader, schemaVersion));
            if (schemaVersion >= 14)
                p.TradeRecordId = reader.ReadUInt64();
            return p;
        }

        private static void NormalizeLegacyVassalContracts(LegacyDiplomacySave save)
        {
            foreach (RelationState relation in save.Relations)
            {
                DiplomacyRelationState state = relation.ExtendedState;
                if (state == null)
                    continue;
                CoalitionState coalition = save.Coalitions.Single(c => c.CoalitionId == relation.CoalitionId);
                bool parties = (state.VassalPlayerId == save.LocalPlayerId || coalition.MemberPlayerIds.Contains(state.VassalPlayerId)) && (state.VassalOverlordPlayerId == save.LocalPlayerId || coalition.MemberPlayerIds.Contains(state.VassalOverlordPlayerId));
                if (!relation.Allied || !DiplomacyVassalage.IsActive(state) || !DiplomacyTribute.IsActive(state) || !parties)
                {
                    DiplomacyVassalage.ClearWithoutRelease(state);
                    DiplomacyTribute.Clear(state);
                }
                else
                {
                    state.VassalRole = DiplomacyVassalage.RoleFor(state.VassalPlayerId, state.VassalOverlordPlayerId, save.LocalPlayerId);
                    if (state.VassalEqualityRequestedDay >= 0 && (!DiplomacyVassalage.TermElapsed(state, state.VassalEqualityRequestedDay) || state.VassalEqualityRequestedDay > save.LastProcessedDay))
                        DiplomacyVassalage.ClearEqualityResponse(state);
                }
            }
        }

        internal static byte[] Serialize(LegacyDiplomacySave state, DiplomacyRules rules = null)
        {
            if (state == null)
                throw new ArgumentNullException("state");
            // a write must use the SAME map, roster and day limits
            // enforced at read time. Otherwise a malformed relationship
            // time could generate a valid checksum around an unreadable save.
            if (state.TradeArchive != null)
                state.TradeArchive.Validate(state.MapFingerprint, state.RosterFingerprint, state.LastProcessedDay);
            // Schema15 optional aid/favor ledger: validate/map-bind and
            // advance the DETACHED snapshot before producing any save bytes.
            // No Native receipt or diplomatic credit is synthesized here.
            byte[] aidPayload = state.AidMemory == null ? null : AidPromiseLedgerCodec.Encode(AidPromiseLedgerCodec.SnapshotForSave(state.AidMemory, state.MapFingerprint, state.RosterFingerprint, state.LastProcessedDay));
            ValidateAdvanced(state);
            byte[] basePayload = DiplomacySaveCodec.Serialize(BaseEnvelopeSeed(state), rules, true);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(SchemaVersion);
                writer.Write(basePayload.Length);
                writer.Write(basePayload);
                writer.Write(state.NativeDayAnchor);
                WriteModernTransfer(writer, state.PendingTransfer);
                WritePackage(writer, state.PendingPackage);
                writer.Write(state.Relations.Count);
                foreach (RelationState relation in state.Relations.OrderBy(value => value.CoalitionId, StringComparer.Ordinal))
                {
                    WriteText(writer, relation.CoalitionId);
                    WriteAdvanced(writer, relation.ExtendedState);
                    writer.Write((int)(relation.Demand == null ? DiplomacyDemandPurpose.None : relation.Demand.DemandPurposeDescription));
                    writer.Write(relation.Demand == null ? 0 : relation.Demand.DiplomacyUrgency);
                }

                //  is written INSIDE the existing ExtendedState checksum envelope.
                TradeSaveCodec.Write(writer, state.TradeArchive);
                // Schema15 extends schema14 ONLY AFTER its old fields.
                // Length is constrained by nested checksum codec (32 KiB).
                writer.Write(aidPayload != null);
                if (aidPayload != null)
                {
                    writer.Write(aidPayload.Length);
                    writer.Write(aidPayload);
                }

                TradeIntentMailboxCodec.Write(writer, state.TradeMailbox, state.MapFingerprint, state.RosterFingerprint, state.LastProcessedDay, state.TradeArchive);
                PersistentMapIdentity.Write(writer, state.MapIdentity);
                CanonicalTradeStatePersistence.Write(writer, state);
                DiplomaticRequestHistoryCodec.Write(writer, state.RequestHistory, state.MapFingerprint,
                    state.RosterFingerprint, state.LastProcessedDay, state.TeamPartition, state.LocalPlayerId);
                // Only schema21 carries these fields; legacy transfer and the
                // schema20 request body retain their byte-for-byte layouts.
                ModGiftJournalBinding.Write(writer, state);
                RecordedGoodsResponseSuffix.Write(writer, state);
                writer.Flush();
                byte[] canonical = stream.ToArray();
                byte[] checksum;
                using (SHA256 sha = SHA256.Create())
                {
                    checksum = sha.ComputeHash(canonical);
                }

                writer.Write(ChecksumMagic);
                writer.Write(checksum);
                writer.Flush();
                if (stream.Length > MaximumPayloadBytes)
                {
                    throw new InvalidDataException("diplomacy ExtendedState save payload is oversized");
                }

                return stream.ToArray();
            }
        }

        internal static LegacyDiplomacySave Deserialize(byte[] payload, DiplomacyRules rules = null)
        {
            if (IsInactive(payload))
                throw new InvalidDataException("diplomacy ExtendedState save is explicitly inactive");
            if (payload == null || payload.Length < Magic.Length + 12 + ChecksumMagic.Length + ChecksumLength || payload.Length > MaximumPayloadBytes)
            {
                throw new InvalidDataException("diplomacy ExtendedState save payload size is invalid");
            }

            int expectedCanonicalLength = payload.Length - ChecksumMagic.Length - ChecksumLength;
            byte[] trailerMagic = new byte[ChecksumMagic.Length];
            Buffer.BlockCopy(payload, expectedCanonicalLength, trailerMagic, 0, trailerMagic.Length);
            byte[] trailerChecksum = new byte[ChecksumLength];
            Buffer.BlockCopy(payload, expectedCanonicalLength + ChecksumMagic.Length, trailerChecksum, 0, trailerChecksum.Length);
            byte[] authenticatedChecksum;
            using (SHA256 sha = SHA256.Create())
            {
                authenticatedChecksum = sha.ComputeHash(payload, 0, expectedCanonicalLength);
            }

            if (!trailerMagic.SequenceEqual(ChecksumMagic) || !trailerChecksum.SequenceEqual(authenticatedChecksum))
            {
                throw new InvalidDataException("diplomacy ExtendedState save checksum mismatch");
            }

            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
                {
                    throw new InvalidDataException("diplomacy ExtendedState save magic mismatch");
                }

                int schemaVersion = reader.ReadInt32();
                if (schemaVersion < MinimumSupportedSchemaVersion || schemaVersion > SchemaVersion)
                {
                    throw new InvalidDataException("diplomacy ExtendedState save schema mismatch");
                }

                int baseLength = reader.ReadInt32();
                if (baseLength < 1 || baseLength > MaximumBasePayloadBytes || baseLength > stream.Length - stream.Position)
                {
                    throw new InvalidDataException("diplomacy ExtendedState base payload length is invalid");
                }

                byte[] basePayload = reader.ReadBytes(baseLength);
                if (basePayload.Length != baseLength)
                {
                    throw new EndOfStreamException();
                }

                LegacyDiplomacySave state = DiplomacySaveCodec.Deserialize(basePayload, rules, schemaVersion >= 9);
                if (schemaVersion >= 7)
                {
                    if (state.PendingTransfer != null)
                        throw new InvalidDataException(" transfer journal must have one canonical representation");
                    state.NativeDayAnchor = reader.ReadInt64();
                    state.PendingTransfer = ReadModernTransfer(reader, schemaVersion);
                }

                if (schemaVersion >= 8)
                    state.PendingPackage = ReadPackage(reader, schemaVersion);
                int count = reader.ReadInt32();
                if (count != state.Relations.Count || count < 1 || count > 7)
                {
                    throw new InvalidDataException("diplomacy ExtendedState relationship count is invalid");
                }

                Dictionary<string, RelationState> byId = state.Relations.ToDictionary(relation => relation.CoalitionId, StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < count; index++)
                {
                    string coalitionId = ReadText(reader);
                    RelationState relation;
                    if (!seen.Add(coalitionId) || !byId.TryGetValue(coalitionId, out relation))
                    {
                        throw new InvalidDataException("diplomacy ExtendedState relationship identity is invalid");
                    }

                    relation.ExtendedState = ReadAdvanced(reader, schemaVersion);
                    DiplomacyDemandPurpose purpose = (DiplomacyDemandPurpose)reader.ReadInt32();
                    int urgency = reader.ReadInt32();
                    if (relation.Demand == null)
                    {
                        if (purpose != DiplomacyDemandPurpose.None || urgency != 0)
                        {
                            throw new InvalidDataException("diplomacy ExtendedState idle demand metadata is invalid");
                        }
                    }
                    else
                    {
                        relation.Demand.DemandPurposeDescription = purpose;
                        relation.Demand.DiplomacyUrgency = urgency;
                    }
                }

                // Existing ExtendedState schema2..13 saves have no trade archive.
                if (schemaVersion >= 14)
                    state.TradeArchive = TradeSaveCodec.Read(reader, state.LastProcessedDay);
                // Legacy schema2..14 payloads are unmodified and contain
                // NO aid flag. An absent optional 15 flag also grants no aid.
                if (schemaVersion >= 15 && reader.ReadBoolean())
                {
                    int aidLength = reader.ReadInt32();
                    if (aidLength < 1 || aidLength > AidPromiseLedgerCodec.MaximumBytes || aidLength > expectedCanonicalLength - stream.Position)
                        throw new InvalidDataException("Invalid bounded aid memory size");
                    byte[] aid = reader.ReadBytes(aidLength);
                    if (aid.Length != aidLength)
                        throw new EndOfStreamException();
                    state.AidMemory = AidPromiseLedgerCodec.Decode(aid, state.MapFingerprint, state.RosterFingerprint, state.LastProcessedDay);
                    AidPromiseLedgerCodec.ValidateScope(state.AidMemory, state.MapFingerprint, state.RosterFingerprint, state.LastProcessedDay);
                }

                if (schemaVersion >= 16)
                    state.TradeMailbox = TradeIntentMailboxCodec.ReadExtended(reader, state.MapFingerprint, state.RosterFingerprint, state.LastProcessedDay, state.TradeArchive, schemaVersion >= 17);
                if (schemaVersion >= 18)
                    state.MapIdentity = PersistentMapIdentity.Read(reader);
                if (schemaVersion >= 19)
                    CanonicalTradeStatePersistence.Read(reader, state);
                if (schemaVersion >= 20)
                    state.RequestHistory = DiplomaticRequestHistoryCodec.Read(reader, state.MapFingerprint,
                        state.RosterFingerprint, state.LastProcessedDay, state.TeamPartition, state.LocalPlayerId);
                else
                    state.RequestHistory = new DiplomaticRequestHistory(state.MapFingerprint, state.RosterFingerprint);
                // Each new request provenance or outcome requires its first
                // writer schema even when the record layout stays the same.
                if (state.RequestHistory != null && state.RequestHistory.Records.Any(record =>
                    (schemaVersion < 22 &&
                     record.Origin == DiplomaticRequestOrigin.UnattributedManagedGoods) ||
                    (schemaVersion < 23 &&
                     record.OutcomeEvidence == DiplomaticRequestOutcomeEvidence.ModSubmitted) ||
                    (schemaVersion < 24 &&
                     record.Origin == DiplomaticRequestOrigin.RefusedUnattributedNativeGoods)))
                    throw new InvalidDataException("Request provenance requires a newer save schema");
                if (schemaVersion >= 21)
                    ModGiftJournalBinding.Read(reader, state);
                else
                    ModGiftJournalBinding.MigratePre21(state);
                if (schemaVersion >= 23)
                    RecordedGoodsResponseSuffix.Read(reader, state);
                if (schemaVersion == 2)
                {
                    UpgradeSchema2State(state);
                }

                if (schemaVersion < 7)
                    NormalizeLegacyVassalContracts(state);
                if (schemaVersion < 9)
                    foreach (RelationState relation in state.Relations)
                        DiplomacyVassalage.RestoreLegacyEqualityResponse(relation.ExtendedState, state.LastProcessedDay);
                long parsedCanonicalLength = stream.Position;
                if (parsedCanonicalLength != expectedCanonicalLength || stream.Length - parsedCanonicalLength != ChecksumMagic.Length + ChecksumLength)
                {
                    throw new InvalidDataException("diplomacy ExtendedState save checksum trailer is missing");
                }

                byte[] checksumMagic = reader.ReadBytes(ChecksumMagic.Length);
                byte[] checksum = reader.ReadBytes(ChecksumLength);
                byte[] expected;
                using (SHA256 sha = SHA256.Create())
                {
                    expected = sha.ComputeHash(payload, 0, checked((int)parsedCanonicalLength));
                }

                if (!checksumMagic.SequenceEqual(ChecksumMagic) || checksum.Length != ChecksumLength || !checksum.SequenceEqual(expected))
                {
                    throw new InvalidDataException("diplomacy ExtendedState save checksum mismatch");
                }

                ValidateAdvanced(state);
                NormalizeCompletedDemands(state, rules ?? new DiplomacyRules());
                ValidateAdvanced(state);
                return state;
            }
        }

        internal static byte[] MigrateLegacyPayload(byte[] legacyPayload, DiplomacyRules rules = null)
        {
            LegacyDiplomacySave state = DiplomacySaveCodec.Deserialize(legacyPayload, rules);
            // The original legacy writer already persisted selected-coalition
            // breaks as value 4 (PlayerBreakCoalition), which is precisely
            // ExtendedState's PlayerBreakOne. Preserve that journal only on this import
            // path; the current C fallback contract still permits 1..3.
            ValidateCompatibilityProjection(state, rules, DiplomacySaveCodec.IsOriginalLegacyPayload(legacyPayload));
            Dictionary<string, CoalitionState> coalitions = state.Coalitions.ToDictionary(coalition => coalition.CoalitionId, StringComparer.Ordinal);
            foreach (RelationState relation in state.Relations)
            {
                CoalitionState coalition = coalitions[relation.CoalitionId];
                DiplomacyAgreementRules.InitializeFromLegacy(relation, coalition, state.LastProcessedDay);
                if (relation.Demand != null)
                {
                    relation.Demand.DemandPurposeDescription = relation.Allied ? DiplomacyDemandPurpose.Ultimatum : DiplomacyDemandPurpose.AllianceTerms;
                    relation.Demand.DiplomacyUrgency = relation.Allied ? 3 : 2;
                    if (relation.Allied)
                    {
                        relation.ExtendedState.MaintenanceWarningLevel = 2;
                        relation.ExtendedState.Stage = DiplomacyNegotiationStage.Ultimatum;
                        relation.ExtendedState.LastEvent = DiplomacyEventKind.AllianceWarned;
                        relation.ExtendedState.LastEventDay = state.LastProcessedDay;
                    }
                }
            }

            return Serialize(state, rules);
        }

        internal static byte[] SelectPrimaryOrLegacyLoadPayload(byte[] primaryPayload, byte[] legacyPayload, DiplomacyRules rules, out bool usedLegacy, out string primaryFailureKind)
        {
            usedLegacy = false;
            primaryFailureKind = null;
            // An explicit inactive payload is a deliberate lifecycle outcome,
            // never permission to resurrect an older rolling fallback.
            if (IsInactive(primaryPayload))
                throw new InvalidDataException("primary diplomacy save is explicitly inactive");
            try
            {
                // This is deliberately a non-mutating preflight. RestoreSave
                // may later alter native teams while reconciling journals, so
                // a corrupt ExtendedState envelope must be identified before any such
                // work starts if a rolling legacy recovery slot is available.
                Deserialize(primaryPayload, rules);
                return primaryPayload;
            }
            catch (Exception exception)when (IsPayloadFormatFailure(exception))
            {
                primaryFailureKind = exception.GetType().Name;
                if (legacyPayload == null || legacyPayload.Length == 0)
                {
                    throw new InvalidDataException("diplomacy ExtendedState save is corrupt and no legacy fallback exists", exception);
                }

                try
                {
                    byte[] migrated = MigrateLegacyPayload(legacyPayload, rules);
                    // MigrateLegacyPayload serializes a new ExtendedState envelope. Read
                    // it once here as an independent assertion before the
                    // runtime is allowed to restore native state from it.
                    Deserialize(migrated, rules);
                    usedLegacy = true;
                    return migrated;
                }
                catch (Exception legacyException)when (IsPayloadFormatFailure(legacyException))
                {
                    throw new InvalidDataException("both diplomacy ExtendedState save and legacy fallback are corrupt", new AggregateException(exception, legacyException));
                }
            }
        }

        /// <summary>
        /// Produces a projection that the unchanged legacy plug-in can validate
        /// and continue. ExtendedState-only treaty and restitution demands are omitted;
        /// final alliance terms and alliance-support demands retain every
        /// delivered quantity. Long ExtendedState locks/deadlines are conservatively
        /// clamped to legacy's representable contract instead of corrupting the
        /// rollback slot.
        /// </summary>
        internal static LegacyDiplomacySave BuildCompatibilityProjection(LegacyDiplomacySave source, DiplomacyRules rules = null)
        {
            rules = rules ?? new DiplomacyRules();
            ValidateAdvanced(source);
            if (source.Relations.Any(relation => relation.ExtendedState != null && (relation.ExtendedState.SubmissionOfferAccepted || relation.ExtendedState.SubmissionAcceptancePending || DiplomacyVassalActions.HasPending(relation.ExtendedState) || DiplomacyRelationshipDecisions.HasPending(relation.ExtendedState))))
                throw new InvalidDataException("legacy fallback cannot represent an accepted submission; write an inactive marker");
            if (source.PendingPackage != null)
                throw new InvalidDataException("legacy fallback cannot represent package recovery; write an inactive marker");
            if (source.PendingTransfer != null && (source.PendingTransfer.RollbackPending || source.PendingTransfer.Progress != null || source.PendingTransfer.Purpose != DiplomacyTransferPurpose.Gift || source.PendingTransfer.SenderPlayer > 0 && source.PendingTransfer.SenderPlayer != source.LocalPlayerId))
                throw new InvalidDataException("legacy fallback cannot represent this transfer; write an inactive marker");
            if (source.PendingTeamTransition != null && source.PendingTeamTransition.Kind == DiplomacyTeamTransitionKind.AiBreak && (source.PendingTeamTransition.Reason == "tribute_default" || source.PendingTeamTransition.Reason == "vassal_rebellion"))
                throw new InvalidDataException("legacy fallback cannot represent this alliance-break cause; write an inactive marker");
            LegacyDiplomacySave projectionSeed = BuildProjectionSeed(source);
            LegacyDiplomacySave projection = DiplomacySaveCodec.Deserialize(DiplomacySaveCodec.Serialize(projectionSeed, rules), rules);
            Dictionary<string, RelationState> sourceRelations = source.Relations.ToDictionary(relation => relation.CoalitionId, StringComparer.Ordinal);
            Dictionary<string, CoalitionState> coalitions = projection.Coalitions.ToDictionary(coalition => coalition.CoalitionId, StringComparer.Ordinal);
            foreach (RelationState relation in projection.Relations)
            {
                RelationState advanced = sourceRelations[relation.CoalitionId];
                DiplomacyRelationState state = advanced.ExtendedState;
                CoalitionState coalition = coalitions[relation.CoalitionId];
                relation.SpontaneousOfferBlockedUntilDay = Math.Min(relation.SpontaneousOfferBlockedUntilDay, checked(projection.LastProcessedDay + rules.BreakLockDays));
                relation.RelationshipBlockedUntilDay = Math.Min(relation.RelationshipBlockedUntilDay, checked(projection.LastProcessedDay + rules.BreakLockDays));
                relation.AllianceGraceUntilDay = Math.Min(relation.AllianceGraceUntilDay, checked(projection.LastProcessedDay + rules.StartingAllianceGraceDays));
                bool keepDemand = false;
                if (advanced.Demand != null)
                {
                    keepDemand = advanced.Allied ? advanced.Demand.DemandPurposeDescription == DiplomacyDemandPurpose.Support || advanced.Demand.DemandPurposeDescription == DiplomacyDemandPurpose.Ultimatum : advanced.Demand.DemandPurposeDescription == DiplomacyDemandPurpose.AllianceTerms;
                }

                if (!keepDemand)
                {
                    relation.Demand = null;
                }
                else if (advanced.Demand.Complete)
                {
                    // legacy resolves a completed demand only inside its transfer
                    // callback. A complete package restored from disk would
                    // otherwise remain visible and unpayable forever.
                    relation.Demand = null;
                    if (advanced.Allied)
                    {
                        relation.Goodwill = Math.Max(relation.Goodwill, coalition.Target * rules.UltimatumRestorePercent / 100m);
                    }
                    else
                    {
                        // legacy has no separate "final terms accepted" bit. Its
                        // faithful actionable equivalent is alliance-ready
                        // goodwill with no already-complete demand attached.
                        relation.Goodwill = Math.Max(relation.Goodwill, coalition.Target);
                    }
                }
                else
                {
                    relation.Demand.IsUltimatum = relation.Allied;
                    bool pendingBreak = projection.PendingTeamTransition != null && projection.PendingTeamTransition.Kind == DiplomacyTeamTransitionKind.AiBreak && string.Equals(projection.PendingTeamTransition.CoalitionId, relation.CoalitionId, StringComparison.Ordinal);
                    int lifetime = relation.Allied ? rules.UltimatumLifetimeDays : rules.DemandLifetimeDays;
                    relation.Demand.IssuedDay = pendingBreak ? Math.Max(0, projection.PendingTeamTransition.GameDay - lifetime) : projection.LastProcessedDay;
                    relation.Demand.ExpiresDay = pendingBreak ? projection.PendingTeamTransition.GameDay : checked(projection.LastProcessedDay + lifetime);
                }

                bool permanentAllianceBar = DiplomacyAgreementRules.PermanentHistoryPreventsAlliance(advanced);
                bool finalTermsEarned = state.AllianceTermsFulfilled || advanced.PendingOffer || advanced.Demand != null && advanced.Demand.DemandPurposeDescription == DiplomacyDemandPurpose.AllianceTerms && advanced.Demand.Complete;
                if (!relation.Allied && permanentAllianceBar)
                {
                    // legacy cannot encode ExtendedState's permanent breach counters or
                    // trust ceiling. Do not let an emergency downgrade turn a
                    // permanently barred relation into an instant alliance;
                    // preserve the consequence as the strongest conservative
                    // legacy state that remains valid and recoverable.
                    relation.Goodwill = Math.Min(relation.Goodwill, coalition.Target * 0.25m);
                    relation.RelationshipBlockedUntilDay = Math.Max(relation.RelationshipBlockedUntilDay, checked(projection.LastProcessedDay + rules.BreakLockDays));
                    relation.SpontaneousOfferBlockedUntilDay = Math.Max(relation.SpontaneousOfferBlockedUntilDay, relation.RelationshipBlockedUntilDay);
                }
                else if (!relation.Allied && !finalTermsEarned && relation.Goodwill >= coalition.Target)
                {
                    relation.Goodwill = Math.Max(0m, coalition.Target - 0.01m);
                }
            }

            ValidateCompatibilityProjection(projection, rules);
            return projection;
        }

        /// <summary>
        /// Re-applies the invariants of the unchanged legacy build. The shared
        /// base codec is compiled with DIPLOMACY_ here and intentionally
        /// accepts longer ExtendedState locks, flexible package deadlines and the ExtendedState-only
        /// selected-alliance break journal. None of those may leak into the
        /// rolling fallback slot consumed by a real legacy assembly.
        /// </summary>
        internal static void ValidateCompatibilityProjection(LegacyDiplomacySave projection, DiplomacyRules rules = null, bool importingOriginalLegacy = false)
        {
            rules = rules ?? new DiplomacyRules();
            if (projection.Relations.Any(relation => relation.ExtendedState != null && (relation.ExtendedState.SubmissionOfferAccepted || relation.ExtendedState.SubmissionAcceptancePending || DiplomacyVassalActions.HasPending(relation.ExtendedState) || DiplomacyRelationshipDecisions.HasPending(relation.ExtendedState))))
                throw new InvalidDataException("legacy compatibility projection cannot represent an accepted submission");
            DiplomacySaveCodec.Validate(projection, rules);
            int maximumLockDay = checked(projection.LastProcessedDay + rules.BreakLockDays);
            foreach (RelationState relation in projection.Relations)
            {
                if (relation.SpontaneousOfferBlockedUntilDay > maximumLockDay || relation.RelationshipBlockedUntilDay > maximumLockDay)
                {
                    throw new InvalidDataException("legacy compatibility projection contains a ExtendedState-length lock");
                }

                if (relation.Demand == null)
                {
                    continue;
                }

                int expectedExpiry = checked(relation.Demand.IssuedDay + (relation.Demand.IsUltimatum ? rules.UltimatumLifetimeDays : rules.DemandLifetimeDays));
                if (relation.Demand.ExpiresDay != expectedExpiry)
                {
                    throw new InvalidDataException("legacy compatibility projection contains a ExtendedState deadline");
                }
            }

            if (projection.PendingTeamTransition != null && (int)projection.PendingTeamTransition.Kind > (int)DiplomacyTeamTransitionKind.PlayerBreakAll && !(importingOriginalLegacy && projection.PendingTeamTransition.Kind == DiplomacyTeamTransitionKind.PlayerBreakOne))
            {
                throw new InvalidDataException("legacy compatibility projection contains a ExtendedState transition");
            }
        }

        private static LegacyDiplomacySave BuildProjectionSeed(LegacyDiplomacySave source)
        {
            // The legacy codec deliberately rejects a demand whose final unit was
            // delivered unless an alliance team transaction is already
            // journalled. ExtendedState can nevertheless be asked for its rolling legacy
            // fallback in that short, fully-paid interval. Build a read-only
            // serialization seed with such demands one unit short; the
            // projection loop below then applies the source's true completed
            // outcome and removes the demand. The live ExtendedState state is never
            // mutated merely to create a rollback payload.
            return new LegacyDiplomacySave
            {
                SchemaVersion = source.SchemaVersion,
                MapFingerprint = source.MapFingerprint,
                RosterFingerprint = source.RosterFingerprint,
                LocalPlayerId = source.LocalPlayerId,
                LastProcessedDay = source.LastProcessedDay,
                FrozenPrices = source.FrozenPrices,
                Coalitions = source.Coalitions,
                Relations = source.Relations.Select(CloneProjectionSeedRelation).ToList(),
                TeamPartition = source.TeamPartition,
                OriginalTeamPartition = source.OriginalTeamPartition,
                PendingTransfer = source.PendingTransfer,
                // legacy has no selected-coalition player-break transition. Its
                // rollback slot therefore keeps the last fully committed
                // alliance state if ExtendedState is saved during that short journalled
                // interval; the primary ExtendedState payload still resumes the action.
                PendingTeamTransition = source.PendingTeamTransition != null && source.PendingTeamTransition.Kind == DiplomacyTeamTransitionKind.PlayerBreakOne ? null : source.PendingTeamTransition,
            };
        }

        private static RelationState CloneProjectionSeedRelation(RelationState source)
        {
            return new RelationState
            {
                CoalitionId = source.CoalitionId,
                Goodwill = source.Goodwill,
                LastGiftDay = source.LastGiftDay,
                LastProcessedDay = source.LastProcessedDay,
                LastOfferAttemptDay = source.LastOfferAttemptDay,
                OfferAttemptCounter = source.OfferAttemptCounter,
                GiftValueSinceOfferAttempt = source.GiftValueSinceOfferAttempt,
                SpontaneousOfferBlockedUntilDay = source.SpontaneousOfferBlockedUntilDay,
                RelationshipBlockedUntilDay = source.RelationshipBlockedUntilDay,
                AllianceGraceUntilDay = source.AllianceGraceUntilDay,
                Demand = CloneProjectionSeedDemand(source.Demand),
                PendingOffer = source.PendingOffer,
                PendingOfferIssuedDay = source.PendingOfferIssuedDay,
                Allied = source.Allied,
                StartingAlliance = source.StartingAlliance,
            };
        }

        private static GoodsDemand CloneProjectionSeedDemand(GoodsDemand source)
        {
            if (source == null)
            {
                return null;
            }

            if (source.Items == null || source.Items.Length < 1 || source.Items.Any(item => item == null))
            {
                throw new InvalidDataException("diplomacy ExtendedState demand items are incomplete");
            }

            DemandItem[] items = source.Items.Select(item => new DemandItem { GoodId = item.GoodId, UnitValue = item.UnitValue, RequiredAmount = item.RequiredAmount, DeliveredAmount = item.DeliveredAmount, }).ToArray();
            if (source.Complete)
            {
                items[0].DeliveredAmount = Math.Max(0, items[0].RequiredAmount - 1);
            }

            return new GoodsDemand
            {
                IssuedDay = source.IssuedDay,
                ExpiresDay = source.ExpiresDay,
                IsUltimatum = source.IsUltimatum,
                Items = items,
            };
        }

        private static void NormalizeCompletedDemands(LegacyDiplomacySave state, DiplomacyRules rules)
        {
            Dictionary<string, CoalitionState> coalitions = state.Coalitions.ToDictionary(coalition => coalition.CoalitionId, StringComparer.Ordinal);
            foreach (RelationState relation in state.Relations)
            {
                if (relation.Demand == null || !relation.Demand.Complete)
                {
                    continue;
                }

                CoalitionState coalition = coalitions[relation.CoalitionId];
                DiplomacyAgreementRules.ResolveCompletedDemand(relation, state.LastProcessedDay, coalition.Target, rules);
            }
        }

        internal static void ValidateAdvanced(LegacyDiplomacySave state)
        {
            if (state == null || state.Coalitions == null || state.Relations == null || state.NativeDayAnchor < -1 || state.NativeDayAnchor > uint.MaxValue)
            {
                throw new InvalidDataException("diplomacy ExtendedState save shape is incomplete");
            }

            DiplomaticRequestHistoryCodec.Validate(state.RequestHistory, state.MapFingerprint,
                state.RosterFingerprint, state.LastProcessedDay, state.TeamPartition, state.LocalPlayerId);

            Dictionary<string, CoalitionState> coalitions;
            try
            {
                coalitions = state.Coalitions.ToDictionary(coalition => coalition.CoalitionId, StringComparer.Ordinal);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("diplomacy ExtendedState coalition identities are invalid", exception);
            }

            foreach (RelationState relation in state.Relations)
            {
                CoalitionState coalition;
                if (relation == null || !coalitions.TryGetValue(relation.CoalitionId, out coalition))
                {
                    throw new InvalidDataException("diplomacy ExtendedState relation has no coalition");
                }

                try
                {
                    DiplomacyAgreementRules.ValidateAdvanced(relation, coalition, state.LastProcessedDay);
                    ValidateVassalContract(relation, coalition, state.LocalPlayerId, state.LastProcessedDay);
                    DiplomacyVassalActions.Validate(relation, state.LocalPlayerId, state.LastProcessedDay);
                    DiplomacyRelationshipDecisions.Validate(relation, coalition, state.LocalPlayerId, state.LastProcessedDay);
                    if (DiplomacySubmission.HasOffer(relation.ExtendedState))
                    {
                        DiplomacyRelationState s = relation.ExtendedState;
                        if (coalition.MemberPlayerIds.Length != 1 || coalition.MemberPlayerIds[0] != s.SubmissionOfferPlayerId || s.SubmissionOfferPlayerId == state.LocalPlayerId || !state.TeamPartition.ContainsKey(s.SubmissionOfferPlayerId))
                            throw new InvalidDataException("submission requires exactly one named AI party");
                        if (s.SubmissionOfferAccepted && (state.PendingTeamTransition == null || state.PendingTeamTransition.Kind != DiplomacyTeamTransitionKind.FormAlliance || state.PendingTeamTransition.CoalitionId != relation.CoalitionId || state.PendingTeamTransition.Reason != "submission_offer"))
                            throw new InvalidDataException("accepted submission requires its matching team transaction");
                    }

                    if (relation.ExtendedState.PendingVassalOverlordPlayerId != 0 && (relation.ExtendedState.PendingVassalOverlordPlayerId == state.LocalPlayerId || !coalition.MemberPlayerIds.Contains(relation.ExtendedState.PendingVassalOverlordPlayerId) || !state.TeamPartition.ContainsKey(relation.ExtendedState.PendingVassalOverlordPlayerId)))
                        throw new InvalidDataException("pending fealty offer has no named coalition party");
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException("diplomacy ExtendedState advanced relation is invalid: " + relation.CoalitionId, exception);
                }
            }

            ValidatePendingAllianceAuthorization(state, coalitions);
            ValidateModernTransfer(state);
            ValidatePackage(state);
            ModGiftJournalBinding.Validate(state);
            DiplomacyPendingTeamTransition pending = state.PendingTeamTransition;
            if (pending != null && pending.Kind == DiplomacyTeamTransitionKind.AiBreak)
            {
                DiplomacyBreachKind kind = pending.Reason == "tribute_default" ? DiplomacyBreachKind.TributeDefault : pending.Reason == "vassal_rebellion" ? DiplomacyBreachKind.VassalRebellion : DiplomacyBreachKind.FailedSupportUltimatum;
                if (!DiplomacyAgreementRules.CanCommitAllianceBreak(state.Relations.Single(r => r.CoalitionId == pending.CoalitionId), pending.GameDay, kind))
                    throw new InvalidDataException("ExtendedState AI break is not authorized by its stated cause");
            }
        }

        private static void ValidateVassalContract(RelationState relation, CoalitionState coalition, int local, int day)
        {
            DiplomacyRelationState s = relation.ExtendedState;
            bool active = DiplomacyVassalage.IsActive(s);
            if (!Enum.IsDefined(typeof(DiplomacyVassalRole), s.VassalRole) || s.TributePaidCycles < 0 || s.TributeDefaults < 0 || s.VassalReleases < 0 || s.VassalRebellions < 0 || s.TributePaidCycles > 1000000 || s.TributeDefaults > 1000000 || s.VassalReleases > 1000000 || s.VassalRebellions > 1000000 || s.VassalEqualityNextAllowedDay < 0 || s.VassalEqualityNextAllowedDay > (long)day + DiplomacyVassalage.EqualityRequestCooldownDays || active != DiplomacyTribute.IsActive(s))
                throw new InvalidDataException("ExtendedState vassal and tribute contract is inconsistent");
            // Reassessment is an unfinished next-period action, not a new
            // obligation. It may outlive its next due date while the engine
            // is busy, but needs an active gold contract and a closed cycle.
            if (s.TributeReassessmentPending && (!active || s.TributeGoodId != DiplomacyTribute.GoldGoodId || (s.TributePaidCycles == 0 && s.TributeDefaults == 0)))
                throw new InvalidDataException("ExtendedState tribute reassessment has no completed gold period");
            if (!active)
            {
                if (s.VassalPlayerId != -1 || s.VassalOverlordPlayerId != -1 || s.VassalSinceDay != -1 || s.VassalEqualityRequestedDay != -1 || s.VassalStrengthEdgeDays != 0 || s.VassalEqualityNextAllowedDay != 0 || s.TributeGoodId != 0 || s.TributeAmount != 0 || s.TributeIntervalDays != 0 || s.TributeNextDueDay != -1 || s.TributeMissedCycles != 0)
                    throw new InvalidDataException("ExtendedState inactive contract retains active obligations");
                return;
            }

            if (!relation.Allied || s.Agreement != DiplomacyAgreementKind.Alliance || s.VassalPlayerId <= 0 || s.VassalOverlordPlayerId <= 0 || s.VassalPlayerId == s.VassalOverlordPlayerId || !(s.VassalPlayerId == local || coalition.MemberPlayerIds.Contains(s.VassalPlayerId)) || !(s.VassalOverlordPlayerId == local || coalition.MemberPlayerIds.Contains(s.VassalOverlordPlayerId)) || s.VassalRole != DiplomacyVassalage.RoleFor(s.VassalPlayerId, s.VassalOverlordPlayerId, local) || s.VassalSinceDay < 0 || s.VassalSinceDay > day || s.VassalStrengthEdgeDays < 0 || s.VassalEqualityRequestedDay < -1 || s.VassalEqualityRequestedDay > day || s.VassalEqualityRequestedDay >= 0 && !DiplomacyVassalage.TermElapsed(s, s.VassalEqualityRequestedDay) || !new[]
            {
                2,
                3,
                4,
                6,
                7,
                9,
                10,
                11,
                12,
                13,
                14,
                15,
                16,
                17,
                18,
                19,
                20,
                21,
                22,
                23,
                24
            }.Contains(s.TributeGoodId) || s.TributeAmount < 1 || s.TributeAmount > 1000000 || s.TributeIntervalDays < DiplomacyTribute.MinimumIntervalDays || s.TributeIntervalDays > DiplomacyTribute.MaximumIntervalDays || s.TributeNextDueDay < 0 || s.TributeNextDueDay > (long)day + s.TributeIntervalDays || s.TributeMissedCycles < 0 || s.TributeMissedCycles > DiplomacyTribute.MissedCyclesUntilBreak)
                throw new InvalidDataException("ExtendedState active vassal contract has invalid parties, dates or terms");
        }

        private static void ValidateModernTransfer(LegacyDiplomacySave state)
        {
            DiplomacyPendingTransfer t = state.PendingTransfer;
            if (t == null)
                return;
            int sender = t.SenderPlayer == 0 ? state.LocalPlayerId : t.SenderPlayer;
            if ((!t.RollbackPending && state.PendingTeamTransition != null) || t.SenderPlayer < 0 || state.TeamPartition == null || !state.TeamPartition.ContainsKey(sender) || !state.TeamPartition.ContainsKey(t.TargetPlayer) || sender == t.TargetPlayer || !state.FrozenPrices.ContainsKey(t.GoodId) || t.Amount < 1 || t.Amount > 1000000 || t.SenderBefore < t.Amount || t.RecipientBefore < 0 || t.RecipientBefore > int.MaxValue - t.Amount || !Enum.IsDefined(typeof(DiplomacyTransferPurpose), t.Purpose) || t.GameDay < -1 || t.GameDay > state.LastProcessedDay)
                throw new InvalidDataException("ExtendedState transfer parties, amount or purpose are invalid");
            ValidateProgress(t);
            // Undo intent survives expiry, defeat and other later model days.
            // It authorizes no gift/payment credit and cannot create a treaty.
            if (t.RollbackPending)
                return;
            if (t.Purpose == DiplomacyTransferPurpose.Gift)
            {
#if DIPLOMACY_PRIVATE_TESTS
                if (sender != state.LocalPlayerId && DiplomacyFeature.IsPersistableAidJournal(state, t))
                    return;
#endif
                CoalitionState coalition = state.Coalitions.SingleOrDefault(c => c.MemberPlayerIds.Contains(t.TargetPlayer));
                if (sender != state.LocalPlayerId || coalition == null || t.AlliedNativeTransfer != state.Relations.Single(r => r.CoalitionId == coalition.CoalitionId).Allied)
                    throw new InvalidDataException("ExtendedState gift journal relationship is invalid");
            }
            else
            {
                RelationState relation = state.Relations.SingleOrDefault(r => DiplomacyVassalage.IsActive(r.ExtendedState) && r.ExtendedState.VassalPlayerId == sender && r.ExtendedState.VassalOverlordPlayerId == t.TargetPlayer);
                if (relation == null || !relation.Allied || !t.AlliedNativeTransfer || !t.DirectAtomicTransfer || t.GameDay != state.LastProcessedDay || !DiplomacyTribute.IsDue(relation.ExtendedState, t.GameDay) || !DiplomacyTribute.AcceptsSettlement(relation.ExtendedState, t.GoodId, t.Amount, state.FrozenPrices))
                    throw new InvalidDataException("ExtendedState tribute journal is not authorized by a due contract");
            }
        }

        private static void ValidateProgress(DiplomacyPendingTransfer t)
        {
            DiplomacyTransferProgress p = t.Progress;
            if (p == null)
                return;
            if (!t.DirectAtomicTransfer || p.SenderAfter < 0 || p.RecipientAfter < 0 || p.SenderObserved && !p.SenderTouched || p.RecipientObserved && !p.RecipientTouched || p.RecipientTouched && !p.SenderTouched || !p.SenderObserved && p.SenderAfter != 0 || !p.RecipientObserved && p.RecipientAfter != 0)
                throw new InvalidDataException("ExtendedState transfer checkpoints are invalid");
        }

        private static void ValidatePackage(LegacyDiplomacySave state)
        {
            DiplomacyPendingPackage p = state.PendingPackage;
            if (p == null)
            {
                if (state.TradeArchive != null && state.TradeArchive.PendingNativeId != 0)
                    throw new InvalidDataException("Orphaned prepared trade without package");
                return;
            }

            if (p.TradeRecordId != 0)
            {
                // Owner-bound two-way native trade has exactly two reciprocal
                // lines, and cannot be interpreted as a gift or due tribute.
                if (state.TradeArchive == null || state.TradeArchive.PendingNativeId != p.TradeRecordId || p.Lines == null || p.Lines.Count != 2 || state.PendingTransfer != null || !state.TeamPartition.ContainsKey(p.SenderPlayer) || !state.TeamPartition.ContainsKey(p.TargetPlayer) || p.SenderPlayer == p.TargetPlayer || p.GameDay < 1 || p.GameDay > state.LastProcessedDay)
                    throw new InvalidDataException("Trade package owner/shape invalid");
                TradeRecord record = state.TradeArchive.Record(p.TradeRecordId);
                if (record == null || record.State != TradeDisposition.PreparingNative || record.Requester != p.SenderPlayer || record.Recipient != p.TargetPlayer)
                    throw new InvalidDataException("Trade package not bound to archived quote");
                var first = p.Lines[0];
                var second = p.Lines[1];
                if (first == null || second == null || first.GoodId == second.GoodId || first.GoodId != record.OfferedGood || second.GoodId != record.RequestedGood || first.Amount != record.OfferedAmount || second.Amount != record.RequestedAmount || first.SenderPlayer != record.Requester || first.TargetPlayer != record.Recipient || second.SenderPlayer != record.Recipient || second.TargetPlayer != record.Requester || first.Purpose != DiplomacyTransferPurpose.Barter || second.Purpose != DiplomacyTransferPurpose.Barter)
                    throw new InvalidDataException("Trade package two-way terms mismatch");
                foreach (var transfer in p.Lines)
                {
                    if (!transfer.DirectAtomicTransfer || transfer.Progress == null || transfer.Amount < 1 || transfer.Amount > 100000 || transfer.SenderBefore < transfer.Amount || transfer.RecipientBefore < 0 || transfer.RecipientBefore > int.MaxValue - transfer.Amount || transfer.GameDay != p.GameDay || transfer.RollbackPending && !p.RollbackPending || !state.FrozenPrices.ContainsKey(transfer.GoodId))
                        throw new InvalidDataException("Trade package invalid native line");
                    ValidateProgress(transfer);
                }

                return;
            }

            if (state.TradeArchive != null && state.TradeArchive.PendingNativeId != 0)
                throw new InvalidDataException("Trade prepared with no matching package");
            if (state.PendingTransfer != null || (!p.RollbackPending && state.PendingTeamTransition != null) || p.SenderPlayer != state.LocalPlayerId || p.TargetPlayer == p.SenderPlayer || !state.TeamPartition.ContainsKey(p.TargetPlayer) || p.GameDay < 0 || p.GameDay > state.LastProcessedDay || p.Lines == null || p.Lines.Count < 1 || p.Lines.Count > MaximumGiftMemories)
                throw new InvalidDataException("ExtendedState package identity or shape is invalid");
            CoalitionState coalition = state.Coalitions.SingleOrDefault(c => c.MemberPlayerIds.Contains(p.TargetPlayer));
            if (coalition == null)
                throw new InvalidDataException("ExtendedState package target is outside the coalition roster");
            RelationState relation = state.Relations.Single(r => r.CoalitionId == coalition.CoalitionId);
            var goods = new System.Collections.Generic.HashSet<int>();
            foreach (DiplomacyPendingTransfer t in p.Lines)
            {
                if (t == null || !goods.Add(t.GoodId) || !state.FrozenPrices.ContainsKey(t.GoodId) || t.SenderPlayer != p.SenderPlayer || t.TargetPlayer != p.TargetPlayer || t.GameDay != p.GameDay || t.Purpose != DiplomacyTransferPurpose.Gift || !t.DirectAtomicTransfer || t.Progress == null || t.Amount < 1 || t.Amount > 1000000 || t.SenderBefore < t.Amount || t.RecipientBefore < 0 || t.RecipientBefore > int.MaxValue - t.Amount || t.RollbackPending && !p.RollbackPending)
                    throw new InvalidDataException("ExtendedState package line is invalid");
                ValidateProgress(t);
                if (!p.RollbackPending && (t.AlliedNativeTransfer != relation.Allied || relation.Demand == null || !relation.Demand.Items.Any(i => i != null && i.GoodId == t.GoodId && i.RemainingAmount == t.Amount)))
                    throw new InvalidDataException("ExtendedState package is not bound to its unfulfilled demand");
            }
        }

        private static void ValidatePendingAllianceAuthorization(LegacyDiplomacySave state, IReadOnlyDictionary<string, CoalitionState> coalitions)
        {
            DiplomacyPendingTeamTransition transition = state.PendingTeamTransition;
            if (transition == null || transition.Kind != DiplomacyTeamTransitionKind.FormAlliance)
            {
                return;
            }

            RelationState relation = state.Relations.Single(value => string.Equals(value.CoalitionId, transition.CoalitionId, StringComparison.Ordinal));
            CoalitionState coalition = coalitions[transition.CoalitionId];
            if (transition.Reason == "submission_offer" && !DiplomacySubmission.CanCommitOffer(relation))
                throw new InvalidDataException("submission transition has no accepted offer");
            bool completedTermsCanNormalize = relation.Demand != null && relation.Demand.Complete && relation.Demand.DemandPurposeDescription == DiplomacyDemandPurpose.AllianceTerms && relation.ExtendedState.RestitutionOutstandingValue == 0m && relation.ExtendedState.FulfilledDemands >= 2 && !DiplomacyAgreementRules.PermanentHistoryPreventsAlliance(relation);
            if (!relation.PendingOffer && !DiplomacyAgreementRules.CanFormAlliance(relation, coalition.Target) && !completedTermsCanNormalize && !(transition.Reason == "submission_offer" && DiplomacySubmission.HasOffer(relation.ExtendedState) && relation.ExtendedState.SubmissionOfferAccepted))
            {
                throw new InvalidDataException("pending ExtendedState alliance transition is not authorized by " + "the advanced relationship state");
            }
        }

        private static void UpgradeSchema2State(LegacyDiplomacySave state)
        {
            foreach (RelationState relation in state.Relations)
            {
                DiplomacyRelationState advanced = relation.ExtendedState;
                advanced.Reliability = Clamp100(50m + advanced.FulfilledDemands * 7m - advanced.MissedDemands * 8m - advanced.BrokenAgreements * 12m);
                advanced.BreachPoints = Math.Min(250, advanced.BrokenAgreements * 8);
                advanced.TrustCeiling = Math.Max(65m, 100m - advanced.BreachPoints * 0.52m);
                advanced.Trust = Math.Min(advanced.Trust, advanced.TrustCeiling);
                advanced.LastPositiveEventDay = IsPositiveEvent(advanced.LastEvent) ? advanced.LastEventDay : relation.LastGiftDay;
                advanced.LastNegativeEventDay = IsNegativeEvent(advanced.LastEvent) ? advanced.LastEventDay : -1;
                advanced.LastNegotiationRequestDay = relation.LastOfferAttemptDay;
                // Schema 2 already persisted the date of the last hostile
                // contact but did not yet have the aggregate counter. Infer
                // the minimum history required by that older evidence so the
                // stricter schema-3 invariants do not reject a lawful ExtendedState.0
                // save that had seen combat.
                advanced.HostileActs = advanced.LastHostileActDay >= 0 ? 1 : 0;
                advanced.StrategicThreat = Clamp100(50m + advanced.StrategicLeverage * 0.30m);
                advanced.StrategicDependency = Math.Min(100m, Math.Max(0, advanced.CommonEnemyCoalitions) * 8m);
                advanced.CoalitionValue = 50m;
                advanced.LastSupportDay = relation.Allied ? relation.LastGiftDay : -1;
                advanced.LastGiftCategory = advanced.LastGiftGoodId > 0 ? DiplomacyAgreementRules.GoodCategory(advanced.LastGiftGoodId) : DiplomacyGoodCategory.Materials;
                advanced.LifetimeCreditedGiftValue = Math.Max(0m, relation.Goodwill);
                advanced.AllianceTermsFulfilled = relation.Allied || relation.PendingOffer || advanced.Stage == DiplomacyNegotiationStage.AllianceReady || relation.Demand != null && relation.Demand.DemandPurposeDescription == DiplomacyDemandPurpose.AllianceTerms && relation.Demand.Complete;
                if (advanced.AllianceTermsFulfilled && !relation.Allied)
                {
                    advanced.FulfilledDemands = Math.Max(3, advanced.FulfilledDemands);
                }

                advanced.GiftMemory = new List<DiplomacyGiftMemory>();
                if (advanced.LastGiftGoodId > 0)
                {
                    advanced.GiftMemory.Add(new DiplomacyGiftMemory { GoodId = advanced.LastGiftGoodId, LastGiftDay = relation.LastGiftDay, ConsecutiveCount = Math.Max(1, advanced.RepeatedGiftCount), RecentRawValue = Math.Max(0m, advanced.LastCreditedGiftValue), LifetimeCreditedValue = Math.Max(0m, advanced.LastCreditedGiftValue), });
                }
            }
        }

        private static bool IsPositiveEvent(DiplomacyEventKind kind)
        {
            return kind == DiplomacyEventKind.GiftReceived || kind == DiplomacyEventKind.DemandFulfilled || kind == DiplomacyEventKind.TruceFormed || kind == DiplomacyEventKind.TradePactFormed || kind == DiplomacyEventKind.AllianceFormed;
        }

        private static bool IsNegativeEvent(DiplomacyEventKind kind)
        {
            return kind == DiplomacyEventKind.DemandMissed || kind == DiplomacyEventKind.AllianceWarned || kind == DiplomacyEventKind.AllianceBroken || kind == DiplomacyEventKind.AgreementBrokenByPlayer || kind == DiplomacyEventKind.OfferRejected;
        }

        private static bool IsPayloadFormatFailure(Exception exception)
        {
            return exception is InvalidDataException || exception is IOException || exception is ArgumentException || exception is InvalidOperationException || exception is ArithmeticException || exception is CryptographicException;
        }

        private static decimal Clamp100(decimal value)
        {
            return Math.Max(0m, Math.Min(100m, value));
        }

        private static void WriteAdvanced(BinaryWriter writer, DiplomacyRelationState state)
        {
            writer.Write((int)state.Personality);
            writer.Write(state.PrimaryLordId);
            WriteDecimal(writer, state.Trust);
            WriteDecimal(writer, state.Gratitude);
            WriteDecimal(writer, state.Respect);
            WriteDecimal(writer, state.Grudge);
            writer.Write((int)state.Stage);
            writer.Write((int)state.Agreement);
            writer.Write(state.AgreementUntilDay);
            writer.Write(state.LastStrategicDay);
            writer.Write(state.LastInteractionDay);
            writer.Write(state.LastHostileActDay);
            writer.Write(state.LastGiftGoodId);
            writer.Write(state.RepeatedGiftCount);
            writer.Write(state.FulfilledDemands);
            writer.Write(state.MissedDemands);
            writer.Write(state.BrokenAgreements);
            writer.Write(state.MaintenanceDueDay);
            writer.Write(state.MaintenanceWarningLevel);
            WriteDecimal(writer, state.StrategicLeverage);
            WriteDecimal(writer, state.LastCreditedGiftValue);
            writer.Write((int)state.LastEvent);
            writer.Write(state.LastEventDay);
            WriteDecimal(writer, state.Reliability);
            WriteDecimal(writer, state.TrustCeiling);
            WriteDecimal(writer, state.PositiveMomentum);
            WriteDecimal(writer, state.NegativeMomentum);
            writer.Write(state.LastPositiveEventDay);
            writer.Write(state.LastNegativeEventDay);
            writer.Write(state.LastNegotiationRequestDay);
            writer.Write(state.NegotiationRequestsWithoutGift);
            writer.Write(state.FailedNegotiations);
            writer.Write(state.ConsecutiveMissedDemands);
            writer.Write(state.HostileActs);
            writer.Write(state.ManualTreatyEnds);
            writer.Write(state.TreacherousAttacks);
            writer.Write(state.MilitaryIncursions);
            writer.Write(state.AllianceAbandonments);
            writer.Write(state.ObservedAllianceAbandonments);
            writer.Write(state.BreachPoints);
            writer.Write(state.LastMilitaryIncursionDay);
            WriteDecimal(writer, state.RestitutionOutstandingValue);
            writer.Write(state.AllianceTermsFulfilled);
            WriteDecimal(writer, state.StrategicThreat);
            WriteDecimal(writer, state.StrategicDependency);
            WriteDecimal(writer, state.CoalitionValue);
            writer.Write(state.CommonEnemyCoalitions);
            writer.Write(state.HostileCoalitions);
            writer.Write(state.PlayerAllianceCount);
            WriteDecimal(writer, state.MaintenanceCredit);
            WriteDecimal(writer, state.SupportValueSinceMaintenance);
            writer.Write(state.LastSupportDay);
            writer.Write(state.FulfilledSupportStreak);
            writer.Write(state.UltimatumsIssued);
            writer.Write(state.VariedGiftStreak);
            writer.Write((int)state.LastGiftCategory);
            WriteDecimal(writer, state.LifetimeCreditedGiftValue);
            writer.Write(state.TributeGoodId);
            writer.Write(state.TributeAmount);
            writer.Write(state.TributeIntervalDays);
            writer.Write(state.TributeNextDueDay);
            writer.Write(state.TributeMissedCycles);
            writer.Write(state.TributePaidCycles);
            writer.Write(state.TributeDefaults);
            writer.Write((int)state.VassalRole);
            writer.Write(state.VassalPlayerId);
            writer.Write(state.VassalOverlordPlayerId);
            writer.Write(state.VassalSinceDay);
            writer.Write(state.VassalEqualityRequestedDay);
            writer.Write(state.VassalStrengthEdgeDays);
            writer.Write(state.VassalReleases);
            writer.Write(state.VassalRebellions);
            writer.Write(state.VassalEqualityNextAllowedDay);
            writer.Write(state.PendingVassalOverlordPlayerId);
            writer.Write(state.PendingVassalTributeAmount);
            writer.Write(state.PendingVassalOfferDay);
            writer.Write(state.SubmissionPressureDays);
            writer.Write(state.SubmissionLastComparedDay);
            writer.Write(state.SubmissionNextOfferDay);
            writer.Write(state.SubmissionOfferPlayerId);
            writer.Write(state.SubmissionOfferLordId);
            writer.Write(state.SubmissionOfferTributeAmount);
            writer.Write(state.SubmissionOfferCreatedDay);
            writer.Write(state.SubmissionResponseDaysRemaining);
            writer.Write(state.SubmissionOfferAccepted);
            writer.Write(state.VassalEqualityResponseDaysRemaining);
            writer.Write(state.VassalEqualityLastResponseDay);
            writer.Write(state.SubmissionNextDemandDay);
            writer.Write((int)state.SubmissionOfferOrigin);
            writer.Write(state.TributeReassessmentPending);
            writer.Write(state.SubmissionAcceptancePending);
            writer.Write((int)state.PendingVassalAction);
            writer.Write(state.PendingVassalActionVassalPlayerId);
            writer.Write(state.PendingVassalActionOverlordPlayerId);
            writer.Write(state.PendingVassalActionSinceDay);
            writer.Write(state.PendingVassalActionRequestDay);
            writer.Write(state.PendingVassalActionQuoteAmount);
            writer.Write(state.PendingVassalActionReleaseCount);
            writer.Write((int)state.PendingRelationshipDecision);
            writer.Write(state.PendingRelationshipDecisionPlayerId);
            writer.Write(state.PendingRelationshipDecisionDay);
            WriteRelationshipContract(writer, state.PendingRelationshipDecisionContract);
            writer.Write(state.SubmissionPressureRuleVersion);
            writer.Write(state.ImportedFrom);
            writer.Write(state.MigrationSourceVersion);
            DiplomacyGiftMemory[] memories = (state.GiftMemory ?? new List<DiplomacyGiftMemory>()).OrderBy(memory => memory.GoodId).ToArray();
            writer.Write(memories.Length);
            foreach (DiplomacyGiftMemory memory in memories)
            {
                writer.Write(memory.GoodId);
                writer.Write(memory.LastGiftDay);
                writer.Write(memory.ConsecutiveCount);
                WriteDecimal(writer, memory.RecentRawValue);
                WriteDecimal(writer, memory.LifetimeCreditedValue);
            }
        }

        private static void WriteRelationshipContract(BinaryWriter writer, string value)
        {
            if (value == null)
            {
                writer.Write(-1);
                return;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length > DiplomacyRelationshipDecisions.MaximumContractBytes)
                throw new InvalidDataException("Relationship contract too long");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadRelationshipContract(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == -1)
                return null;
            if (length < 1 || length > DiplomacyRelationshipDecisions.MaximumContractBytes)
                throw new InvalidDataException("Relationship contract length is invalid");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length || bytes.Any(b => b > 127))
                throw new InvalidDataException("Relationship contract bytes are invalid");
            return Encoding.ASCII.GetString(bytes);
        }

        private static DiplomacyRelationState ReadAdvanced(BinaryReader reader, int schemaVersion)
        {
            var state = new DiplomacyRelationState
            {
                Personality = (DiplomacyPersonalityKind)reader.ReadInt32(),
                PrimaryLordId = reader.ReadInt32(),
                Trust = ReadDecimal(reader),
                Gratitude = ReadDecimal(reader),
                Respect = ReadDecimal(reader),
                Grudge = ReadDecimal(reader),
                Stage = (DiplomacyNegotiationStage)reader.ReadInt32(),
                Agreement = (DiplomacyAgreementKind)reader.ReadInt32(),
                AgreementUntilDay = reader.ReadInt32(),
                LastStrategicDay = reader.ReadInt32(),
                LastInteractionDay = reader.ReadInt32(),
                LastHostileActDay = reader.ReadInt32(),
                LastGiftGoodId = reader.ReadInt32(),
                RepeatedGiftCount = reader.ReadInt32(),
                FulfilledDemands = reader.ReadInt32(),
                MissedDemands = reader.ReadInt32(),
                BrokenAgreements = reader.ReadInt32(),
                MaintenanceDueDay = reader.ReadInt32(),
                MaintenanceWarningLevel = reader.ReadInt32(),
                StrategicLeverage = ReadDecimal(reader),
                LastCreditedGiftValue = ReadDecimal(reader),
                LastEvent = (DiplomacyEventKind)reader.ReadInt32(),
                LastEventDay = reader.ReadInt32(),
            };
            if (schemaVersion < 3)
            {
                return state;
            }

            state.Reliability = ReadDecimal(reader);
            state.TrustCeiling = ReadDecimal(reader);
            state.PositiveMomentum = ReadDecimal(reader);
            state.NegativeMomentum = ReadDecimal(reader);
            state.LastPositiveEventDay = reader.ReadInt32();
            state.LastNegativeEventDay = reader.ReadInt32();
            state.LastNegotiationRequestDay = reader.ReadInt32();
            state.NegotiationRequestsWithoutGift = reader.ReadInt32();
            state.FailedNegotiations = reader.ReadInt32();
            state.ConsecutiveMissedDemands = reader.ReadInt32();
            state.HostileActs = reader.ReadInt32();
            state.ManualTreatyEnds = reader.ReadInt32();
            state.TreacherousAttacks = reader.ReadInt32();
            state.MilitaryIncursions = reader.ReadInt32();
            state.AllianceAbandonments = reader.ReadInt32();
            state.ObservedAllianceAbandonments = reader.ReadInt32();
            state.BreachPoints = reader.ReadInt32();
            state.LastMilitaryIncursionDay = reader.ReadInt32();
            state.RestitutionOutstandingValue = ReadDecimal(reader);
            state.AllianceTermsFulfilled = reader.ReadBoolean();
            state.StrategicThreat = ReadDecimal(reader);
            state.StrategicDependency = ReadDecimal(reader);
            state.CoalitionValue = ReadDecimal(reader);
            state.CommonEnemyCoalitions = reader.ReadInt32();
            state.HostileCoalitions = reader.ReadInt32();
            state.PlayerAllianceCount = reader.ReadInt32();
            state.MaintenanceCredit = ReadDecimal(reader);
            state.SupportValueSinceMaintenance = ReadDecimal(reader);
            state.LastSupportDay = reader.ReadInt32();
            state.FulfilledSupportStreak = reader.ReadInt32();
            state.UltimatumsIssued = reader.ReadInt32();
            state.VariedGiftStreak = reader.ReadInt32();
            state.LastGiftCategory = (DiplomacyGoodCategory)reader.ReadInt32();
            state.LifetimeCreditedGiftValue = ReadDecimal(reader);
            if (schemaVersion >= 4)
            {
                state.TributeGoodId = reader.ReadInt32();
                state.TributeAmount = reader.ReadInt32();
                state.TributeIntervalDays = reader.ReadInt32();
                state.TributeNextDueDay = reader.ReadInt32();
                state.TributeMissedCycles = reader.ReadInt32();
                state.TributePaidCycles = reader.ReadInt32();
                state.TributeDefaults = reader.ReadInt32();
            }

            if (schemaVersion >= 5)
            {
                state.VassalRole = (DiplomacyVassalRole)reader.ReadInt32();
                if (schemaVersion >= 6)
                {
                    state.VassalPlayerId = reader.ReadInt32();
                    state.VassalOverlordPlayerId = reader.ReadInt32();
                }

                // Aus Schema 5 kommt eine Rolle OHNE benannte Gegenseite.
                // `Normalize` loescht das Verhaeltnis dann - an wen der
                // Tribut ginge, laesst sich nicht erraten ().
                state.VassalSinceDay = reader.ReadInt32();
                state.VassalEqualityRequestedDay = reader.ReadInt32();
                state.VassalStrengthEdgeDays = reader.ReadInt32();
                state.VassalReleases = reader.ReadInt32();
                state.VassalRebellions = reader.ReadInt32();
                if (schemaVersion >= 7)
                    state.VassalEqualityNextAllowedDay = reader.ReadInt32();
                if (schemaVersion >= 8)
                {
                    state.PendingVassalOverlordPlayerId = reader.ReadInt32();
                    state.PendingVassalTributeAmount = reader.ReadInt32();
                    state.PendingVassalOfferDay = reader.ReadInt32();
                }

                if (schemaVersion >= 9)
                {
                    state.SubmissionPressureDays = reader.ReadInt32();
                    state.SubmissionLastComparedDay = reader.ReadInt32();
                    state.SubmissionNextOfferDay = reader.ReadInt32();
                    state.SubmissionOfferPlayerId = reader.ReadInt32();
                    state.SubmissionOfferLordId = reader.ReadInt32();
                    state.SubmissionOfferTributeAmount = reader.ReadInt32();
                    state.SubmissionOfferCreatedDay = reader.ReadInt32();
                    state.SubmissionResponseDaysRemaining = reader.ReadInt32();
                    state.SubmissionOfferAccepted = reader.ReadBoolean();
                    state.VassalEqualityResponseDaysRemaining = reader.ReadInt32();
                    state.VassalEqualityLastResponseDay = reader.ReadInt32();
                    if (schemaVersion >= 10)
                    {
                        state.SubmissionNextDemandDay = reader.ReadInt32();
                        state.SubmissionOfferOrigin = (DiplomacySubmissionOfferOrigin)reader.ReadInt32();
                        state.TributeReassessmentPending = reader.ReadBoolean();
                        state.SubmissionAcceptancePending = reader.ReadBoolean();
                        state.PendingVassalAction = (DiplomacyVassalActionKind)reader.ReadInt32();
                        state.PendingVassalActionVassalPlayerId = reader.ReadInt32();
                        state.PendingVassalActionOverlordPlayerId = reader.ReadInt32();
                        state.PendingVassalActionSinceDay = reader.ReadInt32();
                        state.PendingVassalActionRequestDay = reader.ReadInt32();
                        state.PendingVassalActionQuoteAmount = reader.ReadInt32();
                        state.PendingVassalActionReleaseCount = reader.ReadInt32();
                    }
                    else
                    {
                        // Schema 9 had only AI-initiated quotes and one
                        // refusal/expiry cooldown. Preserve every quoted term,
                        // remaining response day and accepted team journal;
                        // migration must never perform another chance roll.
                        state.SubmissionOfferOrigin = state.SubmissionOfferPlayerId != 0 ? DiplomacySubmissionOfferOrigin.AiOffer : DiplomacySubmissionOfferOrigin.None;
                        state.SubmissionNextDemandDay = state.SubmissionNextOfferDay;
                    }
                }

                // Ein halb beschriebener Tribut ist ein Datenfehler, keine
                // Pflicht - lieber geloescht als geraten.
                if (schemaVersion < 7)
                {
                    DiplomacyVassalage.Normalize(state);
                    DiplomacyTribute.Normalize(state);
                }
            }

            if (schemaVersion >= 11)
            {
                state.PendingRelationshipDecision = (DiplomacyRelationshipDecisionKind)reader.ReadInt32();
                state.PendingRelationshipDecisionPlayerId = reader.ReadInt32();
                state.PendingRelationshipDecisionDay = reader.ReadInt32();
                state.PendingRelationshipDecisionContract = ReadRelationshipContract(reader);
            }

            if (schemaVersion >= 12)
                state.SubmissionPressureRuleVersion = reader.ReadInt32();
            else
            {
                // Earlier counts measured distant military ratios, not a siege.
                // Keep quotes/consent and journals intact for live revalidation;
                // never import those counts as physically observed pressure.
                state.SubmissionPressureDays = 0;
                state.SubmissionLastComparedDay = -1;
            }

            state.ImportedFrom = reader.ReadBoolean();
            state.MigrationSourceVersion = reader.ReadInt32();
            if (schemaVersion < 13 && state.TributeMissedCycles > 0 && DiplomacyTribute.IsActive(state) && state.TributeNextDueDay >= state.TributeIntervalDays)
            {
                state.TributeNextDueDay -= state.TributeIntervalDays;
                state.TributeMissedCycles = 1;
                state.TributeReassessmentPending = false;
            }

            int memoryCount = reader.ReadInt32();
            if (memoryCount < 0 || memoryCount > MaximumGiftMemories)
            {
                throw new InvalidDataException("diplomacy ExtendedState gift-memory count is invalid");
            }

            state.GiftMemory = new List<DiplomacyGiftMemory>(memoryCount);
            for (int index = 0; index < memoryCount; index++)
            {
                state.GiftMemory.Add(new DiplomacyGiftMemory { GoodId = reader.ReadInt32(), LastGiftDay = reader.ReadInt32(), ConsecutiveCount = reader.ReadInt32(), RecentRawValue = ReadDecimal(reader), LifetimeCreditedValue = ReadDecimal(reader), });
            }

            return state;
        }

        private static void WriteDecimal(BinaryWriter writer, decimal value)
        {
            foreach (int part in decimal.GetBits(value))
            {
                writer.Write(part);
            }
        }

        private static decimal ReadDecimal(BinaryReader reader)
        {
            try
            {
                return new decimal (new[] { reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), });
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("diplomacy ExtendedState decimal is invalid", exception);
            }
        }

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length < 1 || bytes.Length > MaximumTextBytes)
            {
                throw new InvalidDataException("diplomacy ExtendedState text length is invalid");
            }

            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 1 || length > MaximumTextBytes)
            {
                throw new InvalidDataException("diplomacy ExtendedState text length is invalid");
            }

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException();
            }

            return new UTF8Encoding(false, true).GetString(bytes);
        }
    }
}
