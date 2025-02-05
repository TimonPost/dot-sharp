﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using DotQuic.Native.Events;
using DotQuic.Native.Handles;

namespace DotQuic
{
    internal struct PollTask
    {
        public PollTask(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    /// <summary>
    ///     Polls connections if they should be polled.
    /// </summary>
    internal class DeferredTaskExecutor : IDisposable
    {
        private readonly Func<int, ConnectionHandle> _getConnectionHandle;
        private readonly BufferBlock<PollTask> _pollTasks;

        private readonly BufferBlock<Action> _scheduledTasks;
        private readonly CancellationTokenSource Source;
        private Task _connectionPollTask;

        public DeferredTaskExecutor(Func<int, ConnectionHandle> getConnectionHandle)
        {
            _getConnectionHandle = getConnectionHandle;
            _pollTasks = new BufferBlock<PollTask>();
            Source = new CancellationTokenSource();

            EndpointEvents.ConnectionPollable += OnConnectionPollable;
            _scheduledTasks = new BufferBlock<Action>();
        }

        public void Dispose()
        {
            Source.Cancel();
            Source.Dispose();
            _connectionPollTask?.Dispose();
        }

        /// Runs a task that polls a connection when it is ready to be polled.
        /// Rust will invoke a callback into C# that triggers the `OnConnectionPollable` method.
        /// Therefore this task will only poll when it is necessarily.
        public void StartPollingAsync()
        {
            _connectionPollTask = Task.Run(async () =>
            {
                while (!Source.IsCancellationRequested)
                {
                    var scheduled = await _scheduledTasks.ReceiveAsync();
                    scheduled();
                }
            });
        }

        /// A connection poll might result in callbacks from rust to C#. 
        /// To prevent deadlocks on connection handle tasks are schedule for when the poll operation finished.
        public void Schedule(Action task)
        {
            _scheduledTasks.Post(task);
        }

        private void OnConnectionPollable(object? sender, ConnectionIdEventArgs e)
        {
            _pollTasks.SendAsync(new PollTask(e.Id));
        }
    }
}