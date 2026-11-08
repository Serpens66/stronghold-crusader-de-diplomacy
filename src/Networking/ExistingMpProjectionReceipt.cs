// Receipt reads ACTUAL projected single-owner fields, never the candidate.
using System;
using System.Threading;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private sealed class OriginalMultiplayerOwnerReadbackReceipt : IVerifiedExistingProjectionReceipt
        {
            private readonly DiplomacyFeature owner;
            private readonly LegacyDiplomacySave outer;
            private readonly long originalTick, originalGeneration;
            internal OriginalMultiplayerOwnerReadbackReceipt(DiplomacyFeature feature, LegacyDiplomacySave actualOuter, long tick, long generation)
            {
                owner = feature;
                outer = actualOuter;
                originalTick = tick;
                originalGeneration = generation;
            }

            public bool OriginalEngineMonitorHeld
            {
                get
                {
                    return DiplomacyFeature.IsNativeDiplomacySectionHeld;
                }
            }

            public bool OriginalStableSaveSectionHeld
            {
                get
                {
                    return Monitor.IsEntered(owner._gate) && owner._originalOwnerProjectionStable;
                }
            }

            public bool ExistingSchema19AndLegacyProjectionValidated
            {
                get
                {
                    try
                    {
                        CanonicalTradeStatePersistence.Validate(outer);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            public bool OriginalSaveOwnerConfirmedReadback
            {
                get
                {
                    return owner._originalOwnerProjectionStable && outer.CanonicalTradeState != null && ExistingSaveReceiptGate.ExactBytes(outer.CanonicalTradeState, owner._canonicalSaved);
                }
            }

            public long OriginalSharedSimulationTick
            {
                get
                {
                    return originalTick;
                }
            }

            public long OriginalMapGeneration
            {
                get
                {
                    return originalGeneration;
                }
            }

            public string OriginalMapFingerprint
            {
                get
                {
                    return outer.MapFingerprint;
                }
            }

            public string OriginalRosterFingerprint
            {
                get
                {
                    return outer.RosterFingerprint;
                }
            }

            public string OriginalOuterMapFingerprint
            {
                get
                {
                    return outer.MapFingerprint;
                }
            }

            public string OriginalOuterRosterFingerprint
            {
                get
                {
                    return outer.RosterFingerprint;
                }
            }

            public int OriginalOuterSaveDay
            {
                get
                {
                    return outer.LastProcessedDay;
                }
            }

            public byte[] ReadCanonicalSubrecordFromExistingOwner()
            {
                return owner._canonicalSaved == null ? null : (byte[])owner._canonicalSaved.Clone();
            }

            public TradeHistoryArchive ReadOriginalOwnerArchiveProjection()
            {
                return owner._archive == null ? null : owner._archive.Copy();
            }

            public TradeIntentMailbox ReadOriginalOwnerMailboxProjection()
            {
                return owner._mailbox == null ? null : owner._mailbox.Copy();
            }
        }
    }
}
