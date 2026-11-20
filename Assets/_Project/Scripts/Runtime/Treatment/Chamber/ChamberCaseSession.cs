namespace BTP.Treatment
{
    /// <summary>
    /// Identifies one selection the chamber is showing: (specimen, plant, disease, scenario) plus a generation number that
    /// grows with every new selection. Every profile and evaluate request carries the ticket it was made for.
    /// </summary>
    public sealed class SelectionTicket
    {
        public readonly int Generation;
        public readonly string SpecimenId;
        public readonly string PlantId;
        public readonly string DiseaseId;
        public readonly string Scenario;

        public SelectionTicket(int generation, string specimenId, string plantId, string diseaseId, string scenario)
        {
            Generation = generation;
            SpecimenId = specimenId ?? "";
            PlantId = plantId ?? "";
            DiseaseId = diseaseId ?? "";
            Scenario = string.IsNullOrEmpty(scenario) ? "standard" : scenario;
        }

        public bool SameCase(string plantId, string diseaseId) => PlantId == (plantId ?? "") && DiseaseId == (diseaseId ?? "");

        public override string ToString() => $"#{Generation} {SpecimenId} ({PlantId}/{DiseaseId}, {Scenario})";
    }

    /// <summary>
    /// The race guard of the chamber station (pure; unit tested). A selection change starts a new ticket; a response is
    /// accepted only for the latest ticket, so a late profile or evaluation for an earlier specimen is discarded. Apply and
    /// Check Health are allowed only when the displayed specimen and the loaded profile are the same case as the current
    /// ticket and no request is running; Check Health also needs settings applied for that profile.
    /// </summary>
    public sealed class ChamberCaseSession
    {
        int generation;

        public SelectionTicket Current { get; private set; }
        /// <summary>The profile loaded for <see cref="Current"/>, or null.</summary>
        public TreatmentProfile Profile { get; private set; }
        /// <summary>The ticket whose specimen the chamber shows (set once the chamber is fitted).</summary>
        public SelectionTicket Displayed { get; private set; }
        /// <summary>The settings captured by the last Apply for this profile, or null.</summary>
        public TreatmentSettings Applied { get; private set; }

        public bool Loading { get; private set; }
        public bool Applying { get; private set; }
        public bool Checking { get; private set; }
        /// <summary>The ticket of the running evaluate request, or null.</summary>
        public SelectionTicket CheckTicket { get; private set; }

        public bool Busy => Loading || Applying || Checking;

        /// <summary>Starts a new selection: everything loaded for the previous one is dropped.</summary>
        public SelectionTicket Begin(string specimenId, string plantId, string diseaseId, string scenario)
        {
            generation++;
            Current = new SelectionTicket(generation, specimenId, plantId, diseaseId, scenario);
            Profile = null;
            Applied = null;
            Displayed = null;
            Loading = false;
            Applying = false;
            Checking = false;
            CheckTicket = null;
            return Current;
        }

        /// <summary>Whether a response made for <paramref name="ticket"/> belongs to the latest selection.</summary>
        public bool IsCurrent(SelectionTicket ticket) => ticket != null && Current != null && ticket.Generation == Current.Generation;

        /// <summary>The chamber shows the specimen of <paramref name="ticket"/> (ignored when stale).</summary>
        public bool SpecimenShown(SelectionTicket ticket)
        {
            if (!IsCurrent(ticket))
                return false;
            Displayed = ticket;
            return true;
        }

        /// <summary>A profile request for the current ticket started.</summary>
        public void BeginLoad(SelectionTicket ticket)
        {
            if (IsCurrent(ticket))
                Loading = true;
        }

        /// <summary>
        /// A profile response arrived. Accepted (true) only for the current ticket and only when the profile is the
        /// ticket's case; a stale or mismatched response leaves the session unchanged.
        /// </summary>
        public bool AcceptProfile(SelectionTicket ticket, TreatmentProfile profile)
        {
            if (!IsCurrent(ticket))
                return false;
            Loading = false;
            if (profile == null || !ticket.SameCase(profile.PlantId, profile.DiseaseId))
                return false;
            Profile = profile;
            Applied = null;
            return true;
        }

        /// <summary>A profile request for the current ticket failed (404, network).</summary>
        public bool FailLoad(SelectionTicket ticket)
        {
            if (!IsCurrent(ticket))
                return false;
            Loading = false;
            Profile = null;
            return true;
        }

        bool SameCaseEverywhere =>
            Current != null && Profile != null && Displayed != null && Displayed.Generation == Current.Generation &&
            Current.SameCase(Profile.PlantId, Profile.DiseaseId) && Displayed.SameCase(Profile.PlantId, Profile.DiseaseId);

        /// <summary>Apply is allowed: the shown specimen and the loaded profile are the current case and nothing is running.</summary>
        public bool CanApply => SameCaseEverywhere && !Busy;

        /// <summary>Check Health is allowed: as Apply, and settings were applied for this profile.</summary>
        public bool CanCheck => CanApply && Applied != null;

        /// <summary>Captures the applied settings (a copy); false when Apply is not allowed now.</summary>
        public bool Apply(TreatmentSettings settings)
        {
            if (!CanApply || settings == null)
                return false;
            Applied = settings.Clone();
            return true;
        }

        /// <summary>Restores applied settings saved for this specimen (no visual replay is implied).</summary>
        public void RestoreApplied(TreatmentSettings settings)
        {
            if (Profile != null && settings != null)
                Applied = settings.Clone();
        }

        public void SetApplying(bool applying) => Applying = applying && Current != null;

        /// <summary>Starts Check Health for the current ticket; null when it is not allowed.</summary>
        public SelectionTicket BeginCheck()
        {
            if (!CanCheck)
                return null;
            Checking = true;
            CheckTicket = Current;
            return Current;
        }

        /// <summary>An evaluate response arrived: accepted only for the latest selection's running check.</summary>
        public bool AcceptEvaluation(SelectionTicket ticket)
        {
            if (!IsCurrent(ticket) || CheckTicket == null || CheckTicket.Generation != ticket.Generation)
                return false;
            Checking = false;
            CheckTicket = null;
            return true;
        }

        /// <summary>409: the profile is stale; it is dropped (and the applied settings with it) until it is reloaded.</summary>
        public void InvalidateProfile()
        {
            Profile = null;
            Applied = null;
            Checking = false;
            CheckTicket = null;
        }
    }
}
