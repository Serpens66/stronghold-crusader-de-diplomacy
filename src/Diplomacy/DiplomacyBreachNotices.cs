using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private sealed class DeferredBreachNotice
        {
            internal string CoalitionId, Fingerprint, FirstDetail, LastDetail, Key;
            internal int[] Members;
            internal int FirstDay, LastDay, FirstCounter, LastCounter, Count;
            internal long Generation, Epoch;
            internal bool Enqueued;
        }

        // Only existing relation/coalition keys can enter this buffer: at most
        // seven AI coalitions on the eight-player map. Repeated genuine breaks
        // of one coalition are counted, not appended indefinitely or overwritten.
        // One additional frozen batch may be waiting in the shared dialog FIFO.
        private readonly List<DeferredBreachNotice> _deferredBreachNotices = new List<DeferredBreachNotice>();
        private readonly Dictionary<string, int> _breachNoticeCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        private DeferredBreachNotice _breachNoticeInFlight;
        // Called under _gate after the model commits. No UI, native read, speech
        // or logger is reached here, including when called by the native adapter.
        private void QueueBreachNotice(string coalitionId, int targetPlayer, string detail)
        {
            RelationState relation;
            CoalitionState coalition;
            if (!_mapActive || _mapDisabled || string.IsNullOrEmpty(coalitionId) || !_relations.TryGetValue(coalitionId, out relation) || relation.ExtendedState == null || !_coalitions.TryGetValue(coalitionId, out coalition) || coalition.MemberPlayerIds == null || !coalition.MemberPlayerIds.Contains(targetPlayer))
                return;
            int counter = relation.ExtendedState.BrokenAgreements;
            int previous;
            if (counter <= 0 || _breachNoticeCounters.TryGetValue(coalitionId, out previous) && counter <= previous)
                return;
            int day = Math.Max(0, _lastProcessedDay);
            DeferredBreachNotice pending = _deferredBreachNotices.Find(item => item.CoalitionId == coalitionId && item.Generation == _sessionGeneration && item.Epoch == _mapEpoch);
            if (pending == null)
            {
                pending = new DeferredBreachNotice
                {
                    CoalitionId = coalitionId,
                    Members = coalition.MemberPlayerIds.Where(player => player >= 1 && player <= 8).Distinct().OrderBy(player => player).ToArray(),
                    Generation = _sessionGeneration,
                    Epoch = _mapEpoch,
                    Fingerprint = _mapFingerprint,
                    FirstDay = day,
                    LastDay = day,
                    FirstCounter = counter,
                    LastCounter = counter,
                    FirstDetail = detail,
                    LastDetail = detail,
                    Count = 1,
                };
                _deferredBreachNotices.Add(pending);
            }
            else
            {
                pending.LastDay = day;
                pending.LastCounter = counter;
                pending.LastDetail = detail;
                if (pending.Count < int.MaxValue)
                    pending.Count++;
            }

            _breachNoticeCounters[coalitionId] = counter;
        }

        private bool IsCurrentBreachNotice(DeferredBreachNotice notice)
        {
            RelationState relation;
            return notice != null && _mapActive && !_mapDisabled && notice.Generation == _sessionGeneration && notice.Epoch == _mapEpoch && string.Equals(notice.Fingerprint, _mapFingerprint, StringComparison.Ordinal) && _relations.TryGetValue(notice.CoalitionId, out relation) && relation.ExtendedState != null // A later agreement must not erase the historical message. A
 // rollback that removed this break, however, invalidates it.
            && relation.ExtendedState.BrokenAgreements >= notice.LastCounter;
        }

        private void CompleteBreachNotice(DeferredBreachNotice notice)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_breachNoticeInFlight, notice))
                    _breachNoticeInFlight = null;
            }
        }

        private void ClearDeferredNotices()
        {
            DeferredBreachNotice old = _breachNoticeInFlight;
            _breachNoticeInFlight = null;
            _deferredBreachNotices.Clear();
            _breachNoticeCounters.Clear();
            if (old != null && old.Key != null)
                CancelDiplomacyDialog(old.Key);
        }

        private void FlushDeferredNotice()
        {
            DeferredBreachNotice notice = _breachNoticeInFlight;
            if (notice != null && !IsCurrentBreachNotice(notice))
            {
                _breachNoticeInFlight = null;
                if (notice.Key != null)
                    CancelDiplomacyDialog(notice.Key);
                notice = null;
            }

            _deferredBreachNotices.RemoveAll(item => !IsCurrentBreachNotice(item));
            if (!CanPresentDiplomacyUi || !_mapActive || _mapDisabled)
                return;
            if (notice != null && notice.Enqueued)
            {
                if (_dialogQueue.ContainsKey(notice.Key))
                    return;
                // An external cancellation/completed callback must not block
                // every later notice merely because our callback did not run.
                _breachNoticeInFlight = null;
                notice = null;
            }

            if (notice == null)
            {
                if (_deferredBreachNotices.Count == 0)
                    return;
                notice = _deferredBreachNotices[0];
                _deferredBreachNotices.RemoveAt(0);
                notice.Key = "breach_notice:" + notice.Generation.ToString(CultureInfo.InvariantCulture) + ":" + notice.Epoch.ToString(CultureInfo.InvariantCulture) + ":" + notice.CoalitionId + ":" + notice.FirstCounter.ToString(CultureInfo.InvariantCulture) + ":" + notice.LastCounter.ToString(CultureInfo.InvariantCulture);
                _breachNoticeInFlight = notice;
            }

            if (_dialogQueue.ContainsKey(notice.Key))
            {
                // Enqueue may have succeeded before a presentation exception.
                notice.Enqueued = true;
                return;
            }

            DeferredBreachNotice captured = notice;
            try
            {
                string names = string.Join(", ", notice.Members.Select(PlayerName).ToArray());
                string message = notice.Count == 1 ? string.Format(CultureInfo.InvariantCulture, Text("Euer Angriff hat das Abkommen mit {0} gebrochen (Tag {1}).", "Your attack broke the agreement with {0} (day {1})."), names, notice.FirstDay) : string.Format(CultureInfo.InvariantCulture, Text("Eure Angriffe haben {0} Abkommen mit {1} gebrochen (Tag {2}–{3}).", "Your attacks broke {0} agreements with {1} (days {2}–{3})."), notice.Count, names, notice.FirstDay, notice.LastDay);
                bool queued = EnqueueDiplomacyDialog(Text("Abkommen gebrochen", "Agreement broken"), delegate
                {
                    CompleteBreachNotice(captured);
                }, delegate
                {
                    CompleteBreachNotice(captured);
                }, message, true, null, notice.Key, delegate
                {
                    lock (_gate)
                        return IsCurrentBreachNotice(captured);
                });
                notice.Enqueued = queued || _dialogQueue.ContainsKey(notice.Key);
                if (notice.Enqueued)
                    Log("agreement", "coalition=" + notice.CoalitionId + "|status=broken|actor=player_attack|events=" + notice.Count.ToString(CultureInfo.InvariantCulture) + "|first_counter=" + notice.FirstCounter.ToString(CultureInfo.InvariantCulture) + "|last_counter=" + notice.LastCounter.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(notice.FirstDetail) ? "" : "|first=" + notice.FirstDetail) + (string.IsNullOrEmpty(notice.LastDetail) ? "" : "|last=" + notice.LastDetail));
            }
            catch
            {
                // Keep the same frozen batch and key. A pre-enqueue failure is
                // retried; an already inserted dialog is never duplicated.
                notice.Enqueued = _dialogQueue.ContainsKey(notice.Key);
            }
        }
    }
}
