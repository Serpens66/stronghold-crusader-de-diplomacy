// strategic AI-to-AI military cooperation intentions.
// NO issued unit orders, no native hooks, no state changes. Suitable for a
// later host-approved bridge to existing PumpAllyOrders only.
using System;
using System.Linq;
using System.Collections.Generic;

namespace StrongholdEuropeDE.Runtime
{
    internal enum AiMilitaryCooperationKind : byte
    {
        DefendAlly = 1,
        CoordinateSiege = 2
    }

    internal sealed class AiMilitaryRulerFacts
    {
        internal byte PlayerId { get; set; }
        internal bool Human { get; set; }
        internal bool LastStandVerified { get; set; }
        internal bool BusyWithSiege { get; set; }
        internal int MilitaryStrength { get; set; } // verified, comparable score
        internal int CastleDefenseReserve { get; set; }
        internal int ThreatStrength { get; set; }
        internal int CommonEnemyPlayer { get; set; }
    }

    internal sealed class AiMilitaryCooperationPact
    {
        internal byte A { get; set; }
        internal byte B { get; set; }
        internal bool ReciprocalAllianceConfirmed { get; set; }
        internal int Trust { get; set; }
        internal int Grudge { get; set; }
        internal int LastCooperationDay { get; set; } = -100;
        // Positive means B owes A; negative means A owes B, exactly as
        // the reciprocal aid ledger. The caller must attest that evidence
        // for this actual pair before the planner may use even a zero value.
        internal bool HasDirectionalFavorEvidence { get; set; }
        internal int SignedOutstandingFavor { get; set; }
        internal bool RouteVerified { get; set; }
    }

    internal sealed class AiMilitaryCooperationIntent
    {
        internal readonly byte Donor, Recipient;
        internal readonly int Day, Priority, Enemy;
        internal readonly AiMilitaryCooperationKind Kind;
        internal AiMilitaryCooperationIntent(byte a, byte b, int day, int target, int score, AiMilitaryCooperationKind mode)
        {
            Donor = a;
            Recipient = b;
            Day = day;
            Enemy = target;
            Priority = score;
            Kind = mode;
        }
    }

