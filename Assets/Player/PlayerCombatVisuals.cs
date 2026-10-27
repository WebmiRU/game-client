using System.Collections.Generic;
using UnityEngine;

/// <summary>Procedural client-side sword and shield visuals for the capsule player.</summary>
public sealed class PlayerCombatVisuals : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float attackDuration = 0.38f;
    [SerializeField, Range(20f, 120f)] private float attackSwingAngle = 86f;

    private const float BladeLength = 1f;
    private const float GripLength = 0.20f;
    private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

    private Transform visualAnchor;
    private Transform swordPivot;
    private Quaternion swordRestRotation = Quaternion.identity;
    private float attackElapsed;
    private bool isAttacking;
    private bool initialized;

    public bool IsAttacking => isAttacking;

    public void Initialize(Transform anchor)
    {
        if (initialized) return;
        visualAnchor = anchor != null ? anchor : transform;
        CreateSword();
        CreateShield();
        initialized = true;
    }

    /// <summary>Plays the local swing animation only; does not apply damage.</summary>
    public void PlayAttack()
    {
        if (!initialized) Initialize(visualAnchor != null ? visualAnchor : transform);
        if (swordPivot == null) return;
        attackElapsed = 0f;
        isAttacking = true;
        swordPivot.localRotation = swordRestRotation;
    }

    private void Update()
    {
        if (!isAttacking || swordPivot == null) return;

        attackElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(attackElapsed / Mathf.Max(0.05f, attackDuration));
        const float swingPortion = 0.42f;
        float amount = progress <= swingPortion
            ? Mathf.SmoothStep(0f, 1f, progress / swingPortion)
            : 1f - Mathf.SmoothStep(0f, 1f, (progress - swingPortion) / (1f - swingPortion));

        // The upright blade rotates forward around the grip, ending almost horizontal.
        swordPivot.localRotation =
            Quaternion.AngleAxis(attackSwingAngle * amount, Vector3.right) * swordRestRotation;

        if (progress >= 1f)
        {
            swordPivot.localRotation = swordRestRotation;
            isAttacking = false;
        }
    }

    private void CreateSword()
    {
        // Local +X is the character's right. The pivot is at the sword guard.
        GameObject pivot = new GameObject("Sword_AttackPivot");
        swordPivot = pivot.transform;
        swordPivot.SetParent(visualAnchor, false);
        swordPivot.localPosition = new Vector3(0.57f, 0.16f, 0.09f);
        swordPivot.localRotation = Quaternion.identity;
        swordRestRotation = swordPivot.localRotation;

        Material steel = GetMaterial("Sword_Steel", new Color(0.72f, 0.77f, 0.81f), 0.78f, 0.72f);
        Material brightSteel = GetMaterial("Sword_Edge", new Color(0.91f, 0.93f, 0.94f), 0.85f, 0.82f);
        Material grip = GetMaterial("Sword_Grip", new Color(0.19f, 0.075f, 0.035f), 0.05f, 0.22f);
        Material brass = GetMaterial("Sword_Brass", new Color(0.72f, 0.43f, 0.12f), 0.72f, 0.64f);

        CreateBox(swordPivot, "Sword_Grip", new Vector3(0f, -GripLength * 0.5f, 0f),
            new Vector3(0.042f, GripLength, 0.042f), grip);
        CreateSphere(swordPivot, "Sword_Pommel", new Vector3(0f, -GripLength - 0.025f, 0f),
            new Vector3(0.065f, 0.065f, 0.065f), brass);
        CreateBox(swordPivot, "Sword_Crossguard", new Vector3(0f, 0.025f, 0f),
            new Vector3(0.19f, 0.028f, 0.05f), brass);

        GameObject blade = new GameObject("Sword_Blade_1m");
        blade.transform.SetParent(swordPivot, false);
        MeshFilter filter = blade.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateBladeMesh();
        MeshRenderer renderer = blade.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = steel;

        CreateBox(swordPivot, "Sword_BladeHighlight", new Vector3(-0.017f, 0.48f, -0.013f),
            new Vector3(0.008f, 0.76f, 0.003f), brightSteel);
    }

    private void CreateShield()
    {
        GameObject shield = new GameObject("Shield_Static");
        shield.transform.SetParent(visualAnchor, false);
        shield.transform.localPosition = new Vector3(-0.62f, -0.015f, 0.075f);
        shield.transform.localRotation = Quaternion.identity;

        Material wood = GetMaterial("Shield_Wood", new Color(0.31f, 0.13f, 0.045f), 0.03f, 0.28f);
        Material rim = GetMaterial("Shield_Rim", new Color(0.64f, 0.43f, 0.17f), 0.68f, 0.55f);
        Material boss = GetMaterial("Shield_Boss", new Color(0.76f, 0.57f, 0.25f), 0.76f, 0.7f);

        GameObject face = new GameObject("Shield_WoodenFace");
        face.transform.SetParent(shield.transform, false);
        face.AddComponent<MeshFilter>().sharedMesh = CreateShieldMesh();
        face.AddComponent<MeshRenderer>().sharedMaterial = wood;

        Vector2[] outline =
        {
            new Vector2(-0.18f, 0.22f), new Vector2(0.18f, 0.22f),
            new Vector2(0.18f, 0.045f), new Vector2(0.125f, -0.13f),
            new Vector2(0f, -0.28f), new Vector2(-0.125f, -0.13f),
            new Vector2(-0.18f, 0.045f)
        };

        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 from = outline[i];
            Vector2 to = outline[(i + 1) % outline.Length];
            Vector2 delta = to - from;
            Vector3 midpoint = new Vector3((from.x + to.x) * 0.5f, (from.y + to.y) * 0.5f, 0.018f);
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            CreateBox(shield.transform, "Shield_Rim_" + i, midpoint,
                new Vector3(delta.magnitude + 0.012f, 0.025f, 0.05f), rim,
                Quaternion.Euler(0f, 0f, angle));
        }

        CreateSphere(shield.transform, "Shield_CenterBoss", new Vector3(0f, 0.015f, 0.044f),
            new Vector3(0.075f, 0.075f, 0.04f), boss);
        CreateSphere(shield.transform, "Shield_UpperRivetLeft", new Vector3(-0.125f, 0.16f, 0.044f),
            new Vector3(0.025f, 0.025f, 0.025f), rim);
        CreateSphere(shield.transform, "Shield_UpperRivetRight", new Vector3(0.125f, 0.16f, 0.044f),
            new Vector3(0.025f, 0.025f, 0.025f), rim);
    }

    private static Mesh CreateBladeMesh()
    {
        Mesh mesh = new Mesh { name = "ProceduralSwordBlade_1m" };
        Vector3[] v =
        {
            new Vector3(-0.040f, 0.045f, -0.012f), new Vector3(0.040f, 0.045f, -0.012f),
            new Vector3(0.028f, 0.815f, -0.012f), new Vector3(0f, BladeLength + 0.045f, -0.012f),
            new Vector3(-0.028f, 0.815f, -0.012f), new Vector3(-0.040f, 0.045f, 0.012f),
            new Vector3(0.040f, 0.045f, 0.012f), new Vector3(0.028f, 0.815f, 0.012f),
            new Vector3(0f, BladeLength + 0.045f, 0.012f), new Vector3(-0.028f, 0.815f, 0.012f)
        };
        int[] t =
        {
            0,1,2, 0,2,3, 0,3,4, 5,7,6, 5,8,7, 5,9,8,
            0,5,6, 0,6,1, 1,6,7, 1,7,2, 2,7,8, 2,8,3,
            3,8,9, 3,9,4, 4,9,5, 4,5,0
        };
        mesh.vertices = v;
        mesh.triangles = t;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh CreateShieldMesh()
    {
        Mesh mesh = new Mesh { name = "ProceduralKiteShield" };
        Vector2[] p =
        {
            new Vector2(-0.18f, 0.22f), new Vector2(0.18f, 0.22f),
            new Vector2(0.18f, 0.045f), new Vector2(0.125f, -0.13f),
            new Vector2(0f, -0.28f), new Vector2(-0.125f, -0.13f),
            new Vector2(-0.18f, 0.045f)
        };
        Vector3[] v = new Vector3[16];
        for (int i = 0; i < p.Length; i++)
        {
            v[i] = new Vector3(p[i].x, p[i].y, 0.025f);
            v[i + 7] = new Vector3(p[i].x, p[i].y, -0.025f);
        }
        v[14] = new Vector3(0f, 0f, 0.025f);
        v[15] = new Vector3(0f, 0f, -0.025f);

        List<int> t = new List<int>();
        for (int i = 0; i < p.Length; i++)
        {
            int next = (i + 1) % p.Length;
            t.AddRange(new[] { 14, next, i, 15, i + 7, next + 7,
                i, next, next + 7, i, next + 7, i + 7 });
        }
        mesh.vertices = v;
        mesh.triangles = t.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static GameObject CreateBox(Transform parent, string objectName, Vector3 position,
        Vector3 scale, Material material, Quaternion? rotation = null)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation ?? Quaternion.identity;
        part.transform.localScale = scale;
        RemoveCollider(part);
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.sharedMaterial = material;
        return part;
    }

    private static GameObject CreateSphere(Transform parent, string objectName, Vector3 position,
        Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        RemoveCollider(part);
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.sharedMaterial = material;
        return part;
    }

    private static void RemoveCollider(GameObject part)
    {
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.Destroy(collider);
    }

    private static Material GetMaterial(string name, Color color, float metallic, float smoothness)
    {
        if (Materials.TryGetValue(name, out Material cached) && cached != null) return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            Debug.LogWarning("PlayerCombatVisuals: no compatible shader found for " + name);
            return null;
        }

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        else if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
        Materials[name] = material;
        return material;
    }
}
