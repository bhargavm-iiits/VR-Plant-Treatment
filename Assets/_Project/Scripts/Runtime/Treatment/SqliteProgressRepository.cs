using System;
using System.Collections.Generic;
using SQLite;
using KeepAttribute = UnityEngine.Scripting.PreserveAttribute;

namespace BTP.Treatment
{
    /// <summary>
    /// A learner's saved state for one case. Only stable IDs are stored (step, target, equipment
    /// and checkpoint IDs), never scene-object references; the scene is rebuilt from this record.
    /// </summary>
    public sealed class CaseProgress
    {
        public CaseKey Key;
        public int ContentVersion;
        /// <summary>The treatment route the learner chose at a decision step, or null.</summary>
        public string Route;
        public TreatmentOutcome Outcome;
        public bool Completed;
        public DateTime UpdatedUtc;

        public readonly HashSet<string> CompletedSteps = new HashSet<string>();
        /// <summary>Target ID to state ("inspected", "removed", "disposed", "covered", "contained", ...).</summary>
        public readonly Dictionary<string, string> TargetStates = new Dictionary<string, string>();
        /// <summary>Equipment key to state ("vent" = "open", "tool:shears" = "contaminated", "carried" = target ID).</summary>
        public readonly Dictionary<string, string> Equipment = new Dictionary<string, string>();
        /// <summary>Per-step set of targets that already counted towards it, so one leaf cannot count twice.</summary>
        public readonly Dictionary<string, HashSet<string>> StepTargets = new Dictionary<string, HashSet<string>>();
        /// <summary>Follow-up checkpoint ID to the recorded finding.</summary>
        public readonly Dictionary<string, string> Observations = new Dictionary<string, string>();

        public CaseProgress(CaseKey key, int contentVersion)
        {
            Key = key;
            ContentVersion = contentVersion;
        }

        public bool HasStarted => CompletedSteps.Count > 0 || TargetStates.Count > 0 || Equipment.Count > 0;

        public string GetEquipment(string key) => Equipment.TryGetValue(key, out var value) ? value : null;

        public string GetTargetState(string targetId) => TargetStates.TryGetValue(targetId, out var value) ? value : null;
    }

    public interface IProgressRepository : IDisposable
    {
        bool IsReady { get; }
        string Error { get; }
        /// <summary>The saved progress for <paramref name="key"/>, or a fresh record when none exists.</summary>
        CaseProgress Load(CaseKey key, int contentVersion);
        /// <summary>Saves the whole record in one transaction.</summary>
        bool Save(CaseProgress progress);
        /// <summary>Deletes only this case's progress.</summary>
        bool Reset(CaseKey key);
        /// <summary>The most recently updated saved case for a plant, or null.</summary>
        CaseProgress LatestForPlant(string plantId);
        string GetSetting(string key);
        void SetSetting(string key, string value);

        // ---- Manual treatment mode (schema version 2). Sessions are inserted or updated, never deleted.

        /// <summary>The schema version stored in the file after opening (2 once migrated).</summary>
        int StoredSchemaVersion { get; }
        IReadOnlyList<LearnerRecord> ListLearners();
        /// <summary>Creates a learner, or returns the existing one with the same name (case-insensitive).</summary>
        LearnerRecord CreateLearner(string name);
        LearnerRecord GetLearner(long learnerId);
        /// <summary>Inserts (SessionId 0) or updates a session with its selections, new history entries and stage times, in one transaction.</summary>
        bool SaveManualSession(ManualSessionRecord record);
        ManualSessionRecord LoadManualSession(long sessionId);
        /// <summary>The newest unfinished, non-abandoned session of a learner for one case, or null.</summary>
        ManualSessionRecord LatestUnfinishedSession(long learnerId, CaseKey key);
        /// <summary>A learner's sessions, newest first (headers only).</summary>
        IReadOnlyList<ManualSessionRecord> ListManualSessions(long learnerId);
    }

