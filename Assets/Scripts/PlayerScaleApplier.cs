using System.Collections.Generic;
using System.Reflection;
using Klak.Motion;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// The one per-player scale step. Pushes the effective <see cref="HandVfxSettings"/>
/// to a player's hand VFX graphs and applies the bodyScale-dependent transforms
/// (TD debug spheres, TD BrownianMotion wander, hand raycast collider). Plain
/// class in the force-class style: owned by <see cref="SceneController"/>, invoked
/// on player spawn and whenever the effective settings are rebuilt. All values
/// passed in are already effective (base × bodyScale^exp) — nothing here rescales.
/// </summary>
public class PlayerScaleApplier
{
    private readonly struct VfxEntry
    {
        public readonly FieldInfo Field;
        public readonly string Name;
        public readonly int Id;

        public VfxEntry(FieldInfo field, string name)
        {
            Field = field;
            Name = name;
            Id = Shader.PropertyToID(name);
        }
    }

    private static readonly List<VfxEntry> vfxTable = BuildVfxTable();

    private static List<VfxEntry> BuildVfxTable()
    {
        var list = new List<VfxEntry>();
        foreach (
            var field in typeof(HandVfxSettings).GetFields(
                BindingFlags.Public | BindingFlags.Instance
            )
        )
        {
            var attr = field.GetCustomAttribute<VfxPropertyAttribute>();
            if (attr == null)
                continue;
            var t = field.FieldType;
            if (
                t != typeof(float)
                && t != typeof(int)
                && t != typeof(Vector2)
                && t != typeof(Vector3)
            )
            {
                Debug.LogError(
                    $"[PlayerScaleApplier] HandVfxSettings.{field.Name} has [VfxProperty] but type {t.Name} is not pushable."
                );
                continue;
            }
            list.Add(new VfxEntry(field, attr.Name));
        }
        return list;
    }

    // One warning per missing graph name per play session (editor only). Reset on
    // play-mode enter so it also fires again with domain reload disabled.
    private static readonly HashSet<string> warnedMissing = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetWarnings() => warnedMissing.Clear();

    public void Apply(PlayerConstructor player, RuntimeSceneSettings effective)
    {
        if (player == null || effective == null)
            return;
        ApplyHandVfx(player, effective.handVfx);
        ApplyBodyVfx(player, effective);
        ApplyTransforms(player, effective);
    }

    private static readonly int bodySpawnSizeId = Shader.PropertyToID("bodySpawnSize");

    /// <summary>BodyEffects.vfx spawn flash on VFX_Body: exposed <c>bodySpawnSize</c> (length).</summary>
    public void ApplyBodyVfx(PlayerConstructor player, RuntimeSceneSettings effective)
    {
        var vfx = player.bodyVfx;
        if (vfx == null)
            return;
        if (vfx.HasFloat(bodySpawnSizeId))
            vfx.SetFloat(bodySpawnSizeId, effective.bodySpawnSize);
        else
            WarnMissingOnce(vfx, "bodySpawnSize", "float");
    }

    public void ApplyToAll(IEnumerable<GameObject> players, RuntimeSceneSettings effective)
    {
        if (players == null || effective == null)
            return;
        foreach (var go in players)
        {
            if (go != null && go.TryGetComponent<PlayerConstructor>(out var pc))
                Apply(pc, effective);
        }
    }

    /// <summary>Pushes every [VfxProperty] field to both hand graphs (Has*-guarded).</summary>
    public void ApplyHandVfx(PlayerConstructor player, HandVfxSettings handVfx)
    {
        if (handVfx == null)
            return;
        Push(player.leftHandVfx, handVfx);
        Push(player.rightHandVfx, handVfx);
    }

