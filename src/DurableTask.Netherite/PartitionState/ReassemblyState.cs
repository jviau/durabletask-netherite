﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask.Netherite
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.Serialization;

    [DataContract]
    class ReassemblyState : TrackedObject
    {
        [DataMember]
        public Dictionary<string, List<PartitionEventFragment>> Fragments { get; private set; } = new Dictionary<string, List<PartitionEventFragment>>();

        [IgnoreDataMember]
        public override TrackedObjectKey Key => new TrackedObjectKey(TrackedObjectKey.TrackedObjectType.Reassembly);
        public override string ToString()
        {
            return $"Reassembly ({this.Fragments.Count} pending)";
        }

        public override void Process(RecoveryCompleted evt, EffectTracker effects)
        {
            bool IsExpired(List<PartitionEventFragment> list)
            {
                var fragment = list.First();
                if (fragment.Timeout.HasValue)
                {
                    return fragment.Timeout.Value < evt.Timestamp;
                }
                else if (fragment.DedupPosition.HasValue)
                {                  
                    evt.ReceivePositions.TryGetValue(fragment.DedupPosition.Value.Item1, out (long, int) lastProcessed);
                    return lastProcessed.CompareTo((fragment.DedupPosition.Value.Item2, fragment.DedupPosition.Value.Item3)) >= 0;
                }
                else 
                {
                    return false;
                }
            }

            var expired = this.Fragments.Where(kvp => IsExpired(kvp.Value)).ToList();

            foreach (var kvp in expired)
            {
                this.Fragments.Remove(kvp.Key);

                if (!effects.IsReplaying)
                {
                    effects.EventTraceHelper.TraceEventProcessingDetail($"Dropped {kvp.Value.Count()} expired fragments for id={kvp.Value.First().OriginalEventId} during recovery");
                }
            }
        }

        public override void Process(PartitionEventFragment evt, EffectTracker effects)
        {
            // stores fragments until the last one is received
            var originalEventString = evt.OriginalEventId.ToString();

            if (evt.IsLast)
            {
                evt.ReassembledEvent =  FragmentationAndReassembly.Reassemble<PartitionEvent>(this.Fragments[originalEventString], evt, effects.Partition);
                
                effects.EventDetailTracer?.TraceEventProcessingDetail($"Reassembled {evt.ReassembledEvent}");

                this.Fragments.Remove(originalEventString);

                switch (evt.ReassembledEvent)
                {
                    case PartitionUpdateEvent updateEvent:
                        if (!effects.IsReplaying)
                        {
                            updateEvent.OnSubmit(this.Partition);
                        }
                        updateEvent.DetermineEffects(effects);
                        break;

                    case PartitionReadEvent readEvent:
                        this.Partition.SubmitEvent(readEvent);
                        break;

                    case PartitionQueryEvent queryEvent:
                        this.Partition.SubmitParallelEvent(queryEvent);
                        break;

                    default:
                        throw new InvalidCastException("Could not cast to neither PartitionReadEvent nor PartitionUpdateEvent");
                }
            }
            else
            {
                List<PartitionEventFragment> list;

                if (evt.Fragment == 0)
                {
                    this.Fragments[originalEventString] = list = new List<PartitionEventFragment>();
                }
                else
                {
                    list = this.Fragments[originalEventString];
                }

                list.Add(evt);
            }
        }
    }
}