    /// <summary>
    /// Learner progress in a writable SQLite file under Application.persistentDataPath. It is a
    /// separate file from the read-only content database, so content updates never overwrite it.
    /// </summary>
    public sealed class SqliteProgressRepository : IProgressRepository
    {
        /// <summary>1: step-lesson progress. 2: adds learners and manual treatment sessions.</summary>
        public const int SchemaVersion = 2;
        SQLiteConnection db;

        /// <param name="path">Database file path, or ":memory:" for tests.</param>
        public SqliteProgressRepository(string path)
        {
            try
            {
                db = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                CreateSchema();
                IsReady = true;
            }
            catch (Exception exception)
            {
                Error = "Progress could not be opened: " + exception.Message;
                db?.Dispose();
                db = null;
            }
        }

        public bool IsReady { get; private set; }
        public string Error { get; private set; }

        void CreateSchema()
        {
            db.Execute("CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT)");
            db.Execute("CREATE TABLE IF NOT EXISTS case_progress (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, content_version INTEGER, " +
                       "route TEXT, outcome TEXT, completed INTEGER NOT NULL DEFAULT 0, updated_utc INTEGER NOT NULL, PRIMARY KEY (plant_id, disease_id))");
            db.Execute("CREATE TABLE IF NOT EXISTS progress_steps (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, step_id TEXT NOT NULL, " +
                       "PRIMARY KEY (plant_id, disease_id, step_id))");
            db.Execute("CREATE TABLE IF NOT EXISTS progress_step_targets (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, step_id TEXT NOT NULL, " +
                       "target_id TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, step_id, target_id))");
            db.Execute("CREATE TABLE IF NOT EXISTS progress_targets (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, target_id TEXT NOT NULL, " +
                       "state TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, target_id))");
            db.Execute("CREATE TABLE IF NOT EXISTS progress_equipment (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, equipment_key TEXT NOT NULL, " +
                       "state TEXT NOT NULL, PRIMARY KEY (plant_id, disease_id, equipment_key))");
            db.Execute("CREATE TABLE IF NOT EXISTS progress_observations (plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, checkpoint_id TEXT NOT NULL, " +
                       "finding TEXT, PRIMARY KEY (plant_id, disease_id, checkpoint_id))");
            Migrate();
        }

        public int StoredSchemaVersion { get; private set; }

        /// <summary>
        /// Reads the stored version first and only moves it forward: v1 files (step-lesson progress) gain the manual-mode
        /// tables in one transaction; existing rows are never touched. A file written by a newer build keeps its version.
        /// </summary>
        void Migrate()
        {
            var stored = int.TryParse(db.ExecuteScalar<string>("SELECT value FROM settings WHERE key = 'schema_version'"), out var parsed) ? parsed : 0;
            db.RunInTransaction(() =>
            {
                CreateManualTables();
                if (stored < SchemaVersion)
                    db.Execute("INSERT OR REPLACE INTO settings (key, value) VALUES ('schema_version', ?)", SchemaVersion.ToString());
            });
            StoredSchemaVersion = Math.Max(stored, SchemaVersion);
        }

