using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckBuilderUI : MonoBehaviour
{
    [Header("References")]
    public CardSystem cardSystem;
    public GameObject uiRootPanel;

    [Header("Deck Containers (Fixed Rows)")]
    [Tooltip("Top_Row transform (Horizontal Layout Group)")]
    public Transform topRow;
    [Tooltip("Bottom_Row transform (Horizontal Layout Group)")]
    public Transform bottomRow;

    [Header("Collection Container")]
    public Transform collectionContainer;

    [Header("Prefabs")]
    [Tooltip("Active card prefab with button and icon")]
    public GameObject cardUiPrefab;
    [Tooltip("Empty placeholder variant for vacant slots")]
    public GameObject cardPlaceholderPrefab;
    [Tooltip("Row prefab for collection (Horizontal Layout Group for 6 slots)")]
    public GameObject collectionRowPrefab;

    [Header("Collection Settings")]
    [Tooltip("Number of slots per row in the collection grid")]
    public int collectionRowCapacity = 6;

    void OnEnable()
    {
        if (cardSystem == null) cardSystem = FindAnyObjectByType<CardSystem>();
        if (cardSystem != null) cardSystem.StateChanged += RefreshUI;
    }

    void OnDisable()
    {
        if (cardSystem != null) cardSystem.StateChanged -= RefreshUI;
    }

    void Start()
    {
        RefreshUI();
    }

    public void RefreshUI()
    {
        if (cardSystem == null) return;

        bool inBuilderMode = cardSystem.CurrentMode == CardSystem.Mode.DeckBuilder;
        if (uiRootPanel != null) uiRootPanel.SetActive(inBuilderMode);

        if (!inBuilderMode) return;

        PopulateDeckGrid();
        PopulateCollectionGrid();
    }

    void PopulateDeckGrid()
    {
        ClearContainer(topRow);
        ClearContainer(bottomRow);

        int maxDeckSize = cardSystem.deckSize;

        for (int i = 0; i < maxDeckSize; i++)
        {
            Transform targetRow = (i < 4) ? topRow : bottomRow;
            if (targetRow == null) continue;

            bool hasCard = (i < cardSystem.deck.Count && cardSystem.deck[i] != null);

            if (hasCard)
            {
                CardData card = cardSystem.deck[i];
                GameObject item = Instantiate(cardUiPrefab, targetRow);
                SetupCardSlotUI(item, card, isInDeck: true);
            }
            else
            {
                Instantiate(cardPlaceholderPrefab, targetRow);
            }
        }
    }

    void PopulateCollectionGrid()
    {
        ClearContainer(collectionContainer);

        List<CardData> uniqueCollection = new List<CardData>(new HashSet<CardData>(cardSystem.collection));
        int totalCards = uniqueCollection.Count;

        int rowsForCards = Mathf.CeilToInt((float)Mathf.Max(1, totalCards) / collectionRowCapacity);
        int totalRowsNeeded = rowsForCards + 1;

        int cardIndex = 0;

        for (int r = 0; r < totalRowsNeeded; r++)
        {
            GameObject currentRow = Instantiate(collectionRowPrefab, collectionContainer);

            for (int c = 0; c < collectionRowCapacity; c++)
            {
                if (cardIndex < totalCards)
                {
                    GameObject item = Instantiate(cardUiPrefab, currentRow.transform);
                    SetupCardSlotUI(item, uniqueCollection[cardIndex], isInDeck: false);
                    cardIndex++;
                }
                else
                {
                    Instantiate(cardPlaceholderPrefab, currentRow.transform);
                }
            }
        }
    }

    void SetupCardSlotUI(GameObject item, CardData card, bool isInDeck)
    {
        TMP_Text titleText = item.GetComponentInChildren<TMP_Text>();
        if (titleText != null) titleText.text = card.displayName;

        Transform iconTransform = item.transform.Find("Icon");
        if (iconTransform != null)
        {
            Image iconImage = iconTransform.GetComponent<Image>();
            if (iconImage != null && card.frontArt != null)
            {
                iconImage.sprite = Sprite.Create(
                    card.frontArt,
                    new Rect(0, 0, card.frontArt.width, card.frontArt.height),
                    new Vector2(0.5f, 0.5f)
                );
                iconImage.enabled = true;
            }
        }

        Button btn = item.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() =>
            {
                if (isInDeck) cardSystem.RemoveFromDeck(card);
                else cardSystem.AddToDeck(card);
            });
        }
    }

    void ClearContainer(Transform container)
    {
        if (container == null) return;
        foreach (Transform child in container)
        {
            Destroy(child.gameObject);
        }
    }
}