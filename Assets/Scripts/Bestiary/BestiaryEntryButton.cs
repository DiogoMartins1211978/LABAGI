using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class BestiaryEntryButton : MonoBehaviour, ISelectHandler
{
    [SerializeField] TMP_Text entryLabel;
    [SerializeField] Image silhouetteImage;
    [Tooltip("Optional generic icon shared by all undiscovered enemies.")]
    [SerializeField] Sprite unknownPlaceholder;

    Button button;
    BestiaryUI owner;
    BestiaryEntry entry;
    float originalFontSize;

    public Button Button => button;
    public BestiaryEntry Entry => entry;

    void Awake()
    {
        button = GetComponent<Button>();
        if (entryLabel == null) entryLabel = GetComponentInChildren<TMP_Text>(true);
        if (silhouetteImage == null)
        {
            Transform icon = transform.Find("Silhouette");
            if (icon != null) silhouetteImage = icon.GetComponent<Image>();
        }
        if (entryLabel != null) originalFontSize = entryLabel.fontSize;
        button.onClick.AddListener(Activate);
    }

    public void Bind(BestiaryUI view, BestiaryEntry data, bool discovered, float textScale)
    {
        owner = view;
        entry = data;
        Sprite icon = discovered ? data.silhouette : unknownPlaceholder;
        bool hasIcon = silhouetteImage != null && icon != null;
        if (entryLabel != null)
        {
            // Names remain on the details page and in narration. Keep a generic
            // placeholder visible when an undiscovered entry has no artwork.
            entryLabel.text = hasIcon ? string.Empty : "?";
            entryLabel.enabled = !hasIcon;
            entryLabel.alignment = TextAlignmentOptions.Center;
            entryLabel.enableAutoSizing = false;
            entryLabel.fontSize = originalFontSize * textScale;
            entryLabel.raycastTarget = false;
        }
        if (silhouetteImage != null)
        {
            silhouetteImage.sprite = icon;
            silhouetteImage.enabled = silhouetteImage.sprite != null;
            silhouetteImage.color = Color.white;
            silhouetteImage.preserveAspect = true;
            silhouetteImage.raycastTarget = false;
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (owner != null && entry != null) owner.FocusEntry(entry);
    }

    void Activate()
    {
        if (owner == null || entry == null) return;
        owner.ShowEntry(entry);
        owner.ReadSelectedEntry();
    }

    void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Activate);
    }
}