        void CreateManualTables()
        {
            db.Execute("CREATE TABLE IF NOT EXISTS learners (learner_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE COLLATE NOCASE, " +
                       "created_utc INTEGER NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS manual_sessions (session_id INTEGER PRIMARY KEY AUTOINCREMENT, learner_id INTEGER NOT NULL, " +
                       "lesson_id TEXT NOT NULL, plant_id TEXT NOT NULL, disease_id TEXT NOT NULL, content_version INTEGER, started_utc INTEGER NOT NULL, " +
                       "updated_utc INTEGER NOT NULL, completed_utc INTEGER, initial_severity REAL NOT NULL, final_severity REAL, initial_health REAL NOT NULL, " +
                       "final_health REAL, current_severity REAL, current_health REAL, applied_score REAL, applied_valid INTEGER NOT NULL DEFAULT 0, " +
                       "apply_count INTEGER NOT NULL DEFAULT 0, health_checks INTEGER NOT NULL DEFAULT 0, outcome TEXT, completed INTEGER NOT NULL DEFAULT 0, " +
                       "current_stage INTEGER NOT NULL DEFAULT 0, elapsed_seconds REAL NOT NULL DEFAULT 0)");
            db.Execute("CREATE INDEX IF NOT EXISTS manual_sessions_learner ON manual_sessions (learner_id, plant_id, disease_id, completed)");
            db.Execute("CREATE TABLE IF NOT EXISTS manual_selections (session_id INTEGER NOT NULL, control_id TEXT NOT NULL, value REAL NOT NULL, " +
                       "touched INTEGER NOT NULL DEFAULT 0, applied_value REAL, PRIMARY KEY (session_id, control_id))");
            db.Execute("CREATE TABLE IF NOT EXISTS manual_actions (session_id INTEGER NOT NULL, seq INTEGER NOT NULL, utc INTEGER NOT NULL, stage INTEGER NOT NULL, " +
                       "action TEXT NOT NULL, control_id TEXT, value REAL, accepted INTEGER NOT NULL, message TEXT, PRIMARY KEY (session_id, seq))");
            db.Execute("CREATE TABLE IF NOT EXISTS manual_stages (session_id INTEGER NOT NULL, stage INTEGER NOT NULL, completed_utc INTEGER NOT NULL, " +
                       "PRIMARY KEY (session_id, stage))");
        }

        // ------------------------------------------------------------------ learners

        public IReadOnlyList<LearnerRecord> ListLearners()
        {
            var list = new List<LearnerRecord>();
            if (!IsReady)
                return list;
            foreach (var row in db.Query<LearnerRow>("SELECT learner_id, name, created_utc FROM learners ORDER BY learner_id"))
                list.Add(new LearnerRecord { LearnerId = row.learner_id, Name = row.name, CreatedUtc = new DateTime(row.created_utc, DateTimeKind.Utc) });
            return list;
        }

        public LearnerRecord CreateLearner(string name)
        {
            if (!IsReady)
                return null;
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 40)
            {
                Error = "A learner name needs 1 to 40 characters.";
                return null;
            }

            try
            {
                db.RunInTransaction(() =>
                    db.Execute("INSERT OR IGNORE INTO learners (name, created_utc) VALUES (?, ?)", name, DateTime.UtcNow.Ticks));
                var rows = db.Query<LearnerRow>("SELECT learner_id, name, created_utc FROM learners WHERE name = ? COLLATE NOCASE", name);
                return rows.Count == 0 ? null : new LearnerRecord { LearnerId = rows[0].learner_id, Name = rows[0].name, CreatedUtc = new DateTime(rows[0].created_utc, DateTimeKind.Utc) };
            }
            catch (Exception exception)
            {
                Error = "The learner could not be saved: " + exception.Message;
                return null;
            }
        }

        public LearnerRecord GetLearner(long learnerId)
        {
            if (!IsReady)
                return null;
            var rows = db.Query<LearnerRow>("SELECT learner_id, name, created_utc FROM learners WHERE learner_id = ?", learnerId);
            return rows.Count == 0 ? null : new LearnerRecord { LearnerId = rows[0].learner_id, Name = rows[0].name, CreatedUtc = new DateTime(rows[0].created_utc, DateTimeKind.Utc) };
        }

        // ------------------------------------------------------------------ manual sessions

