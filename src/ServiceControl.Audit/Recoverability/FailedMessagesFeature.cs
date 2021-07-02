﻿namespace ServiceControl.Audit.Recoverability
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Auditing;
    using Contracts.MessageFailures;
    using Infrastructure;
    using NServiceBus;
    using NServiceBus.Features;
    using NServiceBus.Routing;
    using NServiceBus.Transport;

    class FailedMessagesFeature : Feature
    {
        public FailedMessagesFeature()
        {
            EnableByDefault();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            context.Container.ConfigureComponent<DetectSuccessfullRetriesEnricher>(DependencyLifecycle.SingleInstance);
        }

        class DetectSuccessfullRetriesEnricher : IEnrichImportedAuditMessages
        {
            public void Enrich(AuditEnricherContext context)
            {
                var headers = context.Headers;
                var isOldRetry = headers.TryGetValue("ServiceControl.RetryId", out _);
                var isNewRetry = headers.TryGetValue("ServiceControl.Retry.UniqueMessageId", out var newRetryMessageId);
                var isAckHandled = headers.ContainsKey("ServiceControl.Retry.AcknowledgementSent");
                var hasAckQueue = headers.TryGetValue("ServiceControl.Retry.AcknowledgementQueue", out var ackQueue);

                var hasBeenRetried = isOldRetry || isNewRetry;

                context.Metadata.Add("IsRetried", hasBeenRetried);

                if (!hasBeenRetried || isAckHandled)
                {
                    return;
                }

                if (hasAckQueue && isNewRetry)
                {
                    var ackMessage = new OutgoingMessage(Guid.NewGuid().ToString(), new Dictionary<string, string>
                    {
                        ["ServiceControl.Retry.Successful"] = "true"
                    }, new byte[0]);
                    var ackOperation = new TransportOperation(ackMessage, new UnicastAddressTag(ackQueue));
                    context.AddForSend(ackOperation);
                }
                else
                {
                    context.AddForSend(new MarkMessageFailureResolvedByRetry
                    {
                        FailedMessageId = isOldRetry ? headers.UniqueId() : newRetryMessageId,
                        AlternativeFailedMessageIds = GetAlternativeUniqueMessageId(headers).ToArray()
                    });
                }
            }

            IEnumerable<string> GetAlternativeUniqueMessageId(IReadOnlyDictionary<string, string> headers)
            {
                var messageId = headers.MessageId();
                if (headers.TryGetValue(Headers.ProcessingEndpoint, out var processingEndpoint))
                {
                    yield return DeterministicGuid.MakeId(messageId, processingEndpoint).ToString();
                }

                if (headers.TryGetValue("NServiceBus.FailedQ", out var failedQ))
                {
                    yield return DeterministicGuid.MakeId(messageId, ExtractQueueNameForLegacyReasons(failedQ)).ToString();
                }

                if (headers.TryGetValue(Headers.ReplyToAddress, out var replyToAddress))
                {
                    yield return DeterministicGuid.MakeId(messageId, ExtractQueueNameForLegacyReasons(replyToAddress)).ToString();
                }
            }

            static string ExtractQueueNameForLegacyReasons(string address)
            {
                var atIndex = address?.IndexOf("@", StringComparison.InvariantCulture);

                if (atIndex.HasValue && atIndex.Value > -1)
                {
                    return address.Substring(0, atIndex.Value);
                }

                return address;
            }
        }
    }
}