using UnityEngine;
using UnityEngine.UI;

public class SaveButtonController : MonoBehaviour
{
    public Button saveButton;
    public Sprite activeSprite;
    public Sprite inactiveSprite;

    void Start()
    {
        if (saveButton == null) saveButton = GetComponent<Button>();
        if (saveButton == null) return;

        // 默认禁用或设置为非高亮状态
        UpdateButtonState(false);
        // 监听数据变化事件
        PlayerDataManager.Instance.OnPlayerDataSaved += () => UpdateButtonState(false);
        PlayerDataManager.Instance.OnDeckChanged += (id, count) => UpdateButtonState(true);
        PlayerDataManager.Instance.OnInventoryChanged += (id, count) => UpdateButtonState(true);
    }

    void UpdateButtonState(bool dataChanged)
    {
        if (saveButton == null) return;
        saveButton.interactable = dataChanged;
        // 如果用 sprite 切换
        var img = saveButton.GetComponent<Image>();
        if (img != null)
            img.sprite = dataChanged ? activeSprite : inactiveSprite;
    }

    void OnDestroy()
    {
        if (PlayerDataManager.Instance != null)
        {
            try
            {
                PlayerDataManager.Instance.OnPlayerDataSaved -= () => UpdateButtonState(false);
                PlayerDataManager.Instance.OnDeckChanged -= (id, count) => UpdateButtonState(true);
                PlayerDataManager.Instance.OnInventoryChanged -= (id, count) => UpdateButtonState(true);
            }
            catch { }
        }
    }
}