        public bool SaveManualSession(ManualSessionRecord r)
        {
            if (!IsReady || r == null || !r.Key.IsValid)
                return false;
            try
            {
                db.RunInTransaction(() =>
                {
                    object[] header =
                    {
                        r.LearnerId, r.LessonId, r.Key.PlantId, r.Key.DiseaseId, r.ContentVersion, r.StartedUtc.Ticks, r.UpdatedUtc.Ticks,
                        r.CompletedUtc?.Ticks, r.InitialSeverity, r.FinalSeverity, r.InitialHealth, r.FinalHealth, r.CurrentSeverity, r.CurrentHealth,
                        r.AppliedScore, r.AppliedValid ? 1 : 0, r.ApplyCount, r.HealthChecks, r.Outcome, r.Completed ? 1 : 0, (int)r.CurrentStage, r.ElapsedSeconds
                    };
                    const string columns = "learner_id, lesson_id, plant_id, disease_id, content_version, started_utc, updated_utc, completed_utc, " +
                                           "initial_severity, final_severity, initial_health, final_health, current_severity, current_health, applied_score, " +
                                           "applied_valid, apply_count, health_checks, outcome, completed, current_stage, elapsed_seconds";
                    var updated = 0;
                    if (r.SessionId > 0)
                    {
                        var set = string.Join(", ", Array.ConvertAll(columns.Split(','), c => c.Trim() + " = ?"));
                        var args = new object[header.Length + 1];
                        header.CopyTo(args, 0);
                        args[header.Length] = r.SessionId;
                        updated = db.Execute($"UPDATE manual_sessions SET {set} WHERE session_id = ?", args);
                    }

                    if (updated == 0)
                    {
                        var marks = string.Join(", ", Array.ConvertAll(columns.Split(','), _ => "?"));
                        db.Execute($"INSERT INTO manual_sessions ({columns}) VALUES ({marks})", header);
                        r.SessionId = db.ExecuteScalar<long>("SELECT last_insert_rowid()");
                    }

                    foreach (var pair in r.Selections)
                    {
                        object applied = r.AppliedValues.TryGetValue(pair.Key, out var value) ? (object)value : null;
                        var changed = db.Execute("UPDATE manual_selections SET value = ?, touched = ?, applied_value = ? WHERE session_id = ? AND control_id = ?",
                            pair.Value.Value, pair.Value.Touched ? 1 : 0, applied, r.SessionId, pair.Key);
                        if (changed == 0)
                            db.Execute("INSERT INTO manual_selections (session_id, control_id, value, touched, applied_value) VALUES (?, ?, ?, ?, ?)",
                                r.SessionId, pair.Key, pair.Value.Value, pair.Value.Touched ? 1 : 0, applied);
                    }

                    // History is append-only: entries already stored keep their original row.
                    foreach (var a in r.Actions)
                        db.Execute("INSERT OR IGNORE INTO manual_actions (session_id, seq, utc, stage, action, control_id, value, accepted, message) " +
                                   "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)", r.SessionId, a.Seq, a.Utc.Ticks, (int)a.Stage, a.Action, a.ControlId, a.Value,
                            a.Accepted ? 1 : 0, a.Message);
                    foreach (var pair in r.StagesCompleted)
                        db.Execute("INSERT OR IGNORE INTO manual_stages (session_id, stage, completed_utc) VALUES (?, ?, ?)", r.SessionId, (int)pair.Key,
                            pair.Value.Ticks);
                });
                return true;
            }
            catch (Exception exception)
            {
                Error = "The manual session could not be saved: " + exception.Message;
                return false;
            }
        }

        const string SessionColumns = "session_id, learner_id, plant_id, disease_id, content_version, started_utc, updated_utc, completed_utc, initial_severity, " +
                                      "final_severity, initial_health, final_health, current_severity, current_health, applied_score, applied_valid, apply_count, " +
                                      "health_checks, outcome, completed, current_stage, elapsed_seconds";

        static ManualSessionRecord Header(SessionRow s) => new ManualSessionRecord
        {
            SessionId = s.session_id, LearnerId = s.learner_id, Key = new CaseKey(s.plant_id, s.disease_id), ContentVersion = s.content_version,
            StartedUtc = new DateTime(s.started_utc, DateTimeKind.Utc), UpdatedUtc = new DateTime(s.updated_utc, DateTimeKind.Utc),
            CompletedUtc = s.completed_utc.HasValue ? new DateTime(s.completed_utc.Value, DateTimeKind.Utc) : (DateTime?)null,
            InitialSeverity = (float)s.initial_severity, FinalSeverity = (float?)s.final_severity, InitialHealth = (float)s.initial_health,
            FinalHealth = (float?)s.final_health, CurrentSeverity = (float)(s.current_severity ?? s.initial_severity),
            CurrentHealth = (float)(s.current_health ?? s.initial_health), AppliedScore = (float?)s.applied_score, AppliedValid = s.applied_valid != 0,
            ApplyCount = s.apply_count, HealthChecks = s.health_checks, Outcome = s.outcome, Completed = s.completed != 0,
            CurrentStage = (ManualStage)s.current_stage, ElapsedSeconds = (float)s.elapsed_seconds,
        };

