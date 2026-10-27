using UnityEditor;
using UnityEngine;

/// <summary>Creates an animator-ready Unity prefab from the imported COLLADA model.</summary>
public static class ClockworkGolemPrefabSetup
{
    private const string ModelPath = "Assets/Models/ClockworkGolem/ClockworkGolem.dae";
    private const string PrefabPath = "Assets/Models/ClockworkGolem/ClockworkGolem.prefab";

    [MenuItem("Tools/Game Client/Build Clockwork Golem Prefab")]
    public static void BuildPrefab()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog(
                "Clockwork Golem",
                "The COLLADA model has not imported yet. Wait for Unity to finish importing Assets, then run this command again.",
                "OK");
            return;
        }

        GameObject instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (instance == null) instance = Object.Instantiate(model);
        instance.name = "ClockworkGolem";

        ClockworkGolemAnimator animator = instance.GetComponent<ClockworkGolemAnimator>();
        if (animator == null) animator = instance.AddComponent<ClockworkGolemAnimator>();
        animator.headPivot = FindDeep(instance.transform, "HeadPivot");
        animator.leftArmPivot = FindDeep(instance.transform, "LeftArmPivot");
        animator.rightArmPivot = FindDeep(instance.transform, "RightArmPivot");
        animator.chestDoorPivot = FindDeep(instance.transform, "ChestDoorPivot");
        animator.cannonSlide = FindDeep(instance.transform, "CannonSlide");
        animator.pedalDrivePivot = FindDeep(instance.transform, "PedalDrivePivot");

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out bool success);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (success)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("Clockwork Golem prefab created at " + PrefabPath);
        }
        else
        {
            Debug.LogError("Failed to save Clockwork Golem prefab.");
        }
    }

    private static Transform FindDeep(Transform parent, string targetName)
    {
        if (parent.name == targetName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeep(parent.GetChild(i), targetName);
            if (found != null) return found;
        }
        return null;
    }
}
