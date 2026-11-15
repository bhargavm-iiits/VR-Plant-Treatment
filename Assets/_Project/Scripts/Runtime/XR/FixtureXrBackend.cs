using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BTP.Treatment;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// A local stand-in for the CropScan XR API, labelled FIXTURE everywhere it shows. It serves the agreed session
    /// fixture, builds the pinned recipe from the bundled treatment content, and saves treatment results with the same
    /// rules the server must enforce: the pinned recipe version, optimistic revisions and one write per idempotency key.
    /// The state is kept in a JSON file so a restarted player reopens the saved result. <see cref="DropNextResponse"/>
    /// commits the next save but reports no response, to test the retry path.
    /// </summary>
    public sealed class FixtureXrBackend : IXrBackend
    {
        [Serializable]
        sealed class Store
        {
            public List<XrSessionData> sessions = new List<XrSessionData>();
            public List<SavedWrite> writes = new List<SavedWrite>();
        }

        [Serializable]
        sealed class SavedWrite
        {
            public string idempotencyKey;
            public string xrSessionId;
            public int revision;
        }

        readonly Func<CaseKey, TreatmentCase> localCase;
        readonly string storePath;
        readonly Store store;

        public string Label => "Fixture";
        public bool IsFixture => true;

        /// <summary>When true, the next save is committed but its response is "lost" (reported as a network failure).</summary>
        public bool DropNextResponse { get; set; }

        /// <summary>How many results were written (not replayed); tests and the panel use it to prove a retry wrote once.</summary>
        public int WriteCount => store.writes.Count;

        /// <param name="seed">The fixture session; an already stored session with the same ID keeps its saved state.</param>
        /// <param name="localCase">Loads a local case (TreatmentServices.GetCase) to build the recipe.</param>
        /// <param name="storePath">JSON file for the saved state; null keeps it in memory (tests).</param>
        public FixtureXrBackend(XrSessionData seed, Func<CaseKey, TreatmentCase> localCase, string storePath)
        {
            this.localCase = localCase;
            this.storePath = storePath;
            store = Load(storePath) ?? new Store();
            if (seed != null && Find(seed.xrSessionId) == null)
            {
                store.sessions.Add(seed.Clone());
                Persist();
            }
        }

        public static XrSessionData ParseFixture(string json, out string error)
        {
            error = null;
            try
            {
                var session = JsonUtility.FromJson<XrSessionData>(json);
                if (session == null || string.IsNullOrEmpty(session.xrSessionId))
                    error = "The fixture has no xrSessionId.";
                return error == null ? session : null;
            }
            catch (Exception exception)
            {
                error = "The fixture is not valid JSON: " + exception.Message;
                return null;
            }
        }

        public Task<XrResult<XrSessionData>> OpenSessionForScan(string scanId)
        {
            var session = string.IsNullOrEmpty(scanId) ? null : store.sessions.Find(x => x.scanId == scanId);
            return Task.FromResult(session != null
                ? XrResult<XrSessionData>.Success(session.Clone())
                : XrResult<XrSessionData>.Fail($"Scan '{scanId}' is not in the local store.", 404));
        }

        public Task<XrResult<XrLaunch>> CreateLaunch(string xrSessionId, string scanId, string idempotencyKey) =>
            Task.FromResult(XrResult<XrLaunch>.Success(new XrLaunch
            {
                launchId = "local-" + (idempotencyKey ?? Guid.NewGuid().ToString("N")), xrSessionId = xrSessionId, status = XrLaunchStatus.Opening,
            }));

        /// <summary>Stores a session unless one with its ID exists; returns the stored copy (with any saved result).</summary>
        public XrSessionData AddSessionIfMissing(XrSessionData session)
        {
            var stored = Find(session.xrSessionId);
            if (stored == null)
            {
                stored = session.Clone();
                store.sessions.Add(stored);
                Persist();
            }

            return stored.Clone();
        }

        public Task<XrResult<XrSessionData>> GetSession(string xrSessionId)
        {
            var session = Find(xrSessionId);
            return Task.FromResult(session != null
                ? XrResult<XrSessionData>.Success(session.Clone())
                : XrResult<XrSessionData>.Fail($"Session '{xrSessionId}' is not in the fixture.", 404));
        }

        public Task<XrResult<XrRecipe>> GetRecipe(string xrSessionId)
        {
            var session = Find(xrSessionId);
            if (session == null)
                return Task.FromResult(XrResult<XrRecipe>.Fail($"Session '{xrSessionId}' is not in the fixture.", 404));
            var content = localCase?.Invoke(session.Case);
            var recipe = content != null ? XrRecipeAdapter.FromCase(content, session.recipeId, session.recipeVersion, "fixture") : null;
            return Task.FromResult(recipe != null
                ? XrResult<XrRecipe>.Success(recipe)
                : XrResult<XrRecipe>.Fail($"No local treatment content for {session.Case} to build the fixture recipe.", 404));
        }

        public Task<XrSubmitResponse> SubmitTreatmentResult(XrTreatmentSubmission submission)
        {
            var response = Submit(submission);
            if (response.Status == XrSubmitStatus.Saved && DropNextResponse)
            {
                DropNextResponse = false;
                return Task.FromResult(new XrSubmitResponse { Status = XrSubmitStatus.Failed, Message = "FIXTURE: response dropped on purpose (the save was committed)." });
            }

            return Task.FromResult(response);
        }

        XrSubmitResponse Submit(XrTreatmentSubmission s)
        {
            var session = s != null ? Find(s.xrSessionId) : null;
            if (session == null)
                return new XrSubmitResponse { Status = XrSubmitStatus.Rejected, Message = "Unknown session." };

            // A repeated key returns the write it made, whatever has happened since: the result is written once.
            var key = s.IdempotencyKey;
            var previous = store.writes.Find(w => w.idempotencyKey == key);
            if (previous != null)
                return new XrSubmitResponse { Status = XrSubmitStatus.Saved, Session = session.Clone(), Message = $"Already saved (revision {previous.revision})." };

            if (s.recipeVersion != session.recipeVersion || s.recipeId != session.recipeId)
                return new XrSubmitResponse { Status = XrSubmitStatus.Rejected, Session = session.Clone(),
                    Message = $"The assessment used recipe {s.recipeId} v{s.recipeVersion}; the session pins {session.recipeId} v{session.recipeVersion}." };
            if (s.expectedRevision != session.revision)
                return new XrSubmitResponse { Status = XrSubmitStatus.Conflict, Session = session.Clone(),
                    Message = $"Revision {session.revision} was saved since this assessment started (expected {s.expectedRevision})." };

            var problem = CheckValues(s);
            if (problem != null)
                return new XrSubmitResponse { Status = XrSubmitStatus.Rejected, Session = session.Clone(), Message = problem };

            session.revision++;
            session.treatmentResult = new XrTreatmentResult
            {
                outcome = s.outcome, improvement = XrImprovement.Of(s.outcome), initialSeverity = s.initialSeverity, finalSeverity = s.finalSeverity,
                initialHealth = s.initialHealth, finalHealth = s.finalHealth, score = s.score, valid = s.valid, recipeVersion = s.recipeVersion,
                revision = session.revision, savedUtc = DateTime.UtcNow.ToString("o"),
            };
            store.writes.Add(new SavedWrite { idempotencyKey = key, xrSessionId = session.xrSessionId, revision = session.revision });
            Persist();
            return new XrSubmitResponse { Status = XrSubmitStatus.Saved, Session = session.Clone(), Message = $"Saved as revision {session.revision}." };
        }

        static string CheckValues(XrTreatmentSubmission s)
        {
            bool Unit(float v) => v >= 0f && v <= 1f && !float.IsNaN(v);
            if (!Unit(s.initialSeverity) || !Unit(s.finalSeverity) || !Unit(s.initialHealth) || !Unit(s.finalHealth) || !Unit(s.score))
                return "Severity, health and score must be between 0 and 1.";
            if (TreatmentResultManager.OutcomeLabel(s.outcome) == "Not finished")
                return $"'{s.outcome}' is not a known outcome.";
            if (s.manualSessionId <= 0)
                return "The assessment has no local session.";
            return null;
        }

        public Task<XrResult<List<XrLaunch>>> GetPendingLaunches() =>
            Task.FromResult(XrResult<List<XrLaunch>>.Success(new List<XrLaunch>()));

        public Task<XrResult<bool>> ReportLaunchStatus(string launchId, string status, string detail, bool xrReady)
        {
            Debug.Log($"[BTP][XR][FIXTURE] Launch {launchId}: {status}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)} (xrReady={xrReady})");
            return Task.FromResult(XrResult<bool>.Success(true));
        }

        /// <summary>Forgets every saved result (the "Reset fixture" action). The seed session returns to revision 0.</summary>
        public void Reset(XrSessionData seed)
        {
            store.sessions.Clear();
            store.writes.Clear();
            if (seed != null)
                store.sessions.Add(seed.Clone());
            Persist();
        }

        XrSessionData Find(string id) => string.IsNullOrEmpty(id) ? null : store.sessions.Find(x => x.xrSessionId == id);

        static Store Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                return JsonUtility.FromJson<Store>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BTP][XR] Ignoring unreadable fixture store {path}: {exception.Message}");
                return null;
            }
        }

        void Persist()
        {
            if (string.IsNullOrEmpty(storePath))
                return;
            try
            {
                var temp = storePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(store, true));
                if (File.Exists(storePath))
                    File.Delete(storePath);
                File.Move(temp, storePath);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[BTP][XR] Could not save the fixture store: {exception.Message}");
            }
        }
    }
}
