using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using UnityEngine.VFX.SDF;

namespace MarchingCubes
{
    [System.Serializable]
    public class Metaball
    {
        public Vector3 Position;
        public float Radius;
    }

    /// <summary>
    /// Owns the shared metaball field and produces the signed distance texture the
    /// hand VFX graphs conform to.
    ///
    /// Two SDF sources:
    /// - <see cref="SdfSource.Analytic"/> (default): one compute dispatch writes the
    ///   signed distance to the isosurface straight from the metaball field
    ///   (Newton step on the field, see MetaballsGenerator.compute). No mesh needed.
    /// - <see cref="SdfSource.MeshBake"/>: the original path — marching cubes into a
    ///   mesh, then the VFX package's MeshToSDFBaker. Kept for A/B comparison.
    ///
    /// The marching-cubes mesh is only built when it is actually consumed: when the
    /// debug "show metaball mesh" setting is on, or in MeshBake mode.
    ///
    /// GPU work only runs when something changed (a metaball moved or resized, a
    /// player joined/left, gridScale changed, the mesh was toggled on). Metaball data
    /// is written from SceneController.FixedUpdate, so rendered frames without a
    /// physics step, and idle time with no players, cost nothing here.
    ///
    /// The SDF texture is a persistent RenderTexture, so the hand graphs are bound to
    /// it once per player (and again only if the texture, box size or depth change),
    /// not every frame.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class MetaballsToSDF : MonoBehaviour
    {
        public enum SdfSource
        {
            Analytic,
            MeshBake,
        }

        SceneController controller = null;

        #region Editable attributes

        [SerializeField]
        Vector3Int _dimensions = new Vector3Int(64, 32, 64);

        [SerializeField]
        float _gridScale = 4.0f / 64;

        [SerializeField]
        int _triangleBudget = 65536;

        [SerializeField]
        float _targetValue = 0.26f;

        [SerializeField]
        [Tooltip(
            "Analytic: signed distance computed directly from the metaball field in one "
                + "dispatch (default). MeshBake: marching cubes + MeshToSDFBaker (legacy, "
                + "much more expensive; kept for comparison)."
        )]
        SdfSource _sdfSource = SdfSource.Analytic;

        [SerializeField]
        List<Metaball> metaballs = new List<Metaball>();

        public List<int> activeMetaballIndices = new List<int>();

        #endregion

        #region Project asset references

        [SerializeField, HideInInspector]
        ComputeShader _volumeCompute = null;

        [SerializeField, HideInInspector]
        ComputeShader _builderCompute = null;

        #endregion

        #region Private members

        const int FieldKernel = 0;
        const int SdfKernel = 1;

        static readonly int DimsId = Shader.PropertyToID("Dims");
        static readonly int ScaleId = Shader.PropertyToID("Scale");
        static readonly int IsovalueId = Shader.PropertyToID("Isovalue");
        static readonly int MaxExtentId = Shader.PropertyToID("MaxExtent");
        static readonly int VoxelsId = Shader.PropertyToID("Voxels");
        static readonly int SdfId = Shader.PropertyToID("Sdf");
        static readonly int MetaballCentersId = Shader.PropertyToID("MetaballCenters");
        static readonly int MetaballRadiiId = Shader.PropertyToID("MetaballRadii");

        static readonly int VfxSdfTextureId = Shader.PropertyToID("sdfTexture");
        static readonly int VfxSdfScaleId = Shader.PropertyToID("sdfScale");
        static readonly int VfxZDepthId = Shader.PropertyToID("zDepth");

        int VoxelCount => _dimensions.x * _dimensions.y * _dimensions.z;

        ComputeBuffer _voxelBuffer;
        ComputeBuffer _positionsBuffer;
        ComputeBuffer _radiiBuffer;
        Vector3[] _positionsScratch;
        float[] _radiiScratch;

        MeshBuilder _builder; // created lazily, only when the mesh is consumed
        MeshFilter _meshFilter;
        MeshRenderer _meshRenderer;

        RenderTexture _analyticSdf;

        // Anything that changes the field or the mesh sets this; Update only
        // dispatches when it is set.
        bool _fieldDirty = true;
        bool _meshWasShown = false;

        // Hand-VFX binding state: rebound only when one of these changes.
        bool _bindingsDirty = true;
        Texture _boundTexture;
        Vector3 _boundSizeBox;
        float _boundZDepth;

        #endregion

        #region SDF baking / VFX graph implementation (MeshBake mode)
        MeshToSDFBaker sdfBaker;
        Vector3 bakerSizeBox;
        Vector3 center = Vector3.zero;
        Vector3 CenterWS => transform.TransformPoint(center); // center in world space
        Vector3 sizeBox;
        public int resolution = 64;
        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            InitializeMetaballBuffers();
            InitializeSdfTexture();
            // May live on its own "Metaballs" GameObject — resolve the
            // controller via the singleton (set in SceneController.Awake,
            // which runs first via [DefaultExecutionOrder(-200)]).
            controller = SceneController.Instance;
            if (controller == null)
            {
                controller = GetComponent<SceneController>();
            }
            _meshFilter = GetComponent<MeshFilter>();
            _meshRenderer = GetComponent<MeshRenderer>();
            sizeBox = new Vector3(
                _dimensions.x * _gridScale,
                _dimensions.y * _gridScale,
                _dimensions.z * _gridScale
            );

            // Make sure the inactive metaballs are out of the way
            foreach (var metaball in metaballs)
            {
                metaball.Position.x = -100f;
                metaball.Position.y = -100f;
                metaball.Position.z = -100f;
            }
            _fieldDirty = true;
        }

