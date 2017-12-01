﻿namespace ServiceControl.ASB.DLQMonitor
{
    using System;
    using System.Configuration;
    using NServiceBus.CustomChecks;
    using Microsoft.ServiceBus;
    using ServiceBus.Management.Infrastructure.Settings;

    public class CheckDeadLetterQueue : CustomCheck
    {
        NamespaceManager namespaceManager;
        string stagingQueue;

        public CheckDeadLetterQueue(Settings settings) : base(id: "Dead Letter Queue", category: "Transport", repeatAfter: TimeSpan.FromHours(1))
        {
            var connectionStringSettings = ConfigurationManager.ConnectionStrings["NServiceBus/Transport"];
            var transportConnectionString = connectionStringSettings.ConnectionString;
            namespaceManager = NamespaceManager.CreateFromConnectionString(transportConnectionString);
            stagingQueue = $"{settings.ServiceName}.staging";
        }


        public override CheckResult PerformCheck()
        {
            var queueDescription = namespaceManager.GetQueue(stagingQueue);
            var messageCountDetails = queueDescription.MessageCountDetails;

            if (messageCountDetails.DeadLetterMessageCount > 0)
            {
                return CheckResult.Failed($"{messageCountDetails.DeadLetterMessageCount} messages in the Dead Letter Queue '{stagingQueue}'. This could indicate a problem with ServiceControl's retries. Please submit a support ticket to Particular using support@particular.net if you would like help from our engineers to ensure no message loss while resolving these dead letter messages.");
            }

            return CheckResult.Pass;
        }
    }
}