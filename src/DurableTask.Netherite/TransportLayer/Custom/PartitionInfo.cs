﻿namespace DurableTask.Netherite.CustomTransport
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    public class PartitionInfo : TransportAbstraction.ISender
    {
        readonly CustomTransport transport;
        readonly uint partitionId;
        readonly PartitionSender[] partitionSenders;
        readonly ConcurrentDictionary<Guid,ClientSender> clientSenders;
        readonly LoadMonitorSender loadMonitorSender;

        public Task StartupTask { get; private set; }

        TransportAbstraction.IPartition partition;

        public PartitionInfo(uint partitionId, CustomTransport transport)
        {
            this.partitionId = partitionId;
            this.transport = transport;
            this.partitionSenders = Enumerable
                .Range(0, transport.Parameters.PartitionCount)
                .Select(i => new PartitionSender(i, transport))
                .ToArray();
            this.clientSenders = new ConcurrentDictionary<Guid, ClientSender>();
            this.loadMonitorSender = new LoadMonitorSender(transport);
            this.StartupTask = Task.Run(this.StartAsync);
        }

        async Task StartAsync()
        {
            this.partition = this.transport.Host.AddPartition(this.partitionId, this);
            var errorHandler = this.transport.Host.CreateErrorHandler(this.partitionId);
            await this.partition.CreateOrRestoreAsync(errorHandler, this.transport.Parameters, "");
        }

        public async Task StopAsync()
        {
            await this.StartupTask;
            await this.partition.StopAsync(false);
        }

        void TransportAbstraction.ISender.Submit(Event element)
        {
            switch (element)
            {
                case PartitionEvent partitionEvent:
                    this.partitionSenders[partitionEvent.PartitionId].Submit(partitionEvent);
                    break;

                case ClientEvent clientEvent:
                    var clientSender = this.clientSenders.GetOrAdd(clientEvent.ClientId, (Guid guid) => new ClientSender(guid, this.transport));
                    clientSender.Submit(clientEvent);
                    break;

                case LoadMonitorEvent loadMonitorEvent:
                    this.loadMonitorSender.Submit(loadMonitorEvent);
                    break;
            }
        }

        public async Task DeliverAsync(Stream stream)
        {
            await this.StartupTask;
            var partitionEvents = Serializer.DeserializePartitionBatch(stream);

            DurabilityWaiter waiter = null;
            for (int i = partitionEvents.Count - 1; i >= 0; i--)
            {
                Debug.Assert(partitionEvents[i].PartitionId == this.partitionId);

                if (partitionEvents[i] is PartitionUpdateEvent updateEvent)
                {
                    waiter = new DurabilityWaiter(updateEvent);
                    break;
                }
            }

            this.partition.SubmitEvents(partitionEvents);

            if (waiter != null)
            {
                await waiter.Task;
            }
        }

        class DurabilityWaiter : TaskCompletionSource<bool>, TransportAbstraction.IDurabilityListener
        {
            public DurabilityWaiter(Event e)
            {
                DurabilityListeners.Register(e, this);
            }

            public void ConfirmDurable(Event evt)
            {
                this.TrySetResult(true);
            }
        }
    }
}