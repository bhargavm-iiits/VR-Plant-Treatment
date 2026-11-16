using System;

namespace BTP.Treatment
{
    public enum TreatmentSubmitState
    {
        /// <summary>The finished assessment can be saved ("Update Treatment in Lab").</summary>
        Ready,
        Saving,
        Saved,
        /// <summary>No answer arrived; the assessment is kept and can be retried with the same idempotency key.</summary>
        Failed,
        /// <summary>A newer result was saved elsewhere; it has been reloaded and can be reviewed before saving again.</summary>
        Conflict,
        /// <summary>The server refused the assessment; it cannot be saved as it is.</summary>
        Rejected
    }

    /// <summary>
    /// Where a finished manual assessment goes when the case belongs to a connected XR session (the CropScan backend or
    /// its labelled fixture). Offline, <see cref="TreatmentServices.ResultSink"/> is null and nothing changes. The
    /// Treatment code only talks to this interface, so it never depends on the XR client.
    /// </summary>
    public interface ITreatmentResultSink
    {
        /// <summary>True when a connected session owns this case.</summary>
        bool Binds(CaseKey key);

        /// <summary>The learner profile that keeps this session's drafts and progress apart from other scans of the same disease.</summary>
        string LearnerName { get; }

        /// <summary>A short line for the panels, e.g. "Connected scan scan-123 (session xr-1)".</summary>
        string SessionLabel { get; }

        TreatmentSubmitState StateFor(ManualSessionRecord record);

        /// <summary>The latest save message for the results panel (empty when there is nothing to say).</summary>
        string StatusText { get; }

        /// <summary>Saves the finished assessment of <paramref name="record"/> for the bound session.</summary>
        void Submit(TreatmentCase content, ManualSessionRecord record);

        /// <summary>Raised on the main thread whenever the binding or a save state changes.</summary>
        event Action Changed;
    }
}
