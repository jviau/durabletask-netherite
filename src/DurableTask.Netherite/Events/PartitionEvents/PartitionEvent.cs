﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask.Netherite
{
    using System;
    using System.Runtime.Serialization;

    /// <summary>
    /// An event that is processed by a partition
    /// </summary>
    [DataContract]
    abstract class PartitionEvent : Event
    {
        [DataMember]
        public uint PartitionId { get; set; }

        /// <summary>
        /// For events coming from the input queue, the next input queue position after this event. For internal events, zero.
        /// </summary>
        [DataMember]
        public long NextInputQueuePosition { get; set; }

        [IgnoreDataMember]
        public double ReceivedTimestamp { get; set; }

        [IgnoreDataMember]
        public double IssuedTimestamp { get; set; }

        [IgnoreDataMember]
        public virtual bool ResetInputQueue => false;

        [IgnoreDataMember]
        public virtual bool CountsAsPartitionActivity => true;

        /// <summary>
        /// For tracing purposes. Subclasses can override this to provide the instance id.
        /// </summary>
        [IgnoreDataMember]
        public virtual string TracedInstanceId => string.Empty;

        // some events trigger some processing immediately upon receive (e.g. prefetches or queries)
        public virtual void OnSubmit(Partition partition) { }

        // make a copy of an event so we run it through the pipeline a second time
        public virtual PartitionEvent Clone()
        {
            var evt = (PartitionEvent)this.MemberwiseClone();

            // clear all the non-data fields
            evt.DurabilityListeners.Clear();
            evt.NextInputQueuePosition = 0;

            // clear the timestamp
            evt.IssuedTimestamp = 0;

            return evt;
        }

    }
}