        public ManualSessionRecord LoadManualSession(long sessionId)
        {
            if (!IsReady)
                return null;
            var rows = db.Query<SessionRow>($"SELECT {SessionColumns} FROM manual_sessions WHERE session_id = ?", sessionId);
            if (rows.Count == 0)
                return null;
            var r = Header(rows[0]);
            foreach (var row in db.Query<SelectionRow>("SELECT control_id, value, touched, applied_value FROM manual_selections WHERE session_id = ?", sessionId))
            {
                r.Selections[row.control_id] = new ControlSelection { Value = (float)row.value, Touched = row.touched != 0 };
                if (row.applied_value.HasValue)
                    r.AppliedValues[row.control_id] = (float)row.applied_value.Value;
            }

            foreach (var row in db.Query<ActionRow>("SELECT seq, utc, stage, action, control_id, value, accepted, message FROM manual_actions " +
                                                    "WHERE session_id = ? ORDER BY seq", sessionId))
                r.Actions.Add(new ManualActionRecord
                {
                    Seq = row.seq, Utc = new DateTime(row.utc, DateTimeKind.Utc), Stage = (ManualStage)row.stage, Action = row.action, ControlId = row.control_id,
                    Value = (float?)row.value, Accepted = row.accepted != 0, Message = row.message,
                });
            foreach (var row in db.Query<StageRow>("SELECT stage, completed_utc FROM manual_stages WHERE session_id = ?", sessionId))
                r.StagesCompleted[(ManualStage)row.stage] = new DateTime(row.completed_utc, DateTimeKind.Utc);
            return r;
        }

        public ManualSessionRecord LatestUnfinishedSession(long learnerId, CaseKey key)
        {
            if (!IsReady || !key.IsValid)
                return null;
            var id = db.ExecuteScalar<long>("SELECT session_id FROM manual_sessions WHERE learner_id = ? AND plant_id = ? AND disease_id = ? AND completed = 0 " +
                                            "AND (outcome IS NULL OR outcome <> ?) AND current_stage > 0 ORDER BY updated_utc DESC, session_id DESC LIMIT 1",
                learnerId, key.PlantId, key.DiseaseId, ManualSessionRecord.OutcomeAbandoned);
            return id > 0 ? LoadManualSession(id) : null;
        }

        public IReadOnlyList<ManualSessionRecord> ListManualSessions(long learnerId)
        {
            var list = new List<ManualSessionRecord>();
            if (!IsReady)
                return list;
            foreach (var row in db.Query<SessionRow>($"SELECT {SessionColumns} FROM manual_sessions WHERE learner_id = ? ORDER BY updated_utc DESC, session_id DESC",
                         learnerId))
                list.Add(Header(row));
            return list;
        }

        public CaseProgress Load(CaseKey key, int contentVersion)
        {
            var progress = new CaseProgress(key, contentVersion);
            if (!IsReady || !key.IsValid)
                return progress;
            var p = key.PlantId;
            var d = key.DiseaseId;
            var header = db.Query<HeaderRow>("SELECT plant_id, disease_id, content_version, route, outcome, completed, updated_utc " +
                                             "FROM case_progress WHERE plant_id = ? AND disease_id = ?", p, d);
            if (header.Count == 0)
                return progress;
            Fill(progress, header[0]);
            return progress;
        }

