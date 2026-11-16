namespace BTP.Core
{
    /// <summary>
    /// The environments. Each is an additive scene with a SpawnAnchor. New values are appended so
    /// serialized values (Farm = 0, Lab = 1) keep their meaning.
    /// </summary>
    public enum EnvironmentId
    {
        Farm,
        Lab,
        Treatment
    }

    /// <summary>
    /// Implemented by scene components that configure the environment after it loads (for example
    /// the Treatment lab reading its case from SQLite). The rig is placed and the fade lifted only
    /// once every such component reports ready.
    /// </summary>
    public interface IEnvironmentReadiness
    {
        bool IsEnvironmentReady { get; }
    }

    /// <summary>Implemented by scene components that must save or park state before their scene unloads.</summary>
    public interface IEnvironmentExit
    {
        void PrepareForExit();
    }
}
