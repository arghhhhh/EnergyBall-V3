using System;
using System.Collections.Generic;
using System.Linq;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.SDF;
using UnityEngine.VFX.Utility;

public class MeshesToSDF : MonoBehaviour
{
    SceneController controller = null;

    MeshToSDFBaker sdfBaker;
    Mesh mesh;

    [Header("Box")]
    public bool setBoxBoundsAutomatically = false;

    [DisableIf("setBoxBoundsAutomatically")]
    public Vector3 center;

    [DisableIf("setBoxBoundsAutomatically")]
    public Vector3 sizeBox = Vector3.one;
    public int maxResolution = 32;

    [Header("SDF Baker")]
    [Range(1, 16)]
    public int signPassesCount = 1;

    [Range(0f, 1f)]
    public float threshold = 0.5f;

    [Range(-1f, 1f)]
    public float offset = 0f;

    [Header("VFX Graph")]
    [SerializeField]
    private ExposedProperty sdfTextureProperty = "sdfTexture";

    [SerializeField]
    private ExposedProperty sdfPositionProperty = "sdfPosition";

    [SerializeField]
    private ExposedProperty sdfScaleProperty = "sdfScale";

    public Vector3 CenterWS => transform.TransformPoint(center); // center in world space

    private void OnValidate()
    {
        sizeBox = new Vector3(
            Mathf.Max(0, sizeBox.x),
            Mathf.Max(0, sizeBox.y),
            Mathf.Max(0, sizeBox.z)
        );
    }

    void Start()
    {
        controller = SceneController.Instance;
    }

    void Update()
    {
        if (controller.Players.Count > 0 && mesh != null)
        {
            if (sdfBaker == null)
            {
                sdfBaker = new MeshToSDFBaker(
                    sizeBox,
                    CenterWS,
                    maxResolution,
                    mesh,
                    signPassesCount,
                    threshold,
                    offset
                );
            }
            else
            {
                sdfBaker.Reinit(
                    sizeBox,
                    CenterWS,
                    maxResolution,
                    mesh,
                    signPassesCount,
                    threshold,
                    offset
                );
            }

            // Automatically set box bounds
            if (setBoxBoundsAutomatically)
            {
                center = mesh.bounds.center;
                sizeBox = mesh.bounds.size;
            }

            sdfBaker.BakeSDF();

            VisualEffect[] playerVfx = FindObjectsByType<VisualEffect>(FindObjectsSortMode.None);

            foreach (VisualEffect vfx in playerVfx)
            {
                vfx.SetTexture(sdfTextureProperty, sdfBaker.SdfTexture);
                vfx.SetVector3(sdfScaleProperty, sizeBox);
                vfx.SetVector3(sdfPositionProperty, CenterWS);
            }
        }
        // else {
            mesh = GetComponent<MeshFilter>().sharedMesh;
        // }
    }

    private void OnDestroy()
    {
        sdfBaker?.Dispose();
        sdfBaker = null;
    }

    private void OnDrawGizmosSelected()
    {
        // Baking box gizmo
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(CenterWS, sizeBox);
    }
}
