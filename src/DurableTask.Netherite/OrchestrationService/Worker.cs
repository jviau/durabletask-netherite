﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace DurableTask.Netherite
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using DurableTask.Core;
    using DurableTask.Core.History;

    class Worker : TransportAbstraction.IWorker
    {
        readonly NetheriteOrchestrationService host;
        readonly CancellationToken shutdownToken;
        readonly string account;
        readonly Guid taskHubGuid;
        readonly Stopwatch workItemStopwatch;
        readonly Func<string, uint> partitionHash;

        long sequenceNumber;

        public Guid WorkerId { get; private set; }
        internal TransportAbstraction.ISender BatchSender { get; }
        internal WorkItemTraceHelper WorkItemTraceHelper { get; }
        internal DoubleWorkItemQueue<ActivityWorkItem> ActivityWorkItemQueue { get; }

        Guid TransportAbstraction.IWorker.WorkerId => this.WorkerId;

        public static string GetShortId(Guid workerId) => workerId.ToString("N").Substring(0, 7).ToUpper();

        public Worker(
            NetheriteOrchestrationService host,
            Guid clientId, 
            Guid taskHubGuid, 
            TransportAbstraction.ISender batchSender,
            DoubleWorkItemQueue<ActivityWorkItem> activityWorkItemQueue,
            WorkItemTraceHelper workItemTraceHelper,
            Func<string,uint> partitionHash,
            CancellationToken shutdownToken)
        {
            this.host = host;
            this.WorkerId = clientId;
            this.taskHubGuid = taskHubGuid;
            this.ActivityWorkItemQueue = activityWorkItemQueue;
            this.WorkItemTraceHelper = workItemTraceHelper;
            this.account = host.StorageAccountName;
            this.BatchSender = batchSender;
            this.partitionHash = partitionHash;
            this.shutdownToken = shutdownToken;
            this.workItemStopwatch = new Stopwatch();
            this.workItemStopwatch.Start();
        }

        public string GetWorkItemId(long seqno) => $"W{GetShortId(this.WorkerId)}A{seqno}";

        void TransportAbstraction.IWorker.Process(WorkerEvent workerEvent, TransportAbstraction.IDurabilityListener listener)
        {
            if (workerEvent is WorkerRequestReceived workerRequestReceived)
            {
                var workItem = new ActivityRemoteWorkItem(
                    this,
                    this.partitionHash(workerRequestReceived.Message.OrchestrationInstance.InstanceId),
                    workerRequestReceived.Message,
                    workerRequestReceived.OriginWorkItemId,
                    this.sequenceNumber++,
                    listener);

                this.ActivityWorkItemQueue.AddRemote(workItem);
                this.WorkItemTraceHelper.TraceTaskMessageReceived(workItem.PartitionId, workItem.TaskMessage, workItem.OriginWorkItem, workItem.WorkItemId);
            }
        }

        Task TransportAbstraction.IWorker.StopAsync()
        {
            return Task.CompletedTask;
        }

        void TransportAbstraction.IWorker.ReportTransportError(string msg, Exception e)
        {
            return;
        }
    }
}