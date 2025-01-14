﻿namespace ServiceControlInstaller.CustomActions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Engine.Configuration.ServiceControl;
    using Engine.FileSystem;
    using Engine.Instances;
    using ServiceControl.LicenseManagement;
    using Engine.Unattended;
    using Microsoft.Deployment.WindowsInstaller;

    public class CustomActionsInstall
    {
        [CustomAction]
        public static ActionResult ServiceControlUnattendedInstall(Session session)
        {
            var logger = new MSILogger(session);

            var unattendedInstaller = new UnattendServiceControlInstaller(logger, session["APPDIR"]);
            var zipInfo = ServiceControlZipInfo.Find(session["APPDIR"] ?? ".");

            if (!zipInfo.Present)
            {
                logger.Error("Zip file not found. Service Control service instances can not be upgraded or installed");
                return ActionResult.Failure;
            }

            UpgradeInstances(session, zipInfo, logger, unattendedInstaller);
            UnattendedInstall(session, logger, unattendedInstaller).Wait();
            ImportLicenseInstall(session, logger);
            return ActionResult.Success;
        }

        static void UpgradeInstances(Session session, PlatformZipInfo zipInfo, MSILogger logger, UnattendServiceControlInstaller unattendedInstaller)
        {
            var options = new ServiceControlUpgradeOptions();

            var upgradeInstancesPropertyValue = session["UPGRADEINSTANCES"];
            if (string.IsNullOrWhiteSpace(upgradeInstancesPropertyValue))
            {
                return;
            }

            upgradeInstancesPropertyValue = upgradeInstancesPropertyValue.Trim();

            var forwardErrorMessagesPropertyValue = session["FORWARDERRORMESSAGES"];
            try
            {
                options.OverrideEnableErrorForwarding = bool.Parse(forwardErrorMessagesPropertyValue);
            }
            catch
            {
                options.OverrideEnableErrorForwarding = null;
            }

            var auditRetentionPeriodPropertyValue = session["AUDITRETENTIONPERIOD"];
            try
            {
                options.AuditRetentionPeriod = TimeSpan.Parse(auditRetentionPeriodPropertyValue);
            }
            catch
            {
                options.AuditRetentionPeriod = null;
            }

            var errorRetentionPeriodPropertyValue = session["ERRORRETENTIONPERIOD"];
            try
            {
                options.ErrorRetentionPeriod = TimeSpan.Parse(errorRetentionPeriodPropertyValue);
            }
            catch
            {
                options.ErrorRetentionPeriod = null;
            }

            //determine what to upgrade
            var instancesToUpgrade = new List<ServiceControlInstance>();
            if (upgradeInstancesPropertyValue.Equals("*", StringComparison.OrdinalIgnoreCase) || upgradeInstancesPropertyValue.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                instancesToUpgrade.AddRange(InstanceFinder.ServiceControlInstances());
            }
            else
            {
                var candidates = upgradeInstancesPropertyValue.Replace(" ", string.Empty).Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                instancesToUpgrade.AddRange(InstanceFinder.ServiceControlInstances().Where(instance => candidates.Contains(instance.Name, StringComparer.OrdinalIgnoreCase)));
            }

            // do upgrades
            foreach (var instance in instancesToUpgrade)
            {
                if (zipInfo.Version > instance.Version)
                {
                    var upgradeInfo = UpgradeControl.GetUpgradeInfoForTargetVersion(zipInfo.Version, instance.Version);

                    options.UpgradeInfo = upgradeInfo;

                    if (!instance.AppConfig.AppSettingExists(ServiceControlSettings.ForwardErrorMessages.Name) & !options.OverrideEnableErrorForwarding.Value)
                    {
                        logger.Warn($"Unattend upgrade {instance.Name} to {zipInfo.Version} not attempted. FORWARDERRORMESSAGES MSI parameter was required because appsettings needed a value for '{ServiceControlSettings.ForwardErrorMessages.Name}'");
                        continue;
                    }

                    if (!options.AuditRetentionPeriod.HasValue)
                    {
                        if (!instance.AppConfig.AppSettingExists(ServiceControlSettings.AuditRetentionPeriod.Name))
                        {
                            //Try migration first
                            if (instance.AppConfig.AppSettingExists(ServiceControlSettings.HoursToKeepMessagesBeforeExpiring.Name))
                            {
                                var i = instance.AppConfig.Read(ServiceControlSettings.HoursToKeepMessagesBeforeExpiring.Name, -1);
                                if (i > 0)
                                {
                                    options.AuditRetentionPeriod = TimeSpan.FromHours(i);
                                }
                            }
                            else
                            {
                                logger.Warn($"Unattend upgrade {instance.Name} to {zipInfo.Version} not attempted. AUDITRETENTIONPERIOD MSI parameter was required because appsettings needed a value for '{ServiceControlSettings.AuditRetentionPeriod.Name}'");
                                continue;
                            }
                        }
                    }

                    if (!instance.AppConfig.AppSettingExists(ServiceControlSettings.ErrorRetentionPeriod.Name) & !options.ErrorRetentionPeriod.HasValue)
                    {
                        logger.Warn($"Unattend upgrade {instance.Name} to {zipInfo.Version} not attempted. ERRORRETENTIONPERIOD MSI parameter was required because appsettings needed a value for '{ServiceControlSettings.ErrorRetentionPeriod.Name}'");
                        continue;
                    }

                    if (!unattendedInstaller.Upgrade(instance, options))
                    {
                        logger.Warn($"Failed to upgrade {instance.Name} to {zipInfo.Version}");
                    }
                }
            }
        }

        static async Task UnattendedInstall(Session session, MSILogger logger, UnattendServiceControlInstaller unattendedInstaller)
        {
            logger.Info("Checking for unattended file");

            var unattendedFilePropertyValue = session["UNATTENDEDFILE"];
            if (string.IsNullOrWhiteSpace(unattendedFilePropertyValue))
            {
                return;
            }

            var serviceAccount = session["SERVICEACCOUNT"];
            var password = session["PASSWORD"];
            logger.Info($"UNATTENDEDFILE: {unattendedFilePropertyValue}");
            var currentDirectory = session["CURRENTDIRECTORY"];
            var unattendedFilePath = Environment.ExpandEnvironmentVariables(Path.IsPathRooted(unattendedFilePropertyValue) ? unattendedFilePropertyValue : Path.Combine(currentDirectory, unattendedFilePropertyValue));

            logger.Info($"Expanded unattended filepath to : {unattendedFilePropertyValue}");

            if (File.Exists(unattendedFilePath))
            {
                logger.Info($"File Exists : {unattendedFilePropertyValue}");
                var instanceToInstallDetails = ServiceControlNewInstance.Load(unattendedFilePath);

                if (!string.IsNullOrWhiteSpace(serviceAccount))
                {
                    instanceToInstallDetails.ServiceAccount = serviceAccount;
                    instanceToInstallDetails.ServiceAccountPwd = password;
                }

                await unattendedInstaller.Add(instanceToInstallDetails, s => Task.FromResult(false))
                    .ConfigureAwait(false);
            }
            else
            {
                logger.Error($"The specified unattended install file was not found : '{unattendedFilePath}'");
            }
        }

        static void ImportLicenseInstall(Session session, MSILogger logger)
        {
            logger.Info("Checking for license file");

            var licenseFilePropertyValue = session["LICENSEFILE"];
            if (string.IsNullOrWhiteSpace(licenseFilePropertyValue))
            {
                return;
            }

            logger.Info($"LICENSEFILE: {licenseFilePropertyValue}");
            var currentDirectory = session["CURRENTDIRECTORY"];
            var licenseFilePath = Environment.ExpandEnvironmentVariables(Path.IsPathRooted(licenseFilePropertyValue) ? licenseFilePropertyValue : Path.Combine(currentDirectory, licenseFilePropertyValue));

            logger.Info($"Expanded license filepath to : {licenseFilePropertyValue}");

            if (File.Exists(licenseFilePath))
            {
                logger.Info($"File Exists : {licenseFilePropertyValue}");
                if (!LicenseManager.TryImportLicense(licenseFilePath, out var errormessage))
                {
                    logger.Error(errormessage);
                }
            }
            else
            {
                logger.Error($"The specified license install file was not found : '{licenseFilePath}'");
            }
        }
    }
}