        void OnDestroy()
        {
            ReleaseBuffers();
            ReleaseSdfTexture();
            _builder?.Dispose();
            _builder = null;
        }

        void Update()
        {
            var runtimeSettings = controller.GetRuntimeSettings();
            bool showMesh = runtimeSettings.showMetaballMesh;
            _meshRenderer.enabled = showMesh;

            // The metaball volume is world-fixed at (0,0,baseZDepth) — clamping,
            // BoundaryForce, and the gizmos all assume this. Keep this transform
            // there so the marching-cubes mesh (built in volume-local space)
            // renders at its true world position instead of baseZDepth behind it.
            transform.position = new Vector3(0f, 0f, runtimeSettings.baseZDepth);

            // gridScale is a per-profile setting (falls back to the serialized
            // value if unset) so differently-scaled worlds keep a well-resolved
            // ball. Voxel count is fixed; only the voxel size changes.
            float gridScale =
                runtimeSettings.gridScale > 0f ? runtimeSettings.gridScale : _gridScale;
            Vector3 newSizeBox = new Vector3(
                _dimensions.x * gridScale,
                _dimensions.y * gridScale,
                _dimensions.z * gridScale
            );
            if (newSizeBox != sizeBox)
            {
                sizeBox = newSizeBox;
                _fieldDirty = true;
            }

            bool needMesh = showMesh || _sdfSource == SdfSource.MeshBake;
            if (needMesh && _builder == null)
            {
                _builder = new MeshBuilder(_dimensions, _triangleBudget, _builderCompute);
                _meshFilter.sharedMesh = _builder.Mesh;
                _fieldDirty = true;
            }
            if (showMesh && !_meshWasShown)
            {
                // Mesh may be stale (or never built) while it was hidden.
                _fieldDirty = true;
            }
            _meshWasShown = showMesh;

            if (_fieldDirty)
            {
                Rebuild(gridScale, needMesh);
                _fieldDirty = false;
            }

            Texture currentSdf = CurrentSdfTexture;
            if (
                _bindingsDirty
                || currentSdf != _boundTexture
                || sizeBox != _boundSizeBox
                || runtimeSettings.baseZDepth != _boundZDepth
            )
            {
                BindSdfToAllPlayers(currentSdf, runtimeSettings.baseZDepth);
            }
        }