        void Fill(CaseProgress progress, HeaderRow header)
        {
            var p = header.plant_id;
            var d = header.disease_id;
            progress.Route = header.route;
            progress.Outcome = Enum.TryParse(header.outcome, out TreatmentOutcome outcome) ? outcome : TreatmentOutcome.None;
            progress.Completed = header.completed != 0;
            progress.UpdatedUtc = new DateTime(header.updated_utc, DateTimeKind.Utc);
            foreach (var row in db.Query<ValueRow>("SELECT step_id AS a, '' AS b FROM progress_steps WHERE plant_id = ? AND disease_id = ?", p, d))
                progress.CompletedSteps.Add(row.a);
            foreach (var row in db.Query<ValueRow>("SELECT step_id AS a, target_id AS b FROM progress_step_targets WHERE plant_id = ? AND disease_id = ?", p, d))
            {
                if (!progress.StepTargets.TryGetValue(row.a, out var set))
                    progress.StepTargets[row.a] = set = new HashSet<string>();
                set.Add(row.b);
            }
            foreach (var row in db.Query<ValueRow>("SELECT target_id AS a, state AS b FROM progress_targets WHERE plant_id = ? AND disease_id = ?", p, d))
                progress.TargetStates[row.a] = row.b;
            foreach (var row in db.Query<ValueRow>("SELECT equipment_key AS a, state AS b FROM progress_equipment WHERE plant_id = ? AND disease_id = ?", p, d))
                progress.Equipment[row.a] = row.b;
            foreach (var row in db.Query<ValueRow>("SELECT checkpoint_id AS a, finding AS b FROM progress_observations WHERE plant_id = ? AND disease_id = ?", p, d))
                progress.Observations[row.a] = row.b;
        }

        public bool Save(CaseProgress progress)
        {
            if (!IsReady || progress == null || !progress.Key.IsValid)
                return false;
            var p = progress.Key.PlantId;
            var d = progress.Key.DiseaseId;
            progress.UpdatedUtc = DateTime.UtcNow;
            try
            {
                db.RunInTransaction(() =>
                {
                    DeleteRows(p, d);
                    db.Execute("INSERT INTO case_progress (plant_id, disease_id, content_version, route, outcome, completed, updated_utc) " +
                               "VALUES (?, ?, ?, ?, ?, ?, ?)", p, d, progress.ContentVersion, progress.Route, progress.Outcome.ToString(),
                        progress.Completed ? 1 : 0, progress.UpdatedUtc.Ticks);
                    foreach (var step in progress.CompletedSteps)
                        db.Execute("INSERT INTO progress_steps (plant_id, disease_id, step_id) VALUES (?, ?, ?)", p, d, step);
                    foreach (var pair in progress.StepTargets)
                    foreach (var target in pair.Value)
                        db.Execute("INSERT INTO progress_step_targets (plant_id, disease_id, step_id, target_id) VALUES (?, ?, ?, ?)", p, d, pair.Key, target);
                    foreach (var pair in progress.TargetStates)
                        db.Execute("INSERT INTO progress_targets (plant_id, disease_id, target_id, state) VALUES (?, ?, ?, ?)", p, d, pair.Key, pair.Value);
                    foreach (var pair in progress.Equipment)
                        db.Execute("INSERT INTO progress_equipment (plant_id, disease_id, equipment_key, state) VALUES (?, ?, ?, ?)", p, d, pair.Key, pair.Value);
                    foreach (var pair in progress.Observations)
                        db.Execute("INSERT INTO progress_observations (plant_id, disease_id, checkpoint_id, finding) VALUES (?, ?, ?, ?)", p, d, pair.Key, pair.Value);
                });
                return true;
            }
            catch (Exception exception)
            {
                Error = "Progress could not be saved: " + exception.Message;
                return false;
            }
        }

        void DeleteRows(string p, string d)
        {
            foreach (var table in new[] { "case_progress", "progress_steps", "progress_step_targets", "progress_targets", "progress_equipment", "progress_observations" })
                db.Execute($"DELETE FROM {table} WHERE plant_id = ? AND disease_id = ?", p, d);
        }

