using System.Collections.Generic;
using UnityEngine;
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

    public class MetaballsToSDF : MonoBehaviour
    {
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

        int VoxelCount => _dimensions.x * _dimensions.y * _dimensions.z;

        ComputeBuffer _voxelBuffer;
        ComputeBuffer _positionsBuffer;
        ComputeBuffer _radiiBuffer;
        MeshBuilder _builder;

        #endregion

        #region SDF baking / VFX graph implementation
        MeshToSDFBaker sdfBaker;
        Vector3 center = Vector3.zero;
        Vector3 CenterWS => transform.TransformPoint(center); // center in world space
        Vector3 sizeBox;
        public int resolution = 64;
        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            InitializeMetaballBuffers();
            _builder = new MeshBuilder(_dimensions, _triangleBudget, _builderCompute);
            controller = GetComponent<SceneController>();
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
        }

        void OnDestroy()
        {
            ReleaseBuffers();
            _builder.Dispose();
        }

        void Update()
        {
            UpdateMetaballBuffers();

            _volumeCompute.SetInts("Dims", _dimensions);
            _volumeCompute.SetFloat("Scale", _gridScale);
            _volumeCompute.SetBuffer(0, "Voxels", _voxelBuffer);
            _volumeCompute.SetBuffer(0, "MetaballCenters", _positionsBuffer);
            _volumeCompute.SetBuffer(0, "MetaballRadii", _radiiBuffer);
            _volumeCompute.DispatchThreads(0, _dimensions);

            // Isosurface reconstruction
            _builder.BuildIsosurface(_voxelBuffer, _targetValue, _gridScale);
            GetComponent<MeshFilter>().sharedMesh = _builder.Mesh;

            if (sdfBaker == null)
            {
                sdfBaker = new MeshToSDFBaker(
                    sizeBox,
                    CenterWS,
                    resolution,
                    _builder.Mesh,
                    1,
                    0.5f,
                    0f
                );
            }
            else
            {
                sdfBaker.Reinit(sizeBox, CenterWS, resolution, _builder.Mesh, 1, 0.5f, 0f);
            }

            sdfBaker.BakeSDF();

            foreach (var playerPair in controller.Players)
            {
                var player = playerPair.Value;
                VisualEffect _vfxLeft = player.GetComponent<PlayerConstructor>().leftHandVfx;
                VisualEffect _vfxRight = player.GetComponent<PlayerConstructor>().rightHandVfx;
                if (_vfxLeft != null)
                {
                    _vfxLeft.SetTexture("sdfTexture", sdfBaker.SdfTexture);
                    _vfxLeft.SetVector3("sdfScale", sizeBox);
                    _vfxLeft.SetFloat("zDepth", controller.so.baseZDepth);
                }
                if (_vfxRight != null)
                {
                    _vfxRight.SetTexture("sdfTexture", sdfBaker.SdfTexture);
                    _vfxRight.SetVector3("sdfScale", sizeBox);
                    _vfxRight.SetFloat("zDepth", controller.so.baseZDepth);
                }
            }
        }

        #endregion

        #region Helper Methods

        void InitializeMetaballBuffers()
        {
            // Create buffers
            _voxelBuffer = new ComputeBuffer(VoxelCount, sizeof(float));
            _positionsBuffer = new ComputeBuffer(metaballs.Count, sizeof(float) * 3);
            _radiiBuffer = new ComputeBuffer(metaballs.Count, sizeof(float));
        }

        void UpdateMetaballBuffers()
        {
            // Ensure buffers match the metaball count
            if (_positionsBuffer.count != metaballs.Count)
            {
                ReleaseBuffers();
                InitializeMetaballBuffers();
            }

            // Update positions and radii arrays
            Vector3[] positions = new Vector3[metaballs.Count];
            float[] radii = new float[metaballs.Count];

            for (int i = 0; i < metaballs.Count; i++)
            {
                positions[i] = metaballs[i].Position;
                radii[i] = metaballs[i].Radius;
            }

            _positionsBuffer.SetData(positions);
            _radiiBuffer.SetData(radii);
        }

        void ReleaseBuffers()
        {
            _voxelBuffer.Dispose();
            _positionsBuffer.Dispose();
            _radiiBuffer.Dispose();
            sdfBaker?.Dispose();
            sdfBaker = null;
        }

        #endregion

        #region Public Methods

        public void AssignMetaballIndex(PlayerConstructor player)
        {
            for (int i = 0; i < metaballs.Count; i++)
            {
                if (!activeMetaballIndices.Contains(i))
                {
                    activeMetaballIndices.Add(i);
                    player.metaballIndex = i;
                    break;
                }
            }
        }

        public void RemoveMetaballIndex(int index)
        {
            activeMetaballIndices.Remove(index);
            metaballs[index].Position = new Vector3(-100f, -100f, -100f);
            metaballs[index].Radius = 0f;
        }

        public void SetMetaballPosition(int index, Vector3 position)
        {
            position.z -= controller.so.baseZDepth;
            metaballs[index].Position = position;
        }

        public void SetMetaballRadius(int index, float radius)
        {
            metaballs[index].Radius = radius;
        }

        #endregion
    }
}
