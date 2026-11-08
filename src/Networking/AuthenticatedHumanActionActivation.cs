// Globally ordered host-signed activation is a separate Chore event.
// It establishes CanonicalModel.InputEpoch for every peer before any
// human action can consume per-human sequence and nonce.
using System;
using System.IO;

namespace StrongholdEuropeDE.Runtime.Networking
{
    internal sealed class AuthenticatedHumanActionActivation
    {
        internal const int BodyLength = 48;
        internal readonly Bytes SessionId;
        internal readonly ulong HostSequence, InputEpoch;
        internal AuthenticatedHumanActionActivation(Bytes session, ulong hostSequence, ulong epoch)
        {
            Require.That(session != null && session.Length == 32 && hostSequence >= 1 && hostSequence < ulong.MaxValue && epoch >= 1 && epoch < ulong.MaxValue, AdmissionReason.invalid_command);
            SessionId = session;
            HostSequence = hostSequence;
            InputEpoch = epoch;
        }

        internal Bytes EncodeBody()
        {
            Bytes bytes = AdmissionCodec.Build(w =>
            {
                w.Write(SessionId.Copy());
                w.Write(HostSequence);
                w.Write(InputEpoch);
            });
            Require.That(bytes.Length == BodyLength, AdmissionReason.malformed);
            return bytes;
        }

        internal static AuthenticatedHumanActionActivation Decode(Bytes body)
        {
            Require.That(body != null && body.Length == BodyLength, AdmissionReason.malformed);
            try
            {
                using (var r = AdmissionCodec.Reader(body))
                {
                    Bytes session = AdmissionCodec.Read(r, 32);
                    ulong sequence = r.ReadUInt64(), epoch = r.ReadUInt64();
                    AdmissionCodec.End(r);
                    return new AuthenticatedHumanActionActivation(session, sequence, epoch);
                }
            }
            catch (EndOfStreamException)
            {
                throw new AdmissionException(AdmissionReason.malformed);
            }
        }
    }
}
