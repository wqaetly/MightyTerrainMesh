namespace MightyTerrainMesh
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using TriangleNet.Geometry;
    //
    public class TessellationJob
    {
        public MTMeshData[] mesh;
        public MTMeshData[,] mesh_TwoArray;
        public MTTerrainScanner[] scanners;
        public bool IsDone
        {
            get
            {
                return curIdx >= mesh.Length;
            }
        }
        public float progress
        {
            get
            {
                return (float)(curIdx / (float)(mesh.Length));
            }
        }
        public TessellationJob(MTTerrainScanner[] s, float minTriArea)
        {
            scanners = s;
            MinTriArea = minTriArea;
            mesh = new MTMeshData[scanners[0].Trees.Length];
        }
        public float MinTriArea { get; private set; }
        protected int curIdx = 0;
        
        /// <summary>
        /// 正式开始调用三角化库生成Mesh
        /// </summary>
        /// <param name="lVerts"></param>
        /// <param name="lod"></param>
        /// <param name="minTriArea"></param>
        protected void RunTessellation(List<SampleVertexData> lVerts, MTMeshData.LOD lod, float minTriArea)
        {
            if (lVerts.Count < 3)
            {
                ++curIdx;
                return;
            }
            InputGeometry geometry = new InputGeometry();
            for (int i = 0; i < lVerts.Count; i++)
            {
                var vert = lVerts[i];
                geometry.AddPoint(vert.Position.x, lVerts[i].Position.z, 0);
            }

            TriangleNet.Mesh meshRepresentation = new TriangleNet.Mesh();
            meshRepresentation.Triangulate(geometry);
            
            if (meshRepresentation.Vertices.Count != lVerts.Count)
            {
                Debug.LogError("trianglate seems failed");
            }
            int vIdx = 0;
            lod.vertices = new List<Vector3>(meshRepresentation.Vertices.Count);
            lod.normals = new List<Vector3>(meshRepresentation.Vertices.Count);
            lod.uvs = new List<Vector2>(meshRepresentation.Vertices.Count);
            lod.faces = new List<int>(meshRepresentation.triangles.Count * 3);
            
            foreach (var v in meshRepresentation.Vertices)
            {
                lod.vertices.Add(new Vector3(v.x, lVerts[vIdx].Position.y, v.y));
                lod.normals.Add(lVerts[vIdx].Normal);
                
                var uv = lVerts[vIdx].UV;
                
                lod.uvs.Add(uv);
                
                ++vIdx;
            }
            vIdx = 0;
            foreach (var t in meshRepresentation.triangles.Values)
            {
                var p = new Vector2[] { new Vector2(lod.vertices[t.P0].x, lod.vertices[t.P0].z),
                    new Vector2(lod.vertices[t.P1].x, lod.vertices[t.P1].z),
                    new Vector2(lod.vertices[t.P2].x, lod.vertices[t.P2].z)};
                var triarea = UnityEngine.Mathf.Abs((p[2].x - p[0].x) * (p[1].y - p[0].y) -
                       (p[1].x - p[0].x) * (p[2].y - p[0].y)) / 2.0f;
                if (triarea < minTriArea)
                    continue;
                
                lod.faces.Add(t.P2);
                lod.faces.Add(t.P1);
                lod.faces.Add(t.P0);
                
                vIdx += 3;
            }
        }
        public virtual void Update()
        {
            if (IsDone)
                return;
            mesh[curIdx] = new MTMeshData(curIdx);
            mesh[curIdx].lods = new MTMeshData.LOD[scanners.Length];
            for (int lod = 0; lod < scanners.Length; ++lod)
            {
                var lodData = new MTMeshData.LOD();
                var tree = scanners[lod].Trees[curIdx];
                RunTessellation(tree.Vertices, lodData, MinTriArea);
                lodData.uvmin = tree.uvMin;
                lodData.uvmax = tree.uvMax;
                
                // 记录samplerTree用于mesh合并
                lodData.samplerTree = tree;
                mesh[curIdx].lods[lod] = lodData;
            }
            //update idx
            ++curIdx;
        }
    }
}
