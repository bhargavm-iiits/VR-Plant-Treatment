using System.Collections.Generic;
using BTP.Core;
using BTP.Plants;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.UI
{
    /// <summary>
    /// Pages a fixed panel of plant buttons (19 crops do not fit one world-space panel at a size that stays
    /// readable and pointable in XR). Buttons on the same slot of different pages share a position, so only the
    /// current page is active. Large Previous / Next buttons work with a mouse and an XR ray alike, and the page
    /// follows the shared selection: a crop chosen elsewhere (e.g. in the other lab) opens the page that shows it.
    /// </summary>
    public sealed class PlantButtonPager : MonoBehaviour
    {
        [SerializeField] List<string> plantIds = new List<string>();
        [SerializeField] List<GameObject> items = new List<GameObject>();
        [SerializeField, Min(1)] int pageSize = 10;
        [Tooltip("Optional heading per page (e.g. \"Farm crops\"); the page number is always shown.")]
        [SerializeField] List<string> pageTitles = new List<string>();
        [SerializeField] Button previous;
        [SerializeField] Button next;
        [SerializeField] TMP_Text label;

        int page;

        public int Page => page;
        public int PageSize => pageSize;
        public int PageCount => Mathf.Max(1, (items.Count + pageSize - 1) / pageSize);

        internal void Bind(List<string> ids, List<GameObject> buttons, int size, List<string> titles, Button previousButton, Button nextButton,
            TMP_Text pageLabel)
        {
            plantIds = ids;
            items = buttons;
            pageSize = Mathf.Max(1, size);
            pageTitles = titles ?? new List<string>();
            previous = previousButton;
            next = nextButton;
            label = pageLabel;
            ShowPage(0);
        }

        /// <summary>The page that holds item <paramref name="index"/>.</summary>
        public static int PageOf(int index, int size) => size <= 0 || index < 0 ? 0 : index / size;

        void Awake()
        {
            if (previous != null)
                previous.onClick.AddListener(() => ShowPage(page - 1));
            if (next != null)
                next.onClick.AddListener(() => ShowPage(page + 1));
        }

        void OnEnable()
        {
            var state = SelectionState.Instance;
            if (state != null)
            {
                state.PlantChanged += ShowPageOf;
                ShowPageOf(state.Current);
            }
            else
            {
                ShowPage(page);
            }
        }

        void OnDisable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.PlantChanged -= ShowPageOf;
        }

        void ShowPageOf(PlantDefinition plant)
        {
            var index = plant != null ? plantIds.IndexOf(plant.PlantId) : -1;
            ShowPage(index >= 0 ? PageOf(index, pageSize) : page);
        }

        public void ShowPage(int value)
        {
            page = Mathf.Clamp(value, 0, PageCount - 1);
            for (var i = 0; i < items.Count; i++)
                if (items[i] != null)
                    items[i].SetActive(PageOf(i, pageSize) == page);
            if (previous != null)
                previous.interactable = page > 0;
            if (next != null)
                next.interactable = page < PageCount - 1;
            if (label != null)
            {
                var title = page < pageTitles.Count ? pageTitles[page] : null;
                label.text = string.IsNullOrEmpty(title) ? $"Page {page + 1} of {PageCount}" : $"{title}  ·  {page + 1}/{PageCount}";
            }
        }
    }
}