        #endregion

        #region Helper Methods

        Texture CurrentSdfTexture =>
            _sdfSource == SdfSource.Analytic ? _analyticSdf : sdfBaker?.SdfTexture;

        void Rebuild(float gridScale, bool needMesh)
        {
            UploadMetaballBuffers();

            _volumeCompute.SetInts(DimsId, _dimensions);
            _volumeCompute.SetFloat(ScaleId, gridScale);

            if (needMesh)
            {
                _volumeCompute.SetBuffer(FieldKernel, VoxelsId, _voxelBuffer);
                _volumeCompute.SetBuffer(FieldKernel, MetaballCentersId, _positionsBuffer);
                _volumeCompute.SetBuffer(FieldKernel, MetaballRadiiId, _radiiBuffer);
                _volumeCompute.DispatchThreads(FieldKernel, _dimensions);

                _builder.BuildIsosurface(_voxelBuffer, _targetValue, gridScale);
            }

            if (_sdfSource == SdfSource.Analytic)
            {
                _volumeCompute.SetFloat(IsovalueId, _targetValue);
                _volumeCompute.SetFloat(
                    MaxExtentId,
                    Mathf.Max(sizeBox.x, Mathf.Max(sizeBox.y, sizeBox.z))
                );
                _volumeCompute.SetTexture(SdfKernel, SdfId, _analyticSdf);
                _volumeCompute.SetBuffer(SdfKernel, MetaballCentersId, _positionsBuffer);
                _volumeCompute.SetBuffer(SdfKernel, MetaballRadiiId, _radiiBuffer);
                _volumeCompute.DispatchThreads(SdfKernel, _dimensions);
            }
            else
            {
                BakeMeshSdf();
            }
        }

        // Legacy path: bake the marching-cubes mesh into an SDF with the VFX
        // package baker, in volume-local space (mesh vertices are local); the VFX
        // graph's ConformToSDF FieldTransform (center z = zDepth = baseZDepth)
        // places the field back at the volume's world position.
        void BakeMeshSdf()
        {
            if (sdfBaker == null)
            {
                sdfBaker = new MeshToSDFBaker(
                    sizeBox,
                    center,
                    resolution,
                    _builder.Mesh,
                    1,
                    0.5f,
                    0f
                );
                bakerSizeBox = sizeBox;
            }
            else if (bakerSizeBox != sizeBox)
            {
                // Reinit re-runs the baker's full setup; only needed when the box changes.
                sdfBaker.Reinit(sizeBox, center, resolution, _builder.Mesh, 1, 0.5f, 0f);
                bakerSizeBox = sizeBox;
            }

            sdfBaker.BakeSDF();
        }

        void InitializeMetaballBuffers()
        {
            _voxelBuffer = new ComputeBuffer(VoxelCount, sizeof(float));
            _positionsBuffer = new ComputeBuffer(metaballs.Count, sizeof(float) * 3);
            _radiiBuffer = new ComputeBuffer(metaballs.Count, sizeof(float));
            _positionsScratch = new Vector3[metaballs.Count];
            _radiiScratch = new float[metaballs.Count];
        }

