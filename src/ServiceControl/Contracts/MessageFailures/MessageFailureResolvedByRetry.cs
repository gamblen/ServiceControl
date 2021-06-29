﻿namespace ServiceControl.Contracts.MessageFailures
{
    using NServiceBus;

    // Comes from unconverted legacy instances
    public class MessageFailureResolvedByRetry : IMessage
    {
        public string FailedMessageId { get; set; }
        public string[] AlternativeFailedMessageIds { get; set; }
    }
}