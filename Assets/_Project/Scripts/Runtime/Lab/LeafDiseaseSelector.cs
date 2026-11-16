using System.Collections.Generic;
using BTP.Plants;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Lab
{
    public sealed class LeafDiseaseSelector : MonoBehaviour
    {
        [SerializeField] LabPlantDisplay display;
        [SerializeField] TMP_Text title;
        [SerializeField] List<Button> buttons = new List<Button>();
        [SerializeField] List<SelectedStateVisual> visuals = new List<SelectedStateVisual>();
        IReadOnlyList<DiseaseClasses.Info> choices;

        internal void Bind(LabPlantDisplay target, TMP_Text heading, List<Button> controls, List<SelectedStateVisual> states)
        {
            display = target; title = heading; buttons = controls; visuals = states;
        }
        void Awake()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                int index = i;
                buttons[i].onClick.AddListener(() =>
                {
                    if (choices != null && index < choices.Count)
                        display.ShowDisease(choices[index].DiseaseId);
                });
            }
        }
        void OnEnable()
        {
            display.ModelChanged += Refresh;
            if (display.CurrentPlant != null)
                Refresh(display.CurrentPlant, display.CurrentModel);
        }
        void OnDisable() => display.ModelChanged -= Refresh;
        void Refresh(PlantDefinition plant, GameObject model)
        {
            choices = DiseaseClasses.For(plant.PlantId);
            title.text = plant.DisplayName + " - choose a leaf condition";
            int columns = Mathf.Min(3, choices.Count);
            int rows = Mathf.CeilToInt(choices.Count / 3f);
            ((RectTransform)transform).sizeDelta = new Vector2(1140f, 76f + rows * 82f);
            for (int i = 0; i < buttons.Count; i++)
            {
                bool visible = i < choices.Count;
                buttons[i].gameObject.SetActive(visible);
                if (!visible) continue;
                float width = (1108f - (columns - 1) * 10f) / columns;
                var rect = (RectTransform)buttons[i].transform;
                rect.anchoredPosition = new Vector2(16f + (i % 3) * (width + 10f), -(62f + (i / 3) * 82f));
                rect.sizeDelta = new Vector2(width, 72f);
                buttons[i].GetComponentInChildren<TMP_Text>().text = choices[i].DisplayName;
                visuals[i].Selected = choices[i].DiseaseId == display.CurrentDiseaseId;
            }
        }
    }
}