        void InitializeSdfTexture()
        {
            var desc = new RenderTextureDescriptor
            {
                width = _dimensions.x,
                height = _dimensions.y,
                volumeDepth = _dimensions.z,
                dimension = TextureDimension.Tex3D,
                graphicsFormat = GraphicsFormat.R16_SFloat,
                enableRandomWrite = true,
                msaaSamples = 1,
            };
            _analyticSdf = new RenderTexture(desc)
            {
                name = "Metaball SDF (analytic)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            _analyticSdf.Create();
        }

        void ReleaseSdfTexture()
        {
            if (_analyticSdf != null)
            {
                _analyticSdf.Release();
                Destroy(_analyticSdf);
                _analyticSdf = null;
            }
        }

        void UploadMetaballBuffers()
        {
            // Ensure buffers match the metaball count
            if (_positionsBuffer.count != metaballs.Count)
            {
                ReleaseBuffers();
                InitializeMetaballBuffers();
            }

            for (int i = 0; i < metaballs.Count; i++)
            {
                _positionsScratch[i] = metaballs[i].Position;
                _radiiScratch[i] = metaballs[i].Radius;
            }

            _positionsBuffer.SetData(_positionsScratch);
            _radiiBuffer.SetData(_radiiScratch);
        }

        void ReleaseBuffers()
        {
            _voxelBuffer?.Dispose();
            _positionsBuffer?.Dispose();
            _radiiBuffer?.Dispose();
            sdfBaker?.Dispose();
            sdfBaker = null;
        }

        void BindSdfToAllPlayers(Texture sdf, float zDepth)
        {
            if (controller == null)
            {
                return;
            }
            foreach (var playerPair in controller.Players)
            {
                if (playerPair.Value.TryGetComponent<PlayerConstructor>(out var player))
                {
                    BindSdf(player, sdf, zDepth);
                }
            }
            _boundTexture = sdf;
            _boundSizeBox = sizeBox;
            _boundZDepth = zDepth;
            _bindingsDirty = false;
        }

        void BindSdf(PlayerConstructor player, Texture sdf, float zDepth)
        {
            BindHand(player.leftHandVfx, sdf, zDepth);
            BindHand(player.rightHandVfx, sdf, zDepth);
        }

        void BindHand(VisualEffect vfx, Texture sdf, float zDepth)
        {
            if (vfx == null)
            {
                return;
            }
            if (sdf != null)
            {
                vfx.SetTexture(VfxSdfTextureId, sdf);
            }
            vfx.SetVector3(VfxSdfScaleId, sizeBox);
            vfx.SetFloat(VfxZDepthId, zDepth);
        }

        #endregion

        #region Public Methods

        public void AssignMetaballIndex(PlayerConstructor player)
        {
            for (int i = 0; i < metaballs.Count; i++)
            {
                if (!activeMetaballIndices.Contains(i))
                {
                    Debug.Log($"Adding metaball index: {i}");
                    activeMetaballIndices.Add(i);
                    player.metaballIndex = i;
                    _fieldDirty = true;
                    // Bind the new player's hand graphs now; Update re-binds
                    // everyone if the texture isn't ready yet.
                    _bindingsDirty = true;
                    break;
                }
            }
        }

        public void RemoveMetaballIndex(int index)
        {
            Debug.Log($"Removing metaball index: {index}");
            activeMetaballIndices.Remove(index);
            metaballs[index].Position = new Vector3(-100f, -100f, -100f);
            metaballs[index].Radius = 0f;
            _fieldDirty = true;
        }

        public void SetMetaballPosition(int index, Vector3 position)
        {
            var runtimeSettings = controller.GetRuntimeSettings();
            position.z -= runtimeSettings.baseZDepth;
            if (metaballs[index].Position != position)
            {
                metaballs[index].Position = position;
                _fieldDirty = true;
            }
        }

        public void SetMetaballRadius(int index, float radius)
        {
            if (metaballs[index].Radius != radius)
            {
                metaballs[index].Radius = radius;
                _fieldDirty = true;
            }
        }

        /// <summary>
        /// Returns the size of the marching cubes grid in world units.
        /// At runtime, returns the cached sizeBox. In edit mode, calculates from serialized fields.
        /// </summary>
        public Vector3 GetGridSize()
        {
            // If sizeBox hasn't been initialized (edit mode), calculate from serialized fields
            if (sizeBox == Vector3.zero)
            {
                return new Vector3(
                    _dimensions.x * _gridScale,
                    _dimensions.y * _gridScale,
                    _dimensions.z * _gridScale
                );
            }
            return sizeBox;
        }

        #endregion
    }
}
