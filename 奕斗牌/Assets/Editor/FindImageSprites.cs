using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class FindPrefabsReferencingSprite
{
    [MenuItem("Tools/Find Prefabs Referencing Sprite")]
    static void Find()
    {
        string spriteName = EditorUtility.DisplayDialogComplex("Find Sprite", "输入要查找的 Sprite 名称（文件名）", "OK", "Cancel", "") == 0
            ? EditorUtility.OpenFilePanel("Select Sprite (just cancel to type name)", "", "")
            : "";
        // 更简单：直接在代码中把 targetName 写死或在弹窗后输入（这里为了简单，直接让用户输入）
        spriteName = UnityEditor.EditorUtility.DisplayDialogComplex("提示", "请在控制台输入要查找的 sprite 名称后按确定", "确定", "取消", "") == 0 ? "10000030" : "10000030";
        var guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            var imgs = go.GetComponentsInChildren<Image>(true);
            foreach (var im in imgs)
            {
                if (im.sprite != null && im.sprite.name == spriteName)
                {
                    Debug.Log($"Prefab {path} references sprite {spriteName}", go);
                    break;
                }
            }
        }
        Debug.Log("查找完成");
    }
}
