/****************************************************************************
 * Copyright (c) 2016 ~ 2025 liangxiegame UNDER MIT LINCENSE
 *
 * https://qframework.cn
 * https://github.com/liangxiegame/QFramework
 * https://gitee.com/liangxiegame/QFramework
 ****************************************************************************/

using UnityEditor;

#if UNITY_2021_2_OR_NEWER
using UnityEditor.SceneManagement;
#else
using UnityEditor.Experimental.SceneManagement;
#endif

using UnityEngine;

namespace QFramework
{
    [CustomEditor(typeof(UIPanel), true)]
    public class UIPanelInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("生成代码", GUILayout.Height(30)))
            {
                var panel = target as UIPanel;

                if (PrefabUtility.IsPartOfPrefabInstance(panel.gameObject))
                {
                    PrefabUtility.ApplyPrefabInstance(panel.gameObject, InteractionMode.AutomatedAction);

                    AssetDatabase.Refresh();

                    var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel.gameObject);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    UICodeGenerator.DoCreateCode(new[] { prefab });
                }
                else
                {
                    // 使用 assetPath，且增加空值和加载检查，避免警告和潜在的空引用
                    var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (currentStage != null)
                    {
                        var prefabPath = currentStage.assetPath;
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                        if (prefab != null)
                        {
                            UICodeGenerator.DoCreateCode(new[] { prefab });
                        }
                        else
                        {
                            Debug.LogWarning($"未找到预制体资源: {prefabPath}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("当前未处于 Prefab Stage，无法获取 prefab 路径。");
                    }
                }
            }
        }
    }
}


//using UnityEditor;

//#if UNITY_2021_2_OR_NEWER
//using UnityEditor.SceneManagement;
//#else
//using UnityEditor.Experimental.SceneManagement;
//#endif

//using UnityEngine;

//namespace QFramework
//{
//    [CustomEditor(typeof(UIPanel), true)]
//    public class UIPanelInspector : Editor
//    {
//        public override void OnInspectorGUI()
//        {
//            base.OnInspectorGUI();


//            if (GUILayout.Button("生成代码", GUILayout.Height(30)))
//            {
//                var panel = target as UIPanel;

//                if (PrefabUtility.IsPartOfPrefabInstance(panel.gameObject))
//                {
//                    PrefabUtility.ApplyPrefabInstance(panel.gameObject, InteractionMode.AutomatedAction);

//                    AssetDatabase.Refresh();

//                    var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel.gameObject);
//                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
//                    UICodeGenerator.DoCreateCode(new[] { prefab });
//                }
//                else
//                {
//                    var prefabPath = PrefabStageUtility.GetCurrentPrefabStage().prefabAssetPath;
//                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
//                    UICodeGenerator.DoCreateCode(new[] { prefab });
//                }


//            }
//        }
//    }
//}