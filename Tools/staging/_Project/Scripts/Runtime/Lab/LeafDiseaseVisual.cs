using UnityEngine;

namespace BTP.Lab
{
    /// <summary>
    /// Drives the BTP/Leaf Disease shader on a plant model's renderers. The comparison split
    /// is a world-space vertical plane: one side shows the baseline (diseased) week, the other
    /// side the current week.
    /// </summary>
    public sealed class LeafDiseaseVisual : MonoBehaviour
    {
        static readonly int SeverityId = Shader.PropertyToID("_Severity");
        static readonly int ChlorosisId = Shader.PropertyToID("_Chlorosis");
        static readonly int CompareSeverityId = Shader.PropertyToID("_CompareSeverity");
        static readonly int CompareChlorosisId = Shader.PropertyToID("_CompareChlorosis");
        static readonly int SplitEnabledId = Shader.PropertyToID("_SplitEnabled");
        static readonly int SplitPlaneId = Shader.PropertyToID("_SplitPlane");

        Renderer[] renderers;
        MaterialPropertyBlock block;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
        }

        /// <summary>Shows one disease state on the whole plant.</summary>
        public void Show(RecoveryWeek week) => Apply(week, week, false, Vector3.right, Vector3.zero);

        /// <summary>
        /// Shows <paramref name="compare"/> on the side of the plane behind <paramref name="planeNormal"/>
        /// and <paramref name="current"/> on the side it points to.
        /// </summary>
        public void ShowComparison(RecoveryWeek current, RecoveryWeek compare, Vector3 planeNormal, Vector3 planePoint)
            => Apply(current, compare, true, planeNormal, planePoint);

        void Apply(RecoveryWeek current, RecoveryWeek compare, bool split, Vector3 planeNormalWS, Vector3 planePointWS)
        {
            if (renderers == null)
                Awake();

            foreach (var target in renderers)
            {
                // The shader tests object-space positions, so express the plane in each renderer's space.
                var local = target.transform;
                var normalOS = local.InverseTransformDirection(planeNormalWS).normalized;
                var offset = Vector3.Dot(normalOS, local.InverseTransformPoint(planePointWS));

                target.GetPropertyBlock(block);
                block.SetFloat(SeverityId, current.diseaseSeverity / 100f);
                block.SetFloat(ChlorosisId, 1f - current.chlorophyll / 100f);
                block.SetFloat(CompareSeverityId, compare.diseaseSeverity / 100f);
                block.SetFloat(CompareChlorosisId, 1f - compare.chlorophyll / 100f);
                block.SetFloat(SplitEnabledId, split ? 1f : 0f);
                block.SetVector(SplitPlaneId, new Vector4(normalOS.x, normalOS.y, normalOS.z, offset));
                target.SetPropertyBlock(block);
            }
        }
    }
}