    private static void Push(VisualEffect vfx, HandVfxSettings handVfx)
    {
        if (vfx == null)
            return;

        foreach (var e in vfxTable)
        {
            var t = e.Field.FieldType;
            if (t == typeof(float))
            {
                if (vfx.HasFloat(e.Id))
                    vfx.SetFloat(e.Id, (float)e.Field.GetValue(handVfx));
                else
                    WarnMissingOnce(vfx, e.Name, "float");
            }
            else if (t == typeof(int))
            {
                int v = (int)e.Field.GetValue(handVfx);
                if (vfx.HasInt(e.Id))
                    vfx.SetInt(e.Id, v);
                else if (vfx.HasUInt(e.Id))
                    vfx.SetUInt(e.Id, (uint)Mathf.Max(0, v));
                else if (vfx.HasFloat(e.Id))
                    vfx.SetFloat(e.Id, v);
                else
                    WarnMissingOnce(vfx, e.Name, "int");
            }
            else if (t == typeof(Vector2))
            {
                if (vfx.HasVector2(e.Id))
                    vfx.SetVector2(e.Id, (Vector2)e.Field.GetValue(handVfx));
                else
                    WarnMissingOnce(vfx, e.Name, "Vector2");
            }
            else if (t == typeof(Vector3))
            {
                if (vfx.HasVector3(e.Id))
                    vfx.SetVector3(e.Id, (Vector3)e.Field.GetValue(handVfx));
                else
                    WarnMissingOnce(vfx, e.Name, "Vector3");
            }
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void WarnMissingOnce(VisualEffect vfx, string name, string type)
    {
        if (!warnedMissing.Add(name))
            return;
        Debug.LogWarning(
            $"[PlayerScaleApplier] HandVfxSettings names '{name}' ({type}) but the graph on "
                + $"'{vfx.name}' ({(vfx.visualEffectAsset ? vfx.visualEffectAsset.name : "no asset")}) "
                + "does not expose it - skipped. Rebuild the VFX graph or fix the [VfxProperty] name."
        );
    }

    /// <summary>
    /// bodyScale-dependent transforms. TD1/TD2, SA and Collider are all unit-scale
    /// siblings under the hand object (TD1/TD2 sit under a unit-scale "Trail
    /// Distorters" container) so none of these compounds with another.
    /// </summary>
    public void ApplyTransforms(PlayerConstructor player, RuntimeSceneSettings effective)
    {
        float bodyScale = effective.bodyScale > 0f ? effective.bodyScale : 1f;
        var handVfx = effective.handVfx;

        if (handVfx != null)
        {
            // TD debug spheres: unit sphere mesh -> diameter = conform radius x 2
            // (tdRadius is already effective, i.e. x bodyScale).
            Vector3 tdScale = Vector3.one * (handVfx.tdRadius * 2f);
            var wander = new Unity.Mathematics.float3(handVfx.tdWanderAmount);
            ForEachTd(
                player,
                td =>
                {
                    td.transform.localScale = tdScale;
                    if (td.TryGetComponent<BrownianMotion>(out var bm))
                        bm.positionAmount = wander;
                }
            );
        }

        // Hand raycast target: prefab BoxCollider.size is authored at 1x (1, 1, 0.02).
        Vector3 colliderScale = Vector3.one * bodyScale;
        if (player.leftHandCollider != null)
            player.leftHandCollider.localScale = colliderScale;
        if (player.rightHandCollider != null)
            player.rightHandCollider.localScale = colliderScale;

        // Secondary-attractor debug spheres (visual only; 0.02 at 1x = the prefab's 0.1 at 5x).
        Vector3 saScale = Vector3.one * (0.02f * bodyScale);
        if (player.leftHandSecondaryAttractor != null)
            player.leftHandSecondaryAttractor.localScale = saScale;
        if (player.rightHandSecondaryAttractor != null)
            player.rightHandSecondaryAttractor.localScale = saScale;
    }

    /// <summary>
    /// When the effective bodyScale changes while players are alive, their length-valued
    /// STATE (not settings) must follow, or the ball would keep its old world size/position
    /// while the body shrank or grew around it. The world scales about the origin (camera),
    /// exactly like Kinect joints do (joint x bodyScale), so positions scale about the origin.
    /// </summary>
    public void RescaleLiveState(PlayerConstructor player, float ratio)
    {
        if (player == null || ratio <= 0f || Mathf.Approximately(ratio, 1f))
            return;

        player.unscaledSize *= ratio;
        player.leftHandPrevPosition *= ratio;
        player.rightHandPrevPosition *= ratio;
        player.metaballRadiusAtAnimationStart *= ratio;

        if (player.sphere != null)
        {
            player.sphere.position *= ratio;
            player.sphere.linearVelocity *= ratio;
        }
    }

    public void RescaleLiveStateAll(IEnumerable<GameObject> players, float ratio)
    {
        if (players == null)
            return;
        foreach (var go in players)
        {
            if (go != null && go.TryGetComponent<PlayerConstructor>(out var pc))
                RescaleLiveState(pc, ratio);
        }
    }

    private static void ForEachTd(PlayerConstructor player, System.Action<GameObject> action)
    {
        Visit(player.leftHandTrailDistorters, action);
        Visit(player.rightHandTrailDistorters, action);
    }

    private static void Visit(GameObject[] objs, System.Action<GameObject> action)
    {
        if (objs == null)
            return;
        foreach (var go in objs)
            if (go != null)
                action(go);
    }
}
