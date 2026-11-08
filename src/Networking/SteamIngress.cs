// deterministic simulation/save data may not depend on raw Steam
// packet arrival order. This module owns only EPHEMERAL transport evidence.
using System;
using System.Collections.Generic;
using System.IO;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed class InboundObservation
    {
        private readonly byte[] _wire;
        internal readonly ulong VerifiedSenderSteamId;
        internal readonly long MapEpoch, SessionGeneration;
        internal InboundObservation(byte[] wire, ulong sender, long mapEpoch, long generation)
        {
            if (wire == null || wire.Length < 40 || wire.Length > TradeWireMessage.MaximumEncodedBytes || sender <= 1000 || mapEpoch <= 0 || generation <= 0)
                throw new InvalidDataException("Untrusted transport envelope");
            _wire = (byte[])wire.Clone();
            VerifiedSenderSteamId = sender;
            MapEpoch = mapEpoch;
            SessionGeneration = generation;
        }

        internal byte[] CopyWire()
        {
            return (byte[])_wire.Clone();
        }
    }

    // A callback owns the specific queue captured by its subscription.
    // Close() prevents even an already-running callback from publishing into
    // a later map or a newly registered subscription. No engine lock is taken.
    internal sealed class SteamIngress
    {
        private readonly object _sync = new object ();
        private readonly Queue<InboundObservation> _pending = new Queue<InboundObservation>();
        private readonly int _capacity;
        private bool _closed;
        private long _refusals;
        internal SteamIngress(int capacity)
        {
            if (capacity < 1 || capacity > 256)
                throw new ArgumentOutOfRangeException("capacity");
            _capacity = capacity;
        }

        internal bool TryEnqueue(byte[] wire, ulong verifiedSteamSender, long mapEpoch, long generation)
        {
            lock (_sync)
            {
                if (_closed || _pending.Count >= _capacity)
                {
                    _refusals++;
                    return false;
                }

                if (wire == null || wire.Length < 40 || wire.Length > TradeWireMessage.MaximumEncodedBytes || verifiedSteamSender <= 1000 || mapEpoch <= 0 || generation <= 0)
                {
                    _refusals++;
                    return false;
                }

                _pending.Enqueue(new InboundObservation(wire, verifiedSteamSender, mapEpoch, generation));
                return true;
            }
        }

        internal bool TryTake(out InboundObservation packet)
        {
            lock (_sync)
            {
                packet = null;
                if (_closed || _pending.Count == 0)
                    return false;
                packet = _pending.Dequeue();
                return true;
            }
        }

        internal void Clear()
        {
            lock (_sync)
            {
                _pending.Clear();
            }
        }

        internal void Close()
        {
            lock (_sync)
            {
                _closed = true;
                _pending.Clear();
            }
        }

        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    return _pending.Count;
                }
            }
        }

        internal bool IsClosed
        {
            get
            {
                lock (_sync)
                {
                    return _closed;
                }
            }
        }

        internal long Refusals
        {
            get
            {
                lock (_sync)
                {
                    return _refusals;
                }
            }
        }
    }

    // Never put this recorder into SaveData or let it settle a trade.
    // Steam events are asynchronous; only the native lockstep Chore execution
    // may eventually mutate the canonical replay ledger.
    internal sealed class TransportObserver
    {
        private TradeSteamReplay _observed = new TradeSteamReplay();
        internal void Reset()
        {
            _observed = new TradeSteamReplay();
        }

        internal ulong ObservedHighWater(int slot)
        {
            if (slot < 1 || slot > 8)
                throw new ArgumentOutOfRangeException("slot");
            return _observed.LastSeenBySender[slot];
        }

        internal bool TryObserve(TradeWireMessage packet, CanonicalQuote saved, int day, SteamDeliveryEvidence transport, string map, string roster, TradeSteamReplay canonicalReplay, out string reason)
        {
            reason = "trusted_chore_host_term_missing";
            // The canonical ledger is a READ-ONLY authority reference here. It is
            // not updated by this method even if the packet is perfectly valid.
            if (canonicalReplay == null || canonicalReplay.HostTerm == 0 || canonicalReplay.HostTerm >= ulong.MaxValue - 1)
                return false;
            ulong term = canonicalReplay.HostTerm;
            if (_observed.HostTerm != 0 && _observed.HostTerm != term)
            {
                reason = "transport_observer_host_term_changed";
                return false;
            }

            return _observed.TryAdmitExisting(packet, saved, day, transport, map, roster, term, out reason);
        }
    }
}