        public bool Reset(CaseKey key)
        {
            if (!IsReady || !key.IsValid)
                return false;
            try
            {
                db.RunInTransaction(() => DeleteRows(key.PlantId, key.DiseaseId));
                return true;
            }
            catch (Exception exception)
            {
                Error = "Progress could not be reset: " + exception.Message;
                return false;
            }
        }

        public CaseProgress LatestForPlant(string plantId)
        {
            if (!IsReady || string.IsNullOrEmpty(plantId))
                return null;
            var rows = db.Query<HeaderRow>("SELECT plant_id, disease_id, content_version, route, outcome, completed, updated_utc FROM case_progress " +
                                           "WHERE plant_id = ? ORDER BY updated_utc DESC LIMIT 1", plantId);
            if (rows.Count == 0)
                return null;
            var progress = new CaseProgress(new CaseKey(rows[0].plant_id, rows[0].disease_id), rows[0].content_version);
            Fill(progress, rows[0]);
            return progress;
        }

        public string GetSetting(string key)
        {
            if (!IsReady)
                return null;
            return db.ExecuteScalar<string>("SELECT value FROM settings WHERE key = ?", key);
        }

        public void SetSetting(string key, string value)
        {
            if (!IsReady)
                return;
            db.Execute("INSERT OR REPLACE INTO settings (key, value) VALUES (?, ?)", key, value);
        }

        public void Dispose()
        {
            db?.Dispose();
            db = null;
            IsReady = false;
        }

#pragma warning disable IDE1006, CS0649
        [Keep] sealed class HeaderRow
        {
            [Keep] public string plant_id { get; set; }
            [Keep] public string disease_id { get; set; }
            [Keep] public int content_version { get; set; }
            [Keep] public string route { get; set; }
            [Keep] public string outcome { get; set; }
            [Keep] public int completed { get; set; }
            [Keep] public long updated_utc { get; set; }
        }
        [Keep] sealed class ValueRow
        {
            [Keep] public string a { get; set; }
            [Keep] public string b { get; set; }
        }
        [Keep] sealed class LearnerRow
        {
            [Keep] public long learner_id { get; set; }
            [Keep] public string name { get; set; }
            [Keep] public long created_utc { get; set; }
        }
        [Keep] sealed class SessionRow
        {
            [Keep] public long session_id { get; set; }
            [Keep] public long learner_id { get; set; }
            [Keep] public string plant_id { get; set; }
            [Keep] public string disease_id { get; set; }
            [Keep] public int content_version { get; set; }
            [Keep] public long started_utc { get; set; }
            [Keep] public long updated_utc { get; set; }
            [Keep] public long? completed_utc { get; set; }
            [Keep] public double initial_severity { get; set; }
            [Keep] public double? final_severity { get; set; }
            [Keep] public double initial_health { get; set; }
            [Keep] public double? final_health { get; set; }
            [Keep] public double? current_severity { get; set; }
            [Keep] public double? current_health { get; set; }
            [Keep] public double? applied_score { get; set; }
            [Keep] public int applied_valid { get; set; }
            [Keep] public int apply_count { get; set; }
            [Keep] public int health_checks { get; set; }
            [Keep] public string outcome { get; set; }
            [Keep] public int completed { get; set; }
            [Keep] public int current_stage { get; set; }
            [Keep] public double elapsed_seconds { get; set; }
        }
        [Keep] sealed class SelectionRow
        {
            [Keep] public string control_id { get; set; }
            [Keep] public double value { get; set; }
            [Keep] public int touched { get; set; }
            [Keep] public double? applied_value { get; set; }
        }
        [Keep] sealed class ActionRow
        {
            [Keep] public int seq { get; set; }
            [Keep] public long utc { get; set; }
            [Keep] public int stage { get; set; }
            [Keep] public string action { get; set; }
            [Keep] public string control_id { get; set; }
            [Keep] public double? value { get; set; }
            [Keep] public int accepted { get; set; }
            [Keep] public string message { get; set; }
        }
        [Keep] sealed class StageRow
        {
            [Keep] public int stage { get; set; }
            [Keep] public long completed_utc { get; set; }
        }
#pragma warning restore IDE1006, CS0649
    }
}