namespace TrafficManager.Manager.Impl {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using API.Manager;
    using API.Traffic.Data;
    using API.Traffic.Enums;
    using ColossalFramework;
    using CSUtil.Commons;
    using GenericGameBridge.Service;
    using State;
    using UnityEngine;

    public class LaneArrowManager
        : AbstractGeometryObservingManager,
          ICustomDataManager<List<Configuration.LaneArrowData>>,
          ICustomDataManager<string>, ILaneArrowManager
    {
        public const NetInfo.LaneType LANE_TYPES =
            NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;

        public const VehicleInfo.VehicleType VEHICLE_TYPES = VehicleInfo.VehicleType.Car;

        public const ExtVehicleType EXT_VEHICLE_TYPES =
            ExtVehicleType.RoadVehicle & ~ExtVehicleType.Emergency;

        public static readonly LaneArrowManager Instance = new LaneArrowManager();

        protected override void InternalPrintDebugInfo() {
            base.InternalPrintDebugInfo();
            Log.NotImpl("InternalPrintDebugInfo for LaneArrowManager");
        }

        public LaneArrows GetFinalLaneArrows(uint laneId) {
            return Flags.GetFinalLaneArrowFlags(laneId, true);
        }

        public bool SetLaneArrows(uint laneId,
                                  LaneArrows flags,
                                  bool overrideHighwayArrows = false) {
            if (Flags.setLaneArrowFlags(laneId, flags, overrideHighwayArrows)) {
                OnLaneChange(laneId);
                return true;
            }

            return false;
        }

        public bool ToggleLaneArrows(uint laneId,
                                     bool startNode,
                                     LaneArrows flags,
                                     out SetLaneArrowError res) {
            if (Flags.ToggleLaneArrowFlags(laneId, startNode, flags, out res)) {
                OnLaneChange(laneId);
                return true;
            }

            return false;
        }

        private void OnLaneChange(uint laneId) {
            Services.NetService.ProcessLane(
                laneId,
                (uint lId, ref NetLane lane) => {
                    RoutingManager.Instance.RequestRecalculation(lane.m_segment);

                    if (OptionsManager.Instance.MayPublishSegmentChanges()) {
                        Services.NetService.PublishSegmentChanges(lane.m_segment);
                    }

                    return true;
                });
        }

        protected override void HandleInvalidSegment(ref ExtSegment seg) {
            Flags.resetSegmentArrowFlags(seg.segmentId);
        }

        protected override void HandleValidSegment(ref ExtSegment seg) { }

        private void ApplyFlags() {
            for (uint laneId = 0; laneId < NetManager.MAX_LANE_COUNT; ++laneId) {
                Flags.ApplyLaneArrowFlags(laneId);
            }
        }

        public override void OnBeforeSaveData() {
            base.OnBeforeSaveData();
            ApplyFlags();
        }

        public override void OnAfterLoadData() {
            base.OnAfterLoadData();
            Flags.ClearHighwayLaneArrows();
            ApplyFlags();
        }

        [Obsolete]
        public bool LoadData(string data) {
            bool success = true;
            Log.Info($"Loading lane arrow data (old method)");
#if DEBUGLOAD
            Log._Debug($"LaneFlags: {data}");
#endif
            var lanes = data.Split(',');

            if (lanes.Length <= 1) {
                return success;
            }

            foreach (string[] split in lanes.Select(lane => lane.Split(':'))
                                            .Where(split => split.Length > 1)) {
                try {
#if DEBUGLOAD
                    Log._Debug($"Split Data: {split[0]} , {split[1]}");
#endif
                    var laneId = Convert.ToUInt32(split[0]);
                    uint flags = Convert.ToUInt32(split[1]);

                    if (!Services.NetService.IsLaneValid(laneId))
                        continue;

                    if (flags > ushort.MaxValue)
                        continue;

                    uint laneArrowFlags = flags & Flags.lfr;
#if DEBUGLOAD
                    uint origFlags =
                        (Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_flags &
                         Flags.lfr);

                    Log._Debug("Setting flags for lane " + laneId + " to " + flags + " (" +
                        ((Flags.LaneArrows)(laneArrowFlags)).ToString() + ")");
                    if ((origFlags | laneArrowFlags) == origFlags) {
                        // only load if setting differs from default
                        Log._Debug("Flags for lane " + laneId + " are original (" +
                            ((NetLane.Flags)(origFlags)).ToString() + ")");
                    }
#endif
                    SetLaneArrows(laneId, (LaneArrows)laneArrowFlags);
                }
                catch (Exception e) {
                    Log.Error(
                        $"Error loading Lane Split data. Length: {split.Length} value: {split}\n" +
                        $"Error: {e}");
                    success = false;
                }
            }

            return success;
        }

        [Obsolete]
        string ICustomDataManager<string>.SaveData(ref bool success) {
            return null;
        }

        public bool LoadData(List<Configuration.LaneArrowData> data) {
            bool success = true;
            Log.Info($"Loading lane arrow data (new method)");

            foreach (Configuration.LaneArrowData laneArrowData in data) {
                try {
                    if (!Services.NetService.IsLaneValid(laneArrowData.laneId)) {
                        continue;
                    }

                    uint laneArrowFlags = laneArrowData.arrows & Flags.lfr;
                    SetLaneArrows(laneArrowData.laneId, (LaneArrows)laneArrowFlags);
                }
                catch (Exception e) {
                    Log.Error(
                        $"Error loading lane arrow data for lane {laneArrowData.laneId}, " +
                        $"arrows={laneArrowData.arrows}: {e}");
                    success = false;
                }
            }

            return success;
        }

        public List<Configuration.LaneArrowData> SaveData(ref bool success) {
            var ret = new List<Configuration.LaneArrowData>();

            for (uint i = 0; i < Singleton<NetManager>.instance.m_lanes.m_buffer.Length; i++) {
                try {
                    LaneArrows? laneArrows = Flags.GetLaneArrowFlags(i);

                    if (laneArrows == null) {
                        continue;
                    }

                    uint laneArrowInt = (uint)laneArrows;
#if DEBUGSAVE
                    Log._Debug($"Saving lane arrows for lane {i}, setting to {laneArrows} ({laneArrowInt})");
#endif
                    ret.Add(new Configuration.LaneArrowData(i, laneArrowInt));
                }
                catch (Exception e) {
                    Log.Error($"Exception occurred while saving lane arrows @ {i}: {e}");
                    success = false;
                }
            }

            return ret;
        }

        /// <summary>
        /// Used for loading and saving LaneFlags
        /// </summary>
        /// <returns>ICustomDataManager for lane flags as a string</returns>
        public static ICustomDataManager<string> AsLaneFlagsDM() {
            return Instance;
        }

        /// <summary>
        /// Used for loading and saving lane arrows
        /// </summary>
        /// <returns>ICustomDataManager for lane arrows</returns>
        public static ICustomDataManager<List<Configuration.LaneArrowData>> AsLaneArrowsDM() {
            return Instance;
        }

        internal void SeparateNode(ushort nodeId) {
            NetNode node = Singleton<NetManager>.instance.m_nodes.m_buffer[nodeId];
            if (nodeId == 0)
                return;
            if ((node.m_flags & NetNode.Flags.Created) == NetNode.Flags.None)
                return;

            //var nSegments = node.CountSegments();

            for (int i = 0; i < 8; i++) {
                ushort segmentId = Singleton<NetManager>.instance.m_nodes.m_buffer[nodeId].GetSegment(i);
                if (segmentId == 0)
                    continue;

                SeparateSegmentLanes(segmentId, nodeId);
            }
        }

        private static void DistributeLanes2(int total, int a, int b, out int x, out int y) {
            /* x+y = total
             * a/b = x/y
             * y = total*b/(a+b)
             * x = total - y 
             */
            y = (total * b) / (a + b); //floor y to favour x
            if (y == 0)
                y = 1;
            x = total - y;

        }

        private static void avoidzero3_helper(ref int x, ref int y, ref int z) {
            if (x == 0) {
                x = 1;
                if (y > z)
                    --y;
                else
                    --z;
            }
        }
        private static void DistributeLanes3(int total, int a, int b, int c, out int x, out int y, out int z) {
            //favour: x then y
            float div = (float)(a + b + c) / (float)total;
            x = (int)Math.Floor((float)a / div);
            y = (int)Math.Floor((float)b / div);
            z = (int)Math.Floor((float)c / div);
            int rem = total - x - y - z;
            switch (rem) {
                case 3:
                    z++;
                    y++;
                    x++;
                    break;
                case 2:
                    y++;
                    x++;
                    break;
                case 1:
                    x++;
                    break;
                case 0:
                    break;
                default:
                    Log.Error($"rem = {rem} : expected rem <= 3");
                    break;
            }
            avoidzero3_helper(ref x, ref y, ref z);
            avoidzero3_helper(ref y, ref x, ref z);
            avoidzero3_helper(ref z, ref x, ref y);
        }

        internal int CountTargetLanesTowardDirection(ushort segmentId, ushort nodeId, ArrowDirection dir) {
            int count = 0;
            ref NetSegment seg = ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentId];
            bool startNode = seg.m_startNode == nodeId;
            IExtSegmentEndManager segEndMan = Constants.ManagerFactory.ExtSegmentEndManager;
            ExtSegmentEnd segEnd = segEndMan.ExtSegmentEnds[segEndMan.GetIndex(segmentId, startNode)];

            Services.NetService.IterateNodeSegments(
                nodeId,
                (ushort otherSegmentId, ref NetSegment otherSeg) => {
                    ArrowDirection dir2 = segEndMan.GetDirection(ref segEnd, otherSegmentId);
                    if (dir == dir2)
                    {
                        int forward = 0, backward = 0;
                        otherSeg.CountLanes(
                            otherSegmentId,
                            NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle,
                            VehicleInfo.VehicleType.Car,
                            ref forward,
                            ref backward);
                        bool startNode2 = otherSeg.m_startNode == nodeId;
                        bool invert2 = (NetManager.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Invert) != NetSegment.Flags.None;
                        //xor because inverting 2 times is redundant.
                        if (invert2 ^ (!startNode2))
                            count += backward;
                        else
                            count += forward;
                    }
                    return true;
                });

            return count;
        }

