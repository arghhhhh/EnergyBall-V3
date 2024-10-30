using UnityEngine;
using System.Collections.Generic;

namespace MarchingCubes
{
    [System.Serializable]
    public class Metaball
    {
        public Vector3 Position;
        public float Radius;
    }

    public class MetaballsVisualizer : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] Vector3Int _dimensions = new Vector3Int(64, 32, 64);
        [SerializeField] float _gridScale = 4.0f / 64;
        [SerializeField] int _triangleBudget = 65536;
        [SerializeField] float _targetValue = 0.26f;

        [Header("Metaballs")]
        public List<Metaball> metaballs = new List<Metaball>()
        {
            new Metaball { Position = new Vector3(100f, 0f, 0f), Radius = 0f },
            new Metaball { Position = new Vector3(100f, 0f, 0f), Radius = 0f },
            new Metaball { Position = new Vector3(100f, 0f, 0f), Radius = 0f },
            new Metaball { Position = new Vector3(100f, 0f, 0f), Radius = 0f },
            new Metaball { Position = new Vector3(100f, 0f, 0f), Radius = 0f },
            new Metaball { Position = new Vector3(100f, 0f, 0f), Radius = 0f },
        };

        public List<int> activeMetaballIndices = new List<int>();

        #endregion

        #region Project asset references

        [SerializeField, HideInInspector] ComputeShader _volumeCompute = null;
        [SerializeField, HideInInspector] ComputeShader _builderCompute = null;

        #endregion

        #region Private members

        int VoxelCount => _dimensions.x * _dimensions.y * _dimensions.z;

        ComputeBuffer _voxelBuffer;
        ComputeBuffer _positionsBuffer;
        ComputeBuffer _radiiBuffer;
        MeshBuilder _builder;

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            InitializeMetaballBuffers();
            _builder = new MeshBuilder(_dimensions, _triangleBudget, _builderCompute);
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
        }

        public void AddMetaball(Vector3 position, float radius)
        {
            metaballs.Add(new Metaball { Position = position, Radius = radius });
        }

        public void RemoveMetaball(int index)
        {
            metaballs.RemoveAt(index);
        }

        public void SetMetaballPosition(int index, Vector3 position)
        {
            metaballs[index].Position = position;
        }

        public void SetMetaballRadius(int index, float radius)
        {
            metaballs[index].Radius = radius;
        }

        #endregion
    }
}
