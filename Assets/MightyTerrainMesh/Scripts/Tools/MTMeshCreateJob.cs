namespace MightyTerrainMesh
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    #region 结构定义

    public class MeshLODCreate
    {
        public int Subdivision = 3;
        public float SlopeAngleError = 5f;
    }

    public class CreateMeshJob
    {
        public MTTerrainScanner[] LODs;
        private int curLodIdx = 0;

        public bool IsDone
        {
            get { return curLodIdx >= LODs.Length; }
        }

        public float progress
        {
            get
            {
                if (curLodIdx < LODs.Length)
                {
                    return (curLodIdx + LODs[curLodIdx].progress) / LODs.Length;
                }

                return 1;
            }
        }

        public CreateMeshJob(Terrain t, Bounds VolumnBound, int mx, int mz, MeshLODCreate[] setting)
        {
            LODs = new MTTerrainScanner[setting.Length];
            for (int i = 0; i < setting.Length; ++i)
            {
                MeshLODCreate s = setting[i];
                //only first lod stitch borders, other lod use the most detailed border to avoid 
                //tearing on the border
                LODs[i] = new MTTerrainScanner(t, VolumnBound, s.Subdivision, s.SlopeAngleError, mx, mz,
                    i == 0);
            }
        }

        public void Update()
        {
            if (LODs == null || IsDone)
                return;
            LODs[curLodIdx].Update();
            if (LODs[curLodIdx].IsDone)
                ++curLodIdx;
        }

        public void EndProcess()
        {
            MTTerrainScanner detail = LODs[0];
            detail.FillData();

            // 只有多LOD才会跑到这里
            for (int i = 1; i < LODs.Length; ++i)
            {
                MTTerrainScanner scaner = LODs[i];
                for (int t = 0; t < detail.Trees.Length; ++t)
                {
                    SamplerTree dt = detail.Trees[t];
                    SamplerTree lt = scaner.Trees[t];
                    foreach (var b in dt.Boundaries)
                    {
                        lt.Boundaries.Add(b.Key, b.Value);
                    }
                }

                scaner.FillData();
            }
        }
    }

    #endregion

    public class MTTerrainScanner : ITerrainTreeScaner
    {
        /// <summary>
        /// x方向tile数量
        /// </summary>
        public int maxX { get; private set; }

        /// <summary>
        /// z方向tile数量
        /// </summary>
        public int maxZ { get; private set; }

        public int subdivision { get; private set; }
        public float slopeAngleErr { get; private set; }

        /// <summary>
        /// 单个Tile的大小，例如1000x1000的Terrian被切成2x2，那么gridSize就是500
        /// </summary>
        public Vector2 gridSize { get; private set; }

        public int detailedSize = 1;

        /// <summary>
        /// 采样树的数量，例如被切成2x2，数组大小就是4，每个Tree代表一个Mesh
        /// </summary>
        public SamplerTree[] Trees { get; private set; }

        public Vector3 center
        {
            get { return volBnd.center; }
        }

        private int curXIdx = 0;
        private int curZIdx = 0;
        private bool stitchBorder = true;

        public bool IsDone
        {
            get { return curXIdx >= maxX && curZIdx >= maxZ; }
        }

        public float progress
        {
            get { return (float)(curXIdx + curZIdx * maxX) / (float)(maxX * maxZ); }
        }

        private Bounds volBnd;
        private Terrain terrain;

        /// <summary>
        /// 看起来是左上角的坐标
        /// </summary>
        private Vector3 check_start;

        /// <summary>
        /// terrian的splatmap信息，x，y代表纹素坐标，z代表splatalpha索引，例如地形用了一张SlpatAlpha图，且各个通道都存储了layar的混合信息，那么z就会有4个维度
        /// </summary>
        private Texture2D m_splatmapInfo;

        private int m_splatHeight;
        private int m_splatWidth;

        public MTTerrainScanner(Terrain t, Bounds VolumnBound, int sub, float angleErr, int mx, int mz, bool sbrd)
        {
            terrain = t;
            volBnd = VolumnBound;
            maxX = mx;
            maxZ = mz;
            subdivision = Mathf.Max(1, sub);
            slopeAngleErr = angleErr;
            stitchBorder = sbrd;
            gridSize = new Vector2(VolumnBound.size.x / mx, VolumnBound.size.z / mz);

            check_start = new Vector3(VolumnBound.center.x - VolumnBound.size.x / 2,
                VolumnBound.center.y + VolumnBound.size.y / 2,
                VolumnBound.center.z - VolumnBound.size.z / 2);

            // 每个tile分辨率，例如64x64的grid组成一个tile
            detailedSize = 1 << subdivision;

            // 分了几个tile就有几个tree
            Trees = new SamplerTree[maxX * maxZ];

            m_splatHeight = t.terrainData.alphamapHeight;
            m_splatWidth = t.terrainData.alphamapWidth;
            m_splatmapInfo = t.terrainData.GetAlphamapTexture(0);
        }

        public SamplerTree GetSubTree(int x, int z)
        {
            if (x < 0 || x >= maxX || z < 0 || z >= maxZ)
                return null;
            return Trees[x * maxZ + z];
        }

        void ITerrainTreeScaner.Run(Vector3 center, out Vector3 hitpos, out Vector3 hitnormal)
        {
            hitpos = center;
            float fx = (center.x - volBnd.min.x) / volBnd.size.x;
            float fy = (center.z - volBnd.min.z) / volBnd.size.z;
            hitpos.y = terrain.SampleHeight(center) + terrain.gameObject.transform.position.y;
            hitnormal = terrain.terrainData.GetInterpolatedNormal(fx, fy);
        }

        private void ScanTree(SamplerTree sampler)
        {
            sampler.RunSampler(this);
            if (!stitchBorder)
                return;
            int detailedX = curXIdx * detailedSize;
            int detailedZ = curZIdx * detailedSize;
            //boundary
            float bfx = curXIdx * gridSize[0];
            float bfz = curZIdx * gridSize[1];
            float borderOffset = 0;
            if (curXIdx == 0 || curZIdx == 0 || curXIdx == maxX - 1 || curZIdx == maxZ - 1)
                borderOffset = 0.000001f;

            // 先采样Tile四个顶点的信息
            RayCastBoundary(bfx + borderOffset, bfz + borderOffset,
                detailedX, detailedZ, SamplerTree.LBCorner, sampler);
            RayCastBoundary(bfx + borderOffset, bfz + gridSize[1] - borderOffset,
                detailedX, detailedZ + detailedSize - 1, SamplerTree.LTCorner, sampler);
            RayCastBoundary(bfx + gridSize[0] - borderOffset, bfz + gridSize[1] - borderOffset,
                detailedX + detailedSize - 1, detailedZ + detailedSize - 1, SamplerTree.RTCorner, sampler);
            RayCastBoundary(bfx + gridSize[0] - borderOffset, bfz + borderOffset,
                detailedX + detailedSize - 1, detailedZ, SamplerTree.RBCorner, sampler);

            // 再根据NXN的值去逐“顶点”采样对应的顶点信息（u方向）
            for (int u = 1; u < detailedSize; ++u)
            {
                float fx = (curXIdx + (float)u / detailedSize) * gridSize[0];
                RayCastBoundary(fx, bfz + borderOffset, u + detailedX, detailedZ, SamplerTree.BBorder, sampler);
                RayCastBoundary(fx, bfz + gridSize[1] - borderOffset,
                    u + detailedX, detailedZ + detailedSize - 1, SamplerTree.TBorder, sampler);
            }

            // 再根据NXN的值去逐“顶点”采样对应的顶点信息（v方向）
            for (int v = 1; v < detailedSize; ++v)
            {
                float fz = (curZIdx + (float)v / detailedSize) * gridSize[1];
                RayCastBoundary(bfx + borderOffset, fz, detailedX, v + detailedZ, SamplerTree.LBorder, sampler);
                RayCastBoundary(bfx + gridSize[0] - borderOffset, fz,
                    detailedX + detailedSize - 1, v + detailedZ, SamplerTree.RBorder, sampler);
            }

            // 采样splatmap，获取混合信息
            float scaleRate = gridSize.x * 1.0f / terrain.terrainData.bounds.size.x;
            var startPoint = new Vector2(bfx, bfz) /
                new Vector2(terrain.terrainData.bounds.size.x, terrain.terrainData.bounds.size.z) * m_splatWidth;
            int sampleCount = Mathf.CeilToInt(scaleRate * m_splatWidth);

            void SampleAndUpdateInfo(int x, int z)
            {
                // 获取splat信息
                int targetXInSplat =
                    Mathf.FloorToInt(Mathf.Clamp(startPoint.x + x, 0, 511));
                int targetZInSplat =
                    Mathf.FloorToInt(Mathf.Clamp((startPoint.y + z), 0,
                        511));

                // TODO 临时测试
                // 取三个通道的Weight，看谁为1，每采样周围4个纹素，保守的保留混合区域，尽可能保证效果
                var color = m_splatmapInfo.GetPixel(targetXInSplat, targetZInSplat);

                if (color.r != 0)
                {
                    sampler.UpdateRefTextureIndex(0);
                }

                if (color.g != 0)
                {
                    sampler.UpdateRefTextureIndex(1);
                }

                if (color.b != 0)
                {
                    sampler.UpdateRefTextureIndex(2);
                }
            }
            
            // 逐纹素采样
            for (int i = 0; i < sampleCount; i++)
            {
                for (int j = 0; j < sampleCount; j++)
                {
                    SampleAndUpdateInfo(i, j );
                    
                    SampleAndUpdateInfo(i - 1, j - 1);
                    SampleAndUpdateInfo(i - 1, j );
                    SampleAndUpdateInfo(i, j - 1);
                    
                    SampleAndUpdateInfo(i + 1, j + 1);
                    SampleAndUpdateInfo(i + 1, j );
                    SampleAndUpdateInfo(i, j + 1 );
                    
                    SampleAndUpdateInfo(i + 1, j - 1);
                    SampleAndUpdateInfo(i - 1, j + 1);
                }
            }
        }

        public void FillData()
        {
            for (int i = 0; i < Trees.Length; ++i)
            {
                Trees[i].FillData(slopeAngleErr);
            }

            float minDis = Mathf.Min(gridSize.x, gridSize.y) / detailedSize / 2f;
            for (int x = 0; x < maxX; ++x)
            {
                for (int z = 0; z < maxZ; ++z)
                {
                    SamplerTree center = GetSubTree(x, z);
                    // 缝合4个角
                    StitchCorner(x, z);

                    // 缝合4个边
                    center.StitchBorder(SamplerTree.BBorder, SamplerTree.TBorder, minDis, GetSubTree(x, z - 1));
                    center.StitchBorder(SamplerTree.LBorder, SamplerTree.RBorder, minDis, GetSubTree(x - 1, z));
                    center.StitchBorder(SamplerTree.RBorder, SamplerTree.LBorder, minDis, GetSubTree(x + 1, z));
                    center.StitchBorder(SamplerTree.TBorder, SamplerTree.BBorder, minDis, GetSubTree(x, z + 1));
                }
            }

            // 将缝合的顶点加入Mesh顶点，用于生成正确的Mesh
            for (int i = 0; i < Trees.Length; ++i)
            {
                foreach (var l in Trees[i].Boundaries.Values)
                    Trees[i].Vertices.AddRange(l);
            }
        }

        private TessellationJob m_tessellationJob;
        private int xLength;
        private int zLength;

        public void MergeMesh(TessellationJob tessellationJob, int xLength, int zLength)
        {
            m_tessellationJob = tessellationJob;
            this.xLength = xLength;
            this.zLength = zLength;
            
            for (int x = 0; x < xLength; x++)
            {
                for (int z = 0; z < zLength; z++)
                {
                    var center = tessellationJob.mesh_TwoArray[x, z];
                    center.x = x;
                    center.z = z;
                }
            }

            for (int x = 0; x < xLength; x++)
            {
                for (int z = 0; z < zLength; z++)
                {
                    var center = tessellationJob.mesh_TwoArray[x, z];
                    MergeNeighbor(center, x, z, center.isSingleBlend);
                }
            }
        }

        /// <summary>
        /// 合并周边Tile
        /// </summary>
        private void MergeNeighbor(MTMeshData mtMeshData, int x, int z, bool singleBlend)
        {
            if (mtMeshData.isSingleBlend != singleBlend)
            {
                return;
            }

            Stack<MTMeshData> tobeHandledMesh = new Stack<MTMeshData>();
            HashSet<MTMeshData> hasVisted = new HashSet<MTMeshData>();
            
            hasVisted.Add(mtMeshData);
            tobeHandledMesh.Push(mtMeshData);

            while (tobeHandledMesh.Count > 0)
            {
                var targetToHandle = tobeHandledMesh.Peek();

                var holdeer = m_tessellationJob.mesh_TwoArray[targetToHandle.x, targetToHandle.z];
                
                if (!MergeInternal(targetToHandle.x + 1, targetToHandle.z, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x - 1, targetToHandle.z, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x + 1, targetToHandle.z + 1, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x, targetToHandle.z + 1, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x - 1, targetToHandle.z + 1, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x - 1, targetToHandle.z - 1, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x, targetToHandle.z - 1, holdeer))
                {
                    continue;
                }
                
                if (!MergeInternal(targetToHandle.x + 1, targetToHandle.z - 1, holdeer))
                {
                    continue;
                }
                
                tobeHandledMesh.Pop();
            }

            // x,z是mergeHolder将要merge的目标tree索引
            // mergeHolder是merge的发起方，holderx，holderz代表mergeHolder的索引
            bool MergeInternal(int x, int z, MTMeshData mergeHolder)
            {
                int holderx = mergeHolder.x;
                int holderz = mergeHolder.z;
                
                if (x < 0 || z < 0 || x >= this.xLength || z >= this.zLength)
                {
                    return true;
                }

                var tree = m_tessellationJob.mesh_TwoArray[x, z];

                bool visitInfo = hasVisted.Contains(tree);

                if (visitInfo)
                {
                    return true;
                }

                if (tree != null &&
                    (!tree.hasMerged) &&
                    tree.isSingleBlend == singleBlend &&
                    !tree.mergedTileIndex.Contains((holderx, holderz)) && !mergeHolder.mergedTileIndex.Contains((x, z)))
                {
                    if (singleBlend)
                    {
                        if (tree.refTextureIndex == mergeHolder.refTextureIndex)
                        {
                            tree.hasMerged = true;
                            tree.mergedTileIndex.Add((holderx, holderz));
                            mergeHolder.mergedTileIndex.Add((x, z));

                            mergeHolder.lods[0].Merge(tree.lods[0]);

                            if (!hasVisted.Contains(tree))
                            {
                                tobeHandledMesh.Push(tree);
                            }
                        }
                    }
                    else
                    {
                        tree.hasMerged = true;
                        tree.mergedTileIndex.Add((holderx, holderz));
                        mergeHolder.mergedTileIndex.Add((x, z));

                        mergeHolder.lods[0].Merge(tree.lods[0]);

                        if (!hasVisted.Contains(tree))
                        {
                            tobeHandledMesh.Push(tree);
                        }
                    }
                }
                else
                {
                    visitInfo = true;
                }
                
                hasVisted.Add(tree);

                return visitInfo;
            }

            /*MergeInternal(x + 1, z, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x - 1, z, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x + 1, z + 1, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x, z + 1, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x - 1, z + 1, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x - 1, z - 1, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x, z - 1, m_tessellationJob.mesh_TwoArray[x, z]);
            MergeInternal(x + 1, z - 1, m_tessellationJob.mesh_TwoArray[x, z]);*/
        }

        public void Update()
        {
            if (IsDone)
                return;
            float fx = (curXIdx + 0.5f) * gridSize[0];
            float fz = (curZIdx + 0.5f) * gridSize[1];
            Vector3 center = check_start + fx * Vector3.right + fz * Vector3.forward;
            Vector2 uv = new Vector2((curXIdx + 0.5f) / maxX, (curZIdx + 0.5f) / maxZ);

            // uvstep代表每个tile覆盖的uv范围的绝对值
            Vector2 uvstep = new Vector2(1f / maxX, 1f / maxZ);
            if (Trees[curXIdx * maxZ + curZIdx] == null)
            {
                var t = new SamplerTree(subdivision, center, gridSize, uv, uvstep);
                t.BND = new Bounds(new Vector3(center.x, center.y, center.z),
                    new Vector3(gridSize.x, volBnd.size.y / 2, gridSize.y));
                Trees[curXIdx * maxZ + curZIdx] = t;
            }

            ScanTree(Trees[curXIdx * maxZ + curZIdx]);
            //update idx
            ++curXIdx;
            if (curXIdx >= maxX)
            {
                if (curZIdx < maxZ - 1)
                    curXIdx = 0;
                ++curZIdx;
            }
        }

        #region 实用函数

        /// <summary>
        /// 对Bound打射线
        /// </summary>
        /// <param name="fx">世界空间x</param>
        /// <param name="fz">世界空间z</param>
        /// <param name="x">本地空间x</param>
        /// <param name="z">本地空间z</param>
        /// <param name="bk">采样目标</param>
        /// <param name="sampler">tile的Tree</param>
        private void RayCastBoundary(float fx, float fz, int x, int z, byte bk, SamplerTree sampler)
        {
            Vector3 hitpos = check_start + fx * Vector3.right + fz * Vector3.forward;
            hitpos.x = Mathf.Clamp(hitpos.x, volBnd.min.x, volBnd.max.x);
            hitpos.z = Mathf.Clamp(hitpos.z, volBnd.min.z, volBnd.max.z);

            float local_x = (hitpos.x - volBnd.min.x) / volBnd.size.x;
            float local_y = (hitpos.z - volBnd.min.z) / volBnd.size.z;
            hitpos.y = terrain.SampleHeight(hitpos) + terrain.gameObject.transform.position.y;
            var hitnormal = terrain.terrainData.GetInterpolatedNormal(local_x, local_y);

            SampleVertexData vert = new SampleVertexData();
            vert.Position = hitpos;
            vert.Normal = hitnormal;
            vert.UV = new Vector2(fx / maxX / gridSize[0], fz / maxZ / gridSize[1]);
            sampler.AddBoundary(subdivision, x, z, bk, vert);
        }

        private Vector3 AverageNormal(List<SampleVertexData> lvers)
        {
            Vector3 normal = Vector3.up;
            for (int i = 0; i < lvers.Count; ++i)
            {
                normal += lvers[i].Normal;
            }

            return normal.normalized;
        }

        private void MergeCorners(List<SampleVertexData> l0, List<SampleVertexData> l1, List<SampleVertexData> l2,
            List<SampleVertexData> l3)
        {
            List<SampleVertexData> lvers = new List<SampleVertexData>();
            //lb
            lvers.Add(l0[0]);
            if (l1 != null)
                lvers.Add(l1[0]);
            if (l2 != null)
                lvers.Add(l2[0]);
            if (l3 != null)
                lvers.Add(l3[0]);
            Vector3 normal = AverageNormal(lvers);
            l0[0].Normal = normal;
            if (l1 != null)
                l1[0].Normal = normal;
            if (l2 != null)
                l2[0].Normal = normal;
            if (l3 != null)
                l3[0].Normal = normal;
        }

        private void StitchCorner(int x, int z)
        {
            SamplerTree center = GetSubTree(x, z);
            if (!center.Boundaries.ContainsKey(SamplerTree.LBCorner))
            {
                Debug.LogError("boundary data missing");
                return;
            }

            SamplerTree right = GetSubTree(x + 1, z);
            SamplerTree left = GetSubTree(x - 1, z);
            SamplerTree right_top = GetSubTree(x + 1, z + 1);
            SamplerTree top = GetSubTree(x, z + 1);
            SamplerTree left_top = GetSubTree(x - 1, z + 1);
            SamplerTree left_down = GetSubTree(x - 1, z - 1);
            SamplerTree down = GetSubTree(x, z - 1);
            SamplerTree right_down = GetSubTree(x + 1, z - 1);
            if (!center.StitchedBorders.Contains(SamplerTree.LBCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.LBCorner],
                    left != null ? left.Boundaries[SamplerTree.RBCorner] : null,
                    left_down != null ? left_down.Boundaries[SamplerTree.RTCorner] : null,
                    down != null ? down.Boundaries[SamplerTree.LTCorner] : null);
                center.StitchedBorders.Add(SamplerTree.LBCorner);
                if (left != null) left.StitchedBorders.Add(SamplerTree.RBCorner);
                if (left_down != null) left_down.StitchedBorders.Add(SamplerTree.RTCorner);
                if (down != null) left.StitchedBorders.Add(SamplerTree.LTCorner);
            }

            if (!center.StitchedBorders.Contains(SamplerTree.RBCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.RBCorner],
                    right != null ? right.Boundaries[SamplerTree.LBCorner] : null,
                    right_down != null ? right_down.Boundaries[SamplerTree.LTCorner] : null,
                    down != null ? down.Boundaries[SamplerTree.RTCorner] : null);
                center.StitchedBorders.Add(SamplerTree.RBCorner);
                if (right != null) right.StitchedBorders.Add(SamplerTree.LBCorner);
                if (right_down != null) right_down.StitchedBorders.Add(SamplerTree.LTCorner);
                if (down != null) down.StitchedBorders.Add(SamplerTree.RTCorner);
            }

            if (!center.StitchedBorders.Contains(SamplerTree.LTCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.LTCorner],
                    left != null ? left.Boundaries[SamplerTree.RTCorner] : null,
                    left_top != null ? left_top.Boundaries[SamplerTree.RBCorner] : null,
                    top != null ? top.Boundaries[SamplerTree.LBCorner] : null);
                center.StitchedBorders.Add(SamplerTree.LTCorner);
                if (left != null) left.StitchedBorders.Add(SamplerTree.RTCorner);
                if (left_top != null) left_top.StitchedBorders.Add(SamplerTree.RBCorner);
                if (top != null) top.StitchedBorders.Add(SamplerTree.LBCorner);
            }

            if (!center.StitchedBorders.Contains(SamplerTree.RTCorner))
            {
                MergeCorners(center.Boundaries[SamplerTree.RTCorner],
                    right != null ? right.Boundaries[SamplerTree.LTCorner] : null,
                    right_top != null ? right_top.Boundaries[SamplerTree.LBCorner] : null,
                    top != null ? top.Boundaries[SamplerTree.RBCorner] : null);
                center.StitchedBorders.Add(SamplerTree.RTCorner);
                if (right != null) right.StitchedBorders.Add(SamplerTree.LTCorner);
                if (right_top != null) right_top.StitchedBorders.Add(SamplerTree.LBCorner);
                if (top != null) top.StitchedBorders.Add(SamplerTree.RBCorner);
            }
        }

        #endregion
    }
}