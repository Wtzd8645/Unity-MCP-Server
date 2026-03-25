using System;

namespace Blanketmen.UnityMcp.Editor.Control
{
    internal enum GatewayProcessState
    {
        Stopped = 0,
        Starting = 1,
        Running = 2,
        Exited = 3,
        Error = 4,
    }

    internal readonly struct GatewayStatusSnapshot : IEquatable<GatewayStatusSnapshot>
    {
        public GatewayStatusSnapshot(
            GatewayProcessState state,
            int? managedPid,
            int? lastExitCode,
            string lastError)
        {
            State = state;
            ManagedPid = managedPid;
            LastExitCode = lastExitCode;
            LastError = lastError ?? string.Empty;
        }

        public GatewayProcessState State { get; }
        public int? ManagedPid { get; }
        public int? LastExitCode { get; }
        public string LastError { get; }

        public bool Equals(GatewayStatusSnapshot other)
        {
            return State == other.State &&
                   ManagedPid == other.ManagedPid &&
                   LastExitCode == other.LastExitCode &&
                   string.Equals(LastError, other.LastError, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is GatewayStatusSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)State;
                hash = (hash * 397) ^ (ManagedPid ?? 0);
                hash = (hash * 397) ^ (LastExitCode ?? 0);
                hash = (hash * 397) ^ (LastError != null ? LastError.GetHashCode() : 0);
                return hash;
            }
        }

        public static bool operator ==(GatewayStatusSnapshot left, GatewayStatusSnapshot right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GatewayStatusSnapshot left, GatewayStatusSnapshot right)
        {
            return !left.Equals(right);
        }
    }
}