        internal void SeparateSegmentLanes(ushort segmentId, ushort nodeId) {
            ref NetSegment seg = ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentId];
            bool startNode = seg.m_startNode == nodeId;

            //list of outgoing lanes from current segment to current node.
            IList<LanePos> laneList =
                Constants.ServiceFactory.NetService.GetSortedLanes(
                    segmentId,
                    ref Singleton<NetManager>.instance.m_segments.m_buffer[segmentId],
                    startNode,
                    LaneArrowManager.LANE_TYPES,
                    LaneArrowManager.VEHICLE_TYPES,
                    true
                    );
            int srcLaneCount = laneList.Count();
            if (srcLaneCount == 1)
                return;

            int leftLanesCount = CountTargetLanesTowardDirection(segmentId, nodeId, ArrowDirection.Left);
            int rightLanesCount = CountTargetLanesTowardDirection(segmentId, nodeId, ArrowDirection.Right);
            int forwardLanesCount = CountTargetLanesTowardDirection(segmentId, nodeId, ArrowDirection.Forward);
            int totalLaneCount = leftLanesCount + forwardLanesCount + rightLanesCount;
            int numdirs = Convert.ToInt32(leftLanesCount > 0) + Convert.ToInt32(rightLanesCount > 0) + Convert.ToInt32(forwardLanesCount > 0);

