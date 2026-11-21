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
    [SerializeField] Sprite filledFrame;
    [SerializeField] Sprite emptyFrame;

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
        button.interactable = data != null && discovered;
        if (filledFrame != null && emptyFrame != null)
        {
            var frame = GetComponent<Image>();
            frame.sprite = data != null && discovered ? filledFrame : emptyFrame;
            frame.color = data != null && discovered ? Color.white : new Color(.32f,.32f,.32f);
            if (entryLabel != null) entryLabel.color = data != null && discovered ? Color.white : new Color(.12f,.14f,.17f);
        }
        if (data == null)
        {
            if (entryLabel != null) entryLabel.enabled = false;
            if (silhouetteImage != null) silhouetteImage.enabled = false;
            return;
        }
        Sprite icon = discovered ? (data.portrait != null ? data.portrait : data.silhouette) : unknownPlaceholder;
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
        if (owner == null || entry == null || !button.interactable) return;
        owner.ShowEntry(entry);
        owner.ReadSelectedEntry();
    }

    void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Activate);
    }
}
