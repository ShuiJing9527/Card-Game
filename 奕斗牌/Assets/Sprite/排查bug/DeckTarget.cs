using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class DeckTarget : MonoBehaviour, ICardDropTarget
{
    public RectTransform deckContainer;   // 可选：放入的视觉父级
    public bool autoSaveOnDrop = false;

    public bool CanAccept(CardDragHandler card)
    {
        return card != null;
    }

    public void Accept(CardDragHandler card, PointerEventData eventData)
    {
        if (card == null)
        {
            Debug.LogWarning("[DeckTarget] Accept called with null card");
            return;
        }

        int cardId = card.CardId;
        int amount = 1;
        if (card.IsStack && card.StackCount > 0) amount = card.StackCount;

        Debug.Log($"[DeckTarget] Accept cardId={cardId} amount={amount} IsStack={card.IsStack} obj={card.gameObject.name}");

        int invCount = PlayerDataManager.Instance.GetCardCount(cardId);
        int deckCount = PlayerDataManager.Instance.GetDeckCount(cardId);
        Debug.Log($"[DeckTarget] BeforeTransfer -> Inventory={invCount}, Deck={deckCount} for id={cardId}");

        if (invCount < amount)
        {
            Debug.LogWarning($"[DeckTarget] Cannot add to deck: insufficient inventory id={cardId} need={amount} have={invCount}");
            card.RestoreToOriginalParentIfNeeded();
            return;
        }

        bool ok = PlayerDataManager.Instance.TryTransferCardToDeckNoSave(cardId, amount);
        Debug.Log($"[DeckTarget] TryTransferCardToDeckNoSave returned {ok} for id={cardId} amt={amount}");

        if (!ok)
        {
            int invAfter = PlayerDataManager.Instance.GetCardCount(cardId);
            int deckAfter = PlayerDataManager.Instance.GetDeckCount(cardId);
            Debug.LogWarning($"[DeckTarget] Transfer failed id={cardId} amt={amount} invBefore={invCount} invAfter={invAfter} deckBefore={deckCount} deckAfter={deckAfter}\nStack:\n{Environment.StackTrace}");
            card.RestoreToOriginalParentIfNeeded();
            return;
        }

        if (deckContainer != null)
        {
            card.PlaceInto(deckContainer, -1);
            var rt = card.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                rt.anchoredPosition = Vector2.zero;
            }
        }

        if (autoSaveOnDrop)
        {
            PlayerDataManager.Instance.SavePlayerData();
        }

        Debug.Log($"[DeckTarget] Card {cardId} successfully transferred to deck.");
    }
}