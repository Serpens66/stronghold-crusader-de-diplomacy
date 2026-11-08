using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StrongholdEuropeDE.Runtime
{
    internal sealed partial class DiplomacyFeature
    {
        private bool TryReadPackageAmounts(int targetPlayer, DemandPackageLine[] lines, out List<string> missing, out bool boundsValid)
        {
            missing = new List<string>();
            boundsValid = true;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return false;
            using (lease)
            {
                foreach (DemandPackageLine line in lines)
                {
                    if (!TryReadTransferAmount(_localPlayer, line.GoodId, out line.SenderBefore) || !TryReadTransferAmount(targetPlayer, line.GoodId, out line.RecipientBefore) || line.RecipientBefore > int.MaxValue - line.Amount)
                    {
                        boundsValid = false;
                        return true;
                    }
                }
            }

            foreach (DemandPackageLine line in lines)
                if (line.SenderBefore < line.Amount)
                {
                    int shortfall = line.Amount - line.SenderBefore;
                    missing.Add(shortfall.ToString(CultureInfo.InvariantCulture) + " " + GoodName(line.GoodId, shortfall));
                }

            return true;
        }

        private void ExecuteDemandPackage(int targetPlayer, RelationState relation, DemandPackageLine[] lines)
        {
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return;
            Exception nativeFailure = null;
            using (lease)
            {
                try
                {
                    if (lines == null || lines.Length < 2 ||
                        !RequestHistoryOwnerReady() || _requestHistory == null ||
                        lines.Any(item => item.RequestId == 0))
                        throw new InvalidOperationException("Mod demand package has no captured response");

                    // Reserve every independently captured response receipt before
                    // the FIRST native write, under the existing owner/engine lease.
                    // Aborted serials remain consumed; no second gift ledger exists.
                    foreach (DemandPackageLine item in lines)
                    {
                        ulong receipt;
                        if (!_requestHistory.TryReserveModGiftJournal(
                                item.RequestId, _localPlayer, targetPlayer,
                                item.GoodId, item.Amount, _lastProcessedDay, out receipt))
                            throw new InvalidOperationException("Mod demand package response is no longer eligible");
                        item.ModGiftReceiptId = receipt;
                    }

                    _inventoryRecoveryBaseState = CaptureInventoryRecoveryBaseState();
                    _pendingPackage = new DiplomacyPendingPackage
                    {
                        SenderPlayer = _localPlayer,
                        TargetPlayer = targetPlayer,
                        GameDay = _lastProcessedDay,
                        Lines = lines.Select(line => new DiplomacyPendingTransfer { SenderPlayer = _localPlayer, TargetPlayer = targetPlayer, GoodId = line.GoodId, Amount = line.Amount, SenderBefore = line.SenderBefore, RecipientBefore = line.RecipientBefore, BoundRequestId = line.RequestId, ModGiftReceiptId = line.ModGiftReceiptId, AlliedNativeTransfer = relation.Allied, Purpose = DiplomacyTransferPurpose.Gift, DirectAtomicTransfer = true, GameDay = _lastProcessedDay, Progress = new DiplomacyTransferProgress(), }).ToList(),
                    };
                    // The coherent model is unchanged throughout the native phase.
                    // SaveData captures the live, progressively updated journal.
                    foreach (DiplomacyPendingTransfer line in _pendingPackage.Lines)
                        DiplomacyTransferRecovery.ApplyLine(line, GoodAmount, ChangeTransferAmount);
                }
                catch (Exception exception)
                {
                    nativeFailure = exception;
                    // Keep the same engine lease through native failure and its
                    // immediate refund; simulation cannot consume a half-package.
                    AbortPendingPackage();
                }
            }

            try
            {
                if (nativeFailure != null)
                    throw nativeFailure;
                CommitPendingPackage();
                Log("demand_package", "coalition=" + relation.CoalitionId + "|status=complete|items=" + lines.Length.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                // CommitPendingPackage may clear the pending journal BEFORE
                // a later stable-section/notification step throws. Absent a
                // journal, AbortPendingPackage returns null WITHOUT doing
                // any native refund: do not claim a completed rollback.
                if (nativeFailure == null && _pendingPackage == null)
                {
                    _tradeFaulted = true;
                    _goodsTransfersUnavailable = true;
                    try
                    {
                        Log("demand_package", "status=gift_commit_outcome_uncertain_quarantined|reason=" + Safe(exception.GetType().Name));
                    }
                    catch (Exception)
                    {
                    }

                    return;
                }

                string failure = AbortPendingPackage();
                Log("demand_package", "coalition=" + relation.CoalitionId + "|status=" + (failure == null ? "rolled_back" : "rollback_pending") + "|reason=" + Safe(exception.Message));
                if (failure == "native_inventory_busy")
                    _goodsTransfersUnavailable = true;
                else if (failure != null)
                    DisableGoodsTransfers("_package_rollback_pending");
                else
                    ShowMessage(Text("Das Güterpaket wurde vollständig zurückgenommen.", "The goods package was rolled back in full."));
            }
        }

        private void CommitPendingPackage()
        {
            DiplomacyPendingPackage package = _pendingPackage;
            if (package == null || package.RollbackPending)
                throw new InvalidOperationException("package has no commit intent");
            if (package.TradeRecordId != 0)
            {
                CommitNativeTradePackage(package);
                return;
            }

            // Validate/commit the complete Mod response set in a detached
            // copy BEFORE any goodwill/UI effect. One failure means NONE of
            // the saved request receipts becomes an asserted delivery.
            bool bound = package.Lines.Any(line =>
                line.BoundRequestId != 0 || line.ModGiftReceiptId != 0);
            DiplomaticRequestHistory committed = null;
            if (bound)
            {
                if (_requestHistory == null || !RequestHistoryOwnerReady() ||
                    package.Lines.Any(line => line.BoundRequestId == 0 ||
                        line.ModGiftReceiptId == 0))
                    throw new InvalidOperationException("Bound package identity is incomplete");
                committed = _requestHistory.Copy();
                foreach (DiplomacyPendingTransfer line in package.Lines)
                {
                    if (!DiplomacyTransferRecovery.HasCompletedNativeWrites(line) ||
                        line.Progress == null || !line.Progress.RecipientObserved ||
                        line.Progress.RecipientAfter != line.RecipientBefore + line.Amount ||
                        !committed.TryCommitModGiftJournal(line.BoundRequestId,
                            line.ModGiftReceiptId, line.SenderPlayer, line.TargetPlayer,
                            line.GoodId, line.Amount, _lastProcessedDay))
                        throw new InvalidOperationException("Mod package lacks exact owner receipt");
                }
            }

            DiplomacyManagedStateMemento memento = CaptureManagedStateMemento();
            bool stable = false;
            try
            {
                // Native inventories are complete; a save during any managed
                // ledger line must retain the old model and the entire journal.
                BeginStableSaveSection("package_bookkeeping", null);
                stable = true;
                if (committed != null)
                    _requestHistory = committed;
                DiplomacyTransferRecovery.CommitPackage(package, line => CompleteTransfer(package.TargetPlayer, line.GoodId, line.Amount, "demand_package"), () => RestoreManagedStateMemento(memento));
                _pendingPackage = null;
                _inventoryRecoveryBaseState = null;
                _goodsTransfersUnavailable = false;
            }
            catch
            {
                package.RollbackPending = true;
                throw;
            }
            finally
            {
                if (stable)
                    EndStableSaveSection();
                PublishCombatProtectionSnapshot();
            }

            ClearGoodsOutcome(package.TargetPlayer);
        }

        private string AbortPendingPackage()
        {
            DiplomacyPendingPackage package = _pendingPackage;
            if (package == null)
                return null;
            // on a loaded unverified PREPARED barter, a journal is NOT
            // a signed native engine receipt. Do not auto-debit/refund/commit.
            if (package.TradeRecordId != 0 && _unattestedSavedNativeTrade)
                return "unattested_loaded_native_barter_quarantine";
            package.RollbackPending = true;
            IDisposable lease;
            if (!TryEnterNativeInventorySection(out lease))
                return "native_inventory_busy";
            bool restored = false;
            try
            {
                using (lease)
                    restored = DiplomacyTransferRecovery.Rollback(package, GoodAmount, RestoreGoodAmount);
            }
            catch (Exception rollbackFailure)
            {
                // A delegate may change inventory and throw afterward.
                // Keep the full journal and forbid more transfers; do not
                // silently recast this uncertain state as a cancellation.
                _tradeFaulted = true;
                _goodsTransfersUnavailable = true;
                _inventoryRestorePending = true;
                try
                {
                    Log("demand_package", "status=rollback_exception_quarantined|reason=" + Safe(rollbackFailure.GetType().Name));
                }
                catch (Exception)
                {
                }

                return "package_rollback_exception_quarantine";
            }

            if (!restored)
            {
                _goodsTransfersUnavailable = true;
                return "package_rollback_pending";
            }

            // A verified goods refund is insufficient until its native
            // archive receipt is terminal. Keep the pending journal if the
            // receipt update throws; never lose the recovery identity first.
            if (package.TradeRecordId != 0)
                RecordNativeTradeAbort(package.TradeRecordId);
            _pendingPackage = null;
            if (_nativeTransferProbe == null)
            {
                _inventoryRecoveryBaseState = null;
                _goodsTransfersUnavailable = false;
            }

            return null;
        }

        private void RestorePendingPackage(DiplomacyPendingPackage saved)
        {
            if (saved == null)
                return;
            _pendingPackage = DiplomacyTransferRecovery.Copy(saved);
            // A genuine engine SaveData callback gives no attested durable
            // two-line Native receipt on load. Its SHA-only serialized
            // journal can be rehashed/forged. Never grant phantom goods or
            // goodwill, and do not write a guessed refund to native stocks.
            if (_pendingPackage.TradeRecordId != 0 && !StrictNativeTradeAuthorities.CanResumeSavedPreparedTrade(false, false, false))
            {
                _unattestedSavedNativeTrade = true;
                _tradeFaulted = true;
                _pendingPackage.RollbackPending = true;
                _goodsTransfersUnavailable = true;
                _inventoryRestorePending = true;
                Log("trade_authority", "status=loaded_native_barter_quarantined_no_engine_receipt");
                return;
            }

            bool bound = _pendingPackage.Lines.Any(line =>
                line.BoundRequestId != 0 || line.ModGiftReceiptId != 0);
            // Saved checkpoints are never a Cold native-world receipt for a
            // request-bound Mod response. Read BOTH loaded native cells.
            bool fullyApplied = !bound && !_pendingPackage.RollbackPending &&
                _pendingPackage.Lines.All(DiplomacyTransferRecovery.HasCompletedNativeWrites);
            if (bound || !fullyApplied)
            {
                IDisposable lease;
                if (!TryEnterNativeInventorySection(out lease))
                {
                    // Preserve commit intent while simulation owns the monitor.
                    _goodsTransfersUnavailable = true;
                    _inventoryRestorePending = true;
                    return;
                }

                try
                {
                    using (lease)
                    {
                        fullyApplied = !_pendingPackage.RollbackPending &&
                            (bound ? _pendingPackage.Lines.All(line =>
                                DiplomacyJournalRecovery.ClassifyTransfer(line,
                                    GoodAmount(line.SenderPlayer, line.GoodId),
                                    GoodAmount(line.TargetPlayer, line.GoodId)) ==
                                        DiplomacyTransferJournalDisposition.FullyApplied &&
                                DiplomacyTransferRecovery.HasCompletedNativeWrites(line) &&
                                line.Progress != null && line.Progress.RecipientObserved &&
                                line.Progress.RecipientAfter == line.RecipientBefore + line.Amount)
                            : DiplomacyTransferRecovery.IsFullyApplied(_pendingPackage, GoodAmount));
                    }
                }
                catch
                {
                    fullyApplied = false;
                }
            }

            _inventoryRestorePending = false;
            if (fullyApplied)
            {
                try
                {
                    CommitPendingPackage();
                    return;
                }
                catch (Exception exception)
                {
                    // Commit may have fully published the managed ledger and
                    // cleared the pending journal before a later exception.
                    // A missing journal is NOT evidence of rolled back goods.
                    if (_pendingPackage == null)
                    {
                        _tradeFaulted = true;
                        _goodsTransfersUnavailable = true;
                        _inventoryRestorePending = true;
                        try
                        {
                            Log("demand_package", "status=restore_commit_uncertain_no_refund_claim|reason=" + Safe(exception.GetType().Name));
                        }
                        catch (Exception)
                        {
                        }

                        return;
                    }

                    try
                    {
                        Log("demand_package", "status=restore_commit_failed|reason=" + Safe(exception.GetType().Name));
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            if (_pendingPackage == null)
            {
                // A previous branch may have terminalized it. No second
                // rollback or payment can be inferred from a null pointer.
                _tradeFaulted = true;
                _goodsTransfersUnavailable = true;
                _inventoryRestorePending = true;
                return;
            }

            string failure = AbortPendingPackage();
            if (failure == "native_inventory_busy")
                _goodsTransfersUnavailable = true;
            else if (failure != null)
                DisableGoodsTransfers(failure);
            Log("demand_package", "status=" + (failure == null ? "save_rollback_complete" : "save_rollback_pending"));
        }

        private void RetryPackageRecovery()
        {
            if (_pendingPackage == null || _featureTicks < _inventoryRecoveryRetryTick)
                return;
            // Do not retry a forged or unattested saved trade automatically.
            if (_pendingPackage.TradeRecordId != 0 && _unattestedSavedNativeTrade)
                return;
            _inventoryRecoveryRetryTick = _featureTicks + 60;
            if (!_pendingPackage.RollbackPending)
            {
                RestorePendingPackage(_pendingPackage);
                return;
            }

            if (AbortPendingPackage() == null)
                Log("demand_package", "status=deferred_rollback_complete");
        }
    }
}
