using UnityEngine;

// Create cards with: Assets > Create > Cards > Card
// Later, make subclasses (e.g. DamageCard) and override OnPlay for real effects.
[CreateAssetMenu(menuName = "Cards/Card", fileName = "NewCard")]
public class CardData : ScriptableObject
{
    public string displayName = "New Card";
    [TextArea] public string description;
    [Tooltip("Max copies of this card allowed in one deck")]
    public int maxCopies = 3;

    [Header("Visuals")]
    [Tooltip("Card prefab (or Prefab Variant) with a CardView on its root")]
    public CardView cardPrefab;
    [Tooltip("Picture shown on the front of the card")]
    public Texture2D frontArt;

    // Spawns this card's prefab, filled with this card's data
    public CardView CreateView(Transform parent = null)
    {
        if (cardPrefab == null)
        {
            Debug.LogWarning($"[Card] {displayName} has no Card Prefab assigned.");
            return null;
        }

        CardView view = Instantiate(cardPrefab, parent);
        view.Setup(this);
        return view;
    }

    // Placeholder: replace or override with the real effect
    public virtual void OnPlay(GameObject player)
    {
        Debug.Log($"[Card] {displayName} played (no effect yet)");
    }
}
