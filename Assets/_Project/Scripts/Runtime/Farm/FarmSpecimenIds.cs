using System.Collections.Generic;
using BTP.Core;
using BTP.XR;

namespace BTP.Farm
{
    /// <summary>
    /// The specimen ID of one Farm plant. A plant listed in the XR specimen list (Resources/XR/xr_specimens.json) keeps
    /// that ID, so a connected scan and a hand-picked plant name the same specimen; every other plant gets the stable
    /// <see cref="SpecimenRef.DefaultId"/> ("tomato/early_blight#03").
    /// </summary>
    public static class FarmSpecimenIds
    {
        static Dictionary<(string, string, int), string> listed;

        public static string For(string plantId, string diseaseId, int plantIndex)
        {
            if (listed == null)
            {
                listed = new Dictionary<(string, string, int), string>();
                var catalog = XrSpecimenCatalog.LoadDefault(out _);
                foreach (var specimen in catalog.All)
                    listed[(specimen.plantId, specimen.diseaseId, specimen.plantIndex)] = specimen.specimenId;
            }

            return listed.TryGetValue((plantId, diseaseId ?? "", plantIndex), out var id) ? id : SpecimenRef.DefaultId(plantId, diseaseId, plantIndex);
        }

        public static SpecimenRef Ref(string plantId, string diseaseId, int plantIndex) =>
            new SpecimenRef(For(plantId, diseaseId, plantIndex), plantId, diseaseId, plantIndex);
    }
}
