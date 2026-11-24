
using System.IO;
using UnityEditor;
using UnityEngine;

public static class AssignCardVisuals
{
    private const string PrefabPath =
        "Assets/Prefabs/Cards/card_base.prefab";

    private const string SpritesPath =
        "Assets/Prefabs/Cards/Sprites/";

    [MenuItem("Tools/Cards/Assign Card Visuals")]
    public static void AssignVisuals()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"Card prefab not found: {PrefabPath}");
            return;
        }

        CardView cardView = prefab.GetComponent<CardView>();

        if (cardView == null)
        {
            Debug.LogError("card_base prefab has no CardView component on its root.");
            return;
        }

        string[] cardGuids = AssetDatabase.FindAssets("t:CardData");

        int assigned = 0;
        int missingSprites = 0;

        foreach (string guid in cardGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(assetPath);

            if (card == null)
                continue;

            string cardName = Path.GetFileNameWithoutExtension(assetPath);
            string spritePath = SpritesPath + cardName + ".png";

            Texture2D artwork =
                AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);

            Undo.RecordObject(card, "Assign Card Visuals");

            card.cardPrefab = cardView;

            if (artwork != null)
            {
                card.frontArt = artwork;
            }
            else
            {
                missingSprites++;
                Debug.LogWarning(
                    $"No matching artwork found for {cardName}: {spritePath}"
                );
            }

            EditorUtility.SetDirty(card);
            assigned++;
        }

        AssetDatabase.SaveAssets();

        Debug.Log(
            $"Card visual assignment complete. " +
            $"Cards processed: {assigned}. " +
            $"Missing sprites: {missingSprites}."
        );
    }
}
