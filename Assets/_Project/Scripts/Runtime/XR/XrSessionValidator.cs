using System;
using System.Collections.Generic;

namespace BTP.XR
{
    /// <summary>
    /// Checks a session from the backend (or the fixture) before Unity acts on it: the backend class must map exactly,
    /// the session's Unity case, specimen and recipe must agree with that mapping, the specimen must be authored in the
    /// Farm and the case must have a recipe this build supports. Any problem leaves the user where they are.
    /// </summary>
    public static class XrSessionValidator
    {
        public static List<string> Validate(XrSessionData session, XrCaseMap map, XrSpecimenCatalog specimens, Func<XrCaseMap.Row, bool> recipeSupported)
        {
            var problems = new List<string>();
            if (session == null)
            {
                problems.Add("The session could not be read.");
                return problems;
            }

            if (string.IsNullOrEmpty(session.xrSessionId))
                problems.Add("The session has no xrSessionId.");
            if (map == null)
            {
                problems.Add("The case map is not loaded.");
                return problems;
            }

            if (!map.TryResolve(session.backendDiseaseId, out var row, out var reason))
            {
                problems.Add(reason);
                return problems;
            }

            if (!session.HasUnityCase)
            {
                problems.Add("This session is AR-only: it carries no Unity case.");
                return problems;
            }

            if (session.plantId != row.plantId || session.diseaseId != row.diseaseId)
                problems.Add($"The session's case {session.plantId}/{session.diseaseId} does not match the mapping for " +
                             $"{session.backendDiseaseId} ({row.plantId}/{row.diseaseId}, map v{map.Version}).");
            if (session.specimenId != row.specimenId)
                problems.Add($"The session's specimen '{session.specimenId}' is not the mapped specimen '{row.specimenId}'.");
            if (session.recipeId != row.recipeId)
                problems.Add($"The session's recipe '{session.recipeId}' is not the mapped recipe '{row.recipeId}'.");
            if (session.recipeVersion <= 0)
                problems.Add("The session has no pinned recipe version.");
            if (session.revision < 0)
                problems.Add("The session revision is negative.");

            if (specimens == null || !specimens.TryGet(session.specimenId, out var specimen))
                problems.Add($"Specimen '{session.specimenId}' is not authored in this build's Farm.");
            else if (specimen.plantId != session.plantId || specimen.diseaseId != session.diseaseId)
                problems.Add($"Specimen '{session.specimenId}' shows {specimen.plantId}/{specimen.diseaseId}, not {session.plantId}/{session.diseaseId}.");

            if (recipeSupported != null && !recipeSupported(row))
                problems.Add($"This build has no manual treatment lesson for {row.plantId}/{row.diseaseId}.");
            return problems;
        }
    }
}