            Debug.Log($"LaneArrowTool.SeparateSegmentLanes: totalLaneCount {totalLaneCount} | numdirs = {numdirs} | outgoingLaneCount = {srcLaneCount}");

            if (numdirs < 2)
                return; // no junction

            if (srcLaneCount == 2 && numdirs == 3) {
                SetLaneArrows(laneList[0].laneId, LaneArrows.LeftForward);
                SetLaneArrows(laneList[1].laneId, LaneArrows.Right);
                return;
            }

            int l = 0, f = 0, r = 0;
            if (numdirs == 2) {
                if (leftLanesCount == 0)
                    DistributeLanes2(srcLaneCount, forwardLanesCount, rightLanesCount, out f, out r);
                else if (rightLanesCount == 0)
                    DistributeLanes2(srcLaneCount, leftLanesCount, forwardLanesCount, out l, out f);
                else //forwarLanesCount == 0
                    DistributeLanes2(srcLaneCount, leftLanesCount, rightLanesCount, out l, out r);
            } else {
                Debug.Assert(numdirs == 3 && srcLaneCount >= 3);
                DistributeLanes3(srcLaneCount, leftLanesCount, forwardLanesCount, rightLanesCount, out l, out f, out r);
            }
            //assign lanes
            Debug.Log($"LaneArrowTool.SeparateSegmentLanes: leftLanesCount {leftLanesCount} | forwardLanesCount {forwardLanesCount} | rightLanesCount {rightLanesCount}");
            Debug.Log($"LaneArrowTool.SeparateSegmentLanes: l {l} | f {f} | r {r}");

            for (var i = 0; i < laneList.Count; i++) {
                var flags = (NetLane.Flags)Singleton<NetManager>.instance.m_lanes.m_buffer[laneList[i].laneId].m_flags;

                LaneArrows arrow = LaneArrows.None;
                if (i < l) {
                    arrow = LaneArrows.Left;
                } else if (l <= i && i < l + f) {
                    arrow = LaneArrows.Forward;
                } else {
                    arrow = LaneArrows.Right;
                }
                SetLaneArrows(laneList[i].laneId, arrow);
            }
        }
    }
}