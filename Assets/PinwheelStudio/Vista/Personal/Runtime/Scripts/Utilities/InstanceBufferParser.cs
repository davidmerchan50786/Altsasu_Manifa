#if VISTA
using UnityEngine;
using System.Collections.Generic;
#if GRIFFIN
using Pinwheel.Griffin;
#endif

namespace Pinwheel.Vista
{
    /// <summary>
    /// Converts Vista instance-sample buffers into Polaris tree and grass instance lists.
    /// </summary>
    public static class InstanceBufferParser
    {
#if GRIFFIN
        /// <summary>
        /// Reads a buffer packed as <see cref="InstanceSample"/> values and appends the valid entries as Polaris tree instances.
        /// </summary>
        /// <param name="instances">The destination list that receives parsed tree instances.</param>
        /// <param name="buffer">The compute buffer containing float-packed <see cref="InstanceSample"/> data.</param>
        /// <param name="prototypeIndex">The Polaris prototype index assigned to every parsed instance.</param>
        /// <remarks>
        /// Samples with <c>isValid &lt;= 0</c> are skipped. If the buffer length is not a multiple of <see cref="InstanceSample.SIZE"/>,
        /// the method logs an error and leaves <paramref name="instances"/> unchanged.
        /// </remarks>
        public static void Parse(List<GTreeInstance> instances, ComputeBuffer buffer, int prototypeIndex)
        {
            if (buffer.count % InstanceSample.SIZE != 0)
            {
                Debug.LogError("Cannot parse instance sample buffer");
                return;
            }

            InstanceSample[] data = new InstanceSample[buffer.count / InstanceSample.SIZE];
            buffer.GetData(data);

            foreach (InstanceSample t in data)
            {
                if (t.isValid <= 0)
                    continue;
                GTreeInstance tree = new GTreeInstance();
                tree.Position = t.position;
                tree.Rotation = Quaternion.Euler(0, t.rotationY, 0);
                tree.Scale = new Vector3(t.horizontalScale, t.verticalScale, t.horizontalScale);
                tree.PrototypeIndex = prototypeIndex;
                instances.Add(tree);
            }
        }

        /// <summary>
        /// Reads a buffer packed as <see cref="InstanceSample"/> values and appends the valid entries as Polaris grass instances.
        /// </summary>
        /// <param name="instances">The destination list that receives parsed grass instances.</param>
        /// <param name="buffer">The compute buffer containing float-packed <see cref="InstanceSample"/> data.</param>
        /// <param name="prototypeIndex">The Polaris prototype index assigned to every parsed instance.</param>
        /// <remarks>
        /// Samples with <c>isValid &lt;= 0</c> are skipped. If the buffer length is not a multiple of <see cref="InstanceSample.SIZE"/>,
        /// the method logs an error and leaves <paramref name="instances"/> unchanged.
        /// </remarks>
        public static void Parse(List<GGrassInstance> instances, ComputeBuffer buffer, int prototypeIndex)
        {
            if (buffer.count % InstanceSample.SIZE != 0)
            {
                Debug.LogError("Cannot parse instance sample buffer");
                return;
            }

            InstanceSample[] data = new InstanceSample[buffer.count / InstanceSample.SIZE];
            buffer.GetData(data);

            foreach (InstanceSample t in data)
            {
                if (t.isValid <= 0)
                    continue;
                GGrassInstance g = new GGrassInstance();
                g.Position = t.position;
                g.Rotation = Quaternion.Euler(0, t.rotationY, 0);
                g.Scale = new Vector3(t.horizontalScale, t.verticalScale, t.horizontalScale);
                g.PrototypeIndex = prototypeIndex;
                instances.Add(g);
            }
        }
#endif
    }
}
#endif