    internal static class AiAllianceMilitaryPlanner
    {
        internal static AiMilitaryCooperationIntent[] Plan(int day, IEnumerable<AiMilitaryRulerFacts> lords, IEnumerable<AiMilitaryCooperationPact> pacts, int maxOrders = 2)
        {
            if (day < 1 || maxOrders < 1 || maxOrders > 4 || lords == null || pacts == null)
                throw new ArgumentException("Invalid military snapshot");
            var roster = lords.ToArray();
            if (roster.Any(x => x == null || x.PlayerId < 1 || x.PlayerId > 8 || x.MilitaryStrength < 0 || x.CastleDefenseReserve < 0 || x.ThreatStrength < 0 || x.CommonEnemyPlayer < 0 || x.CommonEnemyPlayer > 8) || roster.GroupBy(x => x.PlayerId).Any(g => g.Count() != 1))
                throw new ArgumentException("Invalid or duplicate force identity");
            var byId = roster.ToDictionary(x => x.PlayerId, x => x);
            var alliances = pacts.ToArray();
            if (alliances.Any(p => p == null || p.A == p.B || !byId.ContainsKey(p.A) || !byId.ContainsKey(p.B) || p.Trust < 0 || p.Trust > 100 || p.Grudge < 0 || p.Grudge > 100 || p.SignedOutstandingFavor < -400 || p.SignedOutstandingFavor > 400 || (!p.HasDirectionalFavorEvidence && p.SignedOutstandingFavor != 0) || (p.LastCooperationDay != -100 && (p.LastCooperationDay < 1 || p.LastCooperationDay > day))) || alliances.GroupBy(p => Math.Min(p.A, p.B) * 256 + Math.Max(p.A, p.B)).Any(g => g.Count() != 1))
                throw new ArgumentException("Corrupt military pact");
            var ideas = new List<AiMilitaryCooperationIntent>();
            foreach (var pact in alliances)
            {
                if (!pact.ReciprocalAllianceConfirmed || !pact.RouteVerified || !pact.HasDirectionalFavorEvidence || pact.Trust < 55 || pact.Grudge > 80 || day - (long)pact.LastCooperationDay < 3)
                    continue;
                foreach (var pair in new[]
                {
                    Tuple.Create(pact.A, pact.B),
                    Tuple.Create(pact.B, pact.A)
                }

                )
                {
                    var donor = byId[pair.Item1];
                    var beneficiary = byId[pair.Item2];
                    if (donor.Human || beneficiary.Human || donor.BusyWithSiege)
                        continue;
                    // A real castle cannot send units it needs for its own
                    // survival: require at least 1.5x protected defense.
                    long safeRequired = MilitaryReservePolicy.RequiredAtHome(
                        donor.CastleDefenseReserve, donor.ThreatStrength);
                    long surplus = (long)donor.MilitaryStrength - safeRequired;
                    if (surplus < 30)
                        continue;
                    int owedToDonor = donor.PlayerId == pact.A
                        ? Math.Max(0, pact.SignedOutstandingFavor)
                        : Math.Max(0, -pact.SignedOutstandingFavor);
                    if (beneficiary.LastStandVerified)
                    {
                        // This is an independently CONFIRMED emergency; the
                        // hostile-exposure heuristic alone is not enough.
                        if ((long)beneficiary.ThreatStrength <= beneficiary.MilitaryStrength)
                            continue;
                        int score = 400 + Math.Min(150, (int)Math.Min(150L, (long)beneficiary.ThreatStrength - beneficiary.MilitaryStrength)) + pact.Trust / 3 - Math.Min(65, owedToDonor);
                        ideas.Add(new AiMilitaryCooperationIntent(donor.PlayerId, beneficiary.PlayerId, day, 0, score, AiMilitaryCooperationKind.DefendAlly));
                    }
                    else if (donor.CommonEnemyPlayer > 0 && donor.CommonEnemyPlayer == beneficiary.CommonEnemyPlayer && donor.CommonEnemyPlayer != donor.PlayerId && donor.CommonEnemyPlayer != beneficiary.PlayerId && byId.ContainsKey((byte)donor.CommonEnemyPlayer) && pact.Trust >= 75 && pact.Grudge <= 40 && (long)beneficiary.MilitaryStrength >= (long)beneficiary.CastleDefenseReserve + 30L && surplus >= 60)
                    {
                        int score = 140 + pact.Trust / 2 + Math.Min(60, (int)Math.Min(60L, surplus / 20L));
                        ideas.Add(new AiMilitaryCooperationIntent(donor.PlayerId, beneficiary.PlayerId, day, donor.CommonEnemyPlayer, score, AiMilitaryCooperationKind.CoordinateSiege));
                    }
                }
            }

            // No two simultaneous orders from one donor, and only one
            // allied response directed at any single beneficiary.
            var donors = new HashSet<byte>();
            var targets = new HashSet<byte>();
            var pairsUsed = new HashSet<int>();
            var result = new List<AiMilitaryCooperationIntent>();
            foreach (var idea in ideas.OrderByDescending(x => x.Priority).ThenBy(x => x.Donor).ThenBy(x => x.Recipient))
            {
                int pair = Math.Min(idea.Donor, idea.Recipient) * 256 + Math.Max(idea.Donor, idea.Recipient);
                if (pairsUsed.Contains(pair) || donors.Contains(idea.Donor) || targets.Contains(idea.Recipient))
                    continue;
                pairsUsed.Add(pair);
                donors.Add(idea.Donor);
                targets.Add(idea.Recipient);
                result.Add(idea);
                if (result.Count >= maxOrders)
                    break;
            }

            return result.ToArray();
        }
    }
}
