using UnityEngine;

public enum CardType
{
    Attack,
    Defense,
    Ability,
    Trap
}

public enum AbilityType
{
    Heal,
    DamageOverTime,
    None
}

[CreateAssetMenu(menuName = "Cards/Card", fileName = "NewCard")]
public class CardData : ScriptableObject
{
    public string displayName = "New Card";
    [TextArea] public string description;
    [Tooltip("Max copies of this card allowed in one deck")]
    public int maxCopies = 5;

    [Header("Card Type")]
    public CardType type;

    [Header("Ability")]
    public AbilityType abilityType;

    [Tooltip("Damage, shield or other effect value depending on the card type")]
    public int effectValue = 0;

    [Tooltip("Duration in rounds for effects that require it")]
    public int effectDuration = 0;

    [Header("Visuals")]
    [Tooltip("Card prefab (or Prefab Variant) with a CardView on its root")]
    public CardView cardPrefab;
    [Tooltip("Picture shown on the front of the card")]
    public Texture2D frontArt;

    [Header("Audio")]
    [Tooltip("Short sound played when the card is used")]
    public AudioClip cardSound;

    public CardView CreateView(Transform parent = null, int level = 1)
    {
        if (cardPrefab == null)
        {
            Debug.LogWarning($"[Card] {displayName} has no Card Prefab assigned.");
            return null;
        }

        CardView view = Instantiate(cardPrefab, parent);
        view.Setup(this, level);
        return view;
    }

    public virtual void OnPlay(PlayerHealth player, Enemy target)
    {
        switch (type)
        {
            case CardType.Attack:
                Debug.LogWarning($"[Card] {displayName} was played.");
                if (target != null) {
                    target.TakeDamage(effectValue);
                }else Debug.LogWarning("TARGET IS NULL.");


                break;

            case CardType.Defense:

                if (player != null)
                    player.AddShield(effectValue);

                break;

            case CardType.Ability:

                if (player != null)
                {
                    switch (abilityType)
                    {
                        case AbilityType.Heal:
                            player.Heal(effectValue);
                            break;

                        case AbilityType.DamageOverTime:
                            if (target != null)
                            {
                                target.ApplyDamageOverTime(
                                    effectValue,
                                    effectDuration
                                );
                            }
                            break;
                    }
                }

                break;

            case CardType.Trap:

                // Implementar depois
                break;
        }
    }
}
