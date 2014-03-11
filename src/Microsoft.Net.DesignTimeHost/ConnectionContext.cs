﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Communication;
using Microsoft.Net.DesignTimeHost.Models;
using Microsoft.Net.Runtime;

namespace Microsoft.Net.DesignTimeHost
{
    public class ConnectionContext
    {
        private readonly IDictionary<int, ApplicationContext> _contexts = new Dictionary<int, ApplicationContext>();
        private readonly IAssemblyLoaderEngine _loaderEngine;
        private readonly Stream _stream;
        private ProcessingQueue _queue;
        private string _hostId;

        public ConnectionContext(IAssemblyLoaderEngine loaderEngine, Stream stream, string hostId)
        {
            _loaderEngine = loaderEngine;
            _stream = stream;
            _hostId = hostId;
        }

        public void Start()
        {
            _queue = new ProcessingQueue(_stream);
            _queue.OnReceive += OnReceive;
            _queue.Start();
        }

        public void OnReceive(Message message)
        {
            // Check the hostId to ensure it is from our host - throw it away if not
            if (!message.HostId.Equals(_hostId, StringComparison.Ordinal))
            {
                Trace.TraceInformation("[ConnectionContext]: Received message from unknown host {0}. Expected message from {1}. Ignoring", message.HostId, _hostId);
                return;
            }

            ApplicationContext applicationContext;
            if (!_contexts.TryGetValue(message.ContextId, out applicationContext))
            {
                Trace.TraceInformation("[ConnectionContext]: Creating new application context for {0}", message.ContextId);

                applicationContext = new ApplicationContext(_loaderEngine, message.ContextId);
                applicationContext.OnTransmit += OnTransmit;
                _contexts.Add(message.ContextId, applicationContext);
            }

            applicationContext.OnReceive(message);
        }

        public void OnTransmit(Message message)
        {
            message.HostId = _hostId;
            _queue.Post(message);
        }
    }
}
