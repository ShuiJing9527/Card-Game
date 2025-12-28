using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class CardHoverDetector : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
{
    public RectTransform effectZone;
    public RectTransform bondZone;
    public RectTransform targetCardRoot;
    [TextArea] public string titleText;
    [TextArea] public string effectText;
    [TextArea] public string bondText;
    public float verticalOffset = 8f;

    public TooltipController tooltipController; // 可在 Inspector 拖（优先），否则使用 TooltipController.Instance

    bool isOver = false;
    Canvas parentCanvas;

    void Awake()
    {
        if (targetCardRoot == null) targetCardRoot = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        // 如果 Inspector 没拖 tooltipController，尽量使用单例（如果已初始化）
        if (tooltipController == null && TooltipController.Instance != null)
            tooltipController = TooltipController.Instance;

        if (effectZone == null)
        {
            var mainDesc = GetComponentInChildren<TextMeshProUGUI>(); // 或更精确查找 mainDescriptionText
            if (mainDesc != null) effectZone = mainDesc.rectTransform;
        }
        if (bondZone == null)
        {
            // 类似逻辑：找到 linkDescriptionText 或指定子对象
        }
    }

    void UpdateTooltipByScreenPos(Vector2 screenPos, Camera cam)
    {
        var tc = tooltipController ?? TooltipController.Instance;
        if (tc == null)
        {
            Debug.LogWarning($"CardHoverDetector ({name}): TooltipController not found. Assign in Inspector or ensure one exists in scene.");
            return;
        }

        // 优先检测 bond，再检测 effect。使用 RectangleContainsScreenPoint 需传入正确的 cam（null 表示 Overlay）
        if (bondZone != null && RectTransformUtility.RectangleContainsScreenPoint(bondZone, screenPos, cam))
        {
            Debug.Log($"CALL Tooltip show: target=bondZone title=[{titleText}] body=[{bondText}]");
            tc.ShowAbove(bondZone, titleText, bondText, null, verticalOffset, true);
            return;
        }
        if (effectZone != null && RectTransformUtility.RectangleContainsScreenPoint(effectZone, screenPos, cam))
        {
            Debug.Log($"CALL Tooltip show: target=effectZone title=[{titleText}] body=[{effectText}]");
            tc.ShowAbove(effectZone, titleText, effectText, null, verticalOffset, true);
            return;
        }
        tc.Hide();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isOver = true;
        Camera cam = eventData.enterEventCamera ?? (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null);
        UpdateTooltipByScreenPos(eventData.position, cam);
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        if (!isOver) return;
        Camera cam = eventData.enterEventCamera ?? (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? parentCanvas.worldCamera : null);
        UpdateTooltipByScreenPos(eventData.position, cam);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isOver = false;
        var tc = tooltipController ?? TooltipController.Instance;
        if (tc != null) tc.Hide();
    }
}
