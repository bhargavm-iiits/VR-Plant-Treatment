using TMPro;
using UnityEngine;

namespace BTP.Farm
{
    /// <summary>Small world-space card above a selected specimen: plant name and what to do next.</summary>
    public sealed class PlantNameCard : MonoBehaviour
    {
        [SerializeField] GameObject card;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;

        internal void Bind(GameObject cardRoot, TMP_Text titleText, TMP_Text subtitleText)
        {
            card = cardRoot;
            title = titleText;
            subtitle = subtitleText;
        }

        public void Show(string plantName, string detail)
        {
            title.text = plantName;
            subtitle.text = detail;
            card.SetActive(true);
        }

        public void Hide() => card.SetActive(false);
    }
}
