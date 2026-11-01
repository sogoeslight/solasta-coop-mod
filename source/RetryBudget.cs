using System;

namespace SolastaCoop
{
    internal sealed class RetryBudget
    {
        internal const int MaximumRetries = 3;
        internal bool Active, Pending;
        internal int Used;
        internal DateTime Due;
        internal string Reason = "";
        internal void Begin() { Active = true; Pending = false; Used = 0; Reason = ""; }
        internal void Cancel() { Active = false; Pending = false; Reason = ""; }
        internal bool Fail(string cause, DateTime now)
        {
            if (!Active || Pending) return false;
            if (!Transient(cause) || Used >= MaximumRetries) { Cancel(); return false; }
            Reason = cause; Pending = true; Due = now.AddSeconds(1 + Used);
            return true;
        }
        internal bool Take(DateTime now, bool safe)
        {
            if (!Active || !Pending || now < Due || !safe) return false;
            Used++; Pending = false; return true;
        }
        internal static bool Transient(string cause)
        {
            return cause == "ClientTimeout" || cause == "ServerTimeout" || cause == "ExceptionOnConnect" ||
                cause == "Exception" || cause == "DnsException";
        }
    }

}